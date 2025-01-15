(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(************************************************************************************)

module FIO.Runtime.Deadlocking

open FIO.Core

open System.Collections.Concurrent

type internal EvalWorker
    (
        runtime: DeadlockingRuntime,
        workItemQueue: InternalQueue<WorkItem>,
        blockingWorker: BlockingWorker,
        evalSteps
    ) as self =
    let _ =
        (async {
            for workItem in workItemQueue.GetConsumingEnumerable() do
                match runtime.InternalRun workItem.Effect workItem.PrevAction evalSteps with
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
         }
         |> Async.StartAsTask
         |> ignore)

    member private _.CompleteWorkItem(workItem, res) =
        workItem.Complete res
        blockingWorker.RescheduleBlockingEffects workItem.InternalFiber

    member private _.RescheduleForRunning(workItem) = workItemQueue.Add workItem


and internal BlockingWorker
    (
        workItemQueue: InternalQueue<WorkItem>,
        blockingWorkItemMap: BlockingWorkItemMap,
        blockingEventQueue: InternalQueue<Channel<obj>>
    ) as self =
    let _ =
        (async {
            for blockingChan in blockingEventQueue.GetConsumingEnumerable() do
                self.HandleBlockingChannel blockingChan
         }
         |> Async.StartAsTask
         |> ignore)

    member internal this.RescheduleForBlocking blockingItem workItem =
        blockingWorkItemMap.RescheduleWorkItem blockingItem workItem

    member private this.HandleBlockingChannel(blockingChan) =
        let blockingItem = BlockingChannel blockingChan

        match blockingWorkItemMap.TryRemove blockingItem with
        | true, (blockingQueue: InternalQueue<WorkItem>) ->
            workItemQueue.Add <| blockingQueue.Take()
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
    inherit FIORuntime()

    let workItemQueue = new InternalQueue<WorkItem>()
    let blockingEventQueue = new InternalQueue<Channel<obj>>()
    let blockingWorkItemMap = BlockingWorkItemMap()

    do
        let blockingWorkers = self.CreateBlockingWorkers()
        self.CreateEvalWorkers(List.head blockingWorkers) |> ignore

    new() = DeadlockingRuntime(System.Environment.ProcessorCount - 1, 1, 15)

    member internal this.InternalRun eff prevAction evalSteps : FIO<obj, obj> * RuntimeAction * int =
        if evalSteps = 0 then
            (eff, RescheduleForRunning, 0)
        else
            match eff with
            | Send(value, chan) ->
                chan.Add value
                blockingEventQueue.Add <| chan
                (Success value, Evaluated, evalSteps - 1)
            | Receive chan ->
                if prevAction = RescheduleForBlocking(BlockingChannel chan) then
                    (Success <| chan.Take(), Evaluated, evalSteps - 1)
                else
                    (Receive chan, RescheduleForBlocking(BlockingChannel chan), evalSteps)
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
                    BlockingWorker(workItemQueue, blockingWorkItemMap, blockingEventQueue))
                [ start..final ]
        let _, blockingWorkerCount, _ = this.GetConfiguration()
        createBlockingWorkers 0 (blockingWorkerCount - 1)

    member private this.CreateEvalWorkers blockingWorker =
        let createEvalWorkers blockingWorker evalSteps start final =
            List.map
                (fun _ ->
                    EvalWorker(this, workItemQueue, blockingWorker, evalSteps)
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
