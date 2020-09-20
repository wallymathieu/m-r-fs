namespace Core
open System
type IMessage=interface end

type IQuery<'Result>=inherit IMessage 
type ICommand=inherit IMessage

type IHandler<'TMessage when 'TMessage :> IMessage>=
  abstract Handle: 'TMessage->Async<unit>

type IEvent =
  inherit IMessage
  abstract Id:Guid
  abstract Version:int
  abstract TimeStamp: DateTimeOffset

type IEventHandler<'TEvent when 'TEvent :> IEvent>=
  inherit IHandler<'TEvent>

type IEventPublisher=
  abstract Publish<'TEvent when 'TEvent:> IEvent> : 'TEvent->Async<unit>

type IEventStore=
  abstract Save: IEvent seq -> Async<unit>
  abstract Get: id:Guid * fromVersion:(int option) -> IEvent seq

type ICommandHandler<'TCommand when 'TCommand :> ICommand>=
  inherit IHandler<'TCommand>



type IAggregateRoot=
  abstract Id:Guid
  abstract Version: int
  //abstract ApplyChange: event: IEvent -> unit

type IRepository=
  abstract Save<'T when 'T :> IAggregateRoot> : aggregate:'T * expectedVersion:int option -> Async<unit>
  abstract Get<'T when 'T :> IAggregateRoot> : aggregateId:Guid -> Async<'T option>

type ISession=
  abstract Put<'T,'TEvent when 'T :> IAggregateRoot and 'TEvent :> IEvent> : aggregate:'T * events: 'TEvent seq -> Async<unit>
  abstract Get<'T when 'T :> IAggregateRoot> : aggregateId:Guid * expectedVersion:int option -> Async<'T option>
  abstract Commit: unit -> Async<unit>


type WithName={
  Name: string
}
type WithNewName={
  NewName: string
}
type WithCount={
  Count: int
}

module ReadModel=
  type InventoryItemDetailsDto={
    Id:Guid
    Name:string
    CurrentCount:int
    Version:int
  }
  type InventoryItemListDto={
    Id:Guid
    Name:string
  }


  type GetInventoryItemDetails={
   Id:Guid 
  }
  with 
    interface IQuery<InventoryItemDetailsDto>

  type GetInventoryItems=struct end
  with 
    interface IQuery<List<InventoryItemListDto>>


  type EventsT=
    | InventoryItemCreated of WithName
    | InventoryItemDeactivated
    | InventoryItemRenamed of WithNewName
    | ItemsCheckedInToInventory of WithCount
    | ItemsRemovedFromInventory of WithCount
  type Event={
    Id: Guid
    Version: int
    TimeStamp: DateTimeOffset
    T: EventsT
  }
  with
    interface IEvent with
      member e.Id=e.Id
      member e.Version=e.Version
      member e.TimeStamp=e.TimeStamp
  module Event=

    let create (timestamp:DateTimeOffset) (item:#IAggregateRoot) (t:EventsT)={
      Id=item.Id
      Version=item.Version
      TimeStamp=timestamp
      T=t
    }
  open System.Collections.Generic
  type InMemoryDatabase={
    Details: Dictionary<Guid, InventoryItemDetailsDto> 
    List: ResizeArray<InventoryItemListDto> 
  }
  with
    member db.TryGetDetail id=
      match db.Details.TryGetValue (id) with
      | true,item -> Some item
      | _ -> None
    member db.GetDetail id=
      match db.TryGetDetail id with
      | Some item -> item
      | _ -> raise <| InvalidOperationException "did not find the original inventory this shouldnt happen"

  type InventoryItemDetailView(db:InMemoryDatabase)=
    interface IEventHandler<Event> with
      member __.Handle(message)=async{
        match message.T with
          | InventoryItemCreated p->
            db.Details.Add(message.Id, {Id=message.Id; Name= p.Name; CurrentCount= 0; Version=message.Version})
          | InventoryItemRenamed p->
            let item= db.GetDetail message.Id 
            db.Details.[message.Id] <- { item with Name= p.NewName; Version=message.Version }
          | ItemsRemovedFromInventory p->
            let item= db.GetDetail message.Id 
            db.Details.[message.Id] <- { item with CurrentCount= item.CurrentCount - p.Count; Version=message.Version }
          | ItemsCheckedInToInventory p->
            let item= db.GetDetail message.Id 
            db.Details.[message.Id] <- { item with CurrentCount= item.CurrentCount + p.Count; Version=message.Version }
          | InventoryItemDeactivated ->
            db.Details.Remove message.Id |> ignore
      }
    member __.Handle(message:GetInventoryItemDetails) : Async<InventoryItemDetailsDto option>=async{
      return db.TryGetDetail message.Id
    }
  type InventoryListView(db:InMemoryDatabase)=
    interface IEventHandler<Event> with
      member __.Handle(message)=async{
        match message.T with
          | InventoryItemCreated p->
            db.List.Add({Id=message.Id; Name= p.Name;})
          | InventoryItemRenamed p->
            let item = db.List.Find( fun x -> x.Id = message.Id)
            db.List.RemoveAll (fun x -> x.Id = message.Id) |> ignore
            db.List.Add ({ item with Name=p.NewName })
          | ItemsRemovedFromInventory _
          | ItemsCheckedInToInventory _ ->
            ()
          | InventoryItemDeactivated ->
            db.List.RemoveAll (fun x -> x.Id = message.Id) |> ignore
      }
    member __.Handle(_:GetInventoryItems) : Async<InventoryItemListDto list>=async{
      return db.List |> Seq.toList
    }

