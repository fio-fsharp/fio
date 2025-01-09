(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(* -------------------------------------------------------------------------------- *)
(* Big benchmark                                                                    *)
(* Measures: Contention on mailbox; Many-to-Many message passing                    *)
(* Savina benchmark #7                                                              *)
(* (http://soft.vub.ac.be/AGERE14/papers/ageresplash2014_submission_19.pdf)         *)
(************************************************************************************)

module internal FIO.Benchmarks.Suite.Big

open FIO.Core

open FIO.Benchmarks.Tools.Timing.ChannelTimer

type private Message =
    | Ping of int * Channel<Message>
    | Pong of int

type private Actor =
    { Name: string
      PingReceivingChannel: Channel<Message>
      PongReceivingChannel: Channel<Message>
      SendingChannels: Channel<Message> list }

let private createActor actor message roundCount timerChan goChan =

    let rec createSendPings rounds channels = fio {
        if List.length channels = 0 then
            return! createRecvPings actor.SendingChannels.Length rounds
        else
            let! channel, rest = !+ (List.head channels, List.tail channels)
            do! channel <!- Ping (message, actor.PongReceivingChannel)
            return! createSendPings rounds rest
    }

    and createRecvPings recvCount roundCount =
        if recvCount = 0 then
            createRecvPongs actor.SendingChannels.Length roundCount
        else
            !--> actor.PingReceivingChannel
            >>= fun msg ->
                match msg with
                | Ping(x, replyChan) ->
#if DEBUG
                    printfn $"DEBUG: %s{actor.Name} received ping: %i{x}"
#endif
                    let y = x + 1
                    let msgReply = Pong y

                    msgReply --> replyChan
                    >>= fun _ ->
#if DEBUG
                        printfn $"DEBUG: %s{actor.Name} sent pong: %i{y}"
#endif
                        createRecvPings (recvCount - 1) roundCount
                | _ -> failwith "createRecvPings: Received pong when ping should be received!"

    and createRecvPongs recvCount roundCount =
        if recvCount = 0 then
            if roundCount = 0 then
                TimerMessage.Stop --> timerChan
            else
                createSendPings (roundCount - 1) actor.SendingChannels
        else
            !--> actor.PongReceivingChannel
            >>= fun msg ->
                match msg with
                | Pong x ->
#if DEBUG
                    printfn $"DEBUG: %s{actor.Name} received pong: %i{x}"
#endif
                    createRecvPongs (recvCount - 1) roundCount
                | _ -> failwith "createRecvPongs: Received ping when pong should be received!"

    TimerMessage.Start --> timerChan
    >>= fun _ -> !--> goChan >>= fun _ -> createSendPings (roundCount - 1) actor.SendingChannels

let Create processCount roundCount : FIO<BenchmarkResult, obj> =
    let rec createProcesses processCount =
        let rec createRecvChanProcesses processCount acc =
            match processCount with
            | 0 -> acc
            | count ->
                let proc =
                    { Name = $"p{count - 1}"
                      PingReceivingChannel = Channel<Message>()
                      PongReceivingChannel = Channel<Message>()
                      SendingChannels = [] }

                createRecvChanProcesses (count - 1) (acc @ [ proc ])

        let rec create recvChanProcs prevRecvChanProcs acc =
            match recvChanProcs with
            | [] -> acc
            | p :: ps ->
                let otherProcs = prevRecvChanProcs @ ps
                let chansSend = List.map (fun p -> p.PingReceivingChannel) otherProcs

                let proc =
                    { Name = p.Name
                      PingReceivingChannel = p.PingReceivingChannel
                      PongReceivingChannel = p.PongReceivingChannel
                      SendingChannels = chansSend }

                create ps (prevRecvChanProcs @ [ p ]) (proc :: acc)

        let recvChanProcesses = createRecvChanProcesses processCount []
        create recvChanProcesses [] []

    let rec createBig procs msg timerChan goChan acc =
        match procs with
        | [] -> acc
        | p :: ps ->
            let eff = createActor p msg roundCount timerChan goChan <!> acc
            createBig ps (msg + 10) timerChan goChan eff

    let procs = createProcesses processCount

    let pa, pb, ps =
        match procs with
        | pa :: pb :: ps -> (pa, pb, ps)
        | _ -> failwith $"createBig failed! (at least 2 processes should exist) processCount = %i{processCount}"

    let timerChan = Channel<TimerMessage<int>>()
    let goChan = Channel<int>()

    let effEnd =
        createActor pa (10 * (processCount - 2)) roundCount timerChan goChan
        <!> createActor pb (10 * (processCount - 1)) roundCount timerChan goChan

    ! TimerEffect(processCount, processCount, processCount, timerChan)
    >>= fun fiber ->
        (TimerMessage.MessageChannel goChan) --> timerChan
        >>= fun _ ->
            createBig ps 0 timerChan goChan effEnd
            >>= fun _ -> !? fiber >>= fun res -> succeed res