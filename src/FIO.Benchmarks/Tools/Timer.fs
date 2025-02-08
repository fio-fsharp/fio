(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module internal FIO.Benchmarks.Tools.Timer

open System.Diagnostics

open FIO.Core

type internal TimerMessage<'R> =
    | Start
    | Stop
    | MsgChannel of 'R channel

[<TailCall>]
let rec private startLoop count startCount timerChan (stopwatch: Stopwatch) = fio {
    match count with
    | 0 ->
        stopwatch.Start()
        #if DEBUG
        printfn "[DEBUG]: Timer started"
        #endif
    | count when count < 0 ->
        invalidArg "startCount" "Timer: startCount must be non-negative!"
    | count ->
        let! msg = !<-- timerChan
        match msg with
        | Start ->
            #if DEBUG
            printfn $"[DEBUG]: Timer received Start message (%i{startCount - count + 1}/%i{startCount})"
            #endif
            return! startLoop (count - 1) startCount timerChan stopwatch
        | _ -> return! startLoop count startCount timerChan stopwatch
}

[<TailCall>]
let rec private msgLoop count msgCount msg msgChan = fio {
    match count with
    | 0 -> return ()
    | count when count < 0 ->
        invalidArg "msgCount" "Timer: msgCount must be non-negative!"
    | count ->
        let! _ = msgChan <-- msg
        #if DEBUG
        printfn $"[DEBUG]: Timer sent %i{msg} to MsgChannel (%i{msgCount - count + 1}/%i{msgCount})"
        #endif
        return! msgLoop (count - 1) msgCount (msg + 1) msgChan
}

[<TailCall>]
let rec private stopLoop count stopCount timerChan (stopwatch: Stopwatch) = fio {
    match count with
    | 0 ->
        stopwatch.Stop()
        #if DEBUG
        printfn "[DEBUG]: Timer stopped"
        #endif
        return ()
    | count when count < 0 ->
        invalidArg "stopCount" "Timer: stopCount must be non-negative!"
    | count ->
        let! msg = !<-- timerChan
        match msg with
        | Stop ->
            #if DEBUG
            printfn $"[DEBUG]: Timer received Stop message (%i{stopCount - count + 1}/%i{stopCount})"
            #endif
            return! stopLoop (count - 1) stopCount timerChan stopwatch
        | _ -> return! stopLoop count stopCount timerChan stopwatch
}

let internal TimerEff startCount msgCount stopCount timerChan = fio {
    let mutable msgChan = Channel<int>()
    let stopwatch = Stopwatch()
    if msgCount > 0 then
        let! msg = !<-- timerChan
        match msg with
        | MsgChannel chan -> msgChan <- chan
        | _ -> invalidOp "Timer: Did not receive MsgChannel as first message when msgCount > 0!"
    else
        ()
    do! startLoop startCount startCount timerChan stopwatch
    do! msgLoop msgCount msgCount 0 msgChan
    do! stopLoop stopCount stopCount timerChan stopwatch
    return stopwatch.ElapsedMilliseconds
}
