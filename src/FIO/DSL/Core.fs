(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

[<AutoOpen>]
module FIO.DSL.Core

open System
open System.Threading
open System.Threading.Tasks
open System.Threading.Channels

[<AutoOpen>]
module private Utils =
    let inline upcastOnError (onError: exn -> 'E) : (exn -> obj) =
        fun (exn: exn) -> onError exn :> obj

    let inline upcastFunc (func: unit -> 'R) : unit -> obj =
        fun () -> func () :> obj

type internal Cont =
    obj -> FIO<obj, obj>

and internal ContType =
    | SuccessCont
    | FailureCont

and internal ContStackFrame =
    ContType * Cont

and internal ContStack =
    ContStackFrame list

and internal RuntimeAction =
    | Skipped
    | Evaluated
    | RescheduleForRunning
    | RescheduleForBlocking of BlockingItem

and internal BlockingItem =
    | BlockingChannel of Channel<obj>
    | BlockingIFiber of InternalFiber
    
and internal BlockingData =
    { BlockingItem: BlockingItem
      WaitingWorkItem: WorkItem }
    
    static member internal Create (blockingItem, waitingWorkItem) =
        { BlockingItem = blockingItem
          WaitingWorkItem = waitingWorkItem }

and internal WorkItem =
    { Eff: FIO<obj, obj>
      IFiber: InternalFiber
      Stack: ContStack
      PrevAction: RuntimeAction }

    static member internal Create (eff, ifiber, stack, prevAction) =
        { Eff = eff
          IFiber = ifiber
          Stack = stack
          PrevAction = prevAction }

    member internal this.Complete res =
        this.IFiber.Complete res

    member internal this.CompleteAndReschedule res activeWorkItemChan =
        this.IFiber.CompleteAndReschedule res activeWorkItemChan

and internal InternalChannel<'R> (id: Guid) =
    let chan = Channel.CreateUnbounded<'R>()
    let mutable count = 0L
    
    new() = InternalChannel (Guid.NewGuid())

    member internal this.AddAsync (msg: 'R) =
        (chan.Writer.WriteAsync msg)
            .AsTask()
            .ContinueWith(fun _ ->
                Interlocked.Increment &count
                |> ignore
                Task.FromResult ())
            .Unwrap()
    
    member internal this.TakeAsync () =
        chan.Reader.ReadAsync()
            .AsTask()
            .ContinueWith(fun (task: Task<'R>) ->
                Interlocked.Decrement &count
                |> ignore
                task.Result)

    member internal this.WaitToTakeAsync () =
        chan.Reader.WaitToReadAsync().AsTask()

    member internal this.Count =
        Volatile.Read &count

    member internal this.Id =
        id

and internal InternalFiber (id: Guid, resChan: InternalChannel<Result<obj, obj>>, blockingWorkItemChan: InternalChannel<WorkItem>) =
    let mutable completed = false

    let completeAlreadyCalledFail () =
        // TODO: This will not throw an exception. How can we make sure it does?
        printfn "WARNING: InternalFiber: Complete was called on an already completed InternalFiber! Exception will be raised."
        invalidOp $"InternalFiber: Complete was called on an already
            completed InternalFiber! Result channel count: %i{resChan.Count}"

    member internal this.Complete res =
        task {
            if Interlocked.Exchange (&completed, true) = false then
                do! resChan.AddAsync res
            else
                completeAlreadyCalledFail ()
        }
    
    member internal this.CompleteAndReschedule res activeWorkItemChan =
        task {
            if Interlocked.Exchange (&completed, true) = false then
                do! resChan.AddAsync res
                do! this.RescheduleBlockingWorkItems activeWorkItemChan
            else
                completeAlreadyCalledFail ()
        }

    member internal this.AwaitAsync () =
        task {
            let! res = resChan.TakeAsync ()
            // Re-add the result to the channel to allow concurrent awaits
            // (ensures the channel is never depleted)
            do! resChan.AddAsync res
            return res
        }
    
    member internal this.AddBlockingWorkItem (blockingWorkItem: WorkItem) =
        task {
            if this.Completed then
                printfn "WARNING: InternalFiber: Adding a blocking item on a fiber that is already completed!"
                // Current issue: So, 'this' fiber is returning null, but the blocking fiber is expecting a int64.
                // This has something to do with the timer fiber. Why is that being weird?
            do! blockingWorkItemChan.AddAsync blockingWorkItem
        }
    
    member internal this.Count =
        resChan.Count
        
    member internal this.BlockingWorkItemCount =
        blockingWorkItemChan.Count

    member internal this.Completed =
        Volatile.Read &completed

    member internal this.Id =
        id

    member internal this.RescheduleBlockingWorkItems (activeWorkItemChan: InternalChannel<WorkItem>) =
        task {
            // TODO: This has been commented out as it was spamming the test suite.
            // TODO: Figure out why this was spamming the test suite. I don't see scenarios where a fiber should not be completed.
            if not this.Completed then
                printfn "WARNING: InternalFiber: Rescheduling blocking work items on a fiber that has not yet been completed!"
            while blockingWorkItemChan.Count > 0 do
                let! unblockedWorkItem = blockingWorkItemChan.TakeAsync ()
                do! activeWorkItemChan.AddAsync unblockedWorkItem
        }

/// A Fiber is a construct that represents a lightweight-thread.
/// Fibers are used to interpret multiple effects in parallel and
/// can be awaited to retrieve the result of the effect.
and Fiber<'R, 'E> private (id: Guid, resChan: InternalChannel<Result<obj, obj>>, blockingWorkItemChan: InternalChannel<WorkItem>) =
    let ifiber = InternalFiber (id, resChan, blockingWorkItemChan)
    
    new() = Fiber(Guid.NewGuid(), InternalChannel<Result<obj, obj>>(), InternalChannel<WorkItem>())

    /// Creates an effect that waits for the fiber and succeeds with its result.
    member this.Await<'R, 'E> () : FIO<'R, 'E> =
        AwaitFiber ifiber

    /// Waits for the fiber and succeeds with its result.
    member this.AwaitAsync<'R, 'E> () =
        task {
            match! ifiber.AwaitAsync() with
            | Ok res -> return Ok (res :?> 'R)
            | Error err -> return Error (err :?> 'E)
        }

    member this.Id =
        id

     member internal this.Internal =
        ifiber
 
/// A channel represents a communication queue that holds
/// data of the type 'R. Data can be both be sent and
/// received (blocking) on a channel.
and Channel<'R> private (id: Guid, resChan: InternalChannel<obj>, blockingWorkItemChan: InternalChannel<WorkItem>) =

    new() = Channel(Guid.NewGuid(), InternalChannel<obj>(), InternalChannel<WorkItem>())

    /// Send puts the message on the channel and succeeds with the message.
    member this.Send<'R, 'E> (msg: 'R) : FIO<'R, 'E> =
        SendChan (msg, this)

    /// Receive retrieves a message from the channel and succeeds with it.
    member this.Receive<'R, 'E> () : FIO<'R, 'E> =
        ReceiveChan this

    member this.Count =
        resChan.Count
        
    member this.Id =
        id

    member internal this.SendAsync (msg: 'R) =
        resChan.AddAsync msg

    member internal this.ReceiveAsync () =
        task {
            let! res = resChan.TakeAsync()
            return res :?> 'R
        }

    member internal this.AddBlockingWorkItem blockingItem =
        blockingWorkItemChan.AddAsync blockingItem

    member internal this.RescheduleNextBlockingWorkItem (activeWorkItemChan: InternalChannel<WorkItem>) =
        task {
            if blockingWorkItemChan.Count > 0 then
                let! unblockedWorkItem = blockingWorkItemChan.TakeAsync()
                do! activeWorkItemChan.AddAsync unblockedWorkItem
        }

    member internal this.BlockingWorkItemCount =
        blockingWorkItemChan.Count

    member internal this.Upcast () =
        Channel<obj> (id, resChan, blockingWorkItemChan)

and channel<'R> = Channel<'R>

/// Builds a functional effect that can either succeed
/// with a result or fail with an error when interpreted
/// by a runtime.
and FIO<'R, 'E> =
    internal
    | Success of res: 'R
    | Failure of err: 'E
    | Action of func: (unit -> 'R) * onError: (exn -> 'E)
    | SendChan of msg: 'R * chan: Channel<'R>
    | ReceiveChan of chan: Channel<'R>
    | ConcurrentEffect of eff: FIO<obj, obj> * fiber: obj * ifiber: InternalFiber
    | ConcurrentTPLTask of lazyTask: (unit -> Task<obj>) * onError: (exn -> 'E) * fiber: obj * ifiber: InternalFiber
    | AwaitFiber of ifiber: InternalFiber
    | AwaitGenericTPLTask of task: Task<obj> * onError: (exn -> 'E)
    | ChainSuccess of eff: FIO<obj, 'E> * cont: (obj -> FIO<'R, 'E>)
    | ChainError of eff: FIO<'R, obj> * cont: (obj -> FIO<'R, 'E>)

    /// Succeeds immediately with the result.
    static member Succeed<'R, 'E> (res: 'R) : FIO<'R, 'E> =
        Success res

    /// Fails immediately with the error.
    static member Fail<'R, 'E> (err: 'E) : FIO<'R, 'E> =
        Failure err

    /// Converts a function into an effect.
    static member FromFunc<'R, 'E> (func: unit -> 'R, onError: exn -> 'E) : FIO<'R, 'E> =
        Action (func, onError)

    /// Converts a function into an effect with a default onError.
    static member inline FromFunc<'R, 'E> (func: unit -> 'R) : FIO<'R, exn> =
        FIO.FromFunc<'R, exn> (func, id)

    /// Converts a Result into an effect.
    static member inline FromResult<'R, 'E> (res: Result<'R, 'E>) : FIO<'R, 'E> =
        match res with
        | Ok res -> FIO.Succeed res
        | Error err -> FIO.Fail err
    
    /// Converts an Option into an effect.
    static member inline FromOption<'R, 'E> (opt: Option<'R>, onNone: unit -> 'E) : FIO<'R, 'E> =
        match opt with
        | Some res -> FIO.Succeed res
        | None -> FIO.Fail <| onNone ()

    /// Converts a Choice into an effect.
    static member inline FromChoice<'R, 'E> (choice: Choice<'R, 'E>) : FIO<'R, 'E> =
        match choice with
        | Choice1Of2 res -> FIO.Succeed res
        | Choice2Of2 err -> FIO.Fail err

    /// Awaits a Task and turns it into an effect.
    static member AwaitTask<'R, 'E> (task: Task, onError: exn -> 'E) : FIO<unit, 'E> =
        AwaitGenericTPLTask (task.ContinueWith(fun _ -> box ()), onError)

    /// Awaits a Task and turns it into an effect with a default onError.
    static member inline AwaitTask<'R, 'E> (task: Task) : FIO<unit, exn> =
        FIO.AwaitTask<unit, exn> (task, id)

    /// Awaits a generic Task and turns it into an effect.
    static member AwaitGenericTask<'R, 'E> (task: Task<'R>, onError: exn -> 'E) : FIO<'R, 'E> =
        let task = task.ContinueWith(fun (outerTask: Task<'R>) ->
                    if outerTask.IsFaulted then
                        Task.FromException<obj> (outerTask.Exception.GetBaseException())
                    elif outerTask.IsCanceled then
                        Task.FromCanceled<obj> CancellationToken.None
                    else 
                        Task.FromResult (box outerTask.Result)
                   ).Unwrap()
        AwaitGenericTPLTask (task, onError)

    /// Awaits a generic Task and turns it into an effect with a default onError.
    static member inline AwaitGenericTask<'R, 'E> (task: Task<'R>) : FIO<'R, exn> =
        FIO.AwaitGenericTask<'R, exn> (task, id)

    /// Awaits an Async computation and turns it into an effect.
    static member inline AwaitAsync<'R, 'E> (async: Async<'R>, onError: exn -> 'E) : FIO<'R, 'E>  =
        FIO.AwaitGenericTask<'R, 'E> (Async.StartAsTask async, onError)

    /// Awaits an Async computation and turns it into an effect with a default onError.
    static member inline AwaitAsync<'R, 'E> (async: Async<'R>) : FIO<'R, exn> =
        FIO.AwaitAsync<'R, exn> (async, id)

    // Converts a Task into a Fiber.
    static member FromTask<'R, 'E> (lazyTask: unit -> Task, onError: exn -> 'E) : FIO<Fiber<unit, 'E>, 'E> =
        let fiber = Fiber<unit, 'E>()
        ConcurrentTPLTask ((fun () -> (lazyTask ()).ContinueWith(fun _ -> box ())),
            onError, fiber, fiber.Internal)

    // Converts a Task into a Fiber with a default onError.
    static member inline FromTask<'R, 'E> (lazyTask: unit -> Task) : FIO<Fiber<unit, exn>, exn> =
        FIO.FromTask<unit, exn> (lazyTask, id)
    
    // Converts a generic Task into a Fiber.
    static member FromGenericTask<'R, 'E> (lazyTask: unit -> Task<'R>, onError: exn -> 'E) : FIO<Fiber<'R, 'E>, 'E> =
        let fiber = Fiber<'R, 'E>()
        let task = fun () ->
                (lazyTask ()).ContinueWith(fun (outerTask: Task<'R>) ->
                    if outerTask.IsFaulted then
                        Task.FromException<obj> (outerTask.Exception.GetBaseException())
                    elif outerTask.IsCanceled then
                        Task.FromCanceled<obj> CancellationToken.None
                    else 
                        Task.FromResult (box outerTask.Result)
                   ).Unwrap()
        ConcurrentTPLTask (task, onError, fiber, fiber.Internal)

    // Converts a generic Task into a Fiber with a default onError.       
    static member inline FromGenericTask<'R, 'E> (lazyTask: unit -> Task<'R>) : FIO<Fiber<'R, exn>, exn> =
        FIO.FromGenericTask<Fiber<'R, exn>, exn> (lazyTask, id)
        
    /// Interprets an effect concurrently and returns the fiber interpreting it.
    /// The fiber can be awaited for the result of the effect.
    member this.Fork<'R, 'E, 'E1> () : FIO<Fiber<'R, 'E>, 'E1> =
        let fiber = new Fiber<'R, 'E>()
        ConcurrentEffect (this.Upcast(), fiber, fiber.Internal)

    /// Binds a continuation to the result of an effect.
    /// If the effect fails, the error is immediately returned.
    member this.Bind<'R, 'R1, 'E> (cont: 'R -> FIO<'R1, 'E>) : FIO<'R1, 'E> =
        ChainSuccess (this.UpcastResult(), fun res -> cont (res :?> 'R))

    /// Binds a continuation to the error of an effect.
    /// If the effect succeeds, the result is immediately returned.       
    member this.BindError<'R, 'E, 'E1> (cont: 'E -> FIO<'R, 'E1>) : FIO<'R, 'E1> =
        ChainError (this.UpcastError(), fun err -> cont (err :?> 'E))

    /// Maps a function over the result of an effect.
    member inline this.Map<'R, 'R1, 'E> (cont: 'R -> 'R1) : FIO<'R1, 'E> =
        this.Bind <| fun res ->
            FIO.Succeed <| cont res

    // Maps a function over the error of an effect.
    member inline this.MapError<'R, 'E, 'E1> (cont: 'E -> 'E1) : FIO<'R, 'E1> =
        this.BindError <| fun err ->
            FIO.Fail <| cont err

    /// Sequences two effects, ignoring the result of the first effect.
    /// If the first effect fails, the error is immediately returned.
    member inline this.Then<'R, 'R1, 'E> (eff: FIO<'R1, 'E>) : FIO<'R1, 'E> =
        this.Bind <| fun _ -> eff

    /// Sequences two effects, ignoring the error of the first effect.
    /// If the first effect succeeds, the result is immediately returned.
    member inline this.ThenError<'R, 'E, 'E1> (eff: FIO<'R, 'E1>) : FIO<'R, 'E1> =
        this.BindError <| fun _ -> eff

    /// Combines two effects: one produces a result function and the other produces a result value.
    /// The function is applied to the value, and the result is returned.
    /// Errors are immediately returned if any effect fails.
    member inline this.Apply<'R, 'R1, 'E> (eff: FIO<'R -> 'R1, 'E>) : FIO<'R1, 'E> =
        eff.Bind <| this.Map

    /// Combines two effects: one produces an error function and the other produces an error value.
    /// The function is applied to the value, and the error is returned.
    member inline this.ApplyError<'R, 'E, 'E1> (eff: FIO<'R, 'E -> 'E1>) : FIO<'R, 'E1> =
        eff.BindError <| this.MapError

    /// Combines two effects and succeeds with a tuple of their results when both complete.
    /// Errors are immediately returned if any effect fails.
    member inline this.Zip<'R, 'R1, 'E> (eff: FIO<'R1, 'E>) : FIO<'R * 'R1, 'E> =
        this.Bind <| fun res ->
            eff.Bind <| fun res' ->
                FIO.Succeed (res, res')

    /// Combines two effects and succeeds with a tuple of their errors when both complete.
    member inline this.ZipError<'R, 'E, 'E1> (eff: FIO<'R, 'E1>) : FIO<'R, 'E * 'E1> =
        this.BindError <| fun err ->
            eff.BindError <| fun err' ->
                FIO.Fail (err, err')

    /// Interprets two effects concurrently and succeeds with a tuple of their results when both complete.
    /// If either effect fails, the error is immediately returned.
    member inline this.Parallel<'R, 'R1, 'E> (eff: FIO<'R1, 'E>) : FIO<'R * 'R1, 'E> =
        eff.Fork().Bind <| fun fiber ->
            this.Bind <| fun res ->
                fiber.Await().Bind <| fun res' ->
                     FIO.Succeed (res, res')

    /// Interprets two effects concurrently and succeeds with a tuple of their errors when both complete.
    member inline this.ParallelError<'R, 'E, 'E1> (eff: FIO<'R, 'E1>) : FIO<'R, 'E * 'E1> =
        eff.Fork().Bind <| fun fiber ->
            this.BindError <| fun err ->
                fiber.Await().BindError <| fun err' ->
                    FIO.Fail (err, err')

    member internal this.UpcastResult () : FIO<obj, 'E> =
        match this with
        | Success res ->
            Success (res :> obj)
        | Failure err ->
            Failure err
        | Action (func, onError) ->
            Action (upcastFunc func, onError)
        | SendChan (msg, chan) ->
            SendChan (msg :> obj, chan.Upcast())
        | ReceiveChan chan ->
            ReceiveChan <| chan.Upcast()
        | ConcurrentEffect (eff, fiber, ifiber) ->
            ConcurrentEffect (eff, fiber, ifiber)
        | ConcurrentTPLTask (lazyTask, onError, fiber, ifiber) ->
            ConcurrentTPLTask (lazyTask, onError, fiber, ifiber)
        | AwaitFiber ifiber ->
            AwaitFiber ifiber
        | AwaitGenericTPLTask (task, onError) ->
            AwaitGenericTPLTask (task, onError)
        | ChainSuccess (eff, cont) ->
            ChainSuccess (eff, fun res -> (cont res).UpcastResult())
        | ChainError (eff, cont) ->
            ChainError (eff.UpcastResult(), fun err -> (cont err).UpcastResult())

    member internal this.UpcastError () : FIO<'R, obj> =
        match this with
        | Success res ->
            Success res
        | Failure err ->
            Failure (err :> obj)
        | Action (func, onError) ->
            Action (func, upcastOnError onError)
        | SendChan (msg, chan) ->
            SendChan (msg, chan)
        | ReceiveChan chan ->
            ReceiveChan chan
        | ConcurrentEffect (eff, fiber, ifiber) ->
            ConcurrentEffect (eff, fiber, ifiber)
        | ConcurrentTPLTask (lazyTask, onError, fiber, ifiber) ->
            ConcurrentTPLTask (lazyTask, upcastOnError onError, fiber, ifiber)
        | AwaitFiber ifiber ->
            AwaitFiber ifiber
        | AwaitGenericTPLTask (task, onError) ->
            AwaitGenericTPLTask (task, upcastOnError onError)
        | ChainSuccess (eff, cont) ->
            ChainSuccess (eff.UpcastError(), fun res -> (cont res).UpcastError())
        | ChainError (eff, cont) ->
            ChainError (eff, fun err -> (cont err).UpcastError())

    member internal this.Upcast () : FIO<obj, obj> =
        this.UpcastResult().UpcastError()
