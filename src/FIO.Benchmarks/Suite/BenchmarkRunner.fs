(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module internal FIO.Benchmarks.Suite.BenchmarkRunner

open System
open System.IO

open FIO.Runtime

let private writeResultToCsv result savePath =

    let rec csvContent times acc =
        match times with
        | [] -> acc
        | (_, time) :: ts -> csvContent ts (acc + $"%i{time}\n")

    let benchName = 
        result.Config.ToString()
            .ToLowerInvariant()
            .Replace("(", "") 
            .Replace(")", "")
            .Replace(":", "")
            .Replace(" ", "-")
            .Replace(",", "")
            .Replace(".", "")
    let folderName = $"%s{benchName}-runs-%s{result.Times.Length.ToString()}"
    let dirPath = savePath + folderName + @"\" + result.RuntimeFileName.ToLower()

    if not <| Directory.Exists dirPath then
        Directory.CreateDirectory dirPath |> ignore
    else
        ()

    let fileName = $"""{folderName}-{result.RuntimeFileName.ToLower()}-{DateTime.Now.ToString("dd_MM_yyyy-HH-mm-ss")}.csv"""
    let filePath = dirPath + @"\" + fileName

    File.WriteAllText(filePath, "Execution Time (ms)\n" + csvContent result.Times "")
    printfn $"\nSaved benchmark results to file: '%s{filePath}'"

let private printResult result =

    let header =
        $"
┌───────────────────────────────────────────────────────────────────────────┐
│  Benchmark:  %-50s{result.Config.ToString()}           │
│  Runtime:    %-50s{result.RuntimeName}           │
├───────────────────────────────────────────────────────────────────────────┤
│  Run                               Time (ms)                              │
│  ────────────────────────────────  ─────────────────────────────────────  │\n"

    let timeRows =
        let rec timeRows curTimeRows allTimes acc =
            match curTimeRows with
            | [] ->
                let onlyTimes = List.map snd allTimes
                let average = (float (List.sum onlyTimes) / float (List.length onlyTimes))
                (acc
                    + "│                                                                           │\n"
                    + "│                                    Average time (ms)                      │\n"
                    + "│                                    ─────────────────────────────────────  │\n"
                   + $"│                                    %-35f{average}    │\n"
                    + "└───────────────────────────────────────────────────────────────────────────┘")
            | (run, time) :: ts ->
                let str = $"│  #%-10i{run}                       %-35i{time}    │\n"
                timeRows ts allTimes (acc + str)
        timeRows result.Times result.Times ""

    printfn $"%s{header + timeRows}"

let private runBenchmark (runtime: FIORuntime) totalRuns (config: BenchmarkConfig) =

    let rec executeBenchmark eff run acc =
        match run with
        | run when run = totalRuns -> acc
        | run ->
            let thisRun = run + 1
            #if DEBUG
            printfn $"[DEBUG]: Executing benchmark: Name: %s{config.ToString()}, Runtime: %s{runtime.ToString()}, Current run (%i{thisRun}/%i{totalRuns})"
            #endif
            let res = runtime.Run(eff).AwaitAsync()
            let time =
                res.Wait()
                let res = res.Result
                match res with
                | Ok time -> time
                | Error err -> invalidOp $"BenchmarkRunner: Failed executing benchmark with error: %A{err}"
            #if DEBUG
            printfn $"[DEBUG]: Executing benchmark completed: Time: %i{time}ms, Name: %s{config.ToString()}, Runtime: %s{runtime.ToString()}, Current run (%i{thisRun}/%i{totalRuns})"
            #endif
            let result = (thisRun, time)
            executeBenchmark eff thisRun (acc @ [result])

    #if DEBUG
    printfn $"[DEBUG]: Starting benchmark session: Name: %s{config.ToString()}, Runtime: %s{runtime.ToString()}, Total runs: %i{totalRuns}"
    #endif
    let eff =
        match config with
        | PingpongConfig rounds ->
            Pingpong.Create <| PingpongConfig rounds
        | ThreadringConfig (actors, rounds) ->
            Threadring.Create <| ThreadringConfig (actors, rounds)
        | BigConfig (actors, rounds) ->
            Big.Create <| BigConfig (actors, rounds)
        | BangConfig (actors, rounds) ->
            Bang.Create <| BangConfig (actors, rounds)
        | ForkConfig actors ->
            Fork.Create <| ForkConfig actors
    let times = executeBenchmark eff 0 []
    #if DEBUG
    printfn $"[DEBUG]: Completed benchmark session: Name: %s{config.ToString()}, Runtime: %s{runtime.ToString()}, Total runs: %i{totalRuns}"
    #endif
    { Config = config
      RuntimeName = runtime.ToString()
      RuntimeFileName = 
        runtime.ToString()
            .ToLowerInvariant()
            .Replace("(", "")
            .Replace(")", "")
            .Replace(":", "")
            .Replace(' ', '-')
      Times = times  }

let internal Run args =
    let (actorInc, actorIncTimes) = args.ActorIncrement
    let (roundInc, roundIncTimes) = args.RoundIncrement

    let configs =
        args.BenchmarkConfigs
        |> List.collect (fun config ->
            [ for actorIncTime in 0..actorIncTimes do
                for roundIncTime in 0..roundIncTimes do
                    match config with
                    | PingpongConfig rounds -> 
                        yield PingpongConfig (rounds + (roundInc * roundIncTime))
                    | ThreadringConfig (actors, rounds) -> 
                        yield ThreadringConfig (actors + (actorInc * actorIncTime), rounds + (roundInc * roundIncTime))
                    | BigConfig (actors, rounds) -> 
                        yield BigConfig (actors + (actorInc * actorIncTime), rounds + (roundInc * roundIncTime))
                    | BangConfig (actors, rounds) -> 
                        yield BangConfig (actors + (actorInc * actorIncTime), rounds + (roundInc * roundIncTime))
                    | ForkConfig actors -> 
                        yield ForkConfig (actors + (actorInc * actorIncTime)) ])
           
    let results = List.map (fun config -> runBenchmark args.Runtime args.Runs config) configs
    results |> List.iter (fun result ->
        printResult result
        if args.SaveToCsv then writeResultToCsv result args.SavePath)
