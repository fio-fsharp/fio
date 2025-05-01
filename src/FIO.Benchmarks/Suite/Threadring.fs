(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(* --------------------------------------------------------------------------------------------------------- *)
(* Threadring benchmark                                                                                      *)
(* Measures: Message sending; Context switching between actors                                               *)
(* Savina benchmark #5                                                                                       *)
(* (http://soft.vub.ac.be/AGERE14/papers/ageresplash2014_submission_19.pdf)                                  *)
(*************************************************************************************************************)

module internal FIO.Benchmarks.Suite.Threadring

open FIO.Core
open FIO.Benchmarks.Tools.Timer

type private Actor =
    { Name: string
      SendChan: int channel
      RecvChan: int channel }

[<TailCall>]
let rec private createActorHelper actor rounds timerChan = fio {
    if rounds <= 0 then
        let! _ = timerChan <-- Stop
        return ()
    else
        let! msg = !<-- actor.RecvChan
        #if DEBUG
        printfn $"[DEBUG]: %s{actor.Name} received: %i{msg}"
        #endif
        let! msg' = actor.SendChan <-- msg + 1
        #if DEBUG
        printfn $"[DEBUG]: %s{actor.Name} sent: %i{msg'}"
        #endif
        return! createActorHelper actor (rounds - 1) timerChan
}

let private createActor actor rounds timerChan = fio {
    let! _ = timerChan <-- Start
    return! createActorHelper actor rounds timerChan
}

[<TailCall>]
let rec private createThreadring actors rounds timerChan acc = fio {
    match actors with
    | [] -> return! acc
    | ac :: acs ->
        let newAcc = createActor ac rounds timerChan <~> acc
        return! createThreadring acs rounds timerChan newAcc
}

[<TailCall>]
let rec private createActors chans (allChans: Channel<int> list) index acc =
    match chans with
    | [] -> acc
    | chan'::chans' ->
        let actor =
            { Name = $"Actor-{index}"
              SendChan = chan'
              RecvChan =
                match index with
                | index when index - 1 < 0 -> allChans.Item(List.length allChans - 1)
                | index -> allChans.Item(index - 1) }
        createActors chans' allChans (index + 1) (acc @ [actor])

let internal Create config = fio {
    let actorCount, rounds =
        match config with
        | ThreadringConfig (actors, rounds) -> (actors, rounds)
        | _ -> invalidArg "config" "Threadring benchmark requires a ThreadringConfig!"

    let chans = [for _ in 1..actorCount -> Channel<int>()]
    let timerChan = Channel<TimerMessage<int>>()

    let! first, second, acs =
        let actors = createActors chans chans 0 []
        match actors with
        | first::second::acs -> !+ (first, second, acs)
        | _ -> invalidArg "actorCount" $"Threadring failed: At least two actors should exist. actorCount = %i{actorCount}"

    let acc = createActor second rounds timerChan 
              <~> createActor first rounds timerChan
    let! fiber = !~> (TimerEff actorCount 1 actorCount timerChan)
    let! _ = timerChan <-- MsgChannel first.RecvChan
    do! createThreadring acs rounds timerChan acc
    let! res = !<~~ fiber
    return res
}
