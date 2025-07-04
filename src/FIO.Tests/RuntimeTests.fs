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
               
let ``FromTask with onError always succeeds when the task succeeds`` =
    forAllRuntimes <| fun runtime () ->
        let lazyTask = fun () ->
            Task.Run(fun () -> ())
        
        let onError (exn: exn) =
            exn
        
        let eff = FIO.FromTask<Fiber<unit, exn>, exn> (lazyTask, onError)
        
        let actual = (result <| runtime.Run eff).GetType()
        let expected = typeof<Fiber<unit, exn>>
        
        actual = expected

let ``FromTask effect with onError always succeeds with fiber when the task fails`` =
    forAllRuntimes <| fun runtime (exnMsg: string) ->
        let lazyTask = fun () ->
            Task.Run(fun () ->
            invalidOp exnMsg)
        
        let onError (exn: exn) =
            exn
        
        let eff = FIO.FromTask<Fiber<unit, exn>, exn> (lazyTask, onError)
        
        let actual = (result <| runtime.Run eff).GetType()
        let expected = typeof<Fiber<unit, exn>>
        
        actual = expected

let ``FromTask fiber with onError always fails when the task fails and converts error`` =
    forAllRuntimes <| fun runtime (exnMsg: string) ->
        let lazyTask = fun () ->
            Task.Run(fun () ->
            invalidOp exnMsg)
        
        let onError (exn: exn) =
            exn.Message
        
        let eff = FIO.FromTask<Fiber<unit, string>, string> (lazyTask, onError)
        
        let actual = error <| result (runtime.Run eff)
        let expected = exnMsg
        
        actual = expected

let ``FromTask always succeeds when the task succeeds`` =
    forAllRuntimes <| fun runtime () ->
        let lazyTask = fun () ->
            Task.Run(fun () -> ())
        
        let eff = FIO.FromTask<Fiber<unit, exn>, exn> lazyTask
        
        let actual = (result <| runtime.Run eff).GetType()
        let expected = typeof<Fiber<unit, exn>>
        
        actual = expected
        
let ``FromTask effect always succeeds with fiber when the task fails`` =
    forAllRuntimes <| fun runtime (exnMsg: string) ->
        let lazyTask = fun () ->
            Task.Run(fun () ->
            invalidOp exnMsg)
        
        let eff = FIO.FromTask<Fiber<unit, exn>, exn> lazyTask
        
        let actual = (result <| runtime.Run eff).GetType()
        let expected = typeof<Fiber<unit, exn>>
        
        actual = expected
        
let ``FromTask fiber always fails when the task fails`` =
    forAllRuntimes <| fun runtime (exnMsg: string) ->
        let lazyTask = fun () ->
            Task.Run(fun () ->
            invalidOp exnMsg)
        
        let eff = FIO.FromTask<Fiber<unit, exn>, exn> lazyTask
        
        let actual = (error <| result (runtime.Run eff)).Message
        let expected = exnMsg
        
        actual = expected

let ``FromGenericTask with onError always succeeds when the task succeeds`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let lazyTask = fun () ->
            task {
                return res
            }
        
        let onError (exn: exn) =
            exn
        
        let eff = FIO.FromGenericTask<Fiber<int, exn>, exn> (lazyTask, onError)
        
        let actual = (result <| runtime.Run eff).GetType()
        let expected = typeof<Fiber<int, exn>>
        
        actual = expected

let ``FromGenericTask effect with onError always succeeds with fiber when the task fails`` =
    forAllRuntimes <| fun runtime (res: int, exnMsg: string) ->
        let lazyTask = fun () ->
            task {
                invalidOp exnMsg
                return res
            }
        
        let onError (exn: exn) =
            exn
        
        let eff = FIO.FromGenericTask<Fiber<int, exn>, exn> (lazyTask, onError)
        
        let actual = (result <| runtime.Run eff).GetType()
        let expected = typeof<Fiber<int, exn>>
        
        actual = expected

let ``FromGenericTask fiber with onError always fails when the task fails and converts error`` =
    forAllRuntimes <| fun runtime (res: int, exnMsg: string) ->
        let lazyTask = fun () ->
            task {
                invalidOp exnMsg
                return res
            }
        
        let onError (exn: exn) =
            exn.Message
        
        let eff = FIO.FromGenericTask<Fiber<int, string>, string> (lazyTask, onError)
        
        let actual = error <| result (runtime.Run eff)
        let expected = exnMsg
        
        actual = expected

let ``FromGenericTask always succeeds when the task succeeds`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let lazyTask = fun () ->
            task {
                return res
            }
        
        let eff = FIO.FromGenericTask<Fiber<int, exn>, exn> lazyTask
        
        let actual = (result <| runtime.Run eff).GetType()
        let expected = typeof<Fiber<int, exn>>
        
        actual = expected

