module Tests.WhenItemCreated

open System
open CQRSLite.Core.Domain.ReadModel
open CQRSLite.Core.Domain.WriteModel
open Xunit
open Helpers

let publishedEvents = Var []

module given =
  let id = Guid.Parse "baf0556d-891d-4e0e-a33a-4353ddb4b074"

  let session =
    { new ISession with
        member __.Put (aggregate, events) =
          Var.set (Seq.toList events) publishedEvents
          async.Return ()

        member __.Get (id, version) = async { return None }
        member __.Commit () = async.Return () }

type Test () =

  let ``when`` = CreateInventoryItem { Name = "myname" }
  let handler: ICommandHandler = upcast InventoryCommandHandlers (given.session)

  do
    let result =
      handler.Handle
        { Id = Guid.Empty
          ExpectedVersion = 0
          Command = ``when`` }
      |> Async.RunSynchronously

    match result with
    | Ok _ -> ()
    | Error e -> failwithf "%A" e


  [<Fact>]
  member __.``Should create one event`` () =
    Assert.Equal (1, List.length (Var.read publishedEvents))

  [<Fact>]
  member __.``Should create correct event and have correct name`` () =
    Assert.Equal (InventoryItemCreated { Name = "myname" }, List.head (Var.read publishedEvents))
