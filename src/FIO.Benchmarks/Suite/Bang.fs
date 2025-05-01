(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(* --------------------------------------------------------------------------------------------------------- *)
(* Bang benchmark                                                                                            *)
(* Measures: Many-to-One message passing                                                                     *)
(* A Scalability Benchmark Suite for Erlang/OTP                                                              *)
(* (https://dl.acm.org/doi/10.1145/2364489.2364495I)                                                         *)
(*************************************************************************************************************)

module internal FIO.Benchmarks.Suite.Bang

open FIO.Core
open FIO.Benchmarks.Tools.Timer

type private Actor = 
    { Name: string; 
      Chan: int channel }

[<TailCall>]
let rec private createSendActorHelper actor rounds msg = fio {
    if rounds <= 0 then
        return ()
    else
        let! _ = actor.Chan <-- msg
        #if DEBUG
        printfn $"DEBUG: %s{actor.Name} sent: %i{msg}"
        #endif
        return! createSendActorHelper actor (rounds - 1) msg
}

let rec private createSendActor actor rounds msg timerChan startChan = fio {
    let! _ = timerChan <-- Start
    let! _ = !<-- startChan
    return! createSendActorHelper actor rounds msg
}

[<TailCall>]
let rec private createRecvActorHelper actor rounds timerChan = fio {
    if rounds <= 0 then
        let! _ = timerChan <-- Stop
        return ()
    else
        let! msg = !<-- actor.Chan
        #if DEBUG
        printfn $"DEBUG: %s{actor.Name} received: %i{msg}"
        #endif
        return! createRecvActorHelper actor (rounds - 1) timerChan
}

let rec private createRecvActor actor rounds timerChan startChan = fio {
    let! _ = timerChan <-- Start
    let! _ = !<-- startChan
    return! createRecvActorHelper actor rounds timerChan
}

[<TailCall>]
let rec private createBang recvActor sendActors rounds msg timerChan startChan acc = fio {
    match sendActors with
    | [] -> return! acc
    | ac :: acs ->
        let newAcc = createSendActor ac rounds msg timerChan startChan <~> acc
        return! createBang recvActor acs rounds (msg + 1) timerChan startChan newAcc
}

let private createSendActors recvActorChan actorCount =
    List.map (fun index ->
            { Name = $"Actor-{index}"
              Chan = recvActorChan })
        [ 1..actorCount ]

let internal Create config = fio {
    let actorCount, rounds =
        match config with
        | BangConfig (actors, rounds) -> (actors, rounds)
        | _ -> invalidArg "config" "Bang benchmark requires a BangConfig!"

    let timerChan = Channel<TimerMessage<int>>()
    let startChan = Channel<int>()

    let recvActor =
        { Name = "Actor-0"
          Chan = Channel<int>() }

    let sendActors = createSendActors recvActor.Chan actorCount

    let ac, acs =
        match List.rev sendActors with
        | ac :: acs -> (ac, acs)
        | _ -> invalidArg "actorCount" $"createBang failed! (at least 1 sending actor should exist) actorCount = %i{actorCount}"

    let acc = createSendActor ac rounds 1 timerChan startChan
              <~> createRecvActor recvActor (actorCount * rounds) timerChan startChan
    let! timerFiber = !<~ (TimerEff (actorCount + 1) (actorCount + 1) 1 timerChan)
    let! _ = timerChan <-- MsgChannel startChan
    do! createBang recvActor acs rounds 2 timerChan startChan acc
    let! res = !<~~ timerFiber
    return res
}
