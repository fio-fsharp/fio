module private Program

open System.IO

open Plotly.NET

open FIO.Benchmarks.Plots
open FIO.Benchmarks.Plots.DataParser
open Plotly.NET.LayoutObjects

[<EntryPoint>]
let main _ =
    let projectDirPath =
        Directory.GetCurrentDirectory()
        |> Directory.GetParent
        |> (fun di -> di.Parent)
        |> (fun di -> di.Parent)
        |> (fun di -> di.Parent)
        |> function
            | null -> failwith "Unexpected directory structure!"
            | di -> di.FullName

    let data = GetAllCsvFiles (projectDirPath + @"\FIO.Benchmarks\data\")

    let rows = (data.Length + 1) / 2
    let cols = if data.Length = 1 then 1 else 2

    let (titles, charts) =
        List.map (fun innerList ->
            let metadata: FileMetadata = (innerList |> List.head |> fst)
            (metadata.Title(), BoxPlots.CreateBoxPlot innerList)
            ) data
            |> List.unzip

    Chart.Grid(rows, cols, SubPlotTitles = titles) charts
    |> Chart.show
    0 