let ``FromGenericTask effect always succeeds with fiber when the task fails`` =
    forAllRuntimes <| fun runtime (res: int, exnMsg: string) ->
        let lazyTask = fun () ->
            task {
                invalidOp exnMsg
                return res
            }
        
        let eff = FIO.FromGenericTask<Fiber<int, exn>, exn> lazyTask
        
        let actual = (result <| runtime.Run eff).GetType()
        let expected = typeof<Fiber<int, exn>>
        
        actual = expected

let ``FromGenericTask fiber always fails when the task fails`` =
    forAllRuntimes <| fun runtime (res: int, exnMsg: string) ->
        let lazyTask = fun () ->
            task {
                invalidOp exnMsg
                return res
            }
        
        let eff = FIO.FromGenericTask<Fiber<int, exn>, exn> lazyTask
        
        let actual = (error <| result (runtime.Run eff)).Message
        let expected = exnMsg
        
        actual = expected

let ``Fork always succeeds when the effect succeeds`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let eff = (FIO.Succeed res).Fork()
        
        let actual = (result <| runtime.Run eff).GetType()
        let expected = typeof<Fiber<int, obj>>
        
        actual = expected
        
let ``Fork always succeeds when the effect fails`` =
    forAllRuntimes <| fun runtime (err: int) ->
        let eff = (FIO.Fail err).Fork()
        
        let actual = (result <| runtime.Run eff).GetType()
        let expected = typeof<Fiber<obj, int>>
        
        actual = expected
        
let ``Fork fiber always succeeds when the effect succeeds`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let eff = (FIO.Succeed res).Fork()
        
        let actual = result (result <| runtime.Run eff)
        let expected = res
        
        actual = expected
        
let ``Fork fiber always fails when the effect fails`` =
    forAllRuntimes <| fun runtime (err: int) ->
        let eff = (FIO.Fail err).Fork()
        
        let actual = error (result <| runtime.Run eff)
        let expected = err
        
        actual = expected

let ``Bind always succeeds when the initial effect succeeds and continuation succeeds`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let eff = (FIO.Succeed res).Bind(FIO.Succeed)
        
        let actual = result <| runtime.Run eff
        let expected = res
        
        actual = expected
        
let ``Bind always fails when the initial effect fails and continuation fails`` =
    forAllRuntimes <| fun runtime (err: int) ->
        let eff = (FIO.Fail err).Bind(FIO.Fail)
        
        let actual = error <| runtime.Run eff
        let expected = err
        
        actual = expected
    
let ``Bind always fails when the initial effect fails and continuation succeeds`` =
    forAllRuntimes <| fun runtime (err: int) ->
        let eff = (FIO.Fail err).Bind(FIO.Succeed)
        
        let actual = error <| runtime.Run eff
        let expected = err
        
        actual = expected

let ``Bind always fails when the initial effect succeeds and continuation fails`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let eff = (FIO.Succeed res).Bind(FIO.Fail)
        
        let actual = error <| runtime.Run eff
        let expected = res
        
        actual = expected

