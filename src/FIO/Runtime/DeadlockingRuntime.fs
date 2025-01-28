(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module FIO.Runtime.Deadlocking

open System
open System.Threading
open System.Collections.Concurrent

open FIO.Core

type private EvaluationWorkerConfig =
    { Runtime: Runtime
      WorkItemQueue: BlockingQueue<WorkItem>
      BlockingWorker: BlockingWorker
      EWSteps: int }

and private BlockingWorkerConfig =
    { WorkItemQueue: BlockingQueue<WorkItem>
      BlockingWorkItemMap: BlockingWorkItemMap
      BlockingEventQueue: BlockingQueue<Channel<obj>> }

and private EvaluationWorker(config: EvaluationWorkerConfig) =

    let completeWorkItem (workItem: WorkItem) res =
        workItem.Complete res
        config.BlockingWorker.RescheduleBlockingEffects workItem.IFiber

    let rescheduleForRunning workItem =
        config.WorkItemQueue.Add workItem

    let processWorkItem workItem =
        match config.Runtime.InternalRun workItem.Eff workItem.Stack workItem.PrevAction config.EWSteps with
        | Success res, _, Evaluated, _ ->
            completeWorkItem workItem
            <| Ok res
        | Failure err, _, Evaluated, _ ->
            completeWorkItem workItem
            <| Error err
        | eff, stack, RescheduleForRunning, _ ->
            rescheduleForRunning
            <| WorkItem.Create eff workItem.IFiber stack RescheduleForRunning
        | eff, stack, RescheduleForBlocking blockingItem, _ ->
            config.BlockingWorker.RescheduleForBlocking blockingItem
            <| WorkItem.Create eff workItem.IFiber stack (RescheduleForBlocking blockingItem)
        | _ ->
            invalidOp "EvaluationWorker: Unexpected state encountered during effect evaluation."

    let startWorker (cancellationToken: CancellationToken) =
        async {
            config.WorkItemQueue.GetConsumingEnumerable()
            |> Seq.takeWhile (fun _ -> not cancellationToken.IsCancellationRequested)
            |> Seq.iter processWorkItem
        } |> Async.Start

    let cancellationTokenSource = new CancellationTokenSource()
    do startWorker cancellationTokenSource.Token

    interface IDisposable with
        member this.Dispose() =
            cancellationTokenSource.Cancel()

and private BlockingWorker(config: BlockingWorkerConfig) =
    
    let processBlockingChannel blockingChan =
        let blockingItem = BlockingChannel blockingChan
        match config.BlockingWorkItemMap.TryRemove blockingItem with
        | true, (blockingQueue: BlockingQueue<WorkItem>) ->
            config.WorkItemQueue.Add <| blockingQueue.Take()
            if blockingQueue.Count > 0 then
                config.BlockingWorkItemMap.AddOrUpdate(blockingItem, blockingQueue)
        | false, _ ->
            config.BlockingEventQueue.Add blockingChan

    let startWorker (cancellationToken: CancellationToken) =
        async {
            config.BlockingEventQueue.GetConsumingEnumerable()
            |> Seq.takeWhile (fun _ -> not cancellationToken.IsCancellationRequested)
            |> Seq.iter processBlockingChannel
        } |> Async.Start

    let cancellationTokenSource = new CancellationTokenSource()
    do startWorker cancellationTokenSource.Token

    interface IDisposable with
        member this.Dispose() =
            cancellationTokenSource.Cancel()

    member internal this.RescheduleForBlocking blockingItem workItem =
        config.BlockingWorkItemMap.RescheduleWorkItem blockingItem workItem

    member internal this.RescheduleBlockingEffects internalFiber =
        let blockingItem = BlockingIFiber internalFiber
        match config.BlockingWorkItemMap.TryRemove blockingItem with
        | true, (blockingQueue: BlockingQueue<WorkItem>) ->
            while blockingQueue.Count > 0 do
                config.WorkItemQueue.Add <| blockingQueue.Take()
        | false, _ -> ()

and private BlockingWorkItemMap() =
    let blockingWorkItemMap = ConcurrentDictionary<BlockingItem, BlockingQueue<WorkItem>>()

    member internal this.RescheduleWorkItem blockingItem workItem =
        let newBlockingQueue = new BlockingQueue<WorkItem>()
        newBlockingQueue.Add <| workItem
        blockingWorkItemMap.AddOrUpdate(blockingItem, newBlockingQueue,
            fun _ oldQueue ->
                oldQueue.Add workItem
                oldQueue)
        |> ignore

    member internal this.TryRemove(blockingItem) =
        blockingWorkItemMap.TryRemove blockingItem

    member internal this.AddOrUpdate(blockingItem, blockingQueue) =
        blockingWorkItemMap.AddOrUpdate(blockingItem, blockingQueue, fun _ queue -> queue)
        |> ignore

and Runtime(config: WorkerConfig) as this =
    inherit FIOWorkerRuntime(config)

    let workItemQueue = new BlockingQueue<WorkItem>()
    let blockingEventQueue = new BlockingQueue<Channel<obj>>()
    let blockingWorkItemMap = BlockingWorkItemMap()

    let createBlockingWorkers workItemQueue blockingWorkItemMap blockingEventQueue count =
        List.init count <| fun _ ->
            new BlockingWorker({
                WorkItemQueue = workItemQueue
                BlockingWorkItemMap = blockingWorkItemMap
                BlockingEventQueue = blockingEventQueue
            })

    let createEvaluationWorkers runtime workItemQueue blockingWorker evalSteps count =
        List.init count <| fun _ -> 
            new EvaluationWorker({ 
                Runtime = runtime
                WorkItemQueue = workItemQueue
                BlockingWorker = blockingWorker
                EWSteps = evalSteps 
            })

    do
        let blockingWorkers = createBlockingWorkers workItemQueue blockingWorkItemMap blockingEventQueue config.BWCount
        // Currently we take head of the list, as the DeadlockingRuntime
        // only supports a single blocking worker.
        createEvaluationWorkers this workItemQueue (List.head blockingWorkers) config.EWSteps config.EWCount
        |> ignore

    new() =
        Runtime(
            { EWCount = Environment.ProcessorCount - 1
              BWCount = 1
              EWSteps = 20 })

    member internal this.InternalRun eff stack prevAction evalSteps : FIO<obj, obj> * ContStack * RuntimeAction * int =

        let rec handleSuccess res stack evalSteps newEvalSteps =
            match stack with
            | [] -> (Success res, [], Evaluated, newEvalSteps)
            | (SuccessCont, cont) :: ss -> interpret (cont res) ss Evaluated evalSteps
            | (FailureCont, _) :: ss -> handleSuccess res ss evalSteps newEvalSteps

        and handleError err stack evalSteps newEvalSteps =
            match stack with
            | [] -> (Failure err, [], Evaluated, newEvalSteps)
            | (SuccessCont, _) :: ss -> handleError err ss evalSteps newEvalSteps
            | (FailureCont, cont) :: ss -> interpret (cont err) ss Evaluated evalSteps

        and handleResult res stack evalSteps newEvalSteps =
            match res with
            | Ok res -> handleSuccess res stack evalSteps newEvalSteps
            | Error err -> handleError err stack evalSteps newEvalSteps

        and interpret eff stack prevAction evalSteps =
            if evalSteps = 0 then
                (eff, stack, RescheduleForRunning, 0)
            else
                let newEvalSteps = evalSteps - 1
                match eff with
                | Success res ->
                    handleSuccess res stack evalSteps newEvalSteps
                | Failure err ->
                    handleError err stack evalSteps newEvalSteps
                | Action func ->
                    handleResult (func ()) stack evalSteps newEvalSteps
                | Send (msg, chan) ->
                    chan.Add msg
                    blockingEventQueue.Add chan
                    handleSuccess msg stack evalSteps newEvalSteps
                | Receive chan ->
                    if prevAction = RescheduleForBlocking (BlockingChannel chan) then
                        handleSuccess (chan.Take()) stack evalSteps newEvalSteps
                    else
                        (Receive chan, stack, RescheduleForBlocking <| BlockingChannel chan, evalSteps)
                | Concurrent (eff, fiber, ifiber) ->
                    workItemQueue.Add <| WorkItem.Create eff ifiber ContStack.Empty prevAction
                    handleSuccess fiber stack evalSteps newEvalSteps
                | Await ifiber ->
                    if ifiber.Completed() then
                        handleResult (ifiber.AwaitResult()) stack evalSteps newEvalSteps
                    else
                        (Await ifiber, stack, RescheduleForBlocking <| BlockingIFiber ifiber, evalSteps)
                | ChainSuccess (eff, cont) ->
                    interpret eff ((SuccessCont, cont) :: stack) prevAction evalSteps
                | ChainError (eff, cont) ->
                    interpret eff ((FailureCont, cont) :: stack) prevAction evalSteps

        interpret eff stack prevAction evalSteps

    override this.Run (effect: FIO<'R, 'E>) : Fiber<'R, 'E> =
        let fiber = Fiber<'R, 'E>()
        workItemQueue.Add
        <| WorkItem.Create (effect.Upcast()) (fiber.ToInternal()) ContStack.Empty Evaluated
        fiber
