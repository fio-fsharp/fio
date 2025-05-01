(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(* --------------------------------------------------------------------------------------------------------- *)
(* Big benchmark                                                                                             *)
(* Measures: Contention on mailbox; Many-to-Many message passing                                             *)
(* Savina benchmark #7                                                                                       *)
(* (http://soft.vub.ac.be/AGERE14/papers/ageresplash2014_submission_19.pdf)                                  *)
(*************************************************************************************************************)

module internal FIO.Benchmarks.Suite.Big

open FIO.Core
open FIO.Benchmarks.Tools.Timer

type private Message =
    | Ping of int * Message channel
    | Pong of int

type private Actor =
    { Name: string
      PingRecvChan: Message channel
      PongRecvChan: Message channel
      SendChans: Message channel list }

[<TailCall>]
let rec private createSendPings actor rounds ping chans timerChan = fio {
    if List.length chans <= 0 then
        return! createRecvPings actor rounds actor.SendChans.Length ping timerChan
    else
        let chan, chans' = (List.head chans, List.tail chans)
        let! _ = chan <-- Ping (ping, actor.PongRecvChan)
        #if DEBUG
        printfn $"DEBUG: %s{actor.Name} sent ping: %i{ping}"
        #endif
        return! createSendPings actor rounds ping chans' timerChan
}

and private createRecvPings actor rounds recvCount msg timerChan = fio {
    if recvCount <= 0 then
        return! createRecvPongs actor rounds actor.SendChans.Length msg timerChan
    else
        match! !<-- actor.PingRecvChan with
        | Ping (ping, replyChan) ->
            #if DEBUG
            printfn $"DEBUG: %s{actor.Name} received ping: %i{ping}"
            #endif
            match! replyChan <-- Pong (ping + 1) with
            | Pong pong ->
                #if DEBUG
                printfn $"DEBUG: %s{actor.Name} sent pong: %i{pong}"
                #endif
                ()
            | Ping _ -> invalidOp "createRecvPings: Received ping when pong was expected!"
            return! createRecvPings actor rounds (recvCount - 1) ping timerChan
        | _ ->
            invalidOp "createRecvPings: Received pong when ping was expected!"
}

and private createRecvPongs actor rounds recvCount msg timerChan = fio {
    if recvCount <= 0 then
        if rounds <= 0 then
            let! _ = timerChan <-- Stop
            return ()
        else
            return! createSendPings actor (rounds - 1) msg actor.SendChans timerChan
    else
        match! !<-- actor.PongRecvChan with
        | Pong pong -> 
            #if DEBUG
            printfn $"DEBUG: %s{actor.Name} received pong: %i{pong}"
            #endif
            return! createRecvPongs actor rounds (recvCount - 1) msg timerChan
        | _ -> invalidOp "createRecvPongs: Received ping when pong was expected!"
}

let private createActor actor msg rounds timerChan startChan = fio {
    let! _ = timerChan <-- Start
    let! _ = !<-- startChan
    return! createSendPings actor (rounds - 1) msg actor.SendChans timerChan
}

[<TailCall>]
let rec private createRecvActors actorCount acc =
    match actorCount with
    | 0 -> acc
    | count ->
        let actor =
            { Name = $"Actor-{count - 1}"
              PingRecvChan = Channel<Message>()
              PongRecvChan = Channel<Message>()
              SendChans = [] }
        createRecvActors (count - 1) (acc @ [actor])

[<TailCall>]
let rec private createActorsHelper recvActors prevRecvActors acc =
    match recvActors with
    | [] -> acc
    | ac :: acs ->
        let otherActors = prevRecvActors @ acs
        let chansSend = List.map (fun ac -> ac.PingRecvChan) otherActors

        let actor =
            { Name = ac.Name
              PingRecvChan = ac.PingRecvChan
              PongRecvChan = ac.PongRecvChan
              SendChans = chansSend }

        createActorsHelper acs (prevRecvActors @ [ac]) (actor :: acc)

let rec private createActors actorCount =
    let recvActors = createRecvActors actorCount []
    createActorsHelper recvActors [] []

[<TailCall>]
let rec private createBig actors rounds msg timerChan startChan acc = fio {
    match actors with
    | [] -> return! acc
    | ac :: acs ->
        let acc = createActor ac msg rounds timerChan startChan <~> acc
        return! createBig acs rounds (msg + 10) timerChan startChan acc
}

let internal Create config = fio {
    let actorCount, rounds =
        match config with
        | BigConfig (actors, rounds) -> (actors, rounds)
        | _ -> invalidArg "config" "Big benchmark requires a BigConfig!"

    let timerChan = Channel<TimerMessage<int>>()
    let startChan = Channel<int>()

    let actors = createActors actorCount

    let firstActor, secondActor, rest =
        match actors with
        | first :: second :: rest -> (first, second, rest)
        | _ -> invalidArg "actorCount" $"createBig failed! (at least 2 actors should exist) actorCount = %i{actorCount}"

    let acc = createActor firstActor (actorCount - 2) rounds timerChan startChan
              <~> createActor secondActor (actorCount - 1) rounds timerChan startChan

    let! timerFiber = !~> (TimerEff actorCount actorCount actorCount timerChan)
    let! _ = timerChan <-- MsgChannel startChan
    do! createBig rest rounds 0 timerChan startChan acc
    let! res = !<~~ timerFiber
    return res
}
