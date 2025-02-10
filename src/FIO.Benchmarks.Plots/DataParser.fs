module rec FIO.Benchmarks.Plots.DataParser

open FSharp.Data

open System
open System.IO
open System.Globalization

type internal BenchmarkData =
    { Header: string 
      Results: int list }

type internal FileMetadata =
    { BenchmarkName: string
      Actors: int
      Rounds: int
      Runs: int
      RuntimeName: string
      WorkerMetaData: WorkerMetaData option }

    member this.Title () =
        let ci = CultureInfo("en-US")
        $"""%s{this.BenchmarkName} (Actors: %s{this.Actors.ToString("N0", ci)} Rounds: %s{this.Rounds.ToString("N0", ci)})"""

    override this.ToString () = 
        match this.WorkerMetaData with
        | Some metadata -> $"%s{this.BenchmarkName} - %s{this.RuntimeName} (%s{metadata.ToString()})"
        | None -> $"%s{this.BenchmarkName} - %s{this.RuntimeName}"

type internal WorkerMetaData =
    { EWC: int
      EWS: int
      BWC: int }

    override this.ToString () = 
        let ci = CultureInfo("en-US")
        $"""EWC: %s{this.EWC.ToString("N0", ci)} EWS: %s{this.EWS.ToString("N0", ci)} BWC: %s{this.BWC.ToString("N0", ci)}"""

let private parseBenchmarkData (path: string) =
    let csvFile = CsvFile.Load path
    { Header = 
        match csvFile.Headers with
        | Some header -> header[0]
        | None -> failwith "No header found in CSV file!"
      Results = csvFile.Rows |> Seq.map (fun row -> int (row.Item 0)) |> List.ofSeq }

let private parseFileMetadata (path: string) =
    let fileName = path.ToLowerInvariant().Split Path.DirectorySeparatorChar |> Array.last
    let split = fileName.Split('_').[0].Split('-') |> fun s -> s.[..s.Length - 2]

    let bench = split[0].Trim()
    let runtime = split[7].Trim()

    let isWorkerRuntime =
        match runtime with
        | "native" -> false
        | "advanced" | "intermediate" -> true
        | _ -> invalidOp "Unknown runtime!"

    let capitalizeFirst s =
        match s with
        | "" -> s
        | _  -> s[0] |> Char.ToUpper |> string |> fun first -> first + s[1..]

    { BenchmarkName = capitalizeFirst bench
      Actors = int split[2]
      Rounds = int split[4]
      Runs = int split[6]
      RuntimeName = capitalizeFirst runtime
      WorkerMetaData =
          if isWorkerRuntime then
              Some { EWC = int split[9]
                     EWS = int split[11]
                     BWC = int split[13] }
          else None }

let internal GetAllCsvFiles rootDir =
    Directory.GetDirectories rootDir
    |> Array.toList
    |> List.map (fun benchDir ->
        Directory.GetFiles(benchDir, "*.csv", SearchOption.AllDirectories)
        |> Array.toList
        |> List.rev
        |> List.map (fun file -> (parseFileMetadata file, parseBenchmarkData file)))
