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
type Command<'TCommand> =
  { Id: Guid
    ExpectedVersion: int
    Command: 'TCommand }
  interface ICommand

type IEvent =
  inherit IMessage
  abstract EntityId: Guid
  abstract EntityVersion: int
  abstract EventTimeStamp: DateTimeOffset
  abstract EventData: obj
type Event<'T> =
    { EntityId: Guid
      EntityVersion: int
      EventTimeStamp: DateTimeOffset
      EventData: 'T }
    interface IEvent with
      member e.EntityId = e.EntityId
      member e.EntityVersion = e.EntityVersion
      member e.EventTimeStamp = e.EventTimeStamp
      member e.EventData = box e.EventData

/// An event listener is supposed to a passive listener for events
type IEventListener<'TEvent> =
  abstract Handle: Event<'TEvent> -> Async<unit>

type IEventPublisher<'TEvent> =
  abstract Publish: Event<'TEvent> -> Async<unit>
type IEventStore<'TEvent> =
  abstract Save: Event<'TEvent> seq -> Async<unit>
  abstract Get: id:Guid -> Event<'TEvent> seq option

/// a command handler takes a command returns unit on success and returns an error on failure
type ICommandHandler<'TCommand, 'TError> =
  abstract Handle: Command<'TCommand> -> Async<Result<unit,'TError>>

/// a query handler takes a query returns the result of the query on success and returns an error on failure 
type IQueryHandler<'TQuery, 'TQueryResult when 'TQuery :> IQuery<'TQueryResult>> =
  abstract Handle: 'TQuery -> Async<'TQueryResult option>


type WithIdAndVersion<'T> = {
  Id: Guid
  Version: int
  Instance:'T
}

module Event =
  let create (timestamp: DateTimeOffset) (item: WithIdAndVersion<_>) t =
    { EntityId = item.Id
      EntityVersion = item.Version
      EventTimeStamp = timestamp
      EventData = t }
      
type ISession<'TEvent> =
  abstract Put : aggregate:WithIdAndVersion<'T> * events:'TEvent seq->Async<unit>
  //abstract Add : aggregate:'T * events:'TEvent seq->Async<unit>

  abstract Get : aggregateId:Guid * expectedVersion:int option -> Async<WithIdAndVersion<'T> option>
  abstract Commit: unit -> Async<unit>

(*type IRepository=
  abstract Save<'T when 'T :> IAggregateRoot> : aggregate:'T * expectedVersion:int option -> Async<unit>
  abstract Get<'T when 'T :> IAggregateRoot> : aggregateId:Guid -> Async<'T option>

type Session(repo:IRepository)=
  interface ISession with
    member __.Put(aggregate,events)=async.Return ()
    member __.Get(aggregate,events)=async.Return ()
    *)
[<AutoOpen>]
module Session=
  type ISession<'TEvent> with
    member self.Add (id:Guid, instance:'T, events:'TEvent seq):Async<unit> = async{
      let aggregate = { Id=id; Version=0; Instance=instance }
      do! self.Put (aggregate,events)    
    }

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

