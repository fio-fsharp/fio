(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

namespace FIO.Runtime

open FIO.Core

[<AbstractClass>]
type FIORuntime() =
    abstract member Run : FIO<'R, 'E> -> Fiber<'R, 'E>

type WorkerConfig =
    { EWCount: int
      EWSteps: int
      BWCount: int }

[<AbstractClass>]
type FIOWorkerRuntime(config: WorkerConfig) =
    inherit FIORuntime()

    let validateWorkerConfiguration () =
        if config.EWCount <= 0 ||
           config.EWSteps <= 0 ||
           config.BWCount <= 0 then
            invalidArg "config" "Invalid worker configuration!"

    do validateWorkerConfiguration ()

    member this.GetWorkerConfiguration () =
        config
