(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module private FSharp.FIO.Benchmarks.Plots.Program

open FSharp.FIO.Benchmarks.Plots.ArgParser
open FSharp.FIO.Benchmarks.Plots.ChartMaker

[<EntryPoint>]
let main args =
    printArgs args
    createAndShowCharts
    <| parseArgs args
    0
