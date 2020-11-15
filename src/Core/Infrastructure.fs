namespace CQRSLite.Core.Infrastructure

open System
open FSharpPlus
open FSharpPlus.Data
open FSharpPlus.Control

type IMessage  =
  interface
  end

type IQuery<'Result> =
  inherit IMessage

type ICommand =
  inherit IMessage

type IHandler<'TMessage, 'TResult, 'TError when 'TMessage :> IMessage> =
  abstract Handle: 'TMessage -> Async<Result<'TResult,'TError>>

type IEvent =
  inherit IMessage
  abstract EntityId: Guid
  abstract EntityVersion: int
  abstract EventTimeStamp: DateTimeOffset

/// An event listener is supposed to a passive listener for events
type IEventListener<'TEvent when 'TEvent :> IEvent> =
  abstract Handle: 'TEvent -> Async<unit>

type IEventPublisher<'TEvent when 'TEvent :> IEvent> =
  abstract Publish: 'TEvent -> Async<unit>
type IEventStore<'TEvent when 'TEvent :> IEvent> =
  abstract Save: 'TEvent seq -> Async<unit>
  abstract Get: id:Guid -> 'TEvent seq option

/// a command handler takes a command returns unit on success and returns an error on failure
type ICommandHandler<'TCommand, 'TError when 'TCommand :> ICommand> =
  inherit IHandler<'TCommand, unit, 'TError>

/// a query handler takes a query returns the result of the query on success and returns an error on failure 
type IQueryHandler<'TQuery, 'TQueryResult when 'TQuery :> IQuery<'TQueryResult>> =
  abstract Handle: 'TQuery -> Async<'TQueryResult option>


type IAggregateRoot =
  abstract Id: Guid
  abstract Version: int

type ISession<'TEvent when 'TEvent :> IEvent> =
  abstract Put<'T when 'T :> IAggregateRoot> : aggregate:'T * events:'TEvent seq->Async<unit>

  abstract Get<'T when 'T :> IAggregateRoot> : aggregateId:Guid * expectedVersion:int option -> Async<'T option>
  abstract Commit: unit -> Async<unit>

(*type IRepository=
  abstract Save<'T when 'T :> IAggregateRoot> : aggregate:'T * expectedVersion:int option -> Async<unit>
  abstract Get<'T when 'T :> IAggregateRoot> : aggregateId:Guid -> Async<'T option>

type Session(repo:IRepository)=
  interface ISession with
    member __.Put(aggregate,events)=async.Return ()
    member __.Get(aggregate,events)=async.Return ()
    *)

type AsyncResult<'T,'E> = ResultT<Async<Result<'T,'E>>>
module AsyncResult=
  let error v= ResultT <| async.Return (Error v)
type AsyncResultBuilder () =
  member        __.ReturnFrom (expr) = expr                                      : AsyncResult<'T,_>
  member inline __.Return (x: 'T) = result x                                     : AsyncResult<'T,_>
  member inline __.Yield  (x: 'T) = result x                                     : AsyncResult<'T,_>
  member inline __.Bind (p: AsyncResult<'T,_>, rest: 'T->AsyncResult<'U,_>)      : AsyncResult<'U,_>    = (p >>= rest)
  member inline __.MergeSources (t1: AsyncResult<'T,_>, t2: AsyncResult<'U,_>)   : AsyncResult<'T*'U,_> = Lift2.Invoke tuple2 t1 t2
  member inline __.BindReturn   (x : AsyncResult<'T,_>, f: 'T -> 'U)             : AsyncResult<'U,_>    = Map.Invoke f x
  member inline __.Zero () = ResultT (async.Return <| Ok ())                     : AsyncResult<unit,_>
  member inline __.Delay (expr: _->AsyncResult<'T,_>) = Delay.Invoke expr        : AsyncResult<'T,_>

type FakeSession<'TEvent when 'TEvent :> IEvent>(eventStore:IEventStore<'TEvent>)=
  let current = Collections.Generic.Dictionary<Guid,IAggregateRoot>()
  let versions = Collections.Generic.Dictionary<Guid*int,IAggregateRoot>()
  interface ISession<'TEvent> with
      member __.Put(aggregate,events)=async{
        do! eventStore.Save events
        versions.Add((aggregate.Id,aggregate.Version),aggregate)
        current.[aggregate.Id] <- aggregate
      }
      member __.Get<'T when 'T :> IAggregateRoot>(id,maybeVersion)=async{
        match maybeVersion with
        | None ->
          return Dict.tryGetValue id current |> Option.map (fun v-> downcast v : 'T)
        | Some version ->
          return Dict.tryGetValue (id,version) versions |> Option.map (fun v-> downcast v : 'T)
      }
      member __.Commit() = async.Return()
module FakeEventStore=
  type ConcurrencyException()=
    inherit Exception()
open FakeEventStore
type FakeEventStore<'TEvent when 'TEvent :> IEvent>(publisher:IEventPublisher<'TEvent>)=
  let current = Collections.Generic.Dictionary<Guid,ResizeArray<'TEvent>>()
  let saveEvent (event:'TEvent)=async{
    let expectedVersion = event.EntityVersion
    match Dict.tryGetValue event.EntityId current with
    | Some events when 
                events.[events.Count - 1].EntityVersion <> expectedVersion->
                raise <| ConcurrencyException()
    | Some events ->
      events.Add(event)
    | None ->
      let events=ResizeArray()
      current.Add(event.EntityId, events)
      events.Add(event)
    
    do! publisher.Publish event
  }

  interface IEventStore<'TEvent> with
    member __.Save(events)=async{
      for event in events do
        do! saveEvent event
    }
    member __.Get(id)=
      Dict.tryGetValue id current |> Option.map (fun evts-> upcast evts)
    
type FakeEventPublisher<'TEvent when 'TEvent :> IEvent>(listeners : IEventListener<'TEvent> seq)=
  interface IEventPublisher<'TEvent> with
    member __.Publish(event)=async{
      for listener in listeners do
        do! listener.Handle event
    }
