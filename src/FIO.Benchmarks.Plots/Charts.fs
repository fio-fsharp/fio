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
        let x = List.replicate data.Results.Length (metadata.ToString())
        let y = data.Results
        Chart.BoxPlot(
            X = x,
            Y = y,
            ShowLegend = true,
            MarkerColor = Color.fromString color,
            BoxPoints = StyleParam.BoxPoints.All)
        |> Chart.withTraceInfo (metadata.ToString())
        |> Chart.withSize (width, 4000)
        |> Chart.withYAxisStyle data.Header
        
    Chart.combine
    <| List.map createChart data
    
let createLinePlot (data: (FileMetadata * BenchmarkData) list list) width colors =
    
    let createChart (data: (FileMetadata * BenchmarkData) list) color =
        let x = List.map (fun x -> (fst x).ActorCount) data
        let y = List.map (fun x -> (snd x).Results |> List.averageBy float) data
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
        |> Chart.withYAxisStyle (snd data.Head).Header

    Chart.combine
    <| List.map (fun (data, color) -> createChart data color) (List.zip data colors)
