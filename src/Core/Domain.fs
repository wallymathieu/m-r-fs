namespace Core

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
    { Id: Guid
      Version: int
      TimeStamp: DateTimeOffset
      T: EventsT }
    interface IEvent with
      member e.Id = e.Id
      member e.Version = e.Version
      member e.TimeStamp = e.TimeStamp

  module Event =

    let create (timestamp: DateTimeOffset) (item: #IAggregateRoot) (t: EventsT) =
      { Id = item.Id
        Version = item.Version
        TimeStamp = timestamp
        T = t }

  open System.Collections.Generic

  type InMemoryDatabase =
    { Details: Dictionary<Guid, InventoryItemDetailsDto>
      List: ResizeArray<InventoryItemListDto> }
    member db.TryGetDetail id =
      match db.Details.TryGetValue (id) with
      | true, item -> Some item
      | _ -> None

    member db.GetDetail id =
      match db.TryGetDetail id with
      | Some item -> item
      | _ ->
          raise
          <| InvalidOperationException "did not find the original inventory this shouldnt happen"

  type InventoryItemDetailView (db: InMemoryDatabase) =
    interface IEventHandler<Event, unit> with
      member __.Handle (message) =
        async {
          match message.T with
          | InventoryItemCreated p ->
              db.Details.Add
                (message.Id,
                 { Id = message.Id
                   Name = p.Name
                   CurrentCount = 0
                   Version = message.Version })
          | InventoryItemRenamed p ->
              let item = db.GetDetail message.Id
              db.Details.[message.Id] <- { item with
                                             Name = p.NewName
                                             Version = message.Version }
          | ItemsRemovedFromInventory p ->
              let item = db.GetDetail message.Id
              db.Details.[message.Id] <- { item with
                                             CurrentCount = item.CurrentCount - p.Count
                                             Version = message.Version }
          | ItemsCheckedInToInventory p ->
              let item = db.GetDetail message.Id
              db.Details.[message.Id] <- { item with
                                             CurrentCount = item.CurrentCount + p.Count
                                             Version = message.Version }
          | InventoryItemDeactivated -> db.Details.Remove message.Id |> ignore
          return Ok ()
        }

    member __.Handle (message: GetInventoryItemDetails): Async<InventoryItemDetailsDto option> =
      async { return db.TryGetDetail message.Id }

  type InventoryListView (db: InMemoryDatabase) =
    interface IEventHandler<Event, unit> with
      member __.Handle (message) =
        async {
          match message.T with
          | InventoryItemCreated p -> db.List.Add ({ Id = message.Id; Name = p.Name })
          | InventoryItemRenamed p ->
              let item = db.List.Find (fun x -> x.Id = message.Id)
              db.List.RemoveAll (fun x -> x.Id = message.Id)
              |> ignore
              db.List.Add ({ item with Name = p.NewName })
          | ItemsRemovedFromInventory _
          | ItemsCheckedInToInventory _ -> ()
          | InventoryItemDeactivated ->
              db.List.RemoveAll (fun x -> x.Id = message.Id)
              |> ignore
          return Ok ()
        }

    member __.Handle (_: GetInventoryItems): Async<InventoryItemListDto list> = async { return db.List |> Seq.toList }

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

    interface ICommandHandler<Command, ErrorT> with
      member __.Handle (message) = handle message
