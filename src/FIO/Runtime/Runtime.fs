(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(************************************************************************************)

namespace FIO.Runtime

open FIO.Core

type WorkerConfig =
    { EvaluationWorkerCount: int
      EvaluationWorkerSteps: int 
      BlockingWorkerCount: int }

[<AbstractClass>]
type FIORuntime() =
    abstract member Run : FIO<'R, 'E> -> Fiber<'R, 'E>

[<AbstractClass>]
type FIOWorkerRuntime(config: WorkerConfig) =
    inherit FIORuntime()

    let validateWorkerConfiguration () =
        if config.EvaluationWorkerCount <= 0 ||
           config.EvaluationWorkerSteps <= 0 ||
           config.BlockingWorkerCount <= 0 then
            invalidArg "config" "Invalid worker configuration!"

    do validateWorkerConfiguration ()

    member this.GetWorkerConfiguration () =
        config
