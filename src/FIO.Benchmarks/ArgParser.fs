(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(************************************************************************************)

module internal ArgParser

open Argu

open FIO.Runtime

open FIO.Benchmarks.Suite

type Arguments =
    | Naive_Runtime
    | Intermediate_Runtime of evalworkers: int * blockingworkers: int * evalsteps: int
    | Advanced_Runtime of evalworkers: int * blockingworkers: int * evalsteps: int
    | Deadlocking_Runtime of evalworkers: int * blockingworkers: int * evalsteps: int
    | [<Mandatory>] Runs of runs: int
    | Process_Increment of actor_inc: int * inc_times: int
    | Pingpong of rounds: int
    | Threadring of actors: int * rounds: int
    | Big of actors: int * rounds: int
    | Bang of actors: int * rounds: int
    | Fork of actors: int

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Naive_Runtime -> "specify naive runtime."
            | Intermediate_Runtime _ ->
                "specify evaluation workers, blocking workers and eval steps for intermediate runtime."
            | Advanced_Runtime _ -> "specify evaluation workers, blocking workers and eval steps for advanced runtime."
            | Deadlocking_Runtime _ ->
                "specify evaluation workers, blocking workers and eval steps for deadlocking runtime."
            | Runs _ -> "specify the number of runs for each benchmark."
            | Process_Increment _ -> "specify the value of actor increment and how many times."
            | Pingpong _ -> "specify rounds for pingpong benchmark."
            | Threadring _ -> "specify actors and rounds for threadring benchmark."
            | Big _ -> "specify actors and rounds for big benchmark."
            | Bang _ -> "specify actors and rounds for bang benchmark."
            | Fork _ -> "specify actors for fork benchmark."

type Parser() =
    let parser = ArgumentParser.Create<Arguments>()

    member _.PrintArgs args =
        let args = List.fold (fun s acc -> s + " " + acc) "" (List.ofArray args)
        printfn $"benchmark arguments:%s{args}"

    member _.PrintUsage() = printfn $"%s{parser.PrintUsage()}"

    member _.ParseArgs args =
        let results = parser.Parse args
        let runs = results.GetResult Runs

        let processIncrement =
            match results.TryGetResult Process_Increment with
            | Some(x, y) -> x, y
            | _ -> 0, 0

        let pingpongConfig =
            match results.TryGetResult Pingpong with
            | Some rounds -> 
                [ Runner.PingpongConfig (rounds) ]
            | _ -> []

        let threadringConfig =
            match results.TryGetResult Threadring with
            | Some(actors, rounds) ->
                [ Runner.ThreadringConfig (actors, rounds) ]
            | _ -> []

        let bigConfig =
            match results.TryGetResult Big with
            | Some(actors, rounds) ->
                [ Runner.BigConfig (actors, rounds) ]
            | _ -> []

        let bangConfig =
            match results.TryGetResult Bang with
            | Some(actors, rounds) ->
                [ Runner.BangConfig (actors, rounds) ]
            | _ -> []

        let forkConfig =
            match results.TryGetResult Fork with
            | Some actors -> 
                [ Runner.ForkConfig (actors) ]
            | _ -> []

        let configs =
            pingpongConfig @ threadringConfig @ bigConfig @ bangConfig @ forkConfig

        let runtime: Runtime =
            match results.TryGetResult Naive_Runtime with
            | Some _ -> Naive.NaiveRuntime()
            | _ ->
                match results.TryGetResult Intermediate_Runtime with
                | Some(ewc, bwc, esc) -> Intermediate.IntermediateRuntime(ewc, bwc, esc)
                | _ ->
                    match results.TryGetResult Advanced_Runtime with
                    | Some(ewc, bwc, esc) -> Advanced.AdvancedRuntime(ewc, bwc, esc)
                    | _ ->
                        match results.TryGetResult Deadlocking_Runtime with
                        | Some(ewc, bwc, esc) -> Deadlocking.DeadlockingRuntime(ewc, bwc, esc)
                        | _ -> failwith "ArgParser: Invalid runtime specified!"

        (configs, runtime, runs, processIncrement)
