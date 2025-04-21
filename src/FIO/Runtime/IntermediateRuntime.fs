(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module FIO.Runtime.Intermediate

open FIO.Core

open System
open System.Threading
open System.Threading.Tasks

(*
type private EvaluationWorkerConfig =
    { Runtime: Runtime
      WorkItemChan: InternalChannel<WorkItem>
      BlockingWorker: BlockingWorker
      EWSteps: int }

and private BlockingWorkerConfig =
    { WorkItemChan: InternalChannel<WorkItem>
      BlockingItemChan: InternalChannel<BlockingItem * WorkItem> }

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
            // config.WorkItemChan.WriteAsync
            // <| WorkItem.Create eff workItem.IFiber stack RescheduleForRunning
            Task.FromResult ()
        | eff, stack, RescheduleForBlocking blockingItem, _ ->
            config.BlockingWorker.RescheduleForBlocking blockingItem 
            <| WorkItem.Create eff workItem.IFiber stack (RescheduleForBlocking blockingItem)
        | _ ->
            invalidOp "EvaluationWorker: Unexpected state encountered during effect evaluation."

    let startWorker (cancellationToken: CancellationToken) =
        let rec loop () =
            task {
                let! hasItem = config.WorkItemChan.WaitToReadAsync()
                if hasItem then
                    let! item = config.WorkItemChan.ReadAsync()
                    processWorkItem item
                    |> ignore
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

    let handleBlockingItem (blockingItem, workItem) =
        match blockingItem with
        | BlockingChannel chan ->
            // if chan.DataAvailable() then
            //     chan.UseAvailableData()
            config.WorkItemChan.WriteAsync workItem
            // else
            //  config.BlockingItemQueue.Add((blockingItem, workItem))
        | BlockingIFiber ifiber ->
            // if ifiber.Completed() then
            if true then
                config.WorkItemChan.WriteAsync workItem
            else
                config.BlockingItemChan.WriteAsync (blockingItem, workItem)

    let startWorker (cancellationToken: CancellationToken) =
        let rec loop () =
            task {
                let! hasItem = config.BlockingItemChan.WaitToReadAsync()
                if hasItem then
                    let! item = config.BlockingItemChan.ReadAsync()
                    handleBlockingItem item
                    |> ignore
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
        config.BlockingItemChan.WriteAsync (blockingItem, workItem)
*)
type Runtime(config: WorkerConfig) as this =
    inherit FIOWorkerRuntime(config)
    (*
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
                | Action (func, onError) ->
                    handleResult (Ok 1) newEvalSteps stack
                | WriteChan (msg, chan) ->
                    chan.WriteAsync msg
                    |> ignore
                    handleSuccess msg newEvalSteps stack
                | ReadChan chan ->
                    if prevAction = RescheduleForBlocking (BlockingChannel chan) then
                        handleSuccess (chan.ReadAsync()) newEvalSteps stack
                    else
                        (ReadChan chan, stack, RescheduleForBlocking <| BlockingChannel chan, evalSteps)
                | Concurrent (eff, fiber, ifiber) ->
                    workItemChan.WriteAsync <| WorkItem.Create eff ifiber ContStack.Empty prevAction
                    |> ignore
                    handleSuccess fiber newEvalSteps stack
                | AwaitFiber ifiber ->
                    // if ifiber.Completed() then
                    if true then
                        // handleResult (ifiber.AwaitResult()) newEvalSteps stack
                        handleResult (Ok 1) newEvalSteps stack
                    else
                        (AwaitFiber ifiber, stack, RescheduleForBlocking <| BlockingIFiber ifiber, evalSteps)
                | ChainSuccess (eff, cont) ->
                    interpret eff ((SuccessCont, cont) :: stack) prevAction evalSteps
                | ChainError (eff, cont) ->
                    interpret eff ((FailureCont, cont) :: stack) prevAction evalSteps

        interpret eff stack prevAction evalSteps
*)
    override this.Run (eff: FIO<'R, 'E>) : Fiber<'R, 'E> =
        let fiber = Fiber<'R, 'E>()
        //workItemChan.WriteAsync
        //<|  WorkItem.Create (eff.Upcast()) (fiber.ToInternal()) ContStack.Empty Evaluated
        // |> ignore
        fiber

    override this.Name () =
        "Intermediate"
