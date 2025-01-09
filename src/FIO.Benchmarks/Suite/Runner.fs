(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(************************************************************************************)

module internal FIO.Benchmarks.Suite.Runner

open FIO.Core
open FIO.Runtime
open FIO.Runtime.Naive
open FIO.Runtime.Intermediate
open FIO.Runtime.Advanced
open FIO.Runtime.Deadlocking

open System
open System.IO
open System.Globalization

type BenchmarkConfig =
    | PingpongConfig of Rounds: int
    | ThreadringConfig of Actors: int * Rounds: int
    | BigConfig of Actors: int * Rounds: int
    | BangConfig of Actors: int * Rounds: int
    | ForkConfig of Actors: int

and EvalFunc = FIO<int64, obj> -> Fiber<int64, obj>

and BenchmarkResult = string * BenchmarkConfig * string * string * (int * int64) list

let private writeResultsToCsv (result: BenchmarkResult) =
    let configStr config =
        match config with
        | PingpongConfig rounds -> $"rounds-%i{rounds}"
        | ThreadringConfig (actors, rounds) -> $"actors-%i{actors}-rounds-%i{rounds}"
        | BigConfig (actors, rounds) -> $"actors-%i{actors}-rounds-%i{rounds}"
        | BangConfig (actors, rounds) -> $"actors-%i{actors}-rounds-%i{rounds}"
        | ForkConfig actors -> $"actors-%i{actors}"

    let rec fileContentStr times acc =
        match times with
        | [] -> acc
        | (_, time) :: ts -> fileContentStr ts (acc + $"%i{time}\n")

    let headerStr = "Time"

    let homePath =
        if
            (Environment.OSVersion.Platform.Equals(PlatformID.Unix)
                || Environment.OSVersion.Platform.Equals(PlatformID.MacOSX))
        then
            Environment.GetEnvironmentVariable("HOME")
        else
            Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%")

    let benchName, config, runtimeFileName, _, times = result
    let folderName = $"{benchName.ToLower()}-{configStr config}-runs-{times.Length.ToString()}"
    let fileName = $"""{folderName}-{runtimeFileName.ToLower()}-{DateTime.Now.ToString("dd_MM_yyyy-HH-mm-ss")}.csv"""
    let dirPath = homePath + @"\fio\benchmarks\" + folderName + @"\" + runtimeFileName.ToLower()
    let filePath = dirPath + @"\" + fileName

    if (not <| Directory.Exists(dirPath)) then
        Directory.CreateDirectory(dirPath) |> ignore
    else
        ()

    printfn $"\nSaved benchmark results to '%s{filePath}'"
    File.WriteAllText(filePath, headerStr + "\n" + fileContentStr times "")

let private benchStr config =
    let ci = CultureInfo("en-US")
    match config with
    | PingpongConfig rounds -> $"""Pingpong (Rounds: %s{rounds.ToString("N0", ci)})"""
    | ThreadringConfig (actors, rounds) -> $"""Threadring (Actors: %s{actors.ToString("N0", ci)} Rounds: %s{rounds.ToString("N0", ci)})"""
    | BigConfig (actors, rounds) -> $"""Big (Actors: %s{actors.ToString("N0", ci)} Rounds: %s{rounds.ToString("N0", ci)})"""
    | BangConfig (actors, rounds) -> $"""Bang (Actors: %s{actors.ToString("N0", ci)} Rounds: %s{rounds.ToString("N0", ci)})"""
    | ForkConfig actors -> $"""Fork (Actors: %s{actors.ToString("N0", ci)})"""

let private printResult (result: BenchmarkResult) =
    let rec runExecTimesStr runExecTimes allTimes acc =
        match runExecTimes with
        | [] ->
            let onlyTimes = List.map snd allTimes
            let sum = List.sum onlyTimes
            let count = List.length onlyTimes
            let average = (float sum / float count)
            (acc
                + "│                                                                           │\n"
                + "│                                    Average time (ms)                      │\n"
                + "│                                    ─────────────────────────────────────  │\n"
               + $"│                                    %-35f{average}    │\n"
                + "└───────────────────────────────────────────────────────────────────────────┘")
        | (run, time) :: ts ->
            let str = $"│  #%-10i{run}                       %-35i{time}    │\n"
            runExecTimesStr ts allTimes (acc + str)

    let _, config, _, runtimeName, times = result
    let benchName = benchStr config
    let runExecTimesStr = runExecTimesStr times times ""

    let headerStr =
        $"
┌───────────────────────────────────────────────────────────────────────────┐
│  Benchmark:  %-50s{benchName}           │
│  Runtime:    %-50s{runtimeName}           │
├───────────────────────────────────────────────────────────────────────────┤
│  Run                               Time (ms)                              │
│  ────────────────────────────────  ─────────────────────────────────────  │\n"

    let toPrint = headerStr + runExecTimesStr
    printfn $"%s{toPrint}"

let private runBenchmark config runs (runtime: Runtime) : BenchmarkResult =
    let getRuntimeName (runtime: Runtime) =
        match runtime with
        | :? NaiveRuntime -> ("naive", "Naive")
        | :? IntermediateRuntime as r ->
            let ewc, bwc, esc = r.GetConfiguration()
            ($"intermediate-ewc%i{ewc}-bwc%i{bwc}-esc%i{esc}", $"Intermediate (EWC: %i{ewc} BWC: %i{bwc} ESC: %i{esc})")
        | :? AdvancedRuntime as r ->
            let ewc, bwc, esc = r.GetConfiguration()
            ($"advanced-ewc%i{ewc}-bwc%i{bwc}-esc%i{esc}", $"Advanced (EWC: %i{ewc} BWC: %i{bwc} ESC: %i{esc})")
        | :? DeadlockingRuntime as r ->
            let ewc, bwc, esc = r.GetConfiguration()
            ($"deadlocking-ewc%i{ewc}-bwc%i{bwc}-esc%i{esc}", $"Deadlocking (EWC: %i{ewc} BWC: %i{bwc} ESC: %i{esc})")
        | _ -> failwith "runBenchmark: Invalid runtime!"

    let createBenchmark config =
        match config with
        | PingpongConfig rounds -> ("Pingpong", Pingpong.Create rounds)
        | ThreadringConfig (actors, rounds) -> ("Threadring", Threadring.Create actors rounds)
        | BigConfig (actors, rounds) -> ("Big", Big.Create actors rounds)
        | BangConfig (actors, rounds) -> ("Bang", Bang.Create actors rounds)
        | ForkConfig actors -> ("Fork", Fork.Create actors)

    let rec executeBenchmark config curRun acc =    
        let bench, eff = createBenchmark config

        match curRun with
        | curRun' when curRun' = runs -> (bench, acc)
        | curRun' ->
            let res: Result<int64, obj> = runtime.Run(eff).AwaitResult()

            let time =
                match res with
                | Ok time -> time
                | Error _ -> -1

            let runNum = curRun' + 1
            let result = (runNum, time)
            executeBenchmark config runNum (acc @ [ result ])

    let runtimeFileName, runtimeName = getRuntimeName runtime
    let bench, runExecTimes = executeBenchmark config 0 []
    (bench, config, runtimeFileName, runtimeName, runExecTimes)

let Run configs runtime runs (processCountInc, incTimes) =
    let newConfig config incTime =
        match config with
        | PingpongConfig rounds -> PingpongConfig rounds
        | ThreadringConfig (actors, rounds) ->
            ThreadringConfig (actors + (processCountInc * incTime), rounds)
        | BigConfig (actors, rounds) ->
            BigConfig (actors + (processCountInc * incTime), rounds)
        | BangConfig (actors, rounds) ->
            BangConfig (actors + (processCountInc * incTime), rounds)
        | ForkConfig actors ->
            ForkConfig (actors + (processCountInc * incTime))

    let configs =
        configs
        @ (List.concat
            <| List.map (fun incTime -> List.map (fun config -> newConfig config incTime) configs) [ 1..incTimes ])

    let results = List.map (fun config -> runBenchmark config runs runtime) configs

    for result in results do
        printResult result
        writeResultsToCsv result
