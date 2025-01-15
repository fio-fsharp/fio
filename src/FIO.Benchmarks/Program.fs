(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module private Program

open FIO.Benchmarks.Suite.Runner

open System.Threading

let maxThreads = 32767
ThreadPool.SetMaxThreads(maxThreads, maxThreads) |> ignore
ThreadPool.SetMinThreads(maxThreads, maxThreads) |> ignore

let runBenchmarks parsedArgs =
    let configs, runtime, runs, fiberIncrement = parsedArgs
    Run configs runtime runs fiberIncrement

[<EntryPoint>]
let main args =
    let parser = ArgParser.Parser()
    parser.PrintArgs args
    runBenchmarks <| parser.ParseArgs args
    0
