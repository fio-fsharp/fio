(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module FIO.Runtime.Intermediate

open FIO.Core

open System
open System.Threading

type private EvaluationWorkerConfig =
    { Runtime: Runtime
      WorkItemChan: InternalChannel<WorkItem>
      BlockingWorker: BlockingWorker
      EWSteps: int }

and private BlockingWorkerConfig =
    { WorkItemChan: InternalChannel<WorkItem>
      BlockingItemChan: InternalChannel<BlockingItem * WorkItem> }

and private EvaluationWorker(config: EvaluationWorkerConfig) =

    let processWorkItem workItem = task {
        match! config.Runtime.InternalRunAsync workItem config.EWSteps with
        | Success res, _, Evaluated ->
            do! workItem.Complete <| Ok res
        | Failure err, _, Evaluated ->
            do! workItem.Complete <| Error err
        | eff, stack, RescheduleForRunning ->
            do! config.WorkItemChan.SendAsync
                <| WorkItem.Create eff workItem.IFiber stack RescheduleForRunning
        | eff, stack, RescheduleForBlocking blockingItem ->
            do! config.BlockingWorker.RescheduleForBlocking blockingItem 
                <| WorkItem.Create eff workItem.IFiber stack (RescheduleForBlocking blockingItem)
        | _ ->
            invalidOp "EvaluationWorker: Unexpected state encountered during effect evaluation."
    }

    let startWorker (cancellationToken: CancellationToken) =
        let rec loop () =
            task {
                let! hasItem = config.WorkItemChan.WaitToReceiveAsync()
                if hasItem then
                    let! item = config.WorkItemChan.ReceiveAsync()
                    do! processWorkItem item
                    return! loop ()
            }
    
        loop ()
        |> ignore

    let cancellationTokenSource = new CancellationTokenSource()
    do startWorker cancellationTokenSource.Token

    interface IDisposable with
        member this.Dispose() =
            cancellationTokenSource.Cancel()

and private BlockingWorker(config: BlockingWorkerConfig) =

    let handleBlockingItem (blockingItem, workItem) = task {
        match blockingItem with
        | BlockingChannel chan ->
            if chan.DataAvailable() then
                chan.UseAvailableData()
                do! config.WorkItemChan.SendAsync workItem
            else
                do! config.BlockingItemChan.SendAsync (blockingItem, workItem)
        | BlockingIFiber ifiber ->
            if ifiber.Completed() then
                do! config.WorkItemChan.SendAsync workItem
            else
                do! config.BlockingItemChan.SendAsync (blockingItem, workItem)
    }

    let startWorker (cancellationToken: CancellationToken) =
        let rec loop () =
            task {
                let! hasItem = config.BlockingItemChan.WaitToReceiveAsync()
                if hasItem then
                    let! item = config.BlockingItemChan.ReceiveAsync()
                    do! handleBlockingItem item
                    return! loop ()
            }
    
        loop ()
        |> ignore

    let cancellationTokenSource = new CancellationTokenSource()
    do startWorker cancellationTokenSource.Token

    interface IDisposable with
        member this.Dispose() =
            cancellationTokenSource.Cancel()

    member internal this.RescheduleForBlocking blockingItem workItem =
        config.BlockingItemChan.SendAsync (blockingItem, workItem)

and Runtime(config: WorkerConfig) as this =
    inherit FIOWorkerRuntime(config)
    
    let workItemChan = new InternalChannel<WorkItem>()
    let blockingItemChan = new InternalChannel<BlockingItem * WorkItem>()

    let createBlockingWorkers workItemQueue blockingItemQueue count =
        List.init count <| fun _ ->
            new BlockingWorker({
                WorkItemChan = workItemChan
                BlockingItemChan = blockingItemChan
            })

    let createEvaluationWorkers runtime workItemQueue blockingWorker evalSteps count =
        List.init count <| fun _ -> 
            new EvaluationWorker({ 
                Runtime = runtime
                WorkItemChan = workItemQueue
                BlockingWorker = blockingWorker
                EWSteps = evalSteps 
            })

    do
        let blockingWorkers = createBlockingWorkers workItemChan blockingItemChan config.BWCount
        // Currently we take head of the list, as the IntermediateRuntime
        // only supports a single blocking worker.
        createEvaluationWorkers this workItemChan (List.head blockingWorkers) config.EWSteps config.EWCount
        |> ignore

    new() =
        Runtime
            { EWCount =
                let coreCount = Environment.ProcessorCount - 1
                if coreCount >= 2 then coreCount else 2
              BWCount = 1
              EWSteps = 20 }

    [<TailCall>]
    member internal this.InternalRunAsync (workItem: WorkItem) evalSteps =
        let mutable currentEff = workItem.Eff
        let mutable currentContStack = workItem.Stack
        let mutable currentPrevAction = workItem.PrevAction
        let mutable currentEWSteps = evalSteps
        let mutable result = None

        let handleSuccess res =
            let mutable loop = true
            while loop do
                match currentContStack with
                | [] -> 
                    result <- Some (Success res, ContStack.Empty, Evaluated)
                    loop <- false
                | (SuccessCont, cont) :: ss -> 
                    currentEff <- cont res
                    currentPrevAction <- Evaluated
                    currentContStack <- ss
                    loop <- false
                | (FailureCont, _) :: ss -> 
                    currentContStack <- ss

        let handleError err =
            let mutable loop = true
            while loop do
                match currentContStack with
                | [] -> 
                    result <- Some (Failure err, ContStack.Empty, Evaluated)
                    loop <- false
                | (SuccessCont, _) :: ss -> 
                    currentContStack <- ss
                | (FailureCont, cont) :: ss ->
                    currentEff <- cont err
                    currentContStack <- ss
                    currentPrevAction <- Evaluated

        let handleResult res =
            match res with
            | Ok res ->
                handleSuccess res
            | Error err ->
                handleError err

        task {
            while result.IsNone do
                if currentEWSteps = 0 then
                    result <- Some (currentEff, currentContStack, RescheduleForRunning)
                else
                    currentEWSteps <- currentEWSteps - 1
                    match currentEff with
                    | Success res ->
                        handleSuccess res
                    | Failure err ->
                        handleError err
                    | Action (func, onError) ->
                        try 
                            let res = func ()
                            handleSuccess res
                        with exn ->
                            handleResult (Error <| onError exn)
                    | SendChan (msg, chan) ->
                        do! chan.SendAsync msg
                        handleSuccess msg
                    | ReceiveChan chan ->
                        // TODO: Optimize this. Why are we just assuming it is blocking?
                        if currentPrevAction = RescheduleForBlocking (BlockingChannel chan) then
                            let! res = chan.ReceiveAsync()
                            handleSuccess res
                        else
                            currentPrevAction <- RescheduleForBlocking <| BlockingChannel chan
                            result <- Some (ReceiveChan chan, currentContStack, RescheduleForBlocking <| BlockingChannel chan)
                    | Concurrent (eff, fiber, ifiber) ->
                        do! workItemChan.SendAsync
                            <| WorkItem.Create eff ifiber ContStack.Empty currentPrevAction
                        handleSuccess fiber
                    | AwaitFiber ifiber ->
                        if ifiber.Completed() then
                            let! res = ifiber.AwaitAsync()
                            handleResult res
                        else
                            result <- Some (AwaitFiber ifiber, currentContStack, RescheduleForBlocking <| BlockingIFiber ifiber)
                    | AwaitTask (task, onError) ->
                        try
                            let! res = task
                            handleResult <| Ok res
                        with exn ->
                            handleResult (Error <| onError exn)
                    | ChainSuccess (eff, cont) ->
                        currentEff <- eff
                        currentContStack <- (SuccessCont, cont) :: currentContStack
                    | ChainError (eff, cont) ->
                        currentEff <- eff
                        currentContStack <- (FailureCont, cont) :: currentContStack

            return result.Value
        }

    override this.Run (eff: FIO<'R, 'E>) : Fiber<'R, 'E> =
        let fiber = Fiber<'R, 'E>()
        workItemChan.SendAsync
        <| WorkItem.Create (eff.Upcast()) (fiber.ToInternal()) ContStack.Empty Evaluated
        |> ignore
        fiber

    override this.Name () =
        "Intermediate"
