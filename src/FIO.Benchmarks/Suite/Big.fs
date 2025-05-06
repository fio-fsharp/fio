﻿(*************************************************************************************************************)
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
#if DEBUG
open FIO.Lib.IO
#endif

open FIO.Benchmarks.Tools.Timer

open System

type private Message =
    | Ping of int * Message channel
    | Pong of int

type private Actor =
    { Name: string
      PingReceiveChan: Message channel
      PongReceiveChan: Message channel
      SendingChans: Message channel list }

[<TailCall>]
let rec private createSendPings actor roundCount ping (chans: Message channel list) timerChan =
    fio {
        for chan in chans do
            do! chan <!-- Ping (ping, actor.PongReceiveChan)
            #if DEBUG
            do! FConsole.PrintLine $"DEBUG: %s{actor.Name} sent ping: %i{ping}"
            #endif
        
        return! createReceivePings actor roundCount actor.SendingChans.Length ping timerChan
    }

and private createReceivePings actor rounds receiveCount msg timerChan =
    fio {
        for _ in 1..receiveCount do
            match! !<-- actor.PingReceiveChan with
            | Ping (ping, replyChan) ->
                #if DEBUG
                do! FConsole.PrintLine $"DEBUG: %s{actor.Name} received ping: %i{ping}"
                #endif
                match! replyChan <-- Pong (ping + 1) with
                | Pong pong ->
                    #if DEBUG
                    do! FConsole.PrintLine $"DEBUG: %s{actor.Name} sent pong: %i{pong}"
                    #endif
                    return ()
                | Ping _ ->
                    return! !- (InvalidOperationException "createReceivePings: Received ping when pong was expected!")
            | _ ->
                return! !- (InvalidOperationException "createReceivePings: Received pong when ping was expected!")
                
        return! createReceivePongs actor rounds actor.SendingChans.Length msg timerChan
    }

and private createReceivePongs actor roundCount receiveCount msg timerChan =
    fio {
        for _ in 1..receiveCount do
            match! !<-- actor.PongReceiveChan with
            | Pong pong -> 
                #if DEBUG
                do! FConsole.PrintLine $"DEBUG: %s{actor.Name} received pong: %i{pong}"
                #endif
            | _ ->
                return! !- (InvalidOperationException "createRecvPongs: Received ping when pong was expected!")
        
        if roundCount <= 0 then
            do! timerChan <!-- Stop
        else
            return! createSendPings actor (roundCount - 1) msg actor.SendingChans timerChan
    }

let private createActor actor msg roundCount timerChan startChan =
    fio {
        do! timerChan <!-- Start
        do! !<!-- startChan
        return! createSendPings actor (roundCount - 1) msg actor.SendingChans timerChan
    }

[<TailCall>]
let rec private createReceivingActors actorCount acc =
    match actorCount with
    | 0 -> acc
    | count ->
        let actor =
            { Name = $"Actor-{count - 1}"
              PingReceiveChan = Channel<Message>()
              PongReceiveChan = Channel<Message>()
              SendingChans = [] }
        createReceivingActors (count - 1) (acc @ [actor])

[<TailCall>]
let rec private createActorsHelper receivingActors prevReceivingActors acc =
    match receivingActors with
    | [] -> acc
    | ac :: acs ->
        let otherActors = prevReceivingActors @ acs
        let chansSend = List.map _.PingReceiveChan otherActors

        let actor =
            { Name = ac.Name
              PingReceiveChan = ac.PingReceiveChan
              PongReceiveChan = ac.PongReceiveChan
              SendingChans = chansSend }

        createActorsHelper acs (prevReceivingActors @ [ac]) (actor :: acc)

let private createActors actorCount =
    let receivingActors = createReceivingActors actorCount []
    createActorsHelper receivingActors [] []

let private createBig (actors: Actor list) roundCount msg timerChan startChan =
    fio {
        let mutable currentMsg = msg
        let mutable currentEff = createActor actors.Head currentMsg roundCount timerChan startChan
       
        for actor in actors.Tail do
            currentMsg <- currentMsg + 10
            currentEff <- createActor actor currentMsg roundCount timerChan startChan
                          <~> currentEff
        
        return! currentEff
    }

let internal Create config : FIO<int64, exn> =
    fio {
        let! actorCount, roundCount =
            match config with
            | BigConfig (actorCount, roundCount) -> !+ (actorCount, roundCount)
            | _ -> !- ArgumentException("Big benchmark requires a BigConfig!", nameof(config))
        
        if actorCount < 2 then
            return! !- ArgumentException($"Big failed: At least 2 actors should be specified. actorCount = %i{actorCount}", nameof(actorCount))
        
        if roundCount < 1 then
            return! !- ArgumentException($"Big failed: At least 1 round should be specified. roundCount = %i{roundCount}", nameof(roundCount))
        
        let timerChan = Channel<TimerMessage<int>>()
        let startChan = Channel<int>()
        let actors = createActors actorCount

        let! timerFiber = !~> (TimerEff actorCount actorCount actorCount timerChan)
        do! timerChan <!-- MsgChannel startChan
        do! createBig actors roundCount 0 timerChan startChan
        let! res = !<~~ timerFiber
        return res
    }
