﻿(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

namespace FIO.Runtime

open System.Globalization

open FIO.Core

[<AbstractClass>]
type FIORuntime() =
    abstract member Run : FIO<'R, 'E> -> Fiber<'R, 'E>
    abstract member Name : unit -> string
    abstract member ConfigString : unit -> string

    override this.ConfigString () =
        this.Name()

    override this.ToString () = 
        this.ConfigString()

type WorkerConfig =
    { EWCount: int
      EWSteps: int
      BWCount: int }

[<AbstractClass>]
type FIOWorkerRuntime(config: WorkerConfig) =
    inherit FIORuntime()
    let ci = CultureInfo("en-US")

    let validateWorkerConfiguration () =
        if config.EWCount <= 0 ||
           config.EWSteps <= 0 ||
           config.BWCount <= 0 then
            invalidArg "config" "Invalid worker configuration!"

    do validateWorkerConfiguration ()

    member this.GetWorkerConfiguration () =
        config

    override this.ConfigString () =
        $"""EWC: %s{config.EWCount.ToString("N0", ci)} EWS: %s{config.EWSteps.ToString("N0", ci)} BWC: %s{config.BWCount.ToString("N0", ci)}"""

    override this.ToString () =
        $"{this.Name()} ({this.ConfigString()})"
