(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(* -------------------------------------------------------------------------------- *)
(* Pingpong benchmark                                                               *)
(* Measures: Message delivery overhead                                              *)
(* Savina benchmark #1                                                              *)
(* (http://soft.vub.ac.be/AGERE14/papers/ageresplash2014_submission_19.pdf)         *)
(************************************************************************************)

module internal FIO.Benchmarks.Suite.Pingpong

open FIO.Core

open System.Diagnostics

type private Actor =
    { Name: string
      SendingChannel: int channel
      ReceivingChannel: int channel }

[<TailCall>]
let rec private createPingerHelper rounds actor message (stopwatch: Stopwatch) = fio {
    if rounds = 0 then
        do! !+ stopwatch.Stop()
        return stopwatch.ElapsedMilliseconds    
    else
        do! actor.SendingChannel <!- message
        #if DEBUG
        do! !+ printfn($"DEBUG: %s{actor.Name} sent ping: %i{message}")
        #endif
        let! received = !<-- actor.ReceivingChannel
        #if DEBUG
        do! !+ printfn($"DEBUG: %s{actor.Name} received pong: %i{received}")
        #endif
        return! createPingerHelper (rounds - 1) actor (received + 1) stopwatch
}

[<TailCall>]
let rec private createPongerHelper rounds actor : FIO<unit, obj> = fio {
    if rounds = 0 then
        return ()
    else
        let! received = !<-- actor.ReceivingChannel
        #if DEBUG
        do! !+ printfn($"DEBUG: %s{actor.Name} received ping: %i{received}")
        #endif
        let! message = !+ (received + 1)
        do! actor.SendingChannel <!- message
        #if DEBUG
        do! !+ printfn($"DEBUG: %s{actor.Name} sent pong: %i{message}")
        #endif
        return! createPongerHelper (rounds - 1) actor
}

let private createPinger actor startSignalChannel rounds = fio {
    let! stopwatch = !+ Stopwatch()
    do! !<!- startSignalChannel
    do! !+ stopwatch.Start()
    return! createPingerHelper rounds actor 1 stopwatch
}

let private createPonger actor startSignalChannel rounds = fio {
    do! startSignalChannel <!- 0
    return! createPongerHelper rounds actor
}

let internal Create rounds : FIO<BenchmarkResult, obj> = fio {
    let startSignalChannel = Channel<int>()
    let pingSendingChannel = Channel<int>()
    let pongSendingChannel = Channel<int>()

    let pinger =
        { Name = "Pinger"
          SendingChannel = pingSendingChannel
          ReceivingChannel = pongSendingChannel }

    let ponger =
        { Name = "Ponger"
          SendingChannel = pongSendingChannel
          ReceivingChannel = pingSendingChannel }

    let! (result, _) = createPinger pinger startSignalChannel rounds
                       <*> createPonger ponger startSignalChannel rounds
    return result
}
