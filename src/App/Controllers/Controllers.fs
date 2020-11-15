[<AutoOpen>]
module App.Controllers.Controllers
open System
open CQRSLite.Core.Domain.ReadModel
open CQRSLite.Core.Domain.WriteModel
open FSharpPlus
open Microsoft.AspNetCore.Mvc

type ControllerBase with
    /// interpret query result as Http response (i.e. IActionResult)
    member self.InterpretQueryResult<'TQueryResult> (qr:'TQueryResult option) : IActionResult = match qr with | Some v-> upcast ( self.Ok v ) | None -> upcast ( self.NotFound() )
    /// we want to bind the query result into an interpretation of that result  (.i.e Async<IActionResult>) and change Async to Task
    member self.BindReturnQueryInterpretationToTask value = value >>= (self.InterpretQueryResult >> async.Return) |> Async.StartAsTask // note the ceremony
    /// interpret command result as Http response (i.e. IActionResult)
    member self.InterpretCommandResult<'T> (v:Result<unit,ErrorT>) : IActionResult=
      match v with
      | Ok ()-> upcast (self.Ok ())
      | Error err ->
        match err with
        | MissingItem -> upcast (self.NotFound())
        | MissingName -> upcast (self.BadRequest("Missing name"))
        | CantRemoveNegativeCountFromInventory -> upcast (self.BadRequest("Cant remove negative count from"))
        | MustHaveACountGreaterThan0ToAddToInventory ->
          upcast (self.BadRequest("Must have a count greater than 0 to add to inventory"))
        | AlreadyDeactivated -> upcast (self.BadRequest("Missing name"))
    /// we want to bind the command result into an interpretation of that result (.i.e Async<IActionResult>) and change Async to Task
    member self.BindReturnCommandResult value= value >>= (self.InterpretCommandResult >> async.Return) |> Async.StartAsTask // note the ceremony         
