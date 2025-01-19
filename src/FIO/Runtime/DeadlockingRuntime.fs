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
      WorkItemQueue: InternalQueue<WorkItem>
      BlockingWorker: BlockingWorker
      EvaluationWorkerSteps: int }

and private BlockingWorkerConfig =
    { WorkItemQueue: InternalQueue<WorkItem>
      BlockingWorkItemMap: BlockingWorkItemMap
      BlockingEventQueue: InternalQueue<Channel<obj>> }

and private EvaluationWorker(config: EvaluationWorkerConfig) =

    let completeWorkItem (workItem: WorkItem) result  =
        workItem.Complete result
        config.BlockingWorker.RescheduleBlockingEffects workItem.InternalFiber

    let rescheduleForRunning workItem = 
        config.WorkItemQueue.Add workItem

    let processWorkItem workItem =
        match config.Runtime.InternalRun workItem.Effect workItem.Stack workItem.PrevAction config.EvaluationWorkerSteps with
        | Success result, _, Evaluated, _ ->
            completeWorkItem workItem
            <| Ok result

        | Failure error, _, Evaluated, _ ->
            completeWorkItem workItem
            <| Error error

        | effect, stack, RescheduleForRunning, _ ->
            rescheduleForRunning
            <| WorkItem.Create(effect, workItem.InternalFiber, stack, RescheduleForRunning)

        | effect, stack, RescheduleForBlocking blockingItem, _ ->
            config.BlockingWorker.RescheduleForBlocking blockingItem
            <| WorkItem.Create(effect, workItem.InternalFiber, stack, (RescheduleForBlocking blockingItem))

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
    
    let processBlockingChannel blockingChannel =
        let blockingItem = BlockingChannel blockingChannel
        match config.BlockingWorkItemMap.TryRemove blockingItem with
        | true, (blockingQueue: InternalQueue<WorkItem>) ->
            config.WorkItemQueue.Add <| blockingQueue.Take()
            if blockingQueue.Count > 0 then
                config.BlockingWorkItemMap.AddOrUpdate(blockingItem, blockingQueue)
        | false, _ ->
            config.BlockingEventQueue.Add blockingChannel

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
        let blockingItem = BlockingFiber internalFiber
        match config.BlockingWorkItemMap.TryRemove blockingItem with
        | true, (blockingQueue: InternalQueue<WorkItem>) ->
            while blockingQueue.Count > 0 do
                config.WorkItemQueue.Add <| blockingQueue.Take()
        | false, _ -> ()

and private BlockingWorkItemMap() =
    let blockingWorkItemMap = ConcurrentDictionary<BlockingItem, InternalQueue<WorkItem>>()

    member internal this.RescheduleWorkItem blockingItem workItem =
        let newBlockingQueue = new InternalQueue<WorkItem>()
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

    let workItemQueue = new InternalQueue<WorkItem>()
    let blockingEventQueue = new InternalQueue<Channel<obj>>()
    let blockingWorkItemMap = BlockingWorkItemMap()

    let createBlockingWorkers workItemQueue blockingWorkItemMap blockingEventQueue count =
        List.init count <| fun _ ->
            new BlockingWorker({
                WorkItemQueue = workItemQueue;
                BlockingWorkItemMap = blockingWorkItemMap;
                BlockingEventQueue = blockingEventQueue
            })

    let createEvaluationWorkers runtime workItemQueue blockingWorker evalSteps count =
        List.init count <| fun _ -> 
            new EvaluationWorker({ 
                Runtime = runtime;
                WorkItemQueue = workItemQueue;
                BlockingWorker = blockingWorker;
                EvaluationWorkerSteps = evalSteps 
            })

    do
        let blockingWorkers = createBlockingWorkers workItemQueue blockingWorkItemMap blockingEventQueue config.BlockingWorkerCount
        // Currently we take head of the list, as the DeadlockingRuntime
        // only supports a single blocking worker.
        createEvaluationWorkers this workItemQueue (List.head blockingWorkers) config.EvaluationWorkerSteps config.EvaluationWorkerCount
        |> ignore

    new() =
        Runtime(
            { EvaluationWorkerCount = Environment.ProcessorCount - 1
              BlockingWorkerCount = 1
              EvaluationWorkerSteps = 20 })

    member internal this.InternalRun effect stack prevAction evalSteps : FIO<obj, obj> * ContinuationStack * RuntimeAction * int =

        let rec handleSuccess result stack evalSteps newEvalSteps =
            match stack with
            | [] -> (Success result, [], Evaluated, newEvalSteps)
            | (SuccessKind, cont) :: ss -> interpret (cont result) ss Evaluated evalSteps
            | (ErrorKind, _) :: ss -> handleSuccess result ss evalSteps newEvalSteps

        and handleError error stack evalSteps newEvalSteps =
            match stack with
            | [] -> (Failure error, [], Evaluated, newEvalSteps)
            | (SuccessKind, _) :: ss -> handleError error ss evalSteps newEvalSteps
            | (ErrorKind, cont) :: ss -> interpret (cont error) ss Evaluated evalSteps

        and handleResult result stack evalSteps newEvalSteps =
            match result with
            | Ok result -> handleSuccess result stack evalSteps newEvalSteps
            | Error error -> handleError error stack evalSteps newEvalSteps

        and interpret effect' stack' prevAction' evalSteps' =
            if evalSteps' = 0 then
                (effect', stack', RescheduleForRunning, 0)
            else
                let newEvalSteps = evalSteps' - 1
                match effect' with
                | Send (message, channel) ->
                    channel.Add message
                    blockingEventQueue.Add channel
                    handleSuccess message stack evalSteps newEvalSteps

                | Receive channel ->
                    if prevAction' = RescheduleForBlocking(BlockingChannel channel) then
                        handleSuccess (channel.Take()) stack evalSteps newEvalSteps
                    else
                        (Receive channel, stack', RescheduleForBlocking(BlockingChannel channel), evalSteps')

                | Concurrent (effect, fiber, internalFiber) ->
                    workItemQueue.Add <| WorkItem.Create(effect, internalFiber, [], prevAction')
                    handleSuccess fiber stack evalSteps newEvalSteps

                | Await internalFiber ->
                    if internalFiber.Completed() then
                        handleResult (internalFiber.AwaitResult()) stack evalSteps newEvalSteps
                    else
                        (Await internalFiber, stack, RescheduleForBlocking <| BlockingFiber internalFiber, evalSteps)

                | ChainSuccess (effect, continuation) ->
                    interpret effect ((SuccessKind, continuation) :: stack) prevAction evalSteps

                | ChainError (effect, continuation) ->
                    interpret effect ((ErrorKind, continuation) :: stack) prevAction evalSteps

                | Success result ->
                    handleSuccess result stack evalSteps newEvalSteps

                | Failure error ->
                    handleError error stack evalSteps newEvalSteps

        interpret effect stack prevAction evalSteps

    override this.Run (effect: FIO<'R, 'E>) : Fiber<'R, 'E> =
        let fiber = Fiber<'R, 'E>()
        workItemQueue.Add <| WorkItem.Create(effect.Upcast(), fiber.ToInternal(), [], Evaluated)
        fiber
