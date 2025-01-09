(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(* -------------------------------------------------------------------------------- *)
(* Bang benchmark                                                                   *)
(* Measures: Many-to-One message passing                                            *)
(* A Scalability Benchmark Suite for Erlang/OTP                                     *)
(* (https://dl.acm.org/doi/10.1145/2364489.2364495I)                                *)
(************************************************************************************)

module internal FIO.Benchmarks.Suite.Bang

open FIO.Benchmarks.Tools.Timing.ChannelTimer

open FIO.Core

type private Actor = 
    { Name: string; 
      Channel: int channel }

let rec private createSendProcess proc msg roundCount timerChan goChan =
    let rec create proc msg roundCount =
        if roundCount = 0 then
            !+ ()
        else
            msg --> proc.Channel
            >>= fun _ ->
#if DEBUG
                printfn $"DEBUG: %s{proc.Name} sent: %i{msg}"
#endif
                create proc (msg + 10) (roundCount - 1)

    TimerMessage.Start --> timerChan
    >>= fun _ -> !--> goChan >>= fun _ -> create proc msg roundCount

let rec private createRecvProcess proc roundCount timerChan goChan =
    let rec create proc roundCount =
        if roundCount = 0 then
            TimerMessage.Stop --> timerChan >>= fun _ -> !+ ()
        else
            !--> proc.Channel
            >>= fun x ->
#if DEBUG
                printfn $"DEBUG: %s{proc.Name} received: %i{x}"
#endif
                create proc (roundCount - 1)

    TimerMessage.Start --> timerChan
    >>= fun _ -> !--> goChan >>= fun _ -> create proc roundCount

let Create processCount roundCount : FIO<BenchmarkResult, obj> =
    let rec createSendProcesses recvProcChan processCount =
        List.map
            (fun count ->
                { Name = $"p{count}"
                  Channel = recvProcChan })
            [ 1..processCount ]

    let rec createBang recvProc sendProcs msg acc timerChan goChan =
        match sendProcs with
        | [] -> acc
        | p :: ps ->
            let eff = createSendProcess p msg roundCount timerChan goChan <!> acc
            createBang recvProc ps (msg + 10) eff timerChan goChan

    let recvProc = { Name = "p0"; Channel = Channel<int>() }
    let sendProcs = createSendProcesses recvProc.Channel processCount

    let p, ps =
        match List.rev sendProcs with
        | p :: ps -> (p, ps)
        | _ -> failwith $"createBang failed! (at least 1 sending process should exist) processCount = %i{processCount}"

    let timerChan = Channel<TimerMessage<int>>()
    let goChan = Channel<int>()

    let effEnd =
        createSendProcess p 0 roundCount timerChan goChan
        <!> createRecvProcess recvProc (processCount * roundCount) timerChan goChan

    ! TimerEffect(processCount + 1, processCount + 1, 1, timerChan)
    >>= fun fiber ->
        (TimerMessage.MessageChannel goChan) --> timerChan
        >>= fun _ ->
            createBang recvProc ps 10 effEnd timerChan goChan
            >>= fun _ -> !? fiber >>= fun res -> succeed res