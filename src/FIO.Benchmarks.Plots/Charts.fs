(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module internal FIO.Benchmarks.Plots.Charts

open FIO.Benchmarks.Plots.DataParser

open Plotly.NET
open Plotly.NET.TraceObjects

let createBoxPlot (data: (FileMetadata * BenchmarkData) list) width color =
    
    let createChart (data: FileMetadata * BenchmarkData) =
        let metadata, data = data
        let x = List.replicate data.ExecutionTimes.Length (metadata.ToString())
        let y = data.ExecutionTimes
        Chart.BoxPlot(
            X = x,
            Y = y,
            ShowLegend = false,
            MarkerColor = Color.fromString color,
            BoxPoints = StyleParam.BoxPoints.SuspectedOutliers)
        |> Chart.withTraceInfo (metadata.ToString())
        |> Chart.withSize (width, 4000)
        |> Chart.withYAxisStyle (data.Headers[0] + " (lower is better)")
        |> Chart.withLayout (Layout.init (
            PlotBGColor = Color.fromHex "#DCDCDC",
            PaperBGColor = Color.fromString "white"
        ))
        
    Chart.combine
    <| List.map createChart data
    
let createLinePlot (data: (FileMetadata * BenchmarkData) list list) width colors =
    
    let createChart (data: (FileMetadata * BenchmarkData) list) color =
        let x = List.map (fun x -> (fst x).ActorCount) data
        let y = List.map (fun x -> (snd x).ExecutionTimes |> List.averageBy float) data
        Chart.Scatter(
            x,
            y,
            StyleParam.Mode.Lines_Markers,
            Marker = Marker.init(Color = Color.fromString color),
            Line = Line.init(Color = Color.fromString color),
            ShowLegend = true)
        |> Chart.withTraceInfo ((fst data.Head).ToString())
        |> Chart.withSize (width, 2000)
        |> Chart.withXAxisStyle "Forked Fibers"
        |> Chart.withYAxisStyle ((snd data.Head).Headers[0] + " (lower is better)") 
        |> Chart.withLayout (Layout.init (
            PlotBGColor = Color.fromHex "#DCDCDC",
            PaperBGColor = Color.fromString "white"
        ))
    
    Chart.combine
    <| List.map (fun (data, color) -> createChart data color) (List.zip data colors)
