(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module FIO.Runtime.Concurrent

open FIO.DSL

open System
open System.Threading
open System.Threading.Tasks

type private EvaluationWorkerConfig =
    { Runtime: Runtime
      ActiveWorkItemChan: InternalChannel<WorkItem>
      BlockingWorker: BlockingWorker
      EWSteps: int }

and private BlockingWorkerConfig =
    { ActiveWorkItemChan: InternalChannel<WorkItem>
      ActiveBlockingEventChan: InternalChannel<Channel<obj>> }

and private EvaluationWorker (config: EvaluationWorkerConfig) =
    
    let processWorkItem (workItem: WorkItem) =
        task {
            match! config.Runtime.InterpretAsync workItem config.EWSteps with
            | Success res, _, Evaluated ->
                do! workItem.CompleteAndReschedule (Ok res) config.ActiveWorkItemChan
            | Failure err, _, Evaluated ->
                do! workItem.CompleteAndReschedule (Error err) config.ActiveWorkItemChan
            | eff, stack, RescheduleForRunning ->
                do! config.ActiveWorkItemChan.AddAsync
                    <| WorkItem.Create (eff, workItem.IFiber, stack, RescheduleForRunning)
            | _, _, Skipped ->
                ()
            | _ ->
                printfn "ERROR: EvaluationWorker: Unexpected state encountered during effect interpretation!"
                invalidOp "EvaluationWorker: Unexpected state encountered during effect interpretation!"
        }

    let startWorker () =
        (new Task((fun () ->
            task {
                let mutable loop = true
                while loop do
                    let! hasWorkItem = config.ActiveWorkItemChan.WaitToTakeAsync()
                    if not hasWorkItem then
                        loop <- false
                    else
                        let! workItem = config.ActiveWorkItemChan.TakeAsync()
                        do! processWorkItem workItem
            } |> ignore), TaskCreationOptions.LongRunning))
            .Start TaskScheduler.Default

    let cancellationTokenSource = new CancellationTokenSource()
    do startWorker ()
    
    interface IDisposable with
        member _.Dispose () =
            cancellationTokenSource.Cancel()

and private BlockingWorker (config: BlockingWorkerConfig) =

    let processBlockingChannel (blockingChan: Channel<obj>) =
        task {
            if blockingChan.BlockingWorkItemCount > 0 then
                do! blockingChan.RescheduleNextBlockingWorkItem
                    <| config.ActiveWorkItemChan
            else
                do! config.ActiveBlockingEventChan.AddAsync blockingChan
        }

    let startWorker () =
        (new Task((fun () ->
            task {
                let mutable loop = true
                while loop do
                    // When the BlockingWorker receives a channel, it is an "event" that the
                    // the given channel now has received one element, that the next blocking effect can retrieve.
                    let! hasBlockingItem = config.ActiveBlockingEventChan.WaitToTakeAsync()
                    if not hasBlockingItem then
                        loop <- false 
                    else
                        let! blockingChanEvent = config.ActiveBlockingEventChan.TakeAsync()
                        do! processBlockingChannel blockingChanEvent
            } |> ignore), TaskCreationOptions.LongRunning))
            .Start TaskScheduler.Default

    let cancellationTokenSource = new CancellationTokenSource()
    do startWorker ()

    interface IDisposable with
        member _.Dispose () =
            cancellationTokenSource.Cancel()

and Runtime (config: WorkerConfig) as this =
    inherit FWorkerRuntime(config)

    let activeWorkItemChan = InternalChannel<WorkItem>()
    let activeBlockingEventChan = InternalChannel<Channel<obj>>()

    let createBlockingWorkers count =
        List.init count <| fun _ ->
            new BlockingWorker({
                ActiveWorkItemChan = activeWorkItemChan
                ActiveBlockingEventChan = activeBlockingEventChan
            })

    let createEvaluationWorkers runtime blockingWorker evalSteps count =
        List.init count <| fun _ -> 
            new EvaluationWorker({ 
                Runtime = runtime
                ActiveWorkItemChan = activeWorkItemChan
                BlockingWorker = blockingWorker
                EWSteps = evalSteps 
            })

    do
        let blockingWorkers = createBlockingWorkers config.BWCount
        // Currently we take head of the list, as the AdvancedRuntime
        // only supports a single blocking worker.
        createEvaluationWorkers this (List.head blockingWorkers) config.EWSteps config.EWCount
        |> ignore

    override _.Name =
        "Concurrent"

    new() =
        Runtime
            { EWCount =
                let coreCount = Environment.ProcessorCount - 1
                if coreCount >= 2 then coreCount else 2
              BWCount = 1
              EWSteps = 100 }

    [<TailCall>]
    member internal _.InterpretAsync workItem evalSteps =
        let mutable currentEff = workItem.Eff
        let mutable currentContStack = workItem.Stack
        let mutable currentEWSteps = evalSteps
        let mutable resultOpt = None

        let handleSuccess res =
            let mutable loop = true
            while loop do
                match currentContStack with
                | [] -> 
                    resultOpt <- Some (Success res, ContStack.Empty, Evaluated)
                    loop <- false
                | (SuccessCont, cont) :: ss -> 
                    currentEff <- cont res
                    currentContStack <- ss
                    loop <- false
                | (FailureCont, _) :: ss -> 
                    currentContStack <- ss

        let handleError err =
            let mutable loop = true
            while loop do
                match currentContStack with
                | [] ->
                    resultOpt <- Some (Failure err, ContStack.Empty, Evaluated)
                    loop <- false
                | (SuccessCont, _) :: ss -> 
                    currentContStack <- ss
                | (FailureCont, cont) :: ss ->
                    currentEff <- cont err
                    currentContStack <- ss
                    loop <- false

        let handleResult res =
            match res with
            | Ok res ->
                handleSuccess res
            | Error err ->
                handleError err

        task {
            while resultOpt.IsNone do
                if currentEWSteps = 0 then
                    resultOpt <- Some (currentEff, currentContStack, RescheduleForRunning)
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
                        do! activeBlockingEventChan.AddAsync chan
                        handleSuccess msg
                    | ReceiveChan chan ->
                        if chan.Count > 0 then
                            let! res = chan.ReceiveAsync()
                            handleSuccess res
                        else
                            do! chan.AddBlockingWorkItem
                                <| WorkItem.Create (ReceiveChan chan, workItem.IFiber, currentContStack, Skipped)
                            resultOpt <- Some (Success (), ContStack.Empty, Skipped)
                    | ConcurrentEffect (eff, fiber, ifiber) ->
                        do! activeWorkItemChan.AddAsync
                            <| WorkItem.Create (eff, ifiber, ContStack.Empty, workItem.PrevAction)
                        handleSuccess fiber
                    | ConcurrentTPLTask (task, onError, fiber, ifiber) ->
                        do! Task.Run(fun () ->
                            (task ()).ContinueWith((fun (t: Task<obj>) ->
                                if t.IsFaulted then
                                    ifiber.CompleteAndReschedule
                                        (Error <| onError t.Exception.InnerException) activeWorkItemChan
                                elif t.IsCanceled then
                                    ifiber.CompleteAndReschedule
                                        (Error (onError <| TaskCanceledException "Task has been cancelled.")) activeWorkItemChan
                                elif t.IsCompleted then
                                    ifiber.CompleteAndReschedule
                                        (Ok t.Result) activeWorkItemChan
                                else
                                    ifiber.CompleteAndReschedule
                                        (Error (onError <| InvalidOperationException "Task not completed.")) activeWorkItemChan),
                                CancellationToken.None,
                                TaskContinuationOptions.RunContinuationsAsynchronously,
                                TaskScheduler.Default) :> Task)
                        handleSuccess fiber
                    | AwaitFiber ifiber ->
                        if ifiber.Completed then
                            let! res = ifiber.AwaitAsync()
                            handleResult res
                        else
                            do! ifiber.AddBlockingWorkItem (WorkItem.Create (AwaitFiber ifiber, workItem.IFiber, currentContStack, Skipped))
                            // TODO: This double check here fixes a race condition, but is not optimal.
                            if ifiber.Completed then
                                do! ifiber.RescheduleBlockingWorkItems activeWorkItemChan
                            resultOpt <- Some (Success (), ContStack.Empty, Skipped)
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

            return resultOpt.Value
        }
        
    member private _.Reset () =
        activeWorkItemChan.Clear ()
        activeBlockingEventChan.Clear ()

    override _.Run<'R, 'E> (eff: FIO<'R, 'E>) : Fiber<'R, 'E> =
        this.Reset ()
        let fiber = Fiber<'R, 'E>()
        activeWorkItemChan.AddAsync
        <| WorkItem.Create (eff.Upcast(), fiber.Internal, ContStack.Empty, Evaluated)
        |> ignore
        fiber
