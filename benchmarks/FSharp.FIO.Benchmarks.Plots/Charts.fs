(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module internal FSharp.FIO.Benchmarks.Plots.Charts

open FSharp.FIO.Benchmarks.Plots.DataParser

open Plotly.NET
open Plotly.NET.LayoutObjects
open Plotly.NET.TraceObjects

let private defaultLayout = Layout.init (
    PlotBGColor = Color.fromHex "#F8F8F8",
    PaperBGColor = Color.fromHex "#FFFFFF"
)

let private executionTimeHeaderIndex = 1

let createBoxPlot (data: (FileMetadata * BenchmarkData) list) (width: int) (height: int) color =
    
    let createChart (data: FileMetadata * BenchmarkData) =
        let metadata, data = data
        let x = List.replicate data.ExecutionTimes.Length (metadata.ToString ())
        let y = data.ExecutionTimes
        let yAxisLabel = data.Headers[executionTimeHeaderIndex] + " (lower is better)"
        Chart.BoxPlot(
            X = x,
            Y = y,
            ShowLegend = false,
            MarkerColor = Color.fromString color,
            BoxPoints = StyleParam.BoxPoints.SuspectedOutliers)
        |> Chart.withTraceInfo (metadata.ToString ())
        |> Chart.withSize (width, height)
        |> Chart.withXAxis (LinearAxis.init(TickAngle = 45)) 
        |> Chart.withYAxisStyle yAxisLabel
        |> Chart.withLayout defaultLayout
        
    Chart.combine
    <| List.map createChart data
    
let createLinePlot (data: (FileMetadata * BenchmarkData) list list) (width: int) (height: int) colors =
    
    let createChart (data: (FileMetadata * BenchmarkData) list) color =
        let x = List.map (fun x -> (fst x).ActorCount) data
        let y = List.map (fun x -> (snd x).ExecutionTimes |> List.averageBy float) data
        let yAxisLabel = (snd data.Head).Headers[executionTimeHeaderIndex] + " (lower is better)"
        Chart.Scatter(
            x,
            y,
            StyleParam.Mode.Lines_Markers,
            Marker = Marker.init (Color = Color.fromString color),
            Line = Line.init (Color = Color.fromString color),
            ShowLegend = true)
        |> Chart.withTraceInfo ((fst data.Head).ToString ())
        |> Chart.withSize (width, height)
        |> Chart.withXAxisStyle "Forked Fibers"
        |> Chart.withYAxisStyle yAxisLabel
        |> Chart.withLayout defaultLayout
    
    Chart.combine
    <| List.map (fun (data, color) -> createChart data color) (List.zip data colors)
