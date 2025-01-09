(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(************************************************************************************)

module internal FIO.Benchmarks.Tools.Timing

open FIO.Core

open System.Diagnostics

type TimerResult = int64

module internal StopwatchTimer =

    type TimerMessage =
        | Start of Stopwatch
        | Stop

    let TimerEffect(stopCount, channel) : FIO<TimerResult, 'E> =
        let mutable stopwatch = Stopwatch()

        // TODO: Make tail-recursive.
        let rec loopStop count = fio {
            match count with
            | 0 ->
                #if DEBUG
                do! !+ printfn("DEBUG: StopwatchTimerEffect: Timer stopped!")
                #endif
                do! !+ stopwatch.Stop()
            | count ->
                let! received = !<-- channel
                match received with
                | TimerMessage.Stop ->
                    return! loopStop (count - 1)
                | _ -> 
                    return! loopStop count
        }

        fio {
            let! received = !<-- channel
            match received with
            | TimerMessage.Start watch ->
                stopwatch <- watch
            | TimerMessage.Stop ->
                failwith "StopwatchTimerEffect: Received Stop as first message!"
            do! loopStop stopCount
            return stopwatch.ElapsedMilliseconds
        }

module internal ChannelTimer =

    type TimerMessage<'R> =
        | MessageChannel of Channel<'R>
        | Start
        | Stop

    let TimerEffect(startCount, messageCount, stopCount, channel) : FIO<TimerResult, 'E> =
        let stopwatch = Stopwatch()
        let mutable messageChannel = Channel<int>()

        // TODO: Make tail-recursive.
        let rec loopStart count = fio {
            match count with
            | 0 ->
                #if DEBUG
                do! !+ printfn("DEBUG: ChannelTimerEffect: Timer started!")
                #endif
                do! !+ stopwatch.Start()
            | count ->
                let! received = !<-- channel
                match received with
                | TimerMessage.Start ->
                    return! loopStart (count - 1)
                | _ -> 
                    return! loopStart count
        }

        // TODO: Make tail-recursive.
        let rec loopMessage count message = fio {
            match count with
            | 0 -> 
                return ()
            | count ->
                do! message -!> messageChannel
                return! loopMessage (count - 1) message
        }

        // TODO: Make tail-recursive.
        let rec loopStop count = fio {
            match count with
            | 0 -> 
                return stopwatch.Stop()
            | count ->
                let! received = !<-- channel
                match received with
                | TimerMessage.Stop ->
                    return! loopStop (count - 1)
                | _ ->
                    return! loopStop count
            }

        fio {
            let! received = !<-- channel
            match received with
            | MessageChannel mChannel ->
                messageChannel <- mChannel
            | _ ->
                failwith "ChannelTimerEffect: Did not receive channel as first message!"
            do! loopStart startCount
            do! loopMessage messageCount 0
            do! loopStop stopCount
            return stopwatch.ElapsedMilliseconds
        }