(*********************************************************************************************)
(* FIO - A Type-Safe, Purely Functional Effect System for Asynchronous and Concurrent F#     *)
(* Copyright (c) 2022-2025 - Daniel "iyyel" Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                                       *)
(*********************************************************************************************)

module FSharp.FIO.Runtime.Concurrent

open FSharp.FIO.DSL

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
                    let! hasWorkItem = config.ActiveWorkItemChan.WaitToTakeAsync ()
                    if not hasWorkItem then
                        loop <- false
                    else
                        let! workItem = config.ActiveWorkItemChan.TakeAsync ()
                        do! processWorkItem workItem
            } |> ignore), TaskCreationOptions.LongRunning))
            .Start TaskScheduler.Default

    let cancellationTokenSource = new CancellationTokenSource ()
    do startWorker ()
    
    interface IDisposable with
        member _.Dispose () =
            cancellationTokenSource.Cancel ()

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
                    let! hasBlockingItem = config.ActiveBlockingEventChan.WaitToTakeAsync ()
                    if not hasBlockingItem then
                        loop <- false 
                    else
                        let! blockingChanEvent = config.ActiveBlockingEventChan.TakeAsync ()
                        do! processBlockingChannel blockingChanEvent
            } |> ignore), TaskCreationOptions.LongRunning))
            .Start TaskScheduler.Default

    let cancellationTokenSource = new CancellationTokenSource ()
    do startWorker ()

    interface IDisposable with
        member _.Dispose () =
            cancellationTokenSource.Cancel ()

