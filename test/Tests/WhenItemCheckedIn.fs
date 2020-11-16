module Tests.WhenItemCheckedIn

open System
open CQRSLite.Core.Domain.ReadModel
open CQRSLite.Core.Domain.WriteModel
open Xunit
open Tests.Helpers

let publishedEvents = Var []

module Given =
  let id = Guid.Parse "baf0556d-891d-4e0e-a33a-4353ddb4b074"
  let instance = { Activated = true }

  let session =
    { new ISession with
        member __.Put (aggregate, events) =
          Var.set (Seq.toList events) publishedEvents
          async.Return ()

        member __.Get<'a> (id, version) =
          async {
            return Some
                     { Id = id
                       Version = 2
                       Instance = box instance :?> 'a }
          }

        member __.Commit () = async.Return () }

type Test () =
  let ``when`` = CheckInItemsToInventory { Count = 2 }
  let handler: ICommandHandler = upcast InventoryCommandHandlers (Given.session)

  do
    let result =
      handler.Handle
        { Id = Given.id
          ExpectedVersion = 2
          Command = ``when`` }
      |> Async.RunSynchronously

    match result with
    | Ok _ -> ()
    | Error e -> failwithf "%A" e

  [<Fact>]
  member __.``Should create one event`` () =
    Assert.Equal (1, List.length (Var.read publishedEvents))

  [<Fact>]
  member __.``Should create correct event and have correct number of items`` () =
    Assert.Equal (ItemsCheckedInToInventory { Count = 2 }, List.head (Var.read publishedEvents))
