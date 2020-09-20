namespace Core

open System

type IMessage  =
  interface
  end

type IQuery<'Result> =
  inherit IMessage

type ICommand =
  inherit IMessage

type IHandler<'TMessage, 'TError when 'TMessage :> IMessage> =
  abstract Handle: 'TMessage -> Async<'TError>

type IEvent =
  inherit IMessage
  abstract Id: Guid
  abstract Version: int
  abstract TimeStamp: DateTimeOffset

type IEventHandler<'TEvent, 'TError when 'TEvent :> IEvent> =
  inherit IHandler<'TEvent, 'TError>

type IEventPublisher =
  abstract Publish<'TEvent when 'TEvent :> IEvent> : 'TEvent -> Async<unit>

type IEventStore =
  abstract Save: IEvent seq -> Async<unit>
  abstract Get: id:Guid * fromVersion:(int option) -> IEvent seq

type ICommandHandler<'TCommand, 'TError when 'TCommand :> ICommand> =
  inherit IHandler<'TCommand, 'TError>

type IAggregateRoot =
  abstract Id: Guid
  abstract Version: int

type ISession =
  abstract Put<'T, 'TEvent when 'T :> IAggregateRoot and 'TEvent :> IEvent> : aggregate:'T * events:'TEvent seq->Async<unit>

  abstract Get<'T when 'T :> IAggregateRoot> : aggregateId:Guid * expectedVersion:int option -> Async<'T option>
  abstract Commit: unit -> Async<unit>
