﻿(*************************************************************************************************************)
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

open FIO.DSL
#if DEBUG
open FIO.Lib.IO
#endif

open FIO.Benchmarks.Tools.Timer

open System

type private Actor = 
    { Name: string; 
      Chan: int channel }

let private createSendingActor actor roundCount msg timerChan startChan =
    fio {
        do! timerChan <!-- Start
        do! !<!-- startChan
        
        for _ in 1..roundCount do
            do! actor.Chan <!-- msg
            #if DEBUG
            do! FConsole.PrintLine $"[DEBUG]: %s{actor.Name} sent: %i{msg}"
            #endif
    }

let private createReceiveActor actor roundCount timerChan startChan =
    fio {
        do! timerChan <!-- Start
        do! !<!-- startChan
        
        for _ in 1..roundCount do
            let! msg = !<-- actor.Chan
            #if DEBUG
            do! FConsole.PrintLine $"[DEBUG]: %s{actor.Name} received: %i{msg}"
            #endif
            return ()
            
        do! timerChan <!-- Stop
    }

let private createBang receivingActor (sendingActors: Actor list) actorCount roundCount msg timerChan startChan =
    fio {
        let mutable currentMsg = msg
        let mutable currentEff = createReceiveActor receivingActor (actorCount * roundCount) timerChan startChan
        
        for sendingActor in sendingActors do
            currentEff <- createSendingActor sendingActor roundCount currentMsg timerChan startChan
                          <~> currentEff
            currentMsg <- currentMsg + 1
        
        return! currentEff
    }

let private createSendingActors receiveActorChan actorCount =
    List.map (fun index ->
            { Name = $"Actor-{index}"
              Chan = receiveActorChan })
        [ 1..actorCount ]

let createBangBenchmark config : FIO<int64, exn> =
    fio {
        let! actorCount, roundCount =
            match config with
            | BangConfig (actorCount, roundCount) -> !+ (actorCount, roundCount)
            | _ -> !- ArgumentException("Bang benchmark requires a BangConfig!", nameof(config))
        
        if actorCount < 1 then
            return! !- ArgumentException($"Bang failed: At least 1 actor should be specified. actorCount = %i{actorCount}", nameof(actorCount))
        
        if roundCount < 1 then
            return! !- ArgumentException($"Bang failed: At least 1 round should be specified. roundCount = %i{roundCount}", nameof(roundCount))
        
        let timerChan = Channel<TimerMessage<int>>()
        let startChan = Channel<int>()

        let receivingActor =
            { Name = "Actor-0"
              Chan = Channel<int>() }

        let sendingActors = createSendingActors receivingActor.Chan actorCount
        let! timerFiber = !<~ (TimerEff (actorCount + 1) (actorCount + 1) 1 timerChan)
        do! timerChan <!-- MsgChannel startChan
        do! createBang receivingActor sendingActors actorCount roundCount 1 timerChan startChan
        let! res = !<~~ timerFiber
        return res
    }