module WriteModel=
  open ReadModel
  type CommandT=
    | CheckInItemsToInventory of WithCount
    | CreateInventoryItem of WithName
    | DeactivateInventoryItem
    | RemoveItemsFromInventory of WithCount
    | RenameInventoryItem of WithNewName
  type Command={
    Id: Guid
    ExpectedVersion: int 
    T:CommandT
  }
  with interface ICommand

  type InventoryItem={
    Id:Guid
    Version:int
    Activated:bool
  }
  with 
    interface IAggregateRoot with
      member x.Id=x.Id
      member x.Version=x.Version
    static member Create(id:Guid, name:string)=
      let item : InventoryItem = {Id=id; Activated=false; Version=0}
      let evt = InventoryItemCreated { Name = name }
      item,[evt]
    member this.ChangeName(newName:string)=
      if String.IsNullOrEmpty(newName) then
        raise <| ArgumentException"newName"
      let evt = InventoryItemRenamed { NewName=newName }
      this, [evt]
    member this.Remove(count:int)=
      if count <=0 then
        raise <| InvalidOperationException "cant remove negative count from inventory"
      let evt = ItemsRemovedFromInventory { Count=count }
      this, [evt]
    member this.CheckIn(count:int)=
      if count <=0 then
        raise <| InvalidOperationException "must have a count greater than 0 to add to inventory"
      let evt = ItemsCheckedInToInventory { Count=count }
      this, [evt]
    member this.Deactivate()=
      if not this.Activated then
        raise <| InvalidOperationException "already deactivated"
      let evt = InventoryItemDeactivated
      { this with Activated = false }, [evt]

  type InventoryCommandHandlers(session:ISession, now: unit->DateTimeOffset)=
    let putNextAndEvents applied =
      let timestamp = now()
      let evtWithTime = Event.create timestamp
      let (next,evts)=applied
      let evtC = evtWithTime next
      session.Put (next,List.map evtC evts)

    interface ICommandHandler<Command> with
      member __.Handle (message)=async {
        match message.T with
        | CheckInItemsToInventory p ->
          match! session.Get<InventoryItem>(message.Id,Some message.ExpectedVersion) with
          | Some item->
            do! putNextAndEvents <| item.CheckIn (p.Count) 
            do! session.Commit ()
          | None -> ()
        | CreateInventoryItem p ->
          do! putNextAndEvents <| InventoryItem.Create(message.Id, p.Name)
          do! session.Commit()
        | DeactivateInventoryItem ->
          match! session.Get<InventoryItem>(message.Id,Some message.ExpectedVersion) with
          | Some item->
            do! putNextAndEvents <| item.Deactivate () 
            do! session.Commit ()
          | None -> ()
        | RemoveItemsFromInventory p ->
          match! session.Get<InventoryItem>(message.Id,Some message.ExpectedVersion) with
          | Some item->
            do! putNextAndEvents <| item.Remove (p.Count) 
            do! session.Commit ()
          | None -> ()
        | RenameInventoryItem p ->
          match! session.Get<InventoryItem>(message.Id,Some message.ExpectedVersion) with
          | Some item->
            do! putNextAndEvents <| item.ChangeName (p.NewName)
            do! session.Commit ()
          | None -> ()
      }
