module Tests.Helpers
open System

let fakeTime () = DateTimeOffset.FromUnixTimeSeconds 1000000000L

open System.Runtime.CompilerServices
open System.Threading
/// Represents a variable.
type Var<'a when 'a: not struct>(initialValue: 'a) =
    let mutable value: 'a = initialValue
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
    member __.Value () = Volatile.Read(&value)
    
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
    member __.Swap (nval: 'a): 'a = Interlocked.Exchange(&value, nval)
module Var=
  let set value (m:Var<_>)=m.Swap value |> ignore
  let read (m:Var<_>)=m.Value ()