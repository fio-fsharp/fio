(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module internal FIO.Benchmarks.Plots.ArgParser

open FIO.Benchmarks.Plots.ChartMaker

open Argu

type private Arguments =
    | [<Unique; AltCommandLine("-b")>] BoxPlots
    | [<Unique; AltCommandLine("-l")>] LinePlots
    | [<Unique; AltCommandLine("-a")>] All
    
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | BoxPlots -> "Generate box plots"
            | LinePlots -> "Generate line plots"
            | All -> "Generate all plots (box + line)"

let private parser =
    ArgumentParser.Create<Arguments>(programName = "FIO.Benchmarks.Plots")

let printArgs args =
    printfn "%s arguments: %s" parser.ProgramName (String.concat " " args)

let printUsage () =
    printfn $"%s{parser.PrintUsage ()}"

let parseArgs args =
    let results = parser.Parse args
    match results.GetAllResults() with
    | [BoxPlots] -> PlotType.BoxPlots
    | [LinePlots] -> PlotType.LinePlots
    | [All] -> PlotType.All
    | [] -> invalidArg "args" "You must specify one of: --boxplots, --lineplots, or --all."
    | _ -> invalidArg "args" "Only one plot type can be specified at a time."
