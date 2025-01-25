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

// TODO: Is this necessary?
let private maxThreads = 32767
ThreadPool.SetMaxThreads(maxThreads, maxThreads) |> ignore
ThreadPool.SetMinThreads(maxThreads, maxThreads) |> ignore

let private defaultRuntime = Runtime()

let private mapResult (successHandler: 'R -> 'R1) (errorHandler: 'E -> 'E1) (res: Result<'R, 'E>) =
    match res with
    | Ok res -> Ok <| successHandler res
    | Error err -> Error <| errorHandler err

let private mergeResult (successHandler: 'R -> 'F) (errorHandler: 'E -> 'F) (res: Result<'R, 'E>) =
    match res with
    | Ok res -> successHandler res
    | Error err -> errorHandler err

let private mergeFiber (successHandler: 'R -> 'F) (errorHandler: 'E -> 'F) (fiber: Fiber<'R, 'E>) =
    mergeResult successHandler errorHandler (fiber.AwaitResult())

let private defaultSuccessHandler res =
    Console.ForegroundColor <- ConsoleColor.DarkGreen
    Console.WriteLine($"%A{Ok res}")
    Console.ResetColor()

let private defaultErrorHandler err =
    Console.ForegroundColor <- ConsoleColor.DarkRed
    Console.WriteLine($"%A{Error err}")
    Console.ResetColor()

let private defaultFiberHandler fiber = mergeFiber defaultSuccessHandler defaultErrorHandler fiber

[<AbstractClass>]
type FIOApp<'R, 'E> (successHandler: 'R -> unit, errorHandler: 'E -> unit, runtime: FIORuntime) =
    let fiberHandler = mergeFiber successHandler errorHandler

    new() = FIOApp(defaultSuccessHandler, defaultErrorHandler, defaultRuntime)

    static member Run (app: FIOApp<'R, 'E>) =
        app.Run()

    static member Run (app: FIOApp<'R, 'E>, runtime: FIORuntime) =
        app.Run runtime

    static member Run (eff: FIO<'R, 'E>) =
        let fiber = defaultRuntime.Run eff
        defaultFiberHandler fiber

    static member AwaitResult (app: FIOApp<'R, 'E>) =
        app.AwaitResult()

    static member AwaitResult (app: FIOApp<'R, 'E>, runtime: FIORuntime) =
        app.AwaitResult runtime

    static member AwaitResult (eff: FIO<'R, 'E>) =
        let fiber = defaultRuntime.Run eff
        fiber.Await()

    abstract member eff: FIO<'R, 'E>

    member this.Run () =
        this.Run runtime

    member this.Run (runtime: FIORuntime) =
        let fiber = runtime.Run this.eff
        fiberHandler fiber

    member this.Run (successHandler: 'R -> 'F, errorHandler: 'E -> 'F) =
        this.Run (successHandler, errorHandler, runtime)

    member this.Run (successHandler: 'R -> 'F, errorHandler: 'E -> 'F, runtime: FIORuntime) =
        let fiber = runtime.Run this.eff
        mergeFiber successHandler errorHandler fiber

    member this.AwaitResult () =
        this.AwaitResult runtime

    member this.AwaitResult (runtime: FIORuntime) =
        let fiber = runtime.Run this.eff
        fiber.Await()

    member this.AwaitResult (successHandler: 'R -> 'R1, errorHandler: 'E -> 'E1) =
        this.AwaitResult (successHandler, errorHandler, runtime)

    member this.AwaitResult (successHandler: 'R -> 'R1, errorHandler: 'E -> 'E1, runtime: FIORuntime) =
        let fiber = runtime.Run this.eff
        let res = fiber.AwaitResult()
        mapResult successHandler errorHandler res
