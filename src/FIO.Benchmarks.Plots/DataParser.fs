(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module internal FIO.Benchmarks.Plots.DataParser

open FSharp.Data

open System
open System.IO
open System.Globalization

type FileMetadata =
    { BenchmarkName: string
      ActorCount: int
      RoundCount: int
      Runs: int
      RuntimeName: string
      WorkerMetaData: WorkerMetaData option }

    member this.Title () =
        let ci = CultureInfo("en-US")
        $"""%s{this.BenchmarkName} (Actor Count: %s{this.ActorCount.ToString("N0", ci)} Round Count: %s{this.RoundCount.ToString("N0", ci)})"""

    override this.ToString () = 
        match this.WorkerMetaData with
        | Some metadata -> $"%s{this.RuntimeName} (%s{metadata.ToString()})"
        | None -> this.RuntimeName

and WorkerMetaData =
    { EWC: int
      EWS: int
      BWC: int }

    override this.ToString () = 
        let ci = CultureInfo("en-US")
        $"""EWC: %s{this.EWC.ToString("N0", ci)} EWS: %s{this.EWS.ToString("N0", ci)} BWC: %s{this.BWC.ToString("N0", ci)}"""

and BenchmarkData =
    { Header: string 
      Results: int64 list }

let private parseBenchmarkData (path: string) =
    let csvFile = CsvFile.Load path
    { Header = 
        match csvFile.Headers with
        | Some header -> header[0] + " (lower is better)"
        | None -> failwith "No header found in CSV file!"
      Results = csvFile.Rows |> Seq.map (fun row -> int64 (row.Item 0)) |> List.ofSeq }

let private parseFileMetadata (path: string) =
    let fileName = path.ToLowerInvariant().Split Path.DirectorySeparatorChar |> Array.last
    let split = fileName.Split('_').[0].Split('-') |> fun s -> s[..s.Length - 2]

    let bench = split[0].Trim()
    let runtime = split[9].Trim()

    let isWorkerRuntime =
        match runtime with
        | "direct" -> false
        | "cooperative" | "concurrent" -> true
        | _ -> invalidOp "Unknown runtime!"

    let capitalizeFirst s =
        match s with
        | "" -> s
        | _  -> s[0] |> Char.ToUpper |> string |> fun first -> first + s[1..]

    { BenchmarkName = capitalizeFirst bench
      ActorCount = int split[3]
      RoundCount = int split[6]
      Runs = int split[8]
      RuntimeName = capitalizeFirst runtime
      WorkerMetaData =
          if isWorkerRuntime then
              Some { EWC = int split[11]
                     EWS = int split[13]
                     BWC = int split[15] }
          else None }

let getAllCsvFilesData rootDir =
    if not <| Directory.Exists rootDir then
        invalidArg rootDir "Directory does not exist!"
    Directory.GetDirectories rootDir
    |> Array.toList
    |> List.map (fun benchDir ->
        Directory.GetFiles(benchDir, "*.csv", SearchOption.AllDirectories)
        |> Array.toList
        |> List.rev
        |> List.map (fun file -> (parseFileMetadata file, parseBenchmarkData file)))
