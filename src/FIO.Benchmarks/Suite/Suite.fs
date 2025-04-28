(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

namespace FIO.Benchmarks.Suite

open System.Globalization

open FIO.Runtime

type internal BenchmarkConfig =
    | PingpongConfig of rounds: int
    | ThreadringConfig of actors: int * rounds: int
    | BigConfig of actors: int * rounds: int
    | BangConfig of actors: int * rounds: int
    | ForkConfig of actors: int

    member internal this.Name() =
        match this with
        | PingpongConfig _ -> "Pingpong"
        | ThreadringConfig _ -> "Threadring"
        | BigConfig _ -> "Big"
        | BangConfig _ -> "Bang"
        | ForkConfig _ -> "Fork"

    member internal this.ConfigString() =
        let ci = CultureInfo("en-US")
        match this with
        | PingpongConfig rounds ->
            $"""Actors: 2 Rounds: %s{rounds.ToString("N0", ci)}"""
        | ThreadringConfig (actors, rounds) ->
            $"""Actors: %s{actors.ToString("N0", ci)} Rounds: %s{rounds.ToString("N0", ci)}"""
        | BigConfig (actors, rounds) ->
            $"""Actors: %s{actors.ToString("N0", ci)} Rounds: %s{rounds.ToString("N0", ci)}"""
        | BangConfig (actors, rounds) ->
            $"""Actors: %s{actors.ToString("N0", ci)} Rounds: %s{rounds.ToString("N0", ci)}"""
        | ForkConfig actors ->
            $"""Actors: %s{actors.ToString("N0", ci)} Rounds: 1"""

    override this.ToString() =
        $"{this.Name()} ({this.ConfigString()})"

type internal BenchmarkResult = 
    { Config: BenchmarkConfig
      RuntimeName: string
      RuntimeFileName: string
      Times: (int * int64) list }

type internal BenchmarkArgs = 
    { Runtime: FIORuntime
      Runs: int
      ActorIncrement: int * int
      RoundIncrement: int * int
      BenchmarkConfigs: BenchmarkConfig list
      SaveToCsv: bool 
      SavePath: string }
