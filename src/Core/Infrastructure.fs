namespace Core

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
  abstract Id: Guid
  abstract Version: int
  abstract TimeStamp: DateTimeOffset

/// An event handler is supposed to handle events
type IEventHandler<'TEvent, 'TError when 'TEvent :> IEvent> =
  inherit IHandler<'TEvent, unit, 'TError>

/// An event listener is supposed to a passive listener for events
type IEventListener<'TEvent when 'TEvent :> IEvent> =
  inherit IHandler<'TEvent, unit, unit>


type IEventPublisher =
  abstract Publish<'TEvent when 'TEvent :> IEvent> : 'TEvent -> Async<unit>

type IEventStore =
  abstract Save: IEvent seq -> Async<unit>
  abstract Get: id:Guid * fromVersion:(int option) -> IEvent seq

/// a command handler takes a command returns unit on success and returns an error on failure
type ICommandHandler<'TCommand, 'TError when 'TCommand :> ICommand> =
  inherit IHandler<'TCommand, unit, 'TError>

/// a query handler takes a query returns the result of the query on success and returns an error on failure 
type IQueryHandler<'TQuery, 'TQueryResult, 'TError when 'TQuery :> IQuery<'TQueryResult>> =
  inherit IHandler<'TQuery, 'TQueryResult, 'TError>


type IAggregateRoot =
  abstract Id: Guid
  abstract Version: int

type ISession =
  abstract Put<'T, 'TEvent when 'T :> IAggregateRoot and 'TEvent :> IEvent> : aggregate:'T * events:'TEvent seq->Async<unit>

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

#if DEBUG
module TryingThingsOut=
  let s = { new ISession with
      member __.Put(aggregate,events)=async.Return ()
      member __.Get(aggregate,events)=async.Return None
      member __.Commit() = async.Return()
  }
  let ascript = AsyncResultBuilder()
  type AFakeType={ Name:string; Id:Guid; Version:int }
  with
    interface IAggregateRoot with
      member self.Id = self.Id
      member self.Version = self.Version

  let do2_ : AsyncResult<unit,unit> = ascript {
    let! something  = lift <| s.Get<AFakeType> (Guid.Empty, Some 1)
    match something with
    | Some v ->
      let a= v.Name
      do! lift <| s.Put (v,[])
      return ()
    | None ->
      // perhaps this is an OK case?
      return! AsyncResult.error ()
  }
#endif