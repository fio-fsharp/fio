(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(* -------------------------------------------------------------------------------- *)
(* Fork benchmark                                                                   *)
(************************************************************************************)

[<AutoOpen>]
module internal FIO.Benchmarks.Suite.Fork

open FIO.Benchmarks.Tools.Timing.StopwatchTimer

open FIO.Core
open System.Diagnostics

let rec private createActor timerChannel = fio {
    return! timerChannel <!- TimerMessage.Stop
}

[<TailCall>]
let rec private createForkTime actorCount timerChannel acc = fio {
    match actorCount with
    | 0 -> 
        return! acc
    | count ->
        let newAcc = createActor timerChannel <!> acc
        return! createForkTime (count - 1) timerChannel newAcc
}

let internal Create actorCount : FIO<BenchmarkResult, obj> = fio {
    let! timerChannel = !+ Channel<TimerMessage>()
    let! stopwatch = !+ Stopwatch()
    
    let! timerFiber = ! TimerEffect(actorCount, timerChannel)
    do! !+ stopwatch.Start()
    do! timerChannel <!- TimerMessage.Start stopwatch

    let acc = createActor timerChannel <!> createActor timerChannel
    do! createForkTime (actorCount - 2) timerChannel acc
    let! result = !? timerFiber
    return result
}
