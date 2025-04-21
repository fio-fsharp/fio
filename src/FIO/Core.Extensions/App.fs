(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

[<AutoOpen>]
module FIO.Core.App

open FIO.Runtime
open FIO.Runtime.Advanced

open System
open System.Threading
open System.Threading.Tasks

// TODO: Is this necessary?
let private maxThreads = 32767
ThreadPool.SetMaxThreads(maxThreads, maxThreads) |> ignore
ThreadPool.SetMinThreads(maxThreads, maxThreads) |> ignore

let private defaultRuntime = Runtime()

let private mergeResult successHandler errorHandler = function
    | Ok res -> successHandler res
    | Error err -> errorHandler err

let private mergeFiber successHandler errorHandler (fiber: Fiber<'R, 'E>) = task {
    let! res = fiber.AwaitAsync()
    return! mergeResult successHandler errorHandler res
}

let private defaultSuccessHandler res = task {
    Console.ForegroundColor <- ConsoleColor.DarkGreen
    Console.WriteLine $"%A{Ok res}"
    Console.ResetColor()
}

let private defaultErrorHandler err = task {
    Console.ForegroundColor <- ConsoleColor.DarkRed
    Console.WriteLine $"%A{Error err}"
    Console.ResetColor()
}

let private defaultFiberHandler fiber = mergeFiber defaultSuccessHandler defaultErrorHandler fiber

[<AbstractClass>]
type FIOApp<'R, 'E> (successHandler: 'R -> Task<unit>, errorHandler: 'E -> Task<unit>, runtime: FIORuntime) =
    let fiberHandler = mergeFiber successHandler errorHandler

    new() = FIOApp(defaultSuccessHandler, defaultErrorHandler, defaultRuntime)

    abstract member effect: FIO<'R, 'E>

    static member Run (app: FIOApp<'R, 'E>) =
        app.Run()

    static member Run (eff: FIO<'R, 'E>) =
        let fiber = defaultRuntime.Run eff
        let t = defaultFiberHandler fiber
        t.Wait()

    member this.Run () =
        this.Run runtime
        |> ignore

    member this.Run runtime =
        let fiber = runtime.Run this.effect
        let t = fiberHandler fiber
        t.Wait()

    member this.Run (successHandler: 'R -> 'F, errorHandler: 'E -> 'F) =
        let fiber = runtime.Run this.effect
        let t = mergeFiber successHandler errorHandler fiber
        t.Wait()