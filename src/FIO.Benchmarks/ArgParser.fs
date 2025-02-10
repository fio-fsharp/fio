(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module internal ArgParser

open Argu

open System.IO

open FIO.Runtime
open FIO.Benchmarks.Suite

type internal Arguments =
    | Native_Runtime
    | Intermediate_Runtime of ewc: int * ews: int * bwc: int
    | Advanced_Runtime of ewc: int * ews: int * bwc: int
    | Deadlocking_Runtime of ewc: int * ews: int * bwc: int
    | Runs of runs: int
    | Actor_Increment of actorInc: int * times: int
    | Round_Increment of roundInc: int * times: int
    | Pingpong of rounds: int
    | Threadring of actors: int * rounds: int
    | Big of actors: int * rounds: int
    | Bang of actors: int * rounds: int
    | Fork of actors: int
    | Save of saveToCsv: bool
    | SavePath of absolutePath: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Native_Runtime -> 
                "specify Native runtime"
            | Intermediate_Runtime _ -> 
                "specify Intermediate runtime with ewc, ews and bwc"
            | Advanced_Runtime _ -> 
                "specify Advanced runtime with ewc, ews and bwc"
            | Deadlocking_Runtime _ -> 
                "specify Deadlocking runtime with ewc, ews and bwc"
            | Runs _ -> 
                "specify number of runs for each benchmark"
            | Actor_Increment _ -> 
                "specify the value of actor increment and the number of times"
            | Round_Increment _ -> 
                "specify the value of round increment and the number of times"
            | Pingpong _ -> 
                "specify number of rounds for Pingpong benchmark"
            | Threadring _ -> 
                "specify number of actors and rounds for Threadring benchmark"
            | Big _ -> 
                "specify number of actors and rounds for Big benchmark"
            | Bang _ -> 
                "specify number of actors and rounds for Bang benchmark"
            | Fork _ -> 
                "specify number of actors for Fork benchmark"
            | Save _ -> 
                "should save benchmark results to csv file"
            | SavePath _ ->
                "specify absolute path to save the benchmark results csv file"

type internal Parser() =
    let parser = ArgumentParser.Create<Arguments>()

    member internal this.PrintArgs args =
        let argsStr = String.concat " " (Array.toList args)
        printfn $"benchmark arguments: %s{argsStr}"

    member internal this.PrintUsage() =
        printfn "%s" (parser.PrintUsage())

    member internal this.ParseArgs args =
        let results = parser.Parse args

        let runtime: FIORuntime =
            if results.Contains Native_Runtime then
                Native.Runtime()
            elif results.Contains Intermediate_Runtime then
                let (ewc, ews, bwc) = results.GetResult Intermediate_Runtime
                Intermediate.Runtime({ EWCount = ewc; EWSteps = ews; BWCount = bwc })
            elif results.Contains Advanced_Runtime then
                let (ewc, ews, bwc) = results.GetResult Advanced_Runtime
                Advanced.Runtime({ EWCount = ewc; EWSteps = ews; BWCount = bwc })
            elif results.Contains Deadlocking_Runtime then
                let (ewc, ews, bwc) = results.GetResult Advanced_Runtime
                Deadlocking.Runtime({ EWCount = ewc; EWSteps = ews; BWCount = bwc })
            else
                invalidArg "args" "Runtime should be specified!"

        let runs = results.TryGetResult Runs |> Option.defaultValue 1 

        let actorInc = results.TryGetResult Actor_Increment |> Option.defaultValue (0, 0)

        let roundInc = results.TryGetResult Round_Increment |> Option.defaultValue (0, 0)

        let configs =
            [ results.TryGetResult Pingpong |> Option.map (fun rounds -> PingpongConfig rounds)
              results.TryGetResult Threadring |> Option.map (fun (actors, rounds) -> ThreadringConfig (actors, rounds))
              results.TryGetResult Big |> Option.map (fun (actors, rounds) -> BigConfig (actors, rounds))
              results.TryGetResult Bang |> Option.map (fun (actors, rounds) -> BangConfig (actors, rounds))
              results.TryGetResult Fork |> Option.map  (fun actors -> ForkConfig actors) ]
            |> List.choose id

        if configs.IsEmpty then
            invalidArg "args" "At least one benchmark should be specified!"

        let saveToCsv = results.TryGetResult Save |> Option.defaultValue false

        let projectDirPath =
            Directory.GetCurrentDirectory()
            |> Directory.GetParent
            |> (fun di -> di.Parent)
            |> (fun di -> di.Parent)
            |> function
                | null -> failwith "Unexpected directory structure!"
                | di -> di.FullName
        let savePath = results.TryGetResult SavePath |> Option.defaultValue (projectDirPath + @"\data\")

        { Runtime = runtime
          Runs = runs
          ActorIncrement = actorInc
          RoundIncrement = roundInc
          BenchmarkConfigs = configs
          SaveToCsv = saveToCsv
          SavePath = savePath }
