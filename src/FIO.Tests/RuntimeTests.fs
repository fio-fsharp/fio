(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module private FIO.Tests.RuntimeTests

open System.Threading.Tasks
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
    
let ``Succeed always succeeds`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let eff = FIO.Succeed res
        
        let actual = result <| runtime.Run eff
        let expected = res

        actual = expected
        
let ``Fail always fails`` =
    forAllRuntimes <| fun runtime (err: int) ->
        let eff = FIO.Fail err
        
        let actual = error <| runtime.Run eff
        let expected = err

        actual = expected
        
let ``FromFunc with onError succeeds when no exception is thrown`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let func () =
            res
            
        let onError exn =
            exn
            
        let eff = FIO.FromFunc<int, exn> (func, onError)
        
        let actual = result <| runtime.Run eff
        let expected = res

        actual = expected
        
let ``FromFunc with onError fails when exception is thrown and converts error`` =
    forAllRuntimes <| fun runtime (res: int, exnMsg: string) ->
        let func () =
            invalidOp exnMsg
            res
            
        let onError (exn: exn) =
            exn.Message
            
        let eff = FIO.FromFunc<int, string> (func, onError)
        
        let actual = error <| runtime.Run eff
        let expected = exnMsg

        actual = expected

let ``FromFunc succeeds when no exception is thrown`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let func () =
            res
        
        let eff = FIO.FromFunc<int, exn> func
        
        let actual = result <| runtime.Run eff
        let expected = res

        actual = expected

// TODO: Rewrite this. A bit funny.
let ``FromFunc fails when exception is thrown`` =
    forAllRuntimes <| fun runtime (res: int, exnMsg: string) ->
        let func () =
            invalidOp exnMsg
            res
            
        let eff = FIO.FromFunc<int, exn> func
        
        let actual = (error <| runtime.Run eff).Message
        let expected = exnMsg

        actual = expected
        
let ``FromResult always succeeds on Ok`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let res' = Ok res
        
        let eff = FIO.FromResult res'
        
        let actual = result <| runtime.Run eff
        let expected = res

        actual = expected
        
let ``FromResult always fails on Error`` =
    forAllRuntimes <| fun runtime (err: int) ->
        let err' = Error err
        
        let eff = FIO.FromResult err'
        
        let actual = error <| runtime.Run eff
        let expected = err

        actual = expected
        
let ``FromOption always succeeds on Some`` =
    forAllRuntimes <| fun runtime (res: int, err: string) ->
        let some = Some res
        let onNone () = err
        
        let eff = FIO.FromOption (some, onNone)
        
        let actual = result <| runtime.Run eff
        let expected = res

        actual = expected
        
let ``FromOption always fails on None`` =
    forAllRuntimes <| fun runtime (err: string) ->
        let none = None
        let onNone () = err
        
        let eff = FIO.FromOption (none, onNone)
        
        let actual = error <| runtime.Run eff
        let expected = err

        actual = expected
        
let ``FromChoice always succeeds on Choice1`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let choice = Choice1Of2 res
        
        let eff = FIO.FromChoice choice
        
        let actual = result <| runtime.Run eff
        let expected = res

        actual = expected
        
let ``FromChoice always fails on Choice2`` =
    forAllRuntimes <| fun runtime (err: int) ->
        let choice = Choice2Of2 err
        
        let eff = FIO.FromChoice choice
        
        let actual = error <| runtime.Run eff
        let expected = err

        actual = expected
        
let ``AwaitTask with onError always succeeds when the task succeeds`` =
    forAllRuntimes <| fun runtime () ->
        let t =
            Task.Run(fun () -> ())
            
        let onError exn =
            exn
        
        let eff = FIO.AwaitTask<unit, exn> (t, onError)
        
        let actual = result <| runtime.Run eff
        let expected = ()

        actual = expected && t.IsCompletedSuccessfully
        
let ``AwaitTask with onError always fails when the task fails and converts error`` =
    forAllRuntimes <| fun runtime (exnMsg: string) ->
        let t = Task.Run(fun () ->
            invalidOp exnMsg)
        
        let onError (exn: exn) =
            exn.Message
        
        let eff = FIO.AwaitTask<unit, string> (t, onError)
        
        let actual = error <| runtime.Run eff
        let expected = exnMsg

        actual = expected && t.IsFaulted

let ``AwaitTask always succeeds when the task succeeds`` =
    forAllRuntimes <| fun runtime () ->
        let t =
            Task.Run(fun () -> ())

        let eff = FIO.AwaitTask<unit, exn> t
        
        let actual = result <| runtime.Run eff
        let expected = ()

        actual = expected && t.IsCompletedSuccessfully

let ``AwaitTask always fails when the task fails`` =
    forAllRuntimes <| fun runtime (exnMsg: string) ->
        let t = Task.Run(fun () ->
            invalidOp exnMsg)
        
        let eff = FIO.AwaitTask<unit, string> t
        
        let actual = (error <| runtime.Run eff).Message
        let expected = exnMsg

        actual = expected && t.IsFaulted

let ``AwaitGenericTask with onError always succeeds when the task succeeds`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let t = task {
            return res
        }
        
        let onError exn =
            exn

        let eff = FIO.AwaitGenericTask<int, exn> (t, onError)
        
        let actual = result <| runtime.Run eff
        let expected = res

        actual = expected && t.IsCompletedSuccessfully

let ``AwaitGenericTask with onError always fails when the task fails and converts error`` =
    forAllRuntimes <| fun runtime (res: int, exnMsg: string) ->
        let t = task {
            invalidOp exnMsg
            return res
        }
        
        let onError (exn: exn) =
            exn.Message

        let eff = FIO.AwaitGenericTask<int, string> (t, onError)
        
        let actual = error <| runtime.Run eff
        let expected = exnMsg

        actual = expected && t.IsFaulted

let ``AwaitGenericTask always succeeds when the task succeeds`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let t = task {
            return res
        }
      
        let eff = FIO.AwaitGenericTask<int, exn> t
        
        let actual = result <| runtime.Run eff
        let expected = res

        actual = expected && t.IsCompletedSuccessfully

let ``AwaitGenericTask always fails when the task fails and converts error`` =
    forAllRuntimes <| fun runtime (res: int, exnMsg: string) ->
        let t = task {
            invalidOp exnMsg
            return res
        }

        let eff = FIO.AwaitGenericTask<int, string> t
        
        let actual = (error <| runtime.Run eff).Message
        let expected = exnMsg

        actual = expected && t.IsFaulted

let ``AwaitAsync with onError always succeeds when the computation succeeds`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let a = async {
            return res
        }
        
        let onError exn =
            exn

        let eff = FIO.AwaitAsync<int, exn> (a, onError)
        
        let actual = result <| runtime.Run eff
        let expected = res

        actual = expected
        
let ``AwaitAsync with onError always fails when the computation fails and converts error`` =
    forAllRuntimes <| fun runtime (res: int, exnMsg: string) ->
        let a = async {
            invalidOp exnMsg
            return res
        }
        
        let onError (exn: exn) =
            exn.Message

        let eff = FIO.AwaitAsync<int, string> (a, onError)
        
        let actual = error <| runtime.Run eff
        let expected = exnMsg

        actual = expected

let ``AwaitAsync always succeeds when the computation succeeds`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let a = async {
            return res
        }
        
        let eff = FIO.AwaitAsync<int, exn> a
        
        let actual = result <| runtime.Run eff
        let expected = res

        actual = expected

let ``AwaitAsync always fails when the computation fails and converts error`` =
    forAllRuntimes <| fun runtime (res: int, exnMsg: string) ->
        let a = async {
            invalidOp exnMsg
            return res
        }
        
        let eff = FIO.AwaitAsync<int, string> a
        
        let actual = (error <| runtime.Run eff).Message
        let expected = exnMsg

        actual = expected

Check.Quick ``Succeed always succeeds``
Check.Quick ``Fail always fails``

Check.Quick ``FromFunc with onError succeeds when no exception is thrown``
Check.Quick ``FromFunc with onError fails when exception is thrown and converts error``

Check.Quick ``FromFunc succeeds when no exception is thrown``
Check.Quick ``FromFunc fails when exception is thrown``

Check.Quick ``FromResult always succeeds on Ok``
Check.Quick ``FromResult always fails on Error``

Check.Quick ``FromOption always succeeds on Some``
Check.Quick ``FromOption always fails on None``

Check.Quick ``FromChoice always succeeds on Choice1``
Check.Quick ``FromChoice always fails on Choice2``

Check.Quick ``AwaitTask with onError always succeeds when the task succeeds``
Check.Quick ``AwaitTask with onError always fails when the task fails and converts error``

Check.Quick ``AwaitTask always succeeds when the task succeeds``
Check.Quick ``AwaitTask always fails when the task fails``

Check.Quick ``AwaitGenericTask with onError always succeeds when the task succeeds``
Check.Quick ``AwaitGenericTask with onError always fails when the task fails and converts error``

Check.Quick ``AwaitGenericTask always succeeds when the task succeeds``
Check.Quick ``AwaitGenericTask always fails when the task fails and converts error``

Check.Quick ``AwaitAsync with onError always succeeds when the computation succeeds``
Check.Quick ``AwaitAsync with onError always fails when the computation fails and converts error``

Check.Quick ``AwaitAsync always succeeds when the computation succeeds``
Check.Quick ``AwaitAsync always fails when the computation fails and converts error``
