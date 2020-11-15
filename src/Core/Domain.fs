namespace CQRSLite.Core.Domain
open CQRSLite.Core.Infrastructure
open System
open FSharpPlus
open FSharpPlus.Data

type WithName = { Name: string }
type WithNewName = { NewName: string }
type WithCount = { Count: int }

module ReadModel =
  type InventoryItemDetailsDto =
    { Id: Guid
      Name: string
      CurrentCount: int
      Version: int }

  type InventoryItemListDto = { Id: Guid; Name: string }


  type GetInventoryItemDetails =
    { Id: Guid }
    interface IQuery<InventoryItemDetailsDto>

  type GetInventoryItems =
    struct
    end

    interface IQuery<List<InventoryItemListDto>>


  type EventsT =
    | InventoryItemCreated of WithName
    | InventoryItemDeactivated
    | InventoryItemRenamed of WithNewName
    | ItemsCheckedInToInventory of WithCount
    | ItemsRemovedFromInventory of WithCount

  type Event =
    { EntityId: Guid
      EntityVersion: int
      EventTimeStamp: DateTimeOffset
      T: EventsT }
    interface IEvent with
      member e.EntityId = e.EntityId
      member e.EntityVersion = e.EntityVersion
      member e.EventTimeStamp = e.EventTimeStamp

  module Event =

    let create (timestamp: DateTimeOffset) (item: #IAggregateRoot) (t: EventsT) =
      { EntityId = item.Id
        EntityVersion = item.Version
        EventTimeStamp = timestamp
        T = t }

  open System.Collections.Generic
  let raiseCouldNotFindOriginalInventory ()=
    raise <| InvalidOperationException "did not find the original inventory (this shouldnt happen)"
  type InMemoryDatabase =
    { Details: Dictionary<Guid, InventoryItemDetailsDto>
      List: ResizeArray<InventoryItemListDto> }
    member db.TryGetDetail id =
      Dict.tryGetValue id db.Details 

    member db.GetDetail id =
      Dict.tryGetValue id db.Details 
      |> Option.defaultWith raiseCouldNotFindOriginalInventory
  
  type InventoryItemDetailView (db: InMemoryDatabase) =
    interface IEventListener<Event> with
      member __.Handle (message) =
        async {
          match message.T with
          | InventoryItemCreated p ->
              db.Details.Add
                (message.EntityId,
                 { Id = message.EntityId
                   Name = p.Name
                   CurrentCount = 0
                   Version = message.EntityVersion })
          | InventoryItemRenamed p ->
              let item = db.GetDetail message.EntityId
              db.Details.[message.EntityId] <- { item with
                                                  Name = p.NewName
                                                  Version = message.EntityVersion }
          | ItemsRemovedFromInventory p ->
              let item = db.GetDetail message.EntityId
              db.Details.[message.EntityId] <- { item with
                                                  CurrentCount = item.CurrentCount - p.Count
                                                  Version = message.EntityVersion }
          | ItemsCheckedInToInventory p ->
              let item = db.GetDetail message.EntityId
              db.Details.[message.EntityId] <- { item with
                                                  CurrentCount = item.CurrentCount + p.Count
                                                  Version = message.EntityVersion }
          | InventoryItemDeactivated -> db.Details.Remove message.EntityId |> ignore

        }
    interface IQueryHandler<GetInventoryItemDetails,InventoryItemDetailsDto> with
      member __.Handle (message: GetInventoryItemDetails): Async<InventoryItemDetailsDto option> =
        async { return db.TryGetDetail message.Id }

  type InventoryListView (db: InMemoryDatabase) =
    interface IEventListener<Event> with
      member __.Handle (message) =
        async {
          match message.T with
          | InventoryItemCreated p -> db.List.Add ({ Id = message.EntityId; Name = p.Name })
          | InventoryItemRenamed p ->
              let item = db.List.Find (fun x -> x.Id = message.EntityId)
              db.List.RemoveAll (fun x -> x.Id = message.EntityId)
              |> ignore
              db.List.Add ({ item with Name = p.NewName })
          | ItemsRemovedFromInventory _
          | ItemsCheckedInToInventory _ -> ()
          | InventoryItemDeactivated ->
              db.List.RemoveAll (fun x -> x.Id = message.EntityId)
              |> ignore
        }
    interface IQueryHandler<GetInventoryItems,InventoryItemListDto list> with
      member __.Handle (_: GetInventoryItems): Async<Option<InventoryItemListDto list>> = async { return db.List |> Seq.toList |> Some }

module WriteModel =
  open ReadModel

  type CommandT =
    | CheckInItemsToInventory of WithCount
    | CreateInventoryItem of WithName
    | DeactivateInventoryItem
    | RemoveItemsFromInventory of WithCount
    | RenameInventoryItem of WithNewName

  type Command =
    { Id: Guid
      ExpectedVersion: int
      T: CommandT }
    interface ICommand

  type ErrorT =
    | MissingItem
    | MissingName
    | CantRemoveNegativeCountFromInventory
    | MustHaveACountGreaterThan0ToAddToInventory
    | AlreadyDeactivated
  /// ICommandHandler for the domain (i.e. known types of commands and errors)
  type ICommandHandler = ICommandHandler<Command,ErrorT>
  type ISession = ISession<Event>
  type InventoryItem =
    { Id: Guid
      Version: int
      Activated: bool }
    interface IAggregateRoot with
      member x.Id = x.Id
      member x.Version = x.Version

    static member Create (id: Guid, name: string) =
      let item: InventoryItem = { Id = id; Activated = false; Version = 0 }
      item, [ InventoryItemCreated { Name = name } ]

    member this.ChangeName (newName: string) =
      if String.IsNullOrEmpty (newName) then
        Error MissingName
      else
        Ok (this, [ InventoryItemRenamed { NewName = newName } ])

    member this.Remove (count: int) =
      if count <= 0 then
        Error CantRemoveNegativeCountFromInventory
      else
        Ok (this, [ ItemsRemovedFromInventory { Count = count } ])

    member this.CheckIn (count: int) =
      if count <= 0 then
        Error MustHaveACountGreaterThan0ToAddToInventory
      else
        Ok (this, [ ItemsCheckedInToInventory { Count = count } ])

    member this.Deactivate () =
      if not this.Activated then
        Error AlreadyDeactivated
      else
        Ok ({ this with Activated = false }, [ InventoryItemDeactivated ])
  type InventoryCommandHandlers (session: ISession, now: unit -> DateTimeOffset) =
    let sessionPutNextAndEvents applied =
      let timestamp = now ()
      let (next, evts) = applied
      let toFullEvent = Event.create timestamp next
      session.Put (next, List.map toFullEvent evts)

    let handle (message: Command) =
      /// try to fetch item from session
      /// the "apply" use the item to yield the next entity value and events
      let sessionItemOver apply =
        async {
          match! session.Get<InventoryItem> (message.Id, Some message.ExpectedVersion) with
          | Some (item: InventoryItem) ->
              match apply item with
              | Ok next_evts ->
                  do! sessionPutNextAndEvents next_evts
                  do! session.Commit ()
                  return Ok ()
              | Error e -> return Error e
          | None -> return Error MissingItem
        }

      async {
        match message.T with
        | CheckInItemsToInventory p -> return! sessionItemOver (fun item -> item.CheckIn (p.Count))
        | CreateInventoryItem p ->
            do! sessionPutNextAndEvents (InventoryItem.Create (message.Id, p.Name))
            do! session.Commit ()
            return Ok ()
        | DeactivateInventoryItem -> return! sessionItemOver (fun item -> item.Deactivate ())
        | RemoveItemsFromInventory p -> return! sessionItemOver (fun item -> item.Remove (p.Count))
        | RenameInventoryItem p -> return! sessionItemOver (fun item -> item.ChangeName (p.NewName))
      }

    interface ICommandHandler with
      member __.Handle (message) = handle message
