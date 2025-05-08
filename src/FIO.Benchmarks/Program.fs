(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module private Program

open FIO.Benchmarks.Suite

open System.Threading

// TODO: Is this necessary?
let maxThreads = 32767
ThreadPool.SetMaxThreads(maxThreads, maxThreads) |> ignore
ThreadPool.SetMinThreads(maxThreads, maxThreads) |> ignore

[<EntryPoint>]
let main args =
    let parser = ArgParser.Parser()
    parser.PrintArgs args
    let task =
        BenchmarkRunner.Run
        <| parser.ParseArgs args
    task.Wait()
    0
