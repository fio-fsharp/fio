(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(* -------------------------------------------------------------------------------- *)
(* Threadring benchmark                                                             *)
(* Measures: Message sending; Context switching between actors                      *)
(* Savina benchmark #5                                                              *)
(* (http://soft.vub.ac.be/AGERE14/papers/ageresplash2014_submission_19.pdf)         *)
(************************************************************************************)

module internal FIO.Benchmarks.Suite.Threadring

open FIO.Core

open FIO.Benchmarks.Tools.Timing.ChannelTimer

type private Actor =
    { Name: string
      SendingChannel: int channel
      ReceivingChannel: int channel }

[<TailCall>]
let rec private createActorHelper rounds actor timerChannel = fio {
    if rounds = 0 then
        do! timerChannel <!- TimerMessage.Stop
    else
        let! received = !<-- actor.ReceivingChannel
        #if DEBUG
        do! !+ printfn($"DEBUG: %s{actor.Name} received: %i{received}")
        #endif
        let! message = !+ (received + 1)
        do! actor.SendingChannel <!- message
        #if DEBUG
        do! !+ printfn($"DEBUG: %s{actor.Name} sent: %i{message}")
        #endif
        return! createActorHelper (rounds - 1) actor timerChannel
}

let private createActor rounds actor timerChannel = fio {
    do! timerChannel <!- TimerMessage.Start
    return! createActorHelper rounds actor timerChannel
}

[<TailCall>]
let rec private createThreadring rounds procs timerChannel acc =
    match procs with
    | [] ->
        acc
    | p :: ps ->
        let newAcc = createActor rounds p timerChannel <!> acc
        createThreadring rounds ps timerChannel newAcc

[<TailCall>]
let rec private createActors channels (allChannels: Channel<int> list) index acc =
    match channels with
    | [] -> acc
    | chan::chans ->
        let actor =
            { Name = $"Actor-{index}"
              SendingChannel = chan
              ReceivingChannel =
                match index with
                | index when index - 1 < 0 ->
                    allChannels.Item(List.length allChannels - 1)
                | index ->
                    allChannels.Item(index - 1) }
        createActors chans allChannels (index + 1) (acc @ [ actor ])

let internal Create actorCount (rounds : int) : FIO<BenchmarkResult, obj> = fio {
    let! channels = !+ [ for _ in 1..actorCount -> Channel<int>() ]
    let! actors = !+ (createActors channels channels 0 [])
    let! timerChannel = !+ Channel<TimerMessage<int>>()

    let! firstActor, secondActor, rest =
        match actors with
        | first::second::rest -> !+ (first, second, rest)
        | _ -> failwith $"Threadring failed: At least tow actors should exist. actorCount = %i{actorCount}"

    let acc = createActor rounds secondActor timerChannel 
              <!> createActor rounds firstActor timerChannel

    let! fiber = ! TimerEffect(actorCount, 1, actorCount, timerChannel)
    do! timerChannel <!- TimerMessage.MessageChannel (firstActor.ReceivingChannel)
    do! createThreadring rounds rest timerChannel acc
    let! time = !? fiber
    return time
}
