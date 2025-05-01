(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(* --------------------------------------------------------------------------------------------------------- *)
(* Fork benchmark                                                                                            *)
(*************************************************************************************************************)

module internal FIO.Benchmarks.Suite.Fork

open FIO.Core
open FIO.Benchmarks.Tools.Timer

let private createActor timerChan = fio {
    let! _ = timerChan <-- Stop
    return ()
}

[<TailCall>]
let rec private createFork actorCount timerChan acc = fio {
    match actorCount with
    | 0 -> return! acc
    | count ->
        let newAcc = createActor timerChan <~> acc
        return! createFork (count - 1) timerChan newAcc
}

let internal Create config = fio {
    let actorCount =
        match config with
        | ForkConfig actors -> actors
        | _ -> invalidArg "config" "Fork benchmark requires a ForkConfig!"

    let timerChan = Channel<TimerMessage<int>>()
    let! timerFiber = !<~ (TimerEff 1 0 actorCount timerChan)
    let! _ = timerChan <-- Start
    let acc = createActor timerChan
    do! createFork (actorCount - 1) timerChan acc
    let! res = !<~~ timerFiber
    return res
}
