(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module private FIO.Tests

open FIO.DSL
open FIO.Runtime

open FsCheck
open FsCheck.FSharp

type RuntimeGenerator =
    static member Runtime () : Arbitrary<FRuntime> =
        Arb.fromGen <| Gen.oneof [
            Gen.constant <| Direct.Runtime()
            Gen.constant <| Cooperative.Runtime()
            Gen.constant <| Concurrent.Runtime()
        ]

let forAllRuntimes (property: FRuntime -> 'a -> bool) =
    Prop.forAll (RuntimeGenerator.Runtime()) property
        
let result (fiber: Fiber<'R, 'E>) =
    match fiber.AwaitAsync().Result with
    | Ok res -> res
    | Error _ -> failwith "Error happened when result was expected!"
    
let error (fiber: Fiber<'R, 'E>) =
    match fiber.AwaitAsync().Result with
    | Ok _ -> failwith "Result happened when error was expected!"
    | Error err -> err
    
let ``Functor identity for result`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let eff = FIO.Succeed res
        
        let lhs = eff.Map id
        let rhs = eff
        
        let lhs' = result <| runtime.Run lhs
        let rhs' = result <| runtime.Run rhs
        lhs' = rhs'

let ``Functor identity for error`` =
    forAllRuntimes <| fun runtime (err: int) ->
        let eff = FIO.Fail err
        
        let lhs = eff.Map id
        let rhs = eff
        
        let lhs' = error <| runtime.Run lhs
        let rhs' = error <| runtime.Run rhs
        lhs' = rhs'

let ``Functor composition for success`` =
    forAllRuntimes <| fun (runtime: FRuntime) (res: int) ->
        let eff = FIO.Succeed res
        let f x = x + 1
        let g x = x - 2
        
        let lhs = (eff.Map f).Map g
        let rhs = (eff.Map g).Map f
        
        let lhs' = result <| runtime.Run lhs
        let rhs' = result <| runtime.Run rhs
        lhs' = rhs'
        
let ``Functor composition for error`` =
    forAllRuntimes <| fun (runtime: FRuntime) (err: int) ->
        let eff = FIO.Fail err
        let f x = x + 1
        let g x = x - 2
        
        let lhs = (eff.MapError f).MapError g
        let rhs = (eff.MapError g).MapError f
        
        let lhs' = error <| runtime.Run lhs
        let rhs' = error <| runtime.Run rhs
        lhs' = rhs'

let ``Applicative identity for success`` =
    forAllRuntimes <| fun (runtime: FRuntime) (res: int) ->
        let eff = FIO.Succeed res
        
        let lhs = eff.Apply <| FIO.Succeed id
        let rhs = eff
        
        let lhs' = result <| runtime.Run lhs
        let rhs' = result <| runtime.Run rhs
        lhs' = rhs'
        
let ``Applicative identity for error`` =
    forAllRuntimes <| fun (runtime: FRuntime) (err: int) ->
        let eff = FIO.Fail err
        
        let lhs = eff.ApplyError <| FIO.Fail id
        let rhs = eff
        
        let lhs' = error <| runtime.Run lhs
        let rhs' = error <| runtime.Run rhs
        lhs' = rhs'

let ``Applicative homomorphism for success`` (res: int) =
    forAllRuntimes <| fun (runtime: FRuntime) (f: int -> int) ->
        let eff = FIO.Succeed res
        let effF = FIO.Succeed f
        
        let lhs = eff.Apply effF
        let rhs = FIO.Succeed <| f res
        
        let lhs' = result <| runtime.Run lhs
        let rhs' = result <| runtime.Run rhs
        lhs' = rhs'
            
let ``Applicative homomorphism for error`` (err: int) =
    forAllRuntimes <| fun (runtime: FRuntime) (f: int -> int) ->
        let eff = FIO.Fail err
        let effF = FIO.Fail f
        
        let lhs = eff.ApplyError effF
        let rhs = FIO.Fail <| f err
        
        let lhs' = error <| runtime.Run lhs
        let rhs' = error <| runtime.Run rhs
        lhs' = rhs'
    
let ``Applicative composition for success`` (f: int -> int) (g: int -> int) =
    let compose (f: int -> int) (g: int -> int) (x: int) : int =
        f (g x)
    forAllRuntimes <| fun (runtime: FRuntime) (res: int) ->
        let ff = FIO.Succeed f
        let gg = FIO.Succeed g
        let eff = FIO.Succeed res
        
        let lhs = eff.Apply(ff.Apply(gg.Apply(FIO.Succeed compose)))
        let rhs = eff.Apply(ff).Apply gg
        
        let lhs' = result <| runtime.Run lhs
        let rhs' = result <| runtime.Run rhs
        lhs' = rhs'

let ``Applicative composition for error`` (f: int -> int) (g: int -> int) =
    let compose (f: int -> int) (g: int -> int) (x: int) : int =
        f (g x)
    forAllRuntimes <| fun (runtime: FRuntime) (err: int) ->
        let ff = FIO.Fail f
        let gg = FIO.Fail g
        let eff = FIO.Fail err
        
        let lhs = eff.ApplyError(ff.ApplyError(gg.ApplyError(FIO.Fail compose)))
        let rhs = eff.ApplyError(ff).ApplyError gg
        
        let lhs' = error <| runtime.Run lhs
        let rhs' = error <| runtime.Run rhs
        lhs' = rhs'

let ``Bind identity for success`` =
    forAllRuntimes <| fun (runtime: FRuntime) (res: int) ->
        let f = FIO.Succeed
        let lhs = (FIO.Succeed res).Bind f
        let rhs = f res
        
        let lhs' = result <| runtime.Run lhs
        let rhs' = result <| runtime.Run rhs
        lhs' = rhs'

let ``Bind identity for error`` =
    forAllRuntimes <| fun (runtime: FRuntime) (err: int) ->
        let f = FIO.Fail
        let lhs = (FIO.Fail err).BindError f
        let rhs = f err
        
        let lhs' = error <| runtime.Run lhs
        let rhs' = error <| runtime.Run rhs
        lhs' = rhs'

let ``Bind associativity for success`` =
    forAllRuntimes <| fun (runtime: FRuntime) (res: int) ->
        let eff = FIO.Succeed res
        let f = FIO.Succeed
        let g = FIO.Succeed
        
        let lhs = (eff.Bind f).Bind g
        let rhs = (eff.Bind (fun x -> (f x).Bind g))
        
        let lhs' = result <| runtime.Run lhs
        let rhs' = result <| runtime.Run rhs
        lhs' = rhs'
        
let ``Bind associativity for error`` =
    forAllRuntimes <| fun (runtime: FRuntime) (err: int) ->
        let eff = FIO.Fail err
        let f = FIO.Fail
        let g = FIO.Fail
        
        let lhs = (eff.BindError f).BindError g
        let rhs = (eff.BindError (fun x -> (f x).BindError g))
        
        let lhs' = error <| runtime.Run lhs
        let rhs' = error <| runtime.Run rhs
        lhs' = rhs'

Check.Quick ``Functor identity for result``
Check.Quick ``Functor identity for error``
Check.Quick ``Functor composition for success``
Check.Quick ``Functor composition for error``

Check.Quick ``Applicative identity for success``
Check.Quick ``Applicative identity for error``
Check.Quick ``Applicative homomorphism for success``
Check.Quick ``Applicative homomorphism for error``
Check.Quick ``Applicative composition for success``
Check.Quick ``Applicative composition for error``

Check.Quick ``Bind identity for success``
Check.Quick ``Bind identity for error``
Check.Quick ``Bind associativity for success``
Check.Quick ``Bind associativity for error``