and Runtime (config: WorkerConfig) as this =
    inherit FWorkerRuntime(config)

    let activeWorkItemChan = InternalChannel<WorkItem> ()
    let activeBlockingEventChan = InternalChannel<Channel<obj>> ()

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
        let blockingWorkers = createBlockingWorkers config.BWC
        // Currently we take head of the list, as the AdvancedRuntime
        // only supports a single blocking worker.
        createEvaluationWorkers this (List.head blockingWorkers) config.EWS config.EWC
        |> ignore

    override _.Name =
        "Concurrent"

    new() =
        Runtime
            { EWC =
                let coreCount = Environment.ProcessorCount - 1
                if coreCount >= 2 then coreCount else 2
              BWC = 1
              EWS = 200 }

    [<TailCall>]
    member internal _.InterpretAsync workItem evalSteps =
        let mutable currentEff = workItem.Eff
        let mutable currentContStack = workItem.Stack
        let mutable currentEWSteps = evalSteps
        let mutable result = Unchecked.defaultof<_>
        let mutable completed = false

        let inline processSuccess res =
            let mutable loop = true
            while loop do
                if currentContStack.Count = 0 then
                    result <- (Success res, ResizeArray<ContStackFrame> (), Evaluated)
                    completed <- true
                    loop <- false
                else
                    let stackFrame = pop currentContStack
                    match stackFrame.ContType with
                    | SuccessCont ->
                        currentEff <- stackFrame.Cont res
                        loop <- false
                    | FailureCont ->
                        ()

        let inline processError err =
            let mutable loop = true
            while loop do
                if currentContStack.Count = 0 then
                    result <- (Failure err, ResizeArray<ContStackFrame> (), Evaluated)
                    completed <- true
                    loop <- false
                else
                    let stackFrame = pop currentContStack
                    match stackFrame.ContType with
                    | SuccessCont ->
                        ()
                    | FailureCont ->
                        currentEff <- stackFrame.Cont err
                        loop <- false

        let inline processResult res =
            match res with
            | Ok res ->
                processSuccess res
            | Error err ->
                processError err

        task {
            while not completed do
                if currentEWSteps = 0 then
                    result <- (currentEff, currentContStack, RescheduleForRunning)
                    completed <- true
                else
                    currentEWSteps <- currentEWSteps - 1
                    match currentEff with
                    | Success res ->
                        processSuccess res
                    | Failure err ->
                        processError err
                    | Action (func, onError) ->
                        try 
                            let res = func ()
                            processSuccess res
                        with exn ->
                            processError
                            <| onError exn
                    | SendChan (msg, chan) ->
                        do! chan.SendAsync msg
                        do! activeBlockingEventChan.AddAsync chan
                        processSuccess msg
                    | ReceiveChan chan ->
                        if chan.Count > 0 then
                            let! res = chan.ReceiveAsync ()
                            processSuccess res
                        else
                            do! chan.AddBlockingWorkItem
                                <| WorkItem.Create (ReceiveChan chan, workItem.IFiber, currentContStack, Skipped)
                            result <- (Success (), ResizeArray<ContStackFrame> (), Skipped)
                            completed <- true
                    | ConcurrentEffect (eff, fiber, ifiber) ->
                        do! activeWorkItemChan.AddAsync
                            <| WorkItem.Create (eff, ifiber, ResizeArray<ContStackFrame> (), workItem.PrevAction)
                        processSuccess fiber
                    | ConcurrentTPLTask (lazyTask, onError, fiber, ifiber) ->
                        do! Task.Run(fun () ->
                            (lazyTask ()).ContinueWith((fun (t: Task) ->
                                if t.IsFaulted then
                                    ifiber.CompleteAndReschedule
                                        (Error <| onError t.Exception.InnerException) activeWorkItemChan
                                elif t.IsCanceled then
                                    ifiber.CompleteAndReschedule
                                        (Error (onError <| TaskCanceledException "Task has been cancelled.")) activeWorkItemChan
                                elif t.IsCompleted then
                                    ifiber.CompleteAndReschedule
                                        (Ok ()) activeWorkItemChan
                                else
                                    ifiber.CompleteAndReschedule
                                        (Error (onError <| InvalidOperationException "Task not completed.")) activeWorkItemChan),
                                CancellationToken.None,
                                TaskContinuationOptions.RunContinuationsAsynchronously,
                                TaskScheduler.Default) :> Task)
                        processSuccess fiber
                    | ConcurrentGenericTPLTask (lazyTask, onError, fiber, ifiber) ->
                        do! Task.Run(fun () ->
                            (lazyTask ()).ContinueWith((fun (t: Task<obj>) ->
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
                        processSuccess fiber
                    | AwaitFiber ifiber ->
                        if ifiber.Completed then
                            let! res = ifiber.Task
                            processResult res
                        else
                            do! ifiber.AddBlockingWorkItem (WorkItem.Create (AwaitFiber ifiber, workItem.IFiber, currentContStack, Skipped))
                            // TODO: This double check here fixes a race condition, but is not optimal.
                            if ifiber.Completed then
                                do! ifiber.RescheduleBlockingWorkItems activeWorkItemChan
                            result <- (Success (), ResizeArray<ContStackFrame> (), Skipped)
                            completed <- true
                    | AwaitTPLTask (task, onError) ->
                        try
                            let! res = task
                            processSuccess res
                        with exn ->
                            processError <| onError exn
                    | AwaitGenericTPLTask (task, onError) ->
                        try
                            let! res = task
                            processSuccess res
                        with exn ->
                            processError <| onError exn
                    | ChainSuccess (eff, cont) ->
                        currentEff <- eff
                        currentContStack.Add
                        <| ContStackFrame (SuccessCont, cont)
                    | ChainError (eff, cont) ->
                        currentEff <- eff
                        currentContStack.Add
                        <| ContStackFrame (FailureCont, cont)

            return result
        }
        
    member private _.Reset () =
        activeWorkItemChan.Clear ()
        activeBlockingEventChan.Clear ()

    override _.Run<'R, 'E> (eff: FIO<'R, 'E>) : Fiber<'R, 'E> =
        this.Reset ()
        let fiber = Fiber<'R, 'E> ()
        activeWorkItemChan.AddAsync
        <| WorkItem.Create (eff.Upcast (), fiber.Internal, ResizeArray<ContStackFrame> (), Evaluated)
        |> ignore
        fiber
