(*********************************************************************************************)
(* FIO - A Type-Safe, Purely Functional Effect System for Asynchronous and Concurrent F#     *)
(* Copyright (c) 2022-2025 - Daniel "iyyel" Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                                       *)
(*********************************************************************************************)

module private FSharp.FIO.Benchmarks.Program

open FSharp.FIO.Benchmarks.ArgParser
open FSharp.FIO.Benchmarks.Suite.BenchmarkRunner

open System.Threading

// TODO: Is this necessary?
let maxThreads = 32767
ThreadPool.SetMaxThreads(maxThreads, maxThreads) |> ignore
ThreadPool.SetMinThreads(maxThreads, maxThreads) |> ignore

[<EntryPoint>]
let main args =
    printArgs args
    let task =
        runBenchmarks
        <| parseArgs args
    task.Wait ()
    0
