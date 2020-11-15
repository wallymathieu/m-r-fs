namespace App.Controllers

open System
open CQRSLite.Core.Domain.ReadModel
open CQRSLite.Core.Domain.WriteModel
open FSharpPlus
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Logging
open CQRSLite.Core.Infrastructure

[<ApiController>]
[<Route("[controller]")>]
type InventoryItemController (logger : ILogger<InventoryItemController>,
                              getInventoryItem: IQueryHandler<GetInventoryItemDetails,InventoryItemDetailsDto>,
                              getInventoryItems: IQueryHandler<GetInventoryItems,InventoryItemListDto list>,
                              commandHandler: ICommandHandler)  =
    inherit ControllerBase()
    [<HttpGet>]
    member self.Get() = getInventoryItems.Handle(GetInventoryItems()) |> self.BindReturnQueryInterpretationToTask

    [<HttpGet("{id}")>]
    member self.Get(id:Guid) = getInventoryItem.Handle({Id=id}) |> self.BindReturnQueryInterpretationToTask

    [<HttpPost>]
    member self.Post([<FromBody>] model) =
      commandHandler.Handle({ Id=Guid.NewGuid(); ExpectedVersion=0; T=CreateInventoryItem model })
        |> self.BindReturnCommandResult
    [<HttpPost("{id}/{version}")>]
    member self.Put(id, version,[<FromBody>] model) =
      commandHandler.Handle({ Id=id; ExpectedVersion=version; T=RenameInventoryItem model })
        |> self.BindReturnCommandResult
    [<HttpPost("{id}/{version}/DeActivate")>]
    member self.PostDeactivate(id, version) =
      commandHandler.Handle({ Id=id; ExpectedVersion=version; T=DeactivateInventoryItem })
        |> self.BindReturnCommandResult
    [<HttpPost("{id}/{version}/CheckIn")>]
    member self.PostCheckIn(id, version,[<FromBody>] model) =
      commandHandler.Handle({ Id=id; ExpectedVersion=version; T=CheckInItemsToInventory model })
        |> self.BindReturnCommandResult
    [<HttpDelete("{id}/{version}")>]
    member self.DeleteRemove(id, version,[<FromBody>] model) =
      commandHandler.Handle({ Id=id; ExpectedVersion=version; T=RemoveItemsFromInventory model })
        |> self.BindReturnCommandResult
