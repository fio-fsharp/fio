module rec FIO.Benchmarks.Plots.BoxPlots

open Plotly.NET

open FIO.Benchmarks.Plots.DataParser

let internal CreateBoxPlot (data: (FileMetadata * BenchmarkData) list) =
    let colors = ["rgb(128,0,128)"; "rgb(139,0,139)"; "rgb(148,0,211)"; "rgb(186,85,211)"; "rgb(255,0,255)";
                  "rgb(128,0,128)"; "rgb(139,0,139)"; "rgb(148,0,211)"; "rgb(186,85,211)";]

    let createChart (data: FileMetadata * BenchmarkData) color = 
        let (metadata, data) = data
        let x = List.replicate data.Results.Length (metadata.ToString())
        let y = data.Results
        let boxPlot = Chart.BoxPlot(X=x, Y=y, MarkerColor =  Color.fromString color, BoxPoints=StyleParam.BoxPoints.SuspectedOutliers)
        boxPlot
        |> Chart.withTraceInfo (metadata.ToString())
        |> Chart.withSize (2000, 1000)
        |> Chart.withYAxisStyle (data.Header + " (lower is better)")
    let x = (List.zip data colors)
    let charts = List.map (fun ((metadata, data), color) -> createChart (metadata, data) color) (List.zip data colors)
    Chart.combine charts