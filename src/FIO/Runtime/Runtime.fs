(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

namespace FIO.Runtime

open FIO.DSL

open System.Globalization

[<AutoOpen>]
module private Utils =
    
    let inline pop (contStack: ResizeArray<ContStackFrame>) =
        let lastIndex = contStack.Count - 1
        let stackFrame = contStack[lastIndex]
        contStack.RemoveAt lastIndex
        stackFrame

/// Functional runtime
[<AbstractClass>]
type FRuntime internal () =

    abstract member Name : string

    abstract member ConfigString : string

    override this.ConfigString =
        this.Name

    abstract member Run<'R, 'E> : FIO<'R, 'E> -> Fiber<'R, 'E>

    member this.ToFileString () =
        this.ToString()
            .ToLowerInvariant()
            .Replace("(", "")
            .Replace(")", "")
            .Replace(":", "")
            .Replace(' ', '-')
    
    override this.ToString () =
        this.ConfigString

type WorkerConfig =
    { EWCount: int
      EWSteps: int
      BWCount: int }

/// Functional worker runtime
[<AbstractClass>]
type FWorkerRuntime internal (config: WorkerConfig) as this =
    inherit FRuntime ()

    let validateWorkerConfiguration () =
        if config.EWCount <= 0 ||
           config.EWSteps <= 0 ||
           config.BWCount <= 0 then
            invalidArg "config" $"Invalid worker configuration! %s{this.ToString ()}"

    do validateWorkerConfiguration ()

    member _.GetWorkerConfiguration () =
        config

    override _.ConfigString =
        let ci = CultureInfo "en-US"
        $"""EWC: %s{config.EWCount.ToString("N0", ci)} EWS: %s{config.EWSteps.ToString("N0", ci)} BWC: %s{config.BWCount.ToString("N0", ci)}"""

    override this.ToString () =
        $"{this.Name} ({this.ConfigString})"
