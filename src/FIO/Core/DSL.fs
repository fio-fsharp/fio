(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

[<AutoOpen>]
module FIO.Core.DSL

open System.Threading
open System.Threading.Tasks
open System.Threading.Channels

[<AutoOpen>]
module Utils =
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
    | RescheduleForRunning
    | RescheduleForBlocking of BlockingItem
    | Evaluated

and internal WorkItem =
    { Eff: FIO<obj, obj>
      IFiber: InternalFiber
      Stack: ContStack
      PrevAction: RuntimeAction }

    static member internal Create eff ifiber stack prevAction =
        { Eff = eff
          IFiber = ifiber
          Stack = stack
          PrevAction = prevAction }

    member internal this.Complete res =
        this.IFiber.Complete res

and internal BlockingItem =
    | BlockingChannel of Channel<obj>
    | BlockingIFiber of InternalFiber

and internal InternalChannel<'R> internal () =

    let chan = Channel.CreateUnbounded<'R>()
    let mutable count = 0

    member internal this.AddAsync (msg: 'R) =
        (chan.Writer.WriteAsync msg).AsTask()
            .ContinueWith(fun _ ->
                Interlocked.Increment &count
                |> ignore
                Task.FromResult ()).Unwrap()
    
    member internal this.TakeAsync () =
        chan.Reader.ReadAsync().AsTask()
            .ContinueWith(fun (t: Task<'R>) ->
                Interlocked.Decrement &count
                |> ignore
                t.Result
            )

    member internal this.WaitToReceiveAsync () =
        chan.Reader.WaitToReadAsync().AsTask()

    member internal this.Count =
        Volatile.Read &count

and internal InternalFiber internal (chan: InternalChannel<Result<obj, obj>>, blockingWorkItemChan: InternalChannel<WorkItem>) =

    member internal this.Complete res : Task<unit> =
        if chan.Count = 0 then
            printfn $"completed with %A{res}"
            chan.AddAsync res
        else
            invalidOp "InternalFiber: Complete was called on an already completed InternalFiber!"

    member internal this.AwaitAsync () = task {
        let! res = chan.TakeAsync ()
        // Re-add the result to the queue to allow concurrent awaits
        do! chan.AddAsync res
        return res
    }
    
    member internal this.AddBlockingWorkItem workItem =
        blockingWorkItemChan.AddAsync workItem

    member internal this.RescheduleBlockingWorkItems (workItemChan: InternalChannel<WorkItem>) = task {
        while blockingWorkItemChan.Count > 0 do
            let! workItem = blockingWorkItemChan.TakeAsync ()
            do! workItemChan.AddAsync workItem
    }

    member internal this.Completed () =
        chan.Count > 0

    member internal this.BlockingWorkItemsCount () =
        blockingWorkItemChan.Count

/// A Fiber is a construct that represents a lightweight-thread. Fibers are used to interpret multiple effects in parallel and
/// can be awaited to retrieve the result of the effect.
and Fiber<'R, 'E> private (resChan: InternalChannel<Result<obj, obj>>, blockingWorkItemChan: InternalChannel<WorkItem>) =

    new() = Fiber(InternalChannel<Result<obj, obj>>(), InternalChannel<WorkItem>())

    /// Creates an effect that waits for the fiber and succeeds with its result.
    member this.Await () : FIO<'R, 'E> =
        AwaitFiber <| this.ToInternal ()

    /// Waits for the fiber and succeeds with its result.
    member this.AwaitAsync () = task {
        let! res = resChan.TakeAsync ()
        // Re-add the result to the queue to allow concurrent awaits
        do! resChan.AddAsync res
        match res with
        | Ok res -> return Ok (res :?> 'R)
        | Error err -> return Error (err :?> 'E)
    }

    member internal this.ToInternal () =
        InternalFiber (resChan, blockingWorkItemChan)

/// A channel represents a communication queue that holds data of the type 'R. Data can be both be sent and
/// received (blocking) on a channel.
and Channel<'R> private (resChan: InternalChannel<obj>, blockingWorkItemChan: InternalChannel<WorkItem>, dataCounter: int64 ref) =

    new() = Channel(InternalChannel<obj>(), InternalChannel<WorkItem>(), ref 0)

    /// Send puts the message on the channel and succeeds with the message.
    member this.Send msg : FIO<'R, 'E> =
        SendChan (msg, this)

    /// Receive retrieves a message from the channel and succeeds with it.
    member this.Receive () : FIO<'R, 'E> =
        ReceiveChan this

    member this.Count () =
        resChan.Count

    member internal this.SendAsync (msg: 'R) =
        Interlocked.Increment dataCounter
        |> ignore
        resChan.AddAsync msg

    member internal this.ReceiveAsync () = task {
        let! res = resChan.TakeAsync()
        return res :?> 'R
    }

    member internal this.AddBlockingWorkItem workItem =
        blockingWorkItemChan.AddAsync workItem

    // TODO: Should this not be a while loop and not just if?
    // It has previously been and if. Changed to while for testing.
    member internal this.RescheduleBlockingWorkItems (workItemChan: InternalChannel<WorkItem>) = task {
        while blockingWorkItemChan.Count > 0 do
            let! workItem = blockingWorkItemChan.TakeAsync()
            do! workItemChan.AddAsync workItem
    }

    member internal this.HasBlockingWorkItems () =
        blockingWorkItemChan.Count > 0
        
    member internal this.UseAvailableData () =
        Interlocked.Decrement dataCounter |> ignore

    member internal this.DataAvailable () =
        let mutable temp = dataCounter.Value
        Interlocked.Read &temp > 0

    member internal this.Upcast () =
        Channel<obj>(resChan, blockingWorkItemChan, dataCounter)

and channel<'R> = Channel<'R>

/// Builds a functional effect that can either succeed
/// with a result or fail with an error when interpreted.
and FIO<'R, 'E> =
    internal
    | Success of res: 'R
    | Failure of err: 'E
    | Action of func: (unit -> 'R) * onError: (exn -> 'E)
    | SendChan of msg: 'R * chan: Channel<'R>
    | ReceiveChan of chan: Channel<'R>
    | ConcurrentEffect of eff: FIO<obj, obj> * fiber: obj * ifiber: InternalFiber
    | AwaitFiber of ifiber: InternalFiber
    | AwaitGenericTPLTask of task: Task<obj> * onError: (exn -> obj)
    | FiberFromTask of task: Task<obj> * onError: (exn -> obj) * fiber: obj * ifiber: InternalFiber
    | ChainSuccess of eff: FIO<obj, 'E> * cont: (obj -> FIO<'R, 'E>)
    | ChainError of eff: FIO<obj, obj> * cont: (obj -> FIO<'R, 'E>)

    /// Succeeds immediately with the result.
    static member Succeed res : FIO<'R, 'E> =
        Success res

    /// Fails immediately with the error.
    static member Fail err : FIO<'R, 'E> =
        Failure err

    /// Converts a function into an effect.
    static member FromFunc (func: unit -> 'R, onError: exn -> 'E) : FIO<'R, 'E> =
        Action (func, onError)

    /// Converts a function into an effect with a default onError.
    static member inline FromFunc (func: unit -> 'R) : FIO<'R, exn> =
        FIO<'R, exn>.FromFunc (func, id)

    /// Converts a Result into an effect.
    static member inline FromResult (res: Result<'R, 'E>) : FIO<'R, 'E> =
        match res with
        | Ok res -> FIO.Succeed res
        | Error err -> FIO.Fail err
    
    /// Converts an Option into an effect.
    static member inline FromOption (opt: Option<'R>) (err: 'E) : FIO<'R, 'E> =
        match opt with
        | Some res -> FIO.Succeed res
        | None -> FIO.Fail err

    /// Converts a Choice into an effect.
    static member inline FromChoice (choice: Choice<'R, 'E>) : FIO<'R, 'E> =
        match choice with
        | Choice1Of2 res -> FIO.Succeed res
        | Choice2Of2 err -> FIO.Fail err

    /// Converts a Task into an effect.
    static member AwaitTask (task: Task, onError: exn -> 'E) : FIO<unit, 'E> =
        AwaitGenericTPLTask (task.ContinueWith(fun (_: Task) -> box ()), upcastOnError onError)

    /// Converts a Task into an effect with a default onError.
    static member inline AwaitTask (task: Task) : FIO<unit, exn> =
        FIO<unit, exn>.AwaitTask (task, id)

    /// Converts a generic Task into an effect.
    static member AwaitGenericTask (task: Task<'R>, onError: exn -> 'E) : FIO<'R, 'E> =
        let task =
            task.ContinueWith(fun (t: Task<'R>) ->
                if t.IsFaulted then
                    Task.FromException<obj> (t.Exception.GetBaseException())
                elif t.IsCanceled then
                    Task.FromCanceled<obj> CancellationToken.None
                else 
                    Task.FromResult (box t.Result)
            ).Unwrap()
        AwaitGenericTPLTask (task, upcastOnError onError)

    /// Converts a generic Task into an effect with a default onError.
    static member inline AwaitGenericTask (task: Task<'R>) : FIO<'R, exn> =
        FIO<'R, exn>.AwaitGenericTask (task, id)

    /// Converts an Async computation into an effect.
    static member inline AwaitAsync (async: Async<'R>, onError: exn -> 'E) : FIO<'R, 'E>  =
        FIO<'R, 'E>.AwaitGenericTask (Async.StartAsTask async, onError)

    /// Converts an Async computation into an effect with a default onError.
    static member inline AwaitAsync (async: Async<'R>) : FIO<'R, exn> =
        FIO<'R, exn>.AwaitAsync (async, id)
        
    static member ToFiber(task: Task<'R>, onError: exn -> 'E) : FIO<Fiber<'R, exn>, 'E> =
        let fiber = Fiber<'R, exn>()
        let task =
            task.ContinueWith(fun (t: Task<'R>) ->
                if t.IsFaulted then
                    Task.FromException<obj> (t.Exception.GetBaseException())
                elif t.IsCanceled then
                    Task.FromCanceled<obj> CancellationToken.None
                else 
                    Task.FromResult (box t.Result)
            ).Unwrap()
        FiberFromTask (task, upcastOnError onError, fiber, fiber.ToInternal())

    static member ToFiber (task: Task<'R>) : FIO<Fiber<'R, exn>, exn> =
        FIO<'R, exn>.ToFiber (task, id)
        
    /// Interprets an effect concurrently and returns the fiber interpreting it.
    /// The fiber can be awaited for the result of the effect.
    member this.Fork () : FIO<Fiber<'R, 'E>, 'E1> =
        let fiber = new Fiber<'R, 'E>()
        ConcurrentEffect (this.Upcast(), fiber, fiber.ToInternal())

    /// Binds a continuation to the result of an effect.
    /// If the effect fails, the error is immediately returned.
    member this.Bind (cont: 'R -> FIO<'R1, 'E>) : FIO<'R1, 'E> =
        ChainSuccess (this.UpcastResult(), fun res -> cont (res :?> 'R))

    /// Binds a continuation to the error of an effect.
    /// If the effect succeeds, the result is immediately returned.
    member this.BindError (cont: 'E -> FIO<'R, 'E1>) : FIO<'R, 'E1> =
        ChainError (this.Upcast(), fun err -> cont (err :?> 'E))

    /// Maps a function over the result of an effect.
    member inline this.Map (cont: 'R -> 'R1) : FIO<'R1, 'E> =
        this.Bind <| fun res ->
            FIO.Succeed <| cont res

    // Maps a function over the error of an effect.
    member inline this.MapError (cont: 'E -> 'E1) : FIO<'R, 'E1> =
        this.BindError <| fun err ->
            FIO.Fail <| cont err

    /// Sequences two effects, ignoring the result of the first effect.
    /// If the first effect fails, the error is immediately returned.
    member inline this.Then (eff: FIO<'R1, 'E>) : FIO<'R1, 'E> =
        this.Bind <| fun _ -> eff

    /// Sequences two effects, ignoring the error of the first effect.
    /// If the first effect succeeds, the result is immediately returned.
    member inline this.ThenError (eff: FIO<'R, 'E1>) : FIO<'R, 'E1> =
        this.BindError <| fun _ -> eff

    // TODO: What is the Haskell name of this? It's an applicative.
    /// Combines two effects: one produces a result function and the other produces a result value.
    /// The function is applied to the value, and the result is returned.
    /// Errors are immediately returned if any effect fails.
    member inline this.Apply (eff: FIO<'R -> 'R1, 'E>) : FIO<'R1, 'E> =
        eff.Bind <| this.Map

    /// Combines two effects: one produces an error function and the other produces an error value.
    /// The function is applied to the value, and the error is returned.
    member inline this.ApplyError (eff: FIO<'R, 'E -> 'E1>) : FIO<'R, 'E1> =
        eff.BindError <| this.MapError

    /// Combines two effects and succeeds with a tuple of their results when both complete.
    /// Errors are immediately returned if any effect fails.
    member inline this.Zip (eff: FIO<'R1, 'E>) : FIO<'R * 'R1, 'E> =
        this.Bind <| fun res ->
            eff.Bind <| fun res' ->
                FIO.Succeed (res, res')

    /// Combines two effects and succeeds with a tuple of their errors when both complete.
    member inline this.ZipError (eff: FIO<'R, 'E1>) : FIO<'R, 'E * 'E1> =
        this.BindError <| fun err ->
            eff.BindError <| fun err' ->
                FIO.Fail (err, err')

    /// Interprets two effects concurrently and succeeds with a tuple of their results when both complete.
    /// If either effect fails, the error is immediately returned.
    member inline this.Parallel (eff: FIO<'R1, 'E>) : FIO<'R * 'R1, 'E> =
        eff.Fork().Bind <| fun fiber ->
            this.Bind <| fun res ->
                fiber.Await().Bind <| fun res' ->
                     FIO.Succeed (res, res')

    /// Interprets two effects concurrently and succeeds with a tuple of their errors when both complete.
    member inline this.ParallelError (eff: FIO<'R, 'E1>) : FIO<'R, 'E * 'E1> =
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
        | AwaitFiber ifiber ->
            AwaitFiber ifiber
        | AwaitGenericTPLTask (task, onError) ->
            AwaitGenericTPLTask (task, onError)
        | FiberFromTask (task, onError, fiber, ifiber) ->
            FiberFromTask (task, onError, fiber, ifiber)
        | ChainSuccess (eff, cont) ->
            ChainSuccess (eff, fun res -> (cont res).UpcastResult())
        | ChainError (eff, cont) ->
            ChainError (eff, fun err -> (cont err).UpcastResult())

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
        | AwaitFiber ifiber ->
            AwaitFiber ifiber
        | AwaitGenericTPLTask (task, onError) ->
            AwaitGenericTPLTask (task, onError)
        | FiberFromTask (task, onError, fiber, ifiber) ->
            FiberFromTask (task, onError, fiber, ifiber)
        | ChainSuccess (eff, cont) ->
            ChainSuccess (eff.UpcastError(), fun res -> (cont res).UpcastError())
        | ChainError (eff, cont) ->
            ChainError (eff.UpcastError(), fun err -> (cont err).UpcastError())

    member internal this.Upcast () : FIO<obj, obj> =
        this.UpcastResult().UpcastError()
