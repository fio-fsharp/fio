(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(************************************************************************************)

module FIO.Runtime.Deadlocking

open FIO.Core

open System.Collections.Concurrent

#if DETECT_DEADLOCK || MONITOR
open FIO.Monitor
#endif

type internal EvalWorker
    (
        runtime: DeadlockingRuntime,
        workItemQueue: InternalQueue<WorkItem>,
        blockingWorker: BlockingWorker,
#if DETECT_DEADLOCK
        deadlockDetector: DeadlockDetector<BlockingWorker, EvalWorker>,
#endif
        evalSteps
    ) as self =
#if DETECT_DEADLOCK
    inherit Worker()
    let mutable working = false
#endif
    let _ =
        (async {
            for workItem in workItemQueue.GetConsumingEnumerable() do
#if DETECT_DEADLOCK
                working <- true
#endif
                match runtime.InternalRun workItem.Effect workItem.LastAction evalSteps with
                | Success res, Evaluated, _ -> self.CompleteWorkItem(workItem, Ok res)
                | Failure err, Evaluated, _ -> self.CompleteWorkItem(workItem, Error err)
                | eff, RescheduleForRunning, _ ->
                    let workItem = WorkItem.Create(eff, workItem.InternalFiber, [], RescheduleForRunning)
                    self.RescheduleForRunning workItem
                | eff, RescheduleForBlocking blockingItem, _ ->
                    let workItem =
                        WorkItem.Create(eff, workItem.InternalFiber, [], (RescheduleForBlocking blockingItem))

                    blockingWorker.RescheduleForBlocking blockingItem workItem
                | _ -> failwith $"EvalWorker: Error occurred while evaluating effect!"
#if DETECT_DEADLOCK
                working <- false
#endif
         }
         |> Async.StartAsTask
         |> ignore)

    member private _.CompleteWorkItem(workItem, res) =
        workItem.Complete res
        blockingWorker.RescheduleBlockingEffects workItem.InternalFiber
#if DETECT_DEADLOCK
        deadlockDetector.RemoveBlockingItem(BlockingFiber workItem.IFiber)
#endif

    member private _.RescheduleForRunning(workItem) = workItemQueue.Add workItem

#if DETECT_DEADLOCK
    override _.Working() = working && workItemQueue.Count > 0
#endif

and internal BlockingWorker
    (
        workItemQueue: InternalQueue<WorkItem>,
#if DETECT_DEADLOCK
        deadlockDetector: DeadlockDetector<BlockingWorker, EvalWorker>,
#endif
        blockingWorkItemMap: BlockingWorkItemMap,
        blockingEventQueue: InternalQueue<Channel<obj>>
    ) as self =
#if DETECT_DEADLOCK
    inherit Worker()
    let mutable working = false
#endif
    let _ =
        (async {
            for blockingChan in blockingEventQueue.GetConsumingEnumerable() do
#if DETECT_DEADLOCK
                working <- true
#endif
                self.HandleBlockingChannel blockingChan
#if DETECT_DEADLOCK
                working <- false
#endif
         }
         |> Async.StartAsTask
         |> ignore)

    member internal this.RescheduleForBlocking blockingItem workItem =
        blockingWorkItemMap.RescheduleWorkItem blockingItem workItem
#if DETECT_DEADLOCK
        deadlockDetector.AddBlockingItem blockingItem
#endif

    member private this.HandleBlockingChannel(blockingChan) =
        let blockingItem = BlockingChannel blockingChan

        match blockingWorkItemMap.TryRemove blockingItem with
        | true, (blockingQueue: InternalQueue<WorkItem>) ->
            workItemQueue.Add <| blockingQueue.Take()
#if DETECT_DEADLOCK
            deadlockDetector.RemoveBlockingItem blockingItem
#endif
            if blockingQueue.Count > 0 then
                blockingWorkItemMap.Add(blockingItem, blockingQueue)
        | false, _ -> blockingEventQueue.Add blockingChan

    member internal this.RescheduleBlockingEffects(ifiber) =
        let blockingItem = BlockingFiber ifiber

        match blockingWorkItemMap.TryRemove blockingItem with
        | true, (blockingQueue: InternalQueue<WorkItem>) ->
            while blockingQueue.Count > 0 do
                workItemQueue.Add <| blockingQueue.Take()
        | false, _ -> ()

#if DETECT_DEADLOCK
    override _.Working() = working && workItemQueue.Count > 0
#endif

and internal BlockingWorkItemMap() =
    let blockingWorkItemMap =
        ConcurrentDictionary<BlockingItem, InternalQueue<WorkItem>>()

    member internal this.RescheduleWorkItem blockingItem workItem =
        let newBlockingQueue = new InternalQueue<WorkItem>()
        newBlockingQueue.Add <| workItem

        blockingWorkItemMap.AddOrUpdate(
            blockingItem,
            newBlockingQueue,
            fun _ oldQueue ->
                oldQueue.Add workItem
                oldQueue
        )
        |> ignore

    member internal this.TryRemove(blockingItem) =
        blockingWorkItemMap.TryRemove blockingItem

    member internal this.Add(blockingItem, blockingQueue) =
        blockingWorkItemMap.AddOrUpdate(blockingItem, blockingQueue, fun _ queue -> queue)
        |> ignore

    member internal this.Get() : ConcurrentDictionary<BlockingItem, InternalQueue<WorkItem>> = blockingWorkItemMap

and DeadlockingRuntime(evalWorkerCount, blockingWorkerCount, evalStepCount) as self =
    inherit Runtime()

    let workItemQueue = new InternalQueue<WorkItem>()
    let blockingEventQueue = new InternalQueue<Channel<obj>>()
    let blockingWorkItemMap = BlockingWorkItemMap()

#if DETECT_DEADLOCK
    let deadlockDetector =
        new DeadlockDetector<BlockingWorker, EvalWorker>(workItemQueue, 500)
#endif

    do
        let blockingWorkers = self.CreateBlockingWorkers()
        self.CreateEvalWorkers(List.head blockingWorkers) |> ignore
#if DETECT_DEADLOCK
        let evalWorkers = self.CreateEvalWorkers(List.head blockingWorkers)
        deadlockDetector.SetBlockingWorkers blockingWorkers
        deadlockDetector.SetEvalWorkers evalWorkers
#endif
#if MONITOR
        Monitor(workItemQueue, None, Some blockingEventQueue, Some <| blockingWorkItemMap.Get())
        |> ignore
#endif

    new() = DeadlockingRuntime(System.Environment.ProcessorCount - 1, 1, 15)

    member internal this.InternalRun eff prevAction evalSteps : FIO<obj, obj> * RuntimeAction * int =
        if evalSteps = 0 then
            (eff, RescheduleForRunning, 0)
        else
            match eff with
            | NonBlocking action ->
                match action () with
                | Ok res -> (Success res, Evaluated, evalSteps - 1)
                | Error err -> (Failure err, Evaluated, evalSteps - 1)
            | Blocking chan ->
                if prevAction = RescheduleForBlocking(BlockingChannel chan) then
                    (Success <| chan.Take(), Evaluated, evalSteps - 1)
                else
                    (Blocking chan, RescheduleForBlocking(BlockingChannel chan), evalSteps)
            | Send(value, chan) ->
                chan.Add value
                blockingEventQueue.Add <| chan
                (Success value, Evaluated, evalSteps - 1)
            | Concurrent(eff, fiber, ifiber) ->
                workItemQueue.Add <| WorkItem.Create(eff, ifiber, [], prevAction)
                (Success fiber, Evaluated, evalSteps - 1)
            | Await ifiber ->
                if ifiber.Completed() then
                    match ifiber.AwaitResult() with
                    | Ok res -> (Success res, Evaluated, evalSteps - 1)
                    | Error err -> (Failure err, Evaluated, evalSteps - 1)
                else
                    (Await ifiber, RescheduleForBlocking(BlockingFiber ifiber), evalSteps)
            | ChainSuccess(eff, cont) ->
                match this.InternalRun eff prevAction evalSteps with
                | Success res, Evaluated, evalSteps -> this.InternalRun (cont res) Evaluated evalSteps
                | Failure err, Evaluated, evalSteps -> (Failure err, Evaluated, evalSteps)
                | eff, action, evalSteps -> (ChainSuccess(eff, cont), action, evalSteps)
            | ChainError(eff, cont) ->
                match this.InternalRun eff prevAction evalSteps with
                | Success res, Evaluated, evalSteps -> (Success res, Evaluated, evalSteps)
                | Failure err, Evaluated, evalSteps -> this.InternalRun (cont err) Evaluated evalSteps
                | eff, action, evalSteps -> (ChainSuccess(eff, cont), action, evalSteps)
            | Success res -> (Success res, Evaluated, evalSteps - 1)
            | Failure err -> (Failure err, Evaluated, evalSteps - 1)

    override _.Run<'R, 'E>(eff: FIO<'R, 'E>) : Fiber<'R, 'E> =
        let fiber = Fiber<'R, 'E>()

        workItemQueue.Add
        <| WorkItem.Create(eff.Upcast(), fiber.ToInternal(), [], Evaluated)

        fiber

    member private this.CreateBlockingWorkers() =
        let createBlockingWorkers start final =
            List.map
                (fun _ ->
#if DETECT_DEADLOCK
                    BlockingWorker(workItemQueue, deadlockDetector, blockingWorkItemMap, blockingEventQueue))
                [ start..final ]
#else
                    BlockingWorker(workItemQueue, blockingWorkItemMap, blockingEventQueue))
                [ start..final ]
#endif
        let _, blockingWorkerCount, _ = this.GetConfiguration()
        createBlockingWorkers 0 (blockingWorkerCount - 1)

    member private this.CreateEvalWorkers blockingWorker =
        let createEvalWorkers blockingWorker evalSteps start final =
            List.map
                (fun _ ->
#if DETECT_DEADLOCK
                    EvalWorker(this, workItemQueue, blockingWorker, deadlockDetector, evalSteps)
#else
                    EvalWorker(this, workItemQueue, blockingWorker, evalSteps)
#endif
                )
                [ start..final ]

        let evalWorkerCount, _, evalStepCount = this.GetConfiguration()
        createEvalWorkers blockingWorker evalStepCount 0 (evalWorkerCount - 1)

    member _.GetConfiguration() =
        let evalWorkerCount =
            if evalWorkerCount <= 0 then
                System.Environment.ProcessorCount - 1
            else
                evalWorkerCount

        let blockingWorkerCount =
            if blockingWorkerCount <= 0 then 1 else blockingWorkerCount

        let evalStepCount = if evalStepCount <= 0 then 15 else evalStepCount
        (evalWorkerCount, blockingWorkerCount, evalStepCount)
