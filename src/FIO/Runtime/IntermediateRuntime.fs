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
      WorkItemQueue: InternalQueue<WorkItem>
      BlockingWorker: BlockingWorker
      EvaluationWorkerSteps: int }

and private BlockingWorkerConfig =
    { WorkItemQueue: InternalQueue<WorkItem>
      BlockingItemQueue: InternalQueue<BlockingItem * WorkItem> }

and private EvaluationWorker(config: EvaluationWorkerConfig) =

    let processWorkItem workItem = 
        match config.Runtime.InternalRun workItem.Effect workItem.Stack workItem.PrevAction config.EvaluationWorkerSteps with
        | Success result, _, Evaluated, _ ->
            workItem.Complete <| Ok result

        | Failure error, _, Evaluated, _ ->
            workItem.Complete <| Error error

        | effect, stack, RescheduleForRunning, _ ->
            config.WorkItemQueue.Add
            <| WorkItem.Create(effect, workItem.InternalFiber, stack, RescheduleForRunning)

        | effect, stack, RescheduleForBlocking blockingItem, _ ->
            config.BlockingWorker.RescheduleForBlocking blockingItem 
            <| WorkItem.Create(effect, workItem.InternalFiber, stack, RescheduleForBlocking blockingItem)

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
        | BlockingChannel channel ->
            if channel.DataAvailable() then
                channel.UseAvailableData()
                config.WorkItemQueue.Add workItem
            else
              config.BlockingItemQueue.Add((blockingItem, workItem))
        | BlockingFiber ifiber ->
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

    let workItemQueue = new InternalQueue<WorkItem>()
    let blockingItemQueue = new InternalQueue<BlockingItem * WorkItem>()

    let createBlockingWorkers workItemQueue blockingItemQueue count =
        List.init count <| fun _ ->
            new BlockingWorker({
                WorkItemQueue = workItemQueue;
                BlockingItemQueue = blockingItemQueue
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
        let blockingWorkers = createBlockingWorkers workItemQueue blockingItemQueue config.BlockingWorkerCount
        // Currently we take head of the list, as the AdvancedRuntime
        // only supports a single blocking worker.
        createEvaluationWorkers this workItemQueue (List.head blockingWorkers) config.EvaluationWorkerSteps config.EvaluationWorkerCount
        |> ignore

    new() =
        Runtime(
            { EvaluationWorkerCount = Environment.ProcessorCount - 1
              BlockingWorkerCount = 1
              EvaluationWorkerSteps = 20 })

    member internal this.InternalRun effect stack prevAction evalSteps : FIO<obj, obj> * ContinuationStack * RuntimeAction * int =

        let rec handleSuccess result newEvalSteps stack =
            match stack with
            | [] -> (Success result, [], Evaluated, newEvalSteps)
            | (SuccessKind, cont) :: ss -> interpret (cont result) ss Evaluated evalSteps
            | (ErrorKind, _) :: ss -> handleSuccess result newEvalSteps ss

        and handleError error newEvalSteps stack =
            match stack with
            | [] -> (Failure error, [], Evaluated, newEvalSteps)
            | (SuccessKind, _) :: ss -> handleError error newEvalSteps ss
            | (ErrorKind, cont) :: ss -> interpret (cont error) ss Evaluated evalSteps

        and handleResult result newEvalSteps stack =
            match result with
            | Ok result' -> handleSuccess result' newEvalSteps stack
            | Error error -> handleError error newEvalSteps stack

        and interpret effect' stack' prevAction' evalSteps' =
            if evalSteps' = 0 then
                (effect', stack', RescheduleForRunning, 0)
            else
                let newEvalSteps = evalSteps' - 1
                match effect' with
                | Send (message, channel) ->
                    channel.Add message
                    handleSuccess message newEvalSteps stack'

                | Receive channel ->
                    if prevAction' = RescheduleForBlocking(BlockingChannel channel) then
                        handleSuccess (channel.Take()) newEvalSteps stack'
                    else
                        (Receive channel, stack', RescheduleForBlocking(BlockingChannel channel), evalSteps')

                | Concurrent (effect, fiber, ifiber) ->
                    workItemQueue.Add <| WorkItem.Create(effect, ifiber, [], prevAction')
                    handleSuccess fiber newEvalSteps stack'

                | Await ifiber ->
                    if ifiber.Completed() then
                        handleResult (ifiber.AwaitResult()) newEvalSteps stack'
                    else
                        (Await ifiber, stack', RescheduleForBlocking(BlockingFiber ifiber), evalSteps')

                | ChainSuccess (effect, continuation) ->
                    interpret effect ((SuccessKind, continuation) :: stack') prevAction' evalSteps'

                | ChainError (effect, continuation) ->
                    interpret effect ((ErrorKind, continuation) :: stack') prevAction' evalSteps'

                | Success result ->
                    handleSuccess result newEvalSteps stack'
                   
                | Failure error ->
                    handleError error newEvalSteps stack'

        interpret effect stack prevAction evalSteps

    override this.Run (effect: FIO<'R, 'E>) : Fiber<'R, 'E> =
        let fiber = Fiber<'R, 'E>()
        workItemQueue.Add <| WorkItem.Create(effect.Upcast(), fiber.ToInternal(), [], Evaluated)
        fiber
