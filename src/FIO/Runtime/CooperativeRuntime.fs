(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module FIO.Runtime.Cooperative

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
      ActiveBlockingDataChan: InternalChannel<BlockingData> }

and private EvaluationWorker (config: EvaluationWorkerConfig) =

    let processWorkItem (workItem: WorkItem) =
        task {
            match! config.Runtime.InterpretAsync workItem config.EWSteps with
            | Success res, _, Evaluated ->
                do! workItem.Complete <| Ok res
            | Failure err, _, Evaluated ->
                do! workItem.Complete <| Error err
            | eff, stack, RescheduleForRunning ->
                do! config.ActiveWorkItemChan.AddAsync
                    <| WorkItem.Create (eff, workItem.IFiber, stack, RescheduleForRunning)
            | eff, stack, RescheduleForBlocking blockingItem ->
                do! config.BlockingWorker.RescheduleForBlocking
                    <| BlockingData.Create (blockingItem,
                        WorkItem.Create (eff, workItem.IFiber, stack, RescheduleForBlocking blockingItem))
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
    
    let processBlockingChannel blockingData (blockingChan: Channel<obj>) =
        if blockingChan.Count > 0 then
            config.ActiveWorkItemChan.AddAsync blockingData.WaitingWorkItem
        else
            config.ActiveBlockingDataChan.AddAsync blockingData
            
    let processBlockingIFiber blockingData (ifiber: InternalFiber) =
        if ifiber.Completed then
            config.ActiveWorkItemChan.AddAsync blockingData.WaitingWorkItem
        else
            config.ActiveBlockingDataChan.AddAsync blockingData

    let rec processBlockingData blockingData =
        task {
            match blockingData.BlockingItem with
            | BlockingChannel blockingChan ->
                do! processBlockingChannel blockingData blockingChan
            | BlockingIFiber blockingIFiber ->
                do! processBlockingIFiber blockingData blockingIFiber
        }
    
    let startWorker () =
        (new Task((fun () ->
            task {
                let mutable loop = true
                while loop do
                    let! hasBlockingItem = config.ActiveBlockingDataChan.WaitToTakeAsync()
                    if not hasBlockingItem then
                        loop <- false 
                    else
                        let! blockingData = config.ActiveBlockingDataChan.TakeAsync()
                        do! processBlockingData blockingData
            } |> ignore), TaskCreationOptions.LongRunning))
            .Start TaskScheduler.Default

    let cancellationTokenSource = new CancellationTokenSource()
    do startWorker ()

    interface IDisposable with
        member _.Dispose () =
            cancellationTokenSource.Cancel()

    member internal _.RescheduleForBlocking blockingData =
        config.ActiveBlockingDataChan.AddAsync blockingData

and Runtime (config: WorkerConfig) as this =
    inherit FWorkerRuntime(config)
    
    let activeWorkItemChan = InternalChannel<WorkItem>()
    let activeBlockingDataChan = InternalChannel<BlockingData>()

    let createBlockingWorkers count =
        List.init count <| fun _ ->
            new BlockingWorker({
                ActiveWorkItemChan = activeWorkItemChan
                ActiveBlockingDataChan = activeBlockingDataChan
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
        // Currently we take head of the list, as the IntermediateRuntime
        // only supports a single blocking worker.
        createEvaluationWorkers this (List.head blockingWorkers) config.EWSteps config.EWCount
        |> ignore

    override _.Name =
        "Cooperative"

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
        let mutable currentPrevAction = workItem.PrevAction
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
                    resultOpt <- Some (Failure err, ContStack.Empty, Evaluated)
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
                        handleSuccess msg
                    | ReceiveChan chan ->
                        if chan.Count > 0 then
                            let! res = chan.ReceiveAsync()
                            handleSuccess res
                        else
                            let newPrevAction = RescheduleForBlocking <| BlockingChannel chan
                            currentPrevAction <- newPrevAction
                            resultOpt <- Some (ReceiveChan chan, currentContStack, newPrevAction)
                    | ConcurrentEffect (eff, fiber, ifiber) ->
                        do! activeWorkItemChan.AddAsync
                            <| WorkItem.Create (eff, ifiber, ContStack.Empty, currentPrevAction)
                        handleSuccess fiber
                    | ConcurrentTPLTask (task, onError, fiber, ifiber) ->
                        do! Task.Run(fun () -> 
                            (task ()).ContinueWith((fun (t: Task<obj>) ->
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
                                TaskScheduler.Default) :> Task)
                        handleSuccess fiber
                    | AwaitFiber ifiber ->
                        if ifiber.Completed then
                            let! res = ifiber.AwaitAsync()
                            handleResult res
                        else
                            let newPrevAction = RescheduleForBlocking <| BlockingIFiber ifiber
                            currentPrevAction <- newPrevAction
                            resultOpt <- Some (AwaitFiber ifiber, currentContStack, newPrevAction)
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
        activeBlockingDataChan.Clear ()

    override _.Run<'R, 'E> (eff: FIO<'R, 'E>) : Fiber<'R, 'E> =
        this.Reset ()
        let fiber = Fiber<'R, 'E>()
        activeWorkItemChan.AddAsync
        <| WorkItem.Create (eff.Upcast(), fiber.Internal, ContStack.Empty, Evaluated)
        |> ignore
        fiber
