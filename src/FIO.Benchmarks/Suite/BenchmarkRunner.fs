(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module internal FIO.Benchmarks.Suite.BenchmarkRunner

open FIO.DSL
open FIO.Runtime

open System
open System.IO

let private writeResultToCsv result savePath =

    let rec csvContent times acc =
        match times with
        | [] -> acc
        | (_, time) :: ts -> csvContent ts (acc + $"%i{time}\n")

    let benchName = result.Config.ToFileString()
    let folderName = $"%s{benchName}-runs-%s{result.Times.Length.ToString()}"
    let dirPath = savePath + folderName + @"\" + result.RuntimeFileName.ToLower()

    if not <| Directory.Exists dirPath then
        Directory.CreateDirectory dirPath |> ignore

    let fileName = $"""{folderName}-{result.RuntimeFileName.ToLower()}-{DateTime.Now.ToString("dd_MM_yyyy-HH-mm-ss")}.csv"""
    let filePath = dirPath + @"\" + fileName

    File.WriteAllText(filePath, "Execution Time (ms)\n" + csvContent result.Times "")
    printfn $"\nSaved benchmark time results to file: '%s{filePath}'"

let private printResult result =

    let average onlyTimes =
        onlyTimes |> List.averageBy float
    
    let standardDeviation (onlyTimes: int64 list) =
        if onlyTimes.IsEmpty then
            0.0
        else
            let mean = onlyTimes |> List.averageBy float
            let variance = 
                onlyTimes 
                |> List.map (fun x -> pown (float x - mean) 2)
                |> List.average
            sqrt variance
    
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
                let average = average onlyTimes
                let std = standardDeviation onlyTimes
                (acc
                    + "│                                                                           │\n"
                    + "│  Average time (ms)                 Standard deviation                     │\n"
                    + "│  ─────────────────────────-─────   ───────────────────────────-───────-─  │\n"
                   + $"│  %-33f{average} %-33f{std}      │\n"
                    + "└───────────────────────────────────────────────────────────────────────────┘")
            | (run, time) :: ts ->
                let str = $"│  #%-10i{run}                       %-35i{time}    │\n"
                timeRows ts allTimes (acc + str)
        timeRows result.Times result.Times ""

    printfn $"%s{header + timeRows}"

let private runBenchmark (runtime: FRuntime) totalRuns (config: BenchmarkConfig) =
    
    let rec executeBenchmark (eff: FIO<int64, exn>) =
            task {
                let mutable results = []
                
                for currentRun in 1..totalRuns do
                    printfn $"[DEBUG]: Executing benchmark: Name: %s{config.ToString()}, Runtime: %s{runtime.ToString()}, Run (%i{currentRun}/%i{totalRuns})"
                    let! res = runtime.Run(eff).AwaitAsync()
                    let time =
                        match res with
                        | Ok time -> time
                        | Error err -> invalidOp $"BenchmarkRunner: Failed executing benchmark with error: %A{err}"
                            
                    printfn $"[DEBUG]: Executing benchmark completed: Time: %i{time}ms, Name: %s{config.ToString()}, Runtime: %s{runtime.ToString()}, Run (%i{currentRun}/%i{totalRuns})"
                    results <- List.append results [(currentRun, time)]
                    
                return results
            }
    
    task {
        printfn $"[DEBUG]: Starting benchmark session: Name: %s{config.ToString()}, Runtime: %s{runtime.ToString()}, Runs: %i{totalRuns}"
        
        let eff =
            match config with
            | PingpongConfig roundCount ->
                Pingpong.Create <| PingpongConfig roundCount
            | ThreadringConfig (actorCount, roundCount) ->
                Threadring.Create <| ThreadringConfig (actorCount, roundCount)
            | BigConfig (actorCount, roundCount) ->
                Big.Create <| BigConfig (actorCount, roundCount)
            | BangConfig (actorCount, roundCount) ->
                Bang.Create <| BangConfig (actorCount, roundCount)
            | ForkConfig actorCount ->
                Fork.Create <| ForkConfig actorCount

        let! times = executeBenchmark eff
        
        #if DEBUG
        printfn $"[DEBUG]: Completed benchmark session: Name: %s{config.ToString()}, Runtime: %s{runtime.ToString()}, Total runs: %i{totalRuns}"
        #endif
        
        return
            { Config = config
              RuntimeName = runtime.ToString()
              RuntimeFileName = runtime.ToFileString()
              Times = times }
    }

let internal Run args =
    task {
        let actorInc, actorIncTimes = args.ActorIncrement
        let roundInc, roundIncTimes = args.RoundIncrement
        
        if args.Runs < 1 then
            invalidArg (nameof(args.Runs)) $"BenchmarkRunner: Runs argument must at least be 1. Runs = %i{args.Runs}"
        
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
        
        let results = List.map (runBenchmark args.Runtime args.Runs) configs
        
        for task in results do
            let! result = task
            printResult result
            if args.SaveToCsv then
                writeResultToCsv result args.SavePath
    }
