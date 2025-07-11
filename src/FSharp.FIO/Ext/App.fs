(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module FSharp.FIO.App

open FSharp.FIO.DSL
open FSharp.FIO.Runtime
open FSharp.FIO.Runtime.Concurrent

open System
open System.Threading
open System.Threading.Tasks

// TODO: Is this necessary?
let private maxThreads = 32767
ThreadPool.SetMaxThreads(maxThreads, maxThreads) |> ignore
ThreadPool.SetMinThreads(maxThreads, maxThreads) |> ignore

let private defaultRuntime = Runtime()

let private mergeResult onSuccess onError = function
    | Ok res -> onSuccess res
    | Error err -> onError err

let private mergeFiber onSuccess onError (fiber: Fiber<'R, 'E>) = task {
    let! res = fiber.AwaitAsync()
    return! mergeResult onSuccess onError res
}

let private defaultOnSuccess res = task {
    Console.ForegroundColor <- ConsoleColor.DarkGreen
    Console.WriteLine $"%A{Ok res}"
    Console.ResetColor()
}

let private defaultOnError err = task {
    Console.ForegroundColor <- ConsoleColor.DarkRed
    Console.WriteLine $"%A{Error err}"
    Console.ResetColor()
}

let private defaultFiberHandler fiber = mergeFiber defaultOnSuccess defaultOnError fiber

[<AbstractClass>]
type FIOApp<'R, 'E> (onSuccess: 'R -> Task<unit>, onError: 'E -> Task<unit>, runtime: FRuntime) =
    let fiberHandler = mergeFiber onSuccess onError

    new() = FIOApp(defaultOnSuccess, defaultOnError, defaultRuntime)

    abstract member effect: FIO<'R, 'E>

    static member Run<'R, 'E> (app: FIOApp<'R, 'E>) =
        app.Run()

    static member Run<'R, 'E> (eff: FIO<'R, 'E>) =
        let fiber = defaultRuntime.Run eff
        let task = defaultFiberHandler fiber
        task.Wait()

    member this.Run<'R, 'E> () =
        this.Run runtime

    member this.Run<'R, 'E> runtime =
        let fiber = runtime.Run this.effect
        let task = fiberHandler fiber
        task.Wait()

    member this.Run<'R, 'E, 'F> (onSuccess: 'R -> Task<'F>, onError: 'E -> Task<'F>) =
        let fiber = runtime.Run this.effect
        let task = mergeFiber onSuccess onError fiber
        task.Wait()
