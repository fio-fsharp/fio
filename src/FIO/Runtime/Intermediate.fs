(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(************************************************************************************)

module FIO.Runtime.Intermediate

open FIO.Core

#if DETECT_DEADLOCK || MONITOR
open FIO.Runtime.Tools
#endif

type internal EvalWorker(runtime: IntermediateRuntime, workItemQueue: InternalQueue<WorkItem>, blockingWorker: BlockingWorker, evalSteps) =
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
                match runtime.InternalRun workItem.Effect workItem.LastAction evalSteps workItem.Stack with
                | (Success res, _), Evaluated, _ -> workItem.Complete(Ok res)
                | (Failure err, _), Evaluated, _ -> workItem.Complete(Error err)
                | (eff, stack), RescheduleForRunning, _ ->
                    let workItem = WorkItem.Create(eff, workItem.InternalFiber, stack, RescheduleForRunning)
                    workItemQueue.Add workItem
                | (eff, stack), RescheduleForBlocking blockingItem, _ ->
                    let workItem =
                        WorkItem.Create(eff, workItem.InternalFiber, stack, (RescheduleForBlocking blockingItem))

                    blockingWorker.RescheduleForBlocking blockingItem workItem
                | _ -> failwith $"EvalWorker: Error occurred while evaluating effect!"
#if DETECT_DEADLOCK
                working <- false
#endif
         }
         |> Async.StartAsTask
         |> ignore)

#if DETECT_DEADLOCK
    override _.Working() = working && workItemQueue.Count > 0
#endif

and internal BlockingWorker
    (
        workItemQueue: InternalQueue<WorkItem>,
#if DETECT_DEADLOCK
        deadlockDetector: DeadlockDetector<BlockingWorker, EvalWorker>,
#endif
        blockingItemQueue: InternalQueue<BlockingItem * WorkItem>
    ) as self =
#if DETECT_DEADLOCK
    inherit Worker()
    let mutable working = false
#endif
    let _ =
        (async {
            for blockingItem, workItem in blockingItemQueue.GetConsumingEnumerable() do
#if DETECT_DEADLOCK
                working <- true
#endif
                self.HandleBlockingItem blockingItem workItem
#if DETECT_DEADLOCK
                working <- false
#endif
         }
         |> Async.StartAsTask
         |> ignore)

    member private this.HandleBlockingItem blockingItem workItem =
        match blockingItem with
        | BlockingChannel chan ->
            if chan.DataAvailable() then
                chan.UseAvailableData()
                workItemQueue.Add workItem
#if DETECT_DEADLOCK
            deadlockDetector.RemoveBlockingItem blockingItem
#endif
            else
              blockingItemQueue.Add ((blockingItem, workItem))
        | BlockingFiber ifiber ->
            if ifiber.Completed() then
                workItemQueue.Add workItem
#if DETECT_DEADLOCK
                deadlockDetector.RemoveBlockingItem blockingItem
#endif
            else
                blockingItemQueue.Add((blockingItem, workItem))

    member internal this.RescheduleForBlocking blockingItem workItem =
        blockingItemQueue.Add((blockingItem, workItem))
#if DETECT_DEADLOCK
        deadlockDetector.AddBlockingItem blockingItem
#endif

#if DETECT_DEADLOCK
    override _.Working() = working && workItemQueue.Count > 0
#endif

and IntermediateRuntime(evalWorkerCount, blockingWorkerCount, evalStepCount) as self =
    inherit Runtime()

    let workItemQueue = new InternalQueue<WorkItem>()
    let blockingItemQueue = new InternalQueue<BlockingItem * WorkItem>()

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
        Monitor(workItemQueue, Some blockingItemQueue, None, None) |> ignore
#endif

    new() = IntermediateRuntime(System.Environment.ProcessorCount - 1, 1, 15)

    [<TailCall>]
    member internal this.InternalRun effect prevAction evalSteps stack : (FIO<obj, obj> * ContinuationStack) * RuntimeAction * int =
        let rec handleSuccess result newEvalSteps stack =
            match stack with
            | [] -> ((Success result, []), Evaluated, newEvalSteps)
            | s :: ss ->
                match s with
                | SuccConts succCont -> this.InternalRun (succCont result) Evaluated evalSteps ss
                | ErrorConts _ -> handleSuccess result newEvalSteps ss

        let rec handleError error newEvalSteps stack =
            match stack with
            | [] -> ((Failure error, []), Evaluated, newEvalSteps)
            | s :: ss ->
                match s with
                | SuccConts _ -> handleError error newEvalSteps ss
                | ErrorConts errCont -> this.InternalRun (errCont error) Evaluated evalSteps ss

        let handleResult result newEvalSteps stack =
            match result with
            | Ok result -> handleSuccess result newEvalSteps stack
            | Error error -> handleError error newEvalSteps stack

        if evalSteps = 0 then
            ((effect, stack), RescheduleForRunning, 0)
        else
            let newEvalSteps = evalSteps - 1

            match effect with
            | NonBlocking action -> handleResult (action ()) newEvalSteps stack
            | Blocking channel ->
                if prevAction = RescheduleForBlocking(BlockingChannel channel) then
                    let result = channel.Take()
                    handleSuccess result newEvalSteps stack
                else
                    ((Blocking channel, stack), RescheduleForBlocking(BlockingChannel channel), evalSteps)
            | Send(message, channel) ->
                channel.Add message
                handleSuccess message newEvalSteps stack
            | Concurrent(effect, fiber, ifiber) ->
                workItemQueue.Add <| WorkItem.Create(effect, ifiber, [], prevAction)
                handleSuccess fiber newEvalSteps stack
            | Await ifiber ->
                if ifiber.Completed() then
                    handleResult (ifiber.AwaitResult()) newEvalSteps stack
                else
                    ((Await ifiber, stack), RescheduleForBlocking(BlockingFiber ifiber), evalSteps)
            | ChainSuccess(effect, continuation) -> this.InternalRun effect prevAction evalSteps (SuccConts continuation :: stack)
            | ChainError(effect, continuation) -> this.InternalRun effect prevAction evalSteps (ErrorConts continuation :: stack)
            | Success result -> handleSuccess result newEvalSteps stack
            | Failure error -> handleError error newEvalSteps stack

    override this.Run<'R, 'E>(effect: FIO<'R, 'E>) : Fiber<'R, 'E> =
        let fiber = Fiber<'R, 'E>()

        workItemQueue.Add
        <| WorkItem.Create(effect.Upcast(), fiber.ToInternal(), [], Evaluated)

        fiber

    member private this.CreateBlockingWorkers() : BlockingWorker list =
        let createBlockingWorkers start final =
            List.map
                (fun _ ->
#if DETECT_DEADLOCK
                    BlockingWorker(workItemQueue, deadlockDetector, blockingItemQueue))
                [ start..final ]
#else
                    BlockingWorker(workItemQueue, blockingItemQueue))
                [ start..final ]
#endif
        let _, blockingWorkerCount, _ = this.GetConfiguration()
        createBlockingWorkers 0 (blockingWorkerCount - 1)

    member private this.CreateEvalWorkers blockingWorker : EvalWorker list =
        let createEvalWorkers blockingWorker evalSteps start final =
            List.map (fun _ -> EvalWorker(this, workItemQueue, blockingWorker, evalSteps)) [ start..final ]

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
