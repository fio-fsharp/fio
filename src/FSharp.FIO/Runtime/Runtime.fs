(*********************************************************************************************)
(* FIO - A Type-Safe, Purely Functional Effect System for Asynchronous and Concurrent F#     *)
(* Copyright (c) 2022-2025 - Daniel "iyyel" Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                                       *)
(*********************************************************************************************)

namespace FSharp.FIO.Runtime

open FSharp.FIO.DSL

open System.Globalization

[<AutoOpen>]
module private Utils =
    
    let inline pop (contStack: ResizeArray<ContStackFrame>) =
        let lastIndex = contStack.Count - 1
        let stackFrame = contStack[lastIndex]
        contStack.RemoveAt lastIndex
        stackFrame

/// <summary>
/// Represents a functional runtime for interpreting FIO effects.
/// </summary>
[<AbstractClass>]
type FRuntime internal () =
    /// <summary>
    /// Gets the name of the runtime.
    /// </summary>
    abstract member Name : string

    /// <summary>
    /// Gets the configuration string for the runtime.
    /// </summary>
    abstract member ConfigString : string

    override this.ConfigString =
        this.Name

    /// <summary>
    /// Runs an FIO effect and returns a fiber representing its execution.
    /// </summary>
    /// <typeparam name="R">The result type.</typeparam>
    /// <typeparam name="E">The error type.</typeparam>
    /// <param name="eff">The FIO effect to run.</param>
    /// <returns>A fiber representing the running effect.</returns>
    abstract member Run<'R, 'E> : FIO<'R, 'E> -> Fiber<'R, 'E>

    /// <summary>
    /// Gets a file-friendly string representation of the runtime.
    /// </summary>
    /// <returns>A string suitable for file names.</returns>
    member this.ToFileString () =
        this.ToString()
            .ToLowerInvariant()
            .Replace("(", "")
            .Replace(")", "")
            .Replace(":", "")
            .Replace(' ', '-')
    
    override this.ToString () =
        this.ConfigString

/// <summary>
/// Represents the configuration for a worker runtime.
/// </summary>
type WorkerConfig =
    { EWC: int
      EWS: int
      BWC: int }

/// <summary>
/// Represents a functional worker runtime for interpreting FIO effects.
/// </summary>
[<AbstractClass>]
type FWorkerRuntime internal (config: WorkerConfig) as this =
    inherit FRuntime ()

    let validateWorkerConfiguration () =
        if config.EWC <= 0 ||
           config.EWS <= 0 ||
           config.BWC <= 0 then
            invalidArg "config" $"Invalid worker configuration! %s{this.ToString ()}"

    do validateWorkerConfiguration ()

    /// <summary>
    /// Gets the worker configuration.
    /// </summary>
    /// <returns>The worker configuration.</returns>
    member _.GetWorkerConfiguration () =
        config

    override _.ConfigString =
        let ci = CultureInfo "en-US"
        $"""EWC: %s{config.EWC.ToString("N0", ci)} EWS: %s{config.EWS.ToString("N0", ci)} BWC: %s{config.BWC.ToString("N0", ci)}"""

    override this.ToString () =
        $"{this.Name} ({this.ConfigString})"
