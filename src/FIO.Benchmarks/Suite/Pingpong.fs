(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(* --------------------------------------------------------------------------------------------------------- *)
(* Pingpong benchmark                                                                                        *)
(* Measures: Message delivery overhead                                                                       *)
(* Savina benchmark #1                                                                                       *)
(* (http://soft.vub.ac.be/AGERE14/papers/ageresplash2014_submission_19.pdf)                                  *)
(*************************************************************************************************************)

module internal FIO.Benchmarks.Suite.Pingpong

open FIO.Core
open FIO.Benchmarks.Tools.Timer

type private Actor =
    { Name: string
      SendChan: int channel
      RecvChan: int channel }

[<TailCall>]
let rec private createPingerHelper pinger rounds ping timerChan = fio {
    if rounds <= 0 then
        let! _ = timerChan <-- Stop
        return ()
    else
        let! _ = pinger.SendChan <-- ping
        #if DEBUG
        printfn $"[DEBUG]: %s{pinger.Name} sent ping: %i{ping}"
        #endif
        let! pong = !<-- pinger.RecvChan
        #if DEBUG
        printfn $"[DEBUG]: %s{pinger.Name} received pong: %i{pong}"
        #endif
        return! createPingerHelper pinger (rounds - 1) (pong + 1) timerChan
}

[<TailCall>]
let rec private createPongerHelper ponger rounds = fio {
    if rounds <= 0 then
        return ()
    else
        let! ping = !<-- ponger.RecvChan
        #if DEBUG
        printfn $"[DEBUG]: %s{ponger.Name} received ping: %i{ping}"
        #endif
        let! pong = ponger.SendChan <-- ping + 1
        #if DEBUG
        printfn $"[DEBUG]: %s{ponger.Name} sent pong: %i{pong}"
        #endif
        return! createPongerHelper ponger (rounds - 1)
}

let private createPinger pinger rounds startChan timerChan = fio {
    let! _ = !<-- startChan
    let! _ = timerChan <-- Start
    return! createPingerHelper pinger rounds 1 timerChan
}

let private createPonger ponger rounds startChan = fio {
    let! _ = startChan <-- 0
    return! createPongerHelper ponger rounds
}

let internal Create config = fio {
    let rounds =
        match config with
        | PingpongConfig rounds -> rounds
        | _ -> invalidArg "config" "Pingpong benchmark requires a PingpongConfig!"

    let startChan = Channel<int>()
    let timerChan = Channel<TimerMessage<int>>()
    let pingSendChan = Channel<int>()
    let pongSendChan = Channel<int>()

    let pinger =
        { Name = "Pinger"
          SendChan = pingSendChan
          RecvChan = pongSendChan }

    let ponger =
        { Name = "Ponger"
          SendChan = pongSendChan
          RecvChan = pingSendChan }

    let! timerFiber = !<~ (TimerEff 1 0 1 timerChan)
    do! createPinger pinger rounds startChan timerChan
        <~> createPonger ponger rounds startChan
    let! res = !<~~ timerFiber
    return res
}
