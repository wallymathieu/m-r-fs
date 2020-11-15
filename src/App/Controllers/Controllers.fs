[<AutoOpen>]
module App.Controllers.Controllers
open System
open CQRSLite.Core.Domain.ReadModel
open CQRSLite.Core.Domain.WriteModel
open FSharpPlus
open Microsoft.AspNetCore.Mvc
type AR = IActionResult

type ControllerBase with
    /// interpret query result as Http response (i.e. IActionResult)
    member self.InterpretQueryResult<'TQueryResult> (qr:Result<'TQueryResult,unit>) = match qr with | Ok v-> self.Ok v :> AR | Error () -> self.NotFound() :> AR
    /// we want to bind the query result into an interpretation of that result  (.i.e Async<IActionResult>) and change Async to Task
    member self.BindReturnQueryInterpretationToTask value = value >>= (self.InterpretQueryResult >> async.Return) |> Async.StartAsTask // note the ceremony
    /// interpret command result as Http response (i.e. IActionResult)
    member self.InterpretCommandResult<'T> (v:Result<unit,ErrorT>) : IActionResult=
      match v with
      | Ok ()-> self.Ok () :> AR
      | Error err ->
        match err with
        | MissingItem -> self.NotFound() :> AR
        | MissingName -> self.BadRequest("Missing name") :> AR
        | CantRemoveNegativeCountFromInventory -> self.BadRequest("Cant remove negative count from") :> AR
        | MustHaveACountGreaterThan0ToAddToInventory ->
          self.BadRequest("Must have a count greater than 0 to add to inventory") :> AR
        | AlreadyDeactivated -> self.BadRequest("Missing name") :> AR
    /// we want to bind the command result into an interpretation of that result (.i.e Async<IActionResult>) and change Async to Task
    member self.BindReturnCommandResult value= value >>= (self.InterpretCommandResult >> async.Return) |> Async.StartAsTask // note the ceremony         
