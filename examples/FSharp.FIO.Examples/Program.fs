(*********************************************************************************************)
(* FIO - A Type-Safe, Purely Functional Effect System for Asynchronous and Concurrent F#     *)
(* Copyright (c) 2022-2025 - Daniel "iyyel" Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                                       *)
(*********************************************************************************************)

module private FSharp.FIO.Examples

open FSharp.FIO.DSL
open FSharp.FIO.Lib.IO
open FSharp.FIO.Runtime.Concurrent

open System

let helloWorld1 () =
    let hello = FIO.Succeed "Hello world! 🪻"
    let fiber = Runtime().Run hello
    
    task {
        let! result = fiber.AwaitAsync()
        match result with
        | Ok result -> printfn $"Success: %s{result}"
        | Error error -> printfn $"Error: %A{error}"
    } |> ignore

let helloWorld2 () : unit =
    let hello: FIO<string, obj> = FIO.Succeed "Hello world! 🪻"
    let fiber: Fiber<string, obj> = Runtime().Run hello
    
    task {
        let! (result: Result<string, obj>) = fiber.AwaitAsync()
        match result with
        | Ok result -> printfn $"Success: %s{result}"
        | Error error -> printfn $"Error: %A{error}"
    } |> ignore

let helloWorld3 () : unit =
    let hello: FIO<obj, string> = FIO.Fail "Hello world! 🪻"
    let fiber: Fiber<obj, string> = Runtime().Run hello
    
    task {
        let! (result: Result<obj, string>) = fiber.AwaitAsync()
        match result with
        | Ok result -> printfn $"Success: %A{result}"
        | Error error -> printfn $"Error: %s{error}"
    } |> ignore

let helloWorld4 () =
    let hello = FIO.Succeed "Hello world! 🪻"
    let fiber = Runtime().Run hello
    
    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

let helloWorld5 () =
    let hello = !+ "Hello world! 🪻"
    let fiber = Runtime().Run hello
    
    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

let helloWorld6 () =
    let hello = !- "Hello world! 🪻"
    let fiber = Runtime().Run hello
    
    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

let concurrency1 () =
    let concurrent = (FIO.Succeed 42).Fork().Bind(_.Await())
    let fiber = Runtime().Run concurrent
    
    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

let concurrency2 () =
    let concurrent = !~> !+ 42 >>= fun fiber -> !~~> fiber
    let fiber = Runtime().Run concurrent
    
    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

let concurrency3 () =
    let taskA = !+ "Task A completed!"
    let taskB = !+ (200, "Task B OK")
    let concurrent = taskA <!> taskB
    let fiber = Runtime().Run concurrent
    
    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

let computationExpression1 () =
    let hello : FIO<string, obj> =
        fio {
            return "Hello world! 🪻"
        }

    let fiber = Runtime().Run hello
    
    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

let computationExpression2 () =
    let hello : FIO<obj, string> =
        fio {
            return! !- "Hello world! 🪻"
        }

    let fiber = Runtime().Run hello

    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

let computationExpression3 () =
    let welcome =
        fio {
            do! FConsole.PrintLine "Hello! What is your name?"
            let! name = FConsole.ReadLine ()
            do! FConsole.PrintLine $"Hello, %s{name}! Welcome to FIO! 🪻💜"
        }

    let fiber = Runtime().Run welcome

    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

helloWorld1 ()
Console.ReadLine () |> ignore

helloWorld2 ()
Console.ReadLine () |> ignore

helloWorld3 ()
Console.ReadLine () |> ignore

helloWorld4 ()
Console.ReadLine () |> ignore

helloWorld5 ()
Console.ReadLine () |> ignore

helloWorld6 ()
Console.ReadLine () |> ignore

concurrency1 ()
Console.ReadLine () |> ignore

concurrency2 ()
Console.ReadLine () |> ignore

concurrency3 ()
Console.ReadLine () |> ignore

computationExpression1 ()
Console.ReadLine () |> ignore

computationExpression2 ()
Console.ReadLine () |> ignore

computationExpression3 ()
Console.ReadLine () |> ignore
