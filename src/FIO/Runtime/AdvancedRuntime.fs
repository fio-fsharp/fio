(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module FIO.Runtime.Advanced

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
      BlockingItemChan: InternalChannel<BlockingItem> }

and private EvaluationWorker(config: EvaluationWorkerConfig) =

    let completeWorkItem (workItem: WorkItem) res = task {
        do! workItem.Complete res
        do! workItem.IFiber.RescheduleBlockingWorkItems config.WorkItemChan
    }

    let handleBlockingFiber blockingItem = task {
        match blockingItem with
        | BlockingIFiber ifiber when ifiber.Completed() ->
            do! ifiber.RescheduleBlockingWorkItems config.WorkItemChan
        | _ -> return ()
    }

    let processWorkItem workItem = task {
        match! config.Runtime.InternalRunAsync workItem config.EWSteps with
        | Success res, _, Evaluated ->
            do! completeWorkItem workItem <| Ok res
        | Failure err, _, Evaluated ->
            do! completeWorkItem workItem <| Error err
        | eff, stack, RescheduleForRunning ->
            do! config.WorkItemChan.WriteAsync
                <| WorkItem.Create eff workItem.IFiber stack RescheduleForRunning
        | eff, stack, RescheduleForBlocking blockingItem ->
            do! config.BlockingWorker.RescheduleForBlocking blockingItem
                <| WorkItem.Create eff workItem.IFiber stack (RescheduleForBlocking blockingItem)
            do! handleBlockingFiber blockingItem
        | _ ->
            invalidOp "EvaluationWorker: Unexpected state encountered during effect interpretation."
    }

    let startWorker (cancellationToken: CancellationToken) =
        let rec loop () =
            task {
                let! hasWorkItem = config.WorkItemChan.WaitToReadAsync()
                if hasWorkItem then
                    let! workItem = config.WorkItemChan.ReadAsync()
                    do! processWorkItem workItem
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

    let processBlockingChannel (blockingChan: Channel<obj>) = task {
        if blockingChan.HasBlockingWorkItems() then
            do! blockingChan.RescheduleBlockingWorkItems config.WorkItemChan
        else
            do! config.BlockingItemChan.WriteAsync <| BlockingChannel blockingChan
    }
 
    let processBlockingItem blockingItem = task {
        match blockingItem with
        | BlockingChannel chan -> 
            do! processBlockingChannel chan
        | _ -> 
            return ()
    }

    let startWorker (cancellationToken: CancellationToken) =
        let rec loop () =
            task {
                let! hasBlockingItem = config.BlockingItemChan.WaitToReadAsync()
                if hasBlockingItem then
                    let! blockingItem = config.BlockingItemChan.ReadAsync()
                    do! processBlockingItem blockingItem
                    return! loop ()
            }
    
        loop ()
        |> ignore

    let cancellationTokenSource = new CancellationTokenSource()
    do startWorker cancellationTokenSource.Token

    interface IDisposable with
        member this.Dispose() =
            cancellationTokenSource.Cancel()

    member internal this.RescheduleForBlocking blockingItem workItem = task {
        match blockingItem with
        | BlockingChannel chan ->
            do! chan.WriteBlockingWorkItem workItem
        | BlockingIFiber ifiber ->
            do! ifiber.AddBlockingWorkItem workItem
    }

and Runtime(config: WorkerConfig) as this =
    inherit FIOWorkerRuntime(config)

    let workItemChan = InternalChannel<WorkItem>()
    let blockingItemChan = InternalChannel<BlockingItem>()

    let createBlockingWorkers workItemChan blockingItemChan count =
        List.init count <| fun _ ->
            new BlockingWorker({
                WorkItemChan = workItemChan
                BlockingItemChan = blockingItemChan
            })

    let createEvaluationWorkers runtime workItemChan blockingWorker evalSteps count =
        List.init count <| fun _ -> 
            new EvaluationWorker({ 
                Runtime = runtime
                WorkItemChan = workItemChan
                BlockingWorker = blockingWorker
                EWSteps = evalSteps 
            })

    do
        let blockingWorkers = createBlockingWorkers workItemChan blockingItemChan config.BWCount
        // Currently we take head of the list, as the AdvancedRuntime
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
                    | WriteChan (msg, chan) ->
                        do! chan.SendAsync msg
                        do! blockingItemChan.WriteAsync <| BlockingChannel chan
                        handleSuccess msg
                    | ReadChan chan ->
                        // TODO: Optimize this. Why are we just assuming it is blocking?
                        if currentPrevAction = RescheduleForBlocking (BlockingChannel chan) then
                            let! res = chan.ReceiveAsync()
                            handleSuccess res
                        else
                            currentPrevAction <- RescheduleForBlocking <| BlockingChannel chan
                            result <- Some (ReadChan chan, currentContStack, RescheduleForBlocking <| BlockingChannel chan)
                    | Concurrent (eff, fiber, ifiber) ->
                        do! workItemChan.WriteAsync
                            <| WorkItem.Create eff ifiber ContStack.Empty currentPrevAction
                        handleSuccess fiber
                    | AwaitFiber ifiber ->
                        if ifiber.Completed() then
                            let! res = ifiber.AwaitResult()
                            handleResult res
                        else
                            result <- Some (AwaitFiber ifiber, currentContStack, RescheduleForBlocking <| BlockingIFiber ifiber)
                    | AwaitTask (task, onError) ->
                        try
                            let! res = task
                            handleResult (Ok res)
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
        workItemChan.WriteAsync
        <| WorkItem.Create (eff.Upcast()) (fiber.ToInternal()) ContStack.Empty Evaluated
        |> ignore
        fiber

    override this.Name () =
        "Advanced"
