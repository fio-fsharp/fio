(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module FIO.Runtime.Intermediate

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
      BlockingItemQueue: BlockingQueue<BlockingItem * WorkItem> }

and private EvaluationWorker(config: EvaluationWorkerConfig) =

    let processWorkItem workItem =
        match config.Runtime.InternalRun workItem.Eff workItem.Stack workItem.PrevAction config.EWSteps with
        | Success res, _, Evaluated, _ ->
            workItem.Complete
            <| Ok res

        | Failure err, _, Evaluated, _ ->
            workItem.Complete
            <| Error err

        | eff, stack, RescheduleForRunning, _ ->
            config.WorkItemQueue.Add
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

    let handleBlockingItem (blockingItem, workItem) =
        match blockingItem with
        | BlockingChannel chan ->
            if chan.DataAvailable() then
                chan.UseAvailableData()
                config.WorkItemQueue.Add workItem
            else
              config.BlockingItemQueue.Add((blockingItem, workItem))
        | BlockingIFiber ifiber ->
            if ifiber.Completed() then
                config.WorkItemQueue.Add workItem
            else
                config.BlockingItemQueue.Add((blockingItem, workItem))

    let startWorker (cancellationToken: CancellationToken) =
        async {
            config.BlockingItemQueue.GetConsumingEnumerable()
            |> Seq.takeWhile (fun _ -> not cancellationToken.IsCancellationRequested)
            |> Seq.iter handleBlockingItem
        } |> Async.Start

    let cancellationTokenSource = new CancellationTokenSource()
    do startWorker cancellationTokenSource.Token

    interface IDisposable with
        member this.Dispose() =
            cancellationTokenSource.Cancel()

    member internal this.RescheduleForBlocking blockingItem workItem =
        config.BlockingItemQueue.Add((blockingItem, workItem))

and Runtime(config: WorkerConfig) as this =
    inherit FIOWorkerRuntime(config)

    let workItemQueue = new BlockingQueue<WorkItem>()
    let blockingItemQueue = new BlockingQueue<BlockingItem * WorkItem>()

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
            { EWCount = Environment.ProcessorCount - 1
              BWCount = 1
              EWSteps = 20 })

    member internal this.InternalRun eff stack prevAction evalSteps : FIO<obj, obj> * ContStack * RuntimeAction * int =

        let rec handleSuccess res newEvalSteps stack =
            match stack with
            | [] -> (Success res, [], Evaluated, newEvalSteps)
            | (SuccessCont, cont) :: ss -> interpret (cont res) ss Evaluated evalSteps
            | (FailureCont, _) :: ss -> handleSuccess res newEvalSteps ss

        and handleError err newEvalSteps stack =
            match stack with
            | [] -> (Failure err, [], Evaluated, newEvalSteps)
            | (SuccessCont, _) :: ss -> handleError err newEvalSteps ss
            | (FailureCont, cont) :: ss -> interpret (cont err) ss Evaluated evalSteps

        and handleResult res newEvalSteps stack =
            match res with
            | Ok res -> handleSuccess res newEvalSteps stack
            | Error err -> handleError err newEvalSteps stack

        and interpret eff stack prevAction evalSteps =
            if evalSteps = 0 then
                (eff, stack, RescheduleForRunning, 0)
            else
                let newEvalSteps = evalSteps - 1
                match eff with
                | Success res ->
                    handleSuccess res newEvalSteps stack
                   
                | Failure err ->
                    handleError err newEvalSteps stack

                | Concurrent (eff, fiber, ifiber) ->
                    workItemQueue.Add <| WorkItem.Create eff ifiber ContStack.Empty prevAction
                    handleSuccess fiber newEvalSteps stack

                | Await ifiber ->
                    if ifiber.Completed() then
                        handleResult (ifiber.AwaitResult()) newEvalSteps stack
                    else
                        (Await ifiber, stack, RescheduleForBlocking <| BlockingIFiber ifiber, evalSteps)

                | ChainSuccess (eff, cont) ->
                    interpret eff ((SuccessCont, cont) :: stack) prevAction evalSteps

                | ChainError (eff, cont) ->
                    interpret eff ((FailureCont, cont) :: stack) prevAction evalSteps

                | Send (msg, chan) ->
                    chan.Add msg
                    handleSuccess msg newEvalSteps stack

                | Receive chan ->
                    if prevAction = RescheduleForBlocking (BlockingChannel chan) then
                        handleSuccess (chan.Take()) newEvalSteps stack
                    else
                        (Receive chan, stack, RescheduleForBlocking <| BlockingChannel chan, evalSteps)

        interpret eff stack prevAction evalSteps

    override this.Run (eff: FIO<'R, 'E>) : Fiber<'R, 'E> =
        let fiber = Fiber<'R, 'E>()
        workItemQueue.Add <| WorkItem.Create (eff.Upcast()) (fiber.ToInternal()) ContStack.Empty Evaluated
        fiber
