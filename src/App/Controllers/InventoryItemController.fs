namespace App.Controllers

open System
open System.Collections.Generic
open System.Linq
open System.Threading.Tasks
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Logging

[<ApiController>]
[<Route("[controller]")>]
type InventoryItemController (logger : ILogger<InventoryItemController>) =
    inherit ControllerBase()
    [<HttpGet>]
    member _.Get() =
        [|
          
        |]