let ``BindError always succeeds when the initial effect succeeds and continuation fails`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let eff = (FIO.Succeed res).BindError(FIO.Fail)
        
        let actual = result <| runtime.Run eff
        let expected = res
        
        actual = expected
    
let ``BindError always succeeds when the initial effect fails and continuation succeeds`` =
    forAllRuntimes <| fun runtime (err: int) ->
        let eff = (FIO.Fail err).BindError(FIO.Succeed)
        
        let actual = result <| runtime.Run eff
        let expected = err
        
        actual = expected
        
let ``BindError always succeeds with the initial effect when the initial effect succeeds and continuation succeeds`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let eff = (FIO.Succeed res).BindError(fun r -> FIO.Succeed <| r + 1)
        
        let actual = result <| runtime.Run eff
        let expected = res
        
        actual = expected
        
let ``BindError always fails with the continuation when the initial effect fails and continuation fails`` =
    forAllRuntimes <| fun runtime (err: int) ->
        let eff = (FIO.Fail err).BindError(fun e -> FIO.Fail <| e + 1)
        
        let actual = error <| runtime.Run eff
        let expected = err + 1
        
        actual = expected

let ``Map always succeeds when the effect succeeds and transforms result`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let eff = (FIO.Succeed res).Map(string) 
        
        let actual = result <| runtime.Run eff
        let expected = string res
        
        actual = expected
        
let ``Map always fails when the effect fails and does not transform result`` =
    forAllRuntimes <| fun runtime (err: int) ->
        let eff = (FIO.Fail err).Map(string) 
        
        let actual = error <| runtime.Run eff
        let expected = err
        
        actual = expected
        
let ``MapError always succeeds when the effect succeeds and does not transform result`` =
    forAllRuntimes <| fun runtime (res: int) ->
        let eff = (FIO.Succeed res).MapError(string) 
        
        let actual = result <| runtime.Run eff
        let expected = res
        
        actual = expected
        
let ``MapError always fails when the effect fails and transforms result`` =
    forAllRuntimes <| fun runtime (err: int) ->
        let eff = (FIO.Fail err).MapError(string) 
        
        let actual = error <| runtime.Run eff
        let expected = string err
        
        actual = expected

let ``Then always succeeds with the second effect when the initial effect succeeds and second effect succeeds`` =
    forAllRuntimes <| fun runtime (res1: int, res2: int) ->
        let eff = (FIO.Succeed res1).Then(FIO.Succeed res2)
        
        let actual = result <| runtime.Run eff
        let expected = res2
        
        actual = expected
        
let ``Then always fails with the initial effect when the initial effect fails and second effect succeeds`` =
    forAllRuntimes <| fun runtime (err: int, res: int) ->
        let eff = (FIO.Fail err).Then(FIO.Succeed res)
        
        let actual = error <| runtime.Run eff
        let expected = err
        
        actual = expected
        
let ``Then always fails with the second effect when the initial effect succeeds and second effect fails`` =
    forAllRuntimes <| fun runtime (res: int, err: int) ->
        let eff = (FIO.Succeed res).Then(FIO.Fail err)
        
        let actual = error <| runtime.Run eff
        let expected = err
        
        actual = expected
        
let ``Then always fails with the initial effect when the initial effect fails and second effect fails`` =
    forAllRuntimes <| fun runtime (err1: int, err2: int) ->
        let eff = (FIO.Fail err1).Then(FIO.Fail err2)
        
        let actual = error <| runtime.Run eff
        let expected = err1
        
        actual = expected

let ``ThenError always succeeds with the initial effect when the initial effect succeeds and second effect fails`` =
    forAllRuntimes <| fun runtime (err: int, res: int) ->
        let eff = (FIO.Succeed res).ThenError(FIO.Fail err)
        
        let actual = result <| runtime.Run eff
        let expected = res
        
        actual = expected
        
let ``ThenError always succeeds with the second effect when the initial effect fails and second effect succeeds`` =
    forAllRuntimes <| fun runtime (res: int, err: int) ->
        let eff = (FIO.Fail err).ThenError(FIO.Succeed res)
        
        let actual = result <| runtime.Run eff
        let expected = res
        
        actual = expected

let ``ThenError always succeeds with the initial effect when the initial effect succeeds and second effect succeeds`` =
    forAllRuntimes <| fun runtime (res1: int, res2: int) ->
        let eff = (FIO.Succeed res1).ThenError(FIO.Succeed res2)
        
        let actual = result <| runtime.Run eff
        let expected = res1
        
        actual = expected

let ``ThenError always fails with the second effect when the initial effect fails and second effect fails`` =
    forAllRuntimes <| fun runtime (err1: int, err2: int) ->
        let eff = (FIO.Fail err1).ThenError(FIO.Fail err2)
        
        let actual = error <| runtime.Run eff
        let expected = err2
        
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

Check.Quick ``FromTask with onError always succeeds when the task succeeds``
Check.Quick ``FromTask effect with onError always succeeds with fiber when the task fails``
Check.Quick ``FromTask fiber with onError always fails when the task fails and converts error``

Check.Quick ``FromTask always succeeds when the task succeeds``
Check.Quick ``FromTask effect always succeeds with fiber when the task fails``
Check.Quick ``FromTask fiber always fails when the task fails``

Check.Quick ``FromGenericTask with onError always succeeds when the task succeeds``
Check.Quick ``FromGenericTask effect with onError always succeeds with fiber when the task fails``
Check.Quick ``FromGenericTask fiber with onError always fails when the task fails and converts error``

Check.Quick ``FromGenericTask always succeeds when the task succeeds``
Check.Quick ``FromGenericTask effect always succeeds with fiber when the task fails``
Check.Quick ``FromGenericTask fiber always fails when the task fails``

Check.Quick ``Fork always succeeds when the effect succeeds``
Check.Quick ``Fork always succeeds when the effect fails``
Check.Quick ``Fork fiber always succeeds when the effect succeeds``
Check.Quick ``Fork fiber always fails when the effect fails``

Check.Quick ``Bind always succeeds when the initial effect succeeds and continuation succeeds``
Check.Quick ``Bind always fails when the initial effect fails and continuation fails``
Check.Quick ``Bind always fails when the initial effect fails and continuation succeeds``
Check.Quick ``Bind always fails when the initial effect succeeds and continuation fails``

Check.Quick ``BindError always succeeds when the initial effect succeeds and continuation fails``
Check.Quick ``BindError always succeeds when the initial effect fails and continuation succeeds``
Check.Quick ``BindError always succeeds with the initial effect when the initial effect succeeds and continuation succeeds``
Check.Quick ``BindError always fails with the continuation when the initial effect fails and continuation fails``

Check.Quick ``Map always succeeds when the effect succeeds and transforms result``
Check.Quick ``Map always fails when the effect fails and does not transform result``

Check.Quick ``MapError always succeeds when the effect succeeds and does not transform result``
Check.Quick ``MapError always fails when the effect fails and transforms result``

Check.Quick ``Then always succeeds with the second effect when the initial effect succeeds and second effect succeeds``
Check.Quick ``Then always fails with the initial effect when the initial effect fails and second effect succeeds``
Check.Quick ``Then always fails with the second effect when the initial effect succeeds and second effect fails``
Check.Quick ``Then always fails with the initial effect when the initial effect fails and second effect fails``

Check.Quick ``ThenError always succeeds with the initial effect when the initial effect succeeds and second effect fails``
Check.Quick ``ThenError always succeeds with the second effect when the initial effect fails and second effect succeeds``
Check.Quick ``ThenError always succeeds with the initial effect when the initial effect succeeds and second effect succeeds``
Check.Quick ``ThenError always fails with the second effect when the initial effect fails and second effect fails``
