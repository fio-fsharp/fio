(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module FIO.Runtime.Advanced

open System
open System.Threading

open FIO.Core

type private EvaluationWorkerConfig =
    { Runtime: Runtime
      WorkItemQueue: BlockingQueue<WorkItem>
      BlockingWorker: BlockingWorker
      EWSteps: int }

and private BlockingWorkerConfig =
    { WorkItemQueue: BlockingQueue<WorkItem>
      BlockingItemQueue: BlockingQueue<BlockingItem>}

and private EvaluationWorker(config: EvaluationWorkerConfig) =

    let completeWorkItem (workItem: WorkItem) res =
        workItem.Complete res
        workItem.IFiber.RescheduleBlockingWorkItems config.WorkItemQueue

    let handleBlockingFiber blockingItem =
        match blockingItem with
        | BlockingIFiber ifiber when ifiber.Completed() ->
            ifiber.RescheduleBlockingWorkItems config.WorkItemQueue
        | _ -> ()

    let processWorkItem workItem =
        match config.Runtime.InternalRun workItem.Eff workItem.Stack workItem.PrevAction config.EWSteps with
        | Success res, _, Evaluated, _ ->
            completeWorkItem workItem
            <| Ok res
        | Failure err, _, Evaluated, _ ->
            completeWorkItem workItem
            <| Error err
        | eff, stack, RescheduleForRunning, _ ->
            config.WorkItemQueue.Add 
            <| WorkItem.Create eff workItem.IFiber stack RescheduleForRunning
        | eff, stack, RescheduleForBlocking blockingItem, _ ->
            config.BlockingWorker.RescheduleForBlocking blockingItem
            <| WorkItem.Create eff workItem.IFiber stack (RescheduleForBlocking blockingItem)
            handleBlockingFiber blockingItem
        | _ ->
            invalidOp "EvaluationWorker: Unexpected state encountered during effect interpretation."

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

    let processBlockingChannel (blockingChan: Channel<obj>) =
        if blockingChan.HasBlockingWorkItems() then
            blockingChan.RescheduleBlockingWorkItems config.WorkItemQueue
        else
            config.BlockingItemQueue.Add <| BlockingChannel blockingChan

    let processBlockingTask (blockingTask: InternalTask<obj>) =
        if blockingTask.Completed() then
            if blockingTask.HasBlockingWorkItems() then
                blockingTask.RescheduleBlockingWorkItems config.WorkItemQueue
            else
                ()
        else
            config.BlockingItemQueue.Add (BlockingTask blockingTask)

    let processBlockingItem blockingItem =
        match blockingItem with
        | BlockingChannel chan -> processBlockingChannel chan
        | BlockingTask task -> processBlockingTask task
        | _ -> ()

    let startWorker (cancellationToken: CancellationToken) =
        async {
            config.BlockingItemQueue.GetConsumingEnumerable()
            |> Seq.takeWhile (fun _ -> not cancellationToken.IsCancellationRequested)
            |> Seq.iter processBlockingItem
        } |> Async.Start

    let cancellationTokenSource = new CancellationTokenSource()
    do startWorker cancellationTokenSource.Token

    interface IDisposable with
        member this.Dispose() =
            cancellationTokenSource.Cancel()

    member internal this.RescheduleForBlocking blockingItem workItem =
        match blockingItem with
        | BlockingChannel chan ->
            chan.AddBlockingWorkItem workItem
        | BlockingIFiber ifiber ->
            ifiber.AddBlockingWorkItem workItem
        | BlockingTask task ->
            config.BlockingItemQueue.Add <| BlockingTask task
            task.AddBlockingWorkItem workItem

and Runtime(config: WorkerConfig) as this =
    inherit FIOWorkerRuntime(config)

    let workItemQueue = new BlockingQueue<WorkItem>()
    let blockingItemQueue = new BlockingQueue<BlockingItem>()

    let createBlockingWorkers workItemQueue blockingItemQueue count =
        List.init count <| fun _ ->
            new BlockingWorker({
                WorkItemQueue = workItemQueue
                BlockingItemQueue = blockingItemQueue
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
        let blockingWorkers = createBlockingWorkers workItemQueue blockingItemQueue config.BWCount
        // Currently we take head of the list, as the AdvancedRuntime
        // only supports a single blocking worker.
        createEvaluationWorkers this workItemQueue (List.head blockingWorkers) config.EWSteps config.EWCount
        |> ignore

    new() =
        Runtime(
            { EWCount =
                let coreCount = Environment.ProcessorCount - 1
                if coreCount >= 2 then coreCount else 2
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
                | SendChan (msg, chan) ->
                    chan.Add msg
                    blockingItemQueue.Add <| BlockingChannel chan
                    handleSuccess msg stack evalSteps newEvalSteps
                | ReceiveChan chan ->
                    if prevAction = RescheduleForBlocking (BlockingChannel chan) then
                        handleSuccess (chan.Take()) stack evalSteps newEvalSteps
                    else
                        (ReceiveChan chan, stack, RescheduleForBlocking <| BlockingChannel chan, evalSteps)
                | AwaitTask task ->
                    if prevAction = RescheduleForBlocking (BlockingTask task) then
                        handleResult (task.AwaitResult()) stack evalSteps newEvalSteps
                    else
                        (AwaitTask task, stack, RescheduleForBlocking <| BlockingTask task, evalSteps)
                | Concurrent (eff, fiber, ifiber) ->
                    workItemQueue.Add <| WorkItem.Create eff ifiber ContStack.Empty prevAction
                    handleSuccess fiber stack evalSteps newEvalSteps
                | AwaitFiber ifiber ->
                    if ifiber.Completed() then
                        handleResult (ifiber.AwaitResult()) stack evalSteps newEvalSteps
                    else
                        (AwaitFiber ifiber, stack, RescheduleForBlocking <| BlockingIFiber ifiber, evalSteps)
                | ChainSuccess (eff, cont) ->
                    interpret eff ((SuccessCont, cont) :: stack) prevAction evalSteps
                | ChainError (eff, cont) ->
                    interpret eff ((FailureCont, cont) :: stack) prevAction evalSteps

        interpret eff stack prevAction evalSteps

    override this.Run (eff: FIO<'R, 'E>) : Fiber<'R, 'E> =
        let fiber = Fiber<'R, 'E>()
        workItemQueue.Add
        <| WorkItem.Create (eff.Upcast()) (fiber.ToInternal()) ContStack.Empty Evaluated
        fiber

    override this.Name () =
        "Advanced"
