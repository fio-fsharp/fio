(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(************************************************************************************)

[<AutoOpen>]
module FIO.Core.App

open FIO.Runtime
open FIO.Runtime.Advanced

open System
open System.Threading

let private maxThreads = 32767
ThreadPool.SetMaxThreads(maxThreads, maxThreads) |> ignore
ThreadPool.SetMinThreads(maxThreads, maxThreads) |> ignore

let private defaultRuntime = Runtime()

let private mapResult (successHandler: 'R -> 'R1) (errorHandler: 'E -> 'E1) (result: Result<'R, 'E>) =
    match result with
    | Ok result -> Ok <| successHandler result
    | Error error -> Error <| errorHandler error

let private mergeResult (successHandler: 'R -> 'F) (errorHandler: 'E -> 'F) (result: Result<'R, 'E>) =
    match result with
    | Ok result -> successHandler result
    | Error error -> errorHandler error

let private mergeFiber (successHandler: 'R -> 'F) (errorHandler: 'E -> 'F) (fiber: Fiber<'R, 'E>) =
    mergeResult successHandler errorHandler (fiber.AwaitResult())

let private defaultSuccessHandler result =
    Console.ForegroundColor <- ConsoleColor.DarkGreen
    Console.WriteLine($"%A{Ok result}")
    Console.ResetColor()

let private defaultErrorHandler error =
    Console.ForegroundColor <- ConsoleColor.DarkRed
    Console.WriteLine($"%A{Error error}")
    Console.ResetColor()

let private defaultFiberHandler fiber = mergeFiber defaultSuccessHandler defaultErrorHandler fiber

[<AbstractClass>]
type FIOApp<'R, 'E> (successHandler: 'R -> unit, errorHandler: 'E -> unit, runtime: FIORuntime) =
    let fiberHandler = mergeFiber successHandler errorHandler

    new() = FIOApp(defaultSuccessHandler, defaultErrorHandler, defaultRuntime)

    static member Run(app: FIOApp<'R, 'E>) =
        app.Run()

    static member Run(app: FIOApp<'R, 'E>, runtime: FIORuntime) =
        app.Run runtime

    static member Run(effect: FIO<'R, 'E>) =
        let fiber = defaultRuntime.Run effect
        defaultFiberHandler fiber

    static member AwaitResult(app: FIOApp<'R, 'E>) =
        app.AwaitResult()

    static member AwaitResult(app: FIOApp<'R, 'E>, runtime: FIORuntime) =
        app.AwaitResult runtime

    static member AwaitResult(effect: FIO<'R, 'E>) =
        let fiber = defaultRuntime.Run effect
        fiber.AwaitResult()

    abstract member effect: FIO<'R, 'E>

    member this.Run() =
        this.Run(runtime)

    member this.Run(runtime: FIORuntime) =
        let fiber = runtime.Run this.effect
        fiberHandler fiber

    member this.Run(successHandler: 'R -> 'F, errorHandler: 'E -> 'F) =
        this.Run(successHandler, errorHandler, runtime)

    member this.Run(successHandler: 'R -> 'F, errorHandler: 'E -> 'F, runtime: FIORuntime) =
        let fiber = runtime.Run this.effect
        mergeFiber successHandler errorHandler fiber

    member this.AwaitResult() =
        this.AwaitResult runtime

    member this.AwaitResult(runtime: FIORuntime) =
        let fiber = runtime.Run this.effect
        fiber.AwaitResult()

    member this.AwaitResult(successHandler: 'R -> 'R1, errorHandler: 'E -> 'E1) =
        this.AwaitResult(successHandler, errorHandler, runtime)

    member this.AwaitResult(successHandler: 'R -> 'R1, errorHandler: 'E -> 'E1, runtime: FIORuntime) =
        let fiber = runtime.Run this.effect
        let result = fiber.AwaitResult()
        mapResult successHandler errorHandler result
