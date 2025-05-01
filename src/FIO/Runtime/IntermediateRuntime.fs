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

type private EvaluationWorkerConfig =
    { Runtime: Runtime
      WorkItemChan: InternalChannel<WorkItem>
      BlockingWorker: BlockingWorker
      EWSteps: int }

and private BlockingWorkerConfig =
    { WorkItemChan: InternalChannel<WorkItem>
      BlockingItemChan: InternalChannel<BlockingItem * WorkItem> }

and private EvaluationWorker(config: EvaluationWorkerConfig) =

    let processWorkItem workItem = task {
        match! config.Runtime.InterpretAsync workItem config.EWSteps with
        // TODO: Can we make this matching more concise? Make a type for it, for example.
        | Success res, _, Evaluated ->
            do! workItem.Complete <| Ok res
        | Failure err, _, Evaluated ->
            do! workItem.Complete <| Error err
        | eff, stack, RescheduleForRunning ->
            do! config.WorkItemChan.AddAsync
                <| WorkItem.Create (eff, workItem.IFiber, stack, RescheduleForRunning)
        | eff, stack, RescheduleForBlocking blockingItem ->
            do! config.BlockingWorker.RescheduleForBlocking blockingItem
                <| WorkItem.Create (eff, workItem.IFiber, stack, RescheduleForBlocking blockingItem)
        | _ ->
            invalidOp "EvaluationWorker: Unexpected state encountered during effect evaluation."
    }

    let startWorker () =
        task {
            let mutable loop = true
            while loop do
                let! hasWorkItem = config.WorkItemChan.WaitToReceiveAsync()
                if not hasWorkItem then
                    loop <- false
                else
                    let! workItem = config.WorkItemChan.TakeAsync()
                    do! processWorkItem workItem
        } |> ignore

    let cancellationTokenSource = new CancellationTokenSource()
    do startWorker ()

    interface IDisposable with
        member this.Dispose() =
            cancellationTokenSource.Cancel()

and private BlockingWorker(config: BlockingWorkerConfig) =
    
    let processBlockingChannel blockingItem (workItem: WorkItem) (chan: Channel<obj>)  =
        if chan.HasDataAvailable() then
            chan.UseAvailableData()
            config.WorkItemChan.AddAsync workItem
        else
            config.BlockingItemChan.AddAsync (blockingItem, workItem)
            
    let processBlockingFiber blockingItem workItem (ifiber: InternalFiber) =
        if ifiber.Completed() then
            config.WorkItemChan.AddAsync workItem
        else
            config.BlockingItemChan.AddAsync (blockingItem, workItem)

    let rec processBlockingItem (blockingItem, workItem) = task {
        match blockingItem with
        | BlockingChannel chan ->
            do! processBlockingChannel blockingItem workItem chan
        | BlockingIFiber ifiber ->
            do! processBlockingFiber blockingItem workItem ifiber
    }
    
    let startWorker () =
        task {
            let mutable loop = true
            while loop do
                let! hasBlockingItem = config.BlockingItemChan.WaitToReceiveAsync()
                if not hasBlockingItem then
                    loop <- false 
                else
                    let! blockingItem = config.BlockingItemChan.TakeAsync()
                    do! processBlockingItem blockingItem
        } |> ignore

    let cancellationTokenSource = new CancellationTokenSource()
    do startWorker ()

    interface IDisposable with
        member this.Dispose() =
            cancellationTokenSource.Cancel()

    member internal this.RescheduleForBlocking blockingItem workItem =
        config.BlockingItemChan.AddAsync (blockingItem, workItem)

and Runtime(config: WorkerConfig) as this =
    inherit FIOWorkerRuntime(config)
    
    let workItemChan = InternalChannel<WorkItem>()
    let blockingItemChan = InternalChannel<BlockingItem * WorkItem>()

    let createBlockingWorkers count =
        List.init count <| fun _ ->
            new BlockingWorker({
                WorkItemChan = workItemChan
                BlockingItemChan = blockingItemChan
            })

    let createEvaluationWorkers runtime blockingWorker evalSteps count =
        List.init count <| fun _ -> 
            new EvaluationWorker({ 
                Runtime = runtime
                WorkItemChan = workItemChan
                BlockingWorker = blockingWorker
                EWSteps = evalSteps 
            })

    do
        let blockingWorkers = createBlockingWorkers config.BWCount
        // Currently we take head of the list, as the IntermediateRuntime
        // only supports a single blocking worker.
        createEvaluationWorkers this (List.head blockingWorkers) config.EWSteps config.EWCount
        |> ignore

    override this.Name =
        "Intermediate"

    new() =
        Runtime
            { EWCount =
                let coreCount = Environment.ProcessorCount - 1
                if coreCount >= 2 then coreCount else 2
              BWCount = 1
              EWSteps = 100 }

    [<TailCall>]
    member internal this.InterpretAsync (workItem: WorkItem) evalSteps =
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
                    loop <- false

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
                            handleError <| onError exn
                    | SendChan (msg, chan) ->
                        do! chan.SendAsync msg
                        handleSuccess msg
                    | ReceiveChan chan ->
                        if chan.Count > 0 then
                            let! res = chan.ReceiveAsync()
                            handleSuccess res
                        else
                            let newPrevAction = RescheduleForBlocking <| BlockingChannel chan
                            currentPrevAction <- newPrevAction
                            result <- Some (ReceiveChan chan, currentContStack, newPrevAction)
                    | ConcurrentEffect (eff, fiber, ifiber) ->
                        do! workItemChan.AddAsync
                            <| WorkItem.Create (eff, ifiber, ContStack.Empty, currentPrevAction)
                        handleSuccess fiber
                    | ConcurrentTask (task, onError, fiber, ifiber) ->
                        task.ContinueWith((fun (t: Task<obj>) ->
                            if t.IsFaulted then
                                ifiber.Complete
                                <| Error (onError t.Exception.InnerException)
                            elif t.IsCanceled then
                                ifiber.Complete
                                <| Error (onError <| TaskCanceledException "Task has been cancelled.")
                            elif t.IsCompleted then
                                ifiber.Complete
                                <| Ok t.Result
                            else
                                ifiber.Complete
                                <| Error (onError <| InvalidOperationException "Task not completed.")),
                            CancellationToken.None,
                            TaskContinuationOptions.RunContinuationsAsynchronously,
                            TaskScheduler.Default)
                        |> ignore
                        handleSuccess fiber
                    | AwaitFiber ifiber ->
                        if ifiber.Completed() then
                            let! res = ifiber.AwaitAsync()
                            handleResult res
                        else
                            let newPrevAction = RescheduleForBlocking <| BlockingIFiber ifiber
                            currentPrevAction <- newPrevAction
                            result <- Some (AwaitFiber ifiber, currentContStack, newPrevAction)
                    | AwaitGenericTPLTask (task, onError) ->
                        try
                            let! res = task
                            handleSuccess res
                        with exn ->
                            handleError <| onError exn
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
        workItemChan.AddAsync
        <| WorkItem.Create (eff.Upcast(), fiber.Internal, ContStack.Empty, Evaluated)
        |> ignore
        fiber
