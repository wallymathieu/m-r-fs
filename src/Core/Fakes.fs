module CQRSLite.Core.Fakes
open System
open FSharpPlus
open CQRSLite.Core.Infrastructure

type Session<'TEvent>(eventStore:IEventStore<'TEvent>, now: unit -> DateTimeOffset)=
  let current = Collections.Generic.Dictionary<Guid,WithIdAndVersion<obj>>()
  let versions = Collections.Generic.Dictionary<Guid*int,WithIdAndVersion<obj>>()
  let boxAggregate aggregate = { Id=aggregate.Id;Version=aggregate.Version; Instance = box aggregate }
  let unBoxAggregate (boxed:WithIdAndVersion<obj>) = { Id=boxed.Id;Version=boxed.Version; Instance = unbox boxed.Instance }
  let put (aggregate,events)=async{
    let timestamp = now ()
    let toFullEvent = Event.create timestamp aggregate
    let events =Seq.map toFullEvent events
    do! eventStore.Save events
    let boxed = boxAggregate aggregate
    versions.[(aggregate.Id,aggregate.Version)] <- boxed 
    current.[aggregate.Id] <- boxed
  }
  interface ISession<'TEvent> with
      member __.Put(aggregate,events)=put (aggregate,events)
      
      member __.Get<'T>(id,maybeVersion) : Async<WithIdAndVersion<'T> option> =async{
        match maybeVersion with
        | None ->
          return Dict.tryGetValue id current |> Option.map unBoxAggregate
        | Some version ->
          return Dict.tryGetValue (id,version) versions |> Option.map unBoxAggregate
      }
      member __.Commit() = async.Return()
type ConcurrencyException()=
  inherit Exception()
type EventStore<'TEvent>(publisher:IEventPublisher<'TEvent>)=
  let current = Collections.Generic.Dictionary<Guid,ResizeArray<Event<'TEvent>>>()
  let saveEvent (event:Event<'TEvent>)=async{
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
    
type EventPublisher<'TEvent>(listeners : IEventListener<'TEvent> seq)=
  interface IEventPublisher<'TEvent> with
    member __.Publish(event)=async{
      for listener in listeners do
        do! listener.Handle event
    }