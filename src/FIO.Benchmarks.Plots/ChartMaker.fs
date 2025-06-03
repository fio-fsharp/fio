(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module internal FIO.Benchmarks.Plots.ChartMaker

open FIO.Benchmarks.Plots.Charts
open FIO.Benchmarks.Plots.DataParser

open Plotly.NET

open System.IO
    
type PlotType =
    | BoxPlots
    | LinePlots
    | All

let private generatePastelPurples (count: int) : string list =
    let generateSplitDeltas (n: int) : int list =
        let step = 30
        let half = n / 2
        let negatives = [ for i in 1 .. half -> -step * i ]
        let positives = [ for i in 1 .. half -> step * i ]
        let middle = if n % 2 = 1 then [0] else []
        negatives @ middle @ positives
    
    let deltas = generateSplitDeltas count
    
    let clamp v = max 0 (min 255 v)
    List.init count (fun index ->
        let dr = deltas[index % deltas.Length]
        let dg = deltas[(index + 1) % deltas.Length]
        let db = deltas[(index + 2) % deltas.Length]
        
        let baseR = 200 + dr
        let baseG = 150 + dg
        let baseB = 230 + db

        let r = clamp baseR
        let g = clamp baseG
        let b = clamp baseB

        $"rgb({r},{g},{b})"
    )

let private projectDirPath =
    Directory.GetCurrentDirectory ()
    |> Directory.GetParent
    |> _.Parent
    |> _.Parent
    |> _.Parent
    |> function
        | null -> failwith "Unexpected directory structure!"
        | di -> di.FullName

let private generateBoxPlotCharts () =
    let boxplotData = getAllCsvFilesData (projectDirPath + @"\FIO.Benchmarks.Plots\boxplot_data\")
    let colors = generatePastelPurples boxplotData.Length
    
    let titles, charts =
        List.map (fun (innerList, color) ->
            let metadata: FileMetadata = (innerList |> List.head |> fst)
            let width = innerList.Length * 500
            (metadata.Title(), createBoxPlot innerList width color)) (List.zip boxplotData colors)
            |> List.unzip
            
    charts, titles, boxplotData.Length + 1, 1
    
let private generateLineCharts () =
    let lineChartData =
        getAllCsvFilesData (projectDirPath + @"\FIO.Benchmarks.Plots\linechart_data\")
        |> List.collect id
        |> List.groupBy (fst >> _.BenchmarkName)
        |> List.map (fun (_, group) ->
            group
            |> List.groupBy (fst >> _.RuntimeName)
            |> List.map (snd >> List.sortBy (fst >> _.ActorCount)))                                    
    
    let titles, charts =
        List.map (fun innerList ->
            let metadata: FileMetadata = (innerList |> List.head |> List.head |> fst)
            let width = innerList.Length * 500
            let colors = generatePastelPurples innerList.Length
            (metadata.BenchmarkName, createLinePlot innerList width colors)) lineChartData
            |> List.unzip
            
    charts, titles, lineChartData.Length + 1, 1
    
let createAndShowCharts args =
    let charts, plotTitles, rows, cols =
        match args with
        | BoxPlots -> generateBoxPlotCharts ()
        | LinePlots -> generateLineCharts ()
        | All -> [], [], 0, 0
    Chart.Grid (rows, cols, SubPlotTitles = plotTitles) charts
    |> Chart.show
    