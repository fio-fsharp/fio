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

    member internal this.SendAsync (msg: 'R) =
        (chan.Writer.WriteAsync msg).AsTask()
            .ContinueWith(fun _ ->
                Interlocked.Increment &count
                |> ignore
                Task.FromResult ()).Unwrap()
    
    member internal this.ReceiveAsync () =
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

    member internal this.Complete res =
        if chan.Count = 0 then
            chan.SendAsync res
        else
            invalidOp "InternalFiber: Complete was called on an already completed InternalFiber!"

    member internal this.AwaitAsync () = task {
        let! res = chan.ReceiveAsync ()
        // Re-add the result to the queue to allow concurrent awaits
        do! chan.SendAsync res
        return res
    }

    member internal this.Completed () =
        chan.Count > 0

    member internal this.AddBlockingWorkItem workItem =
        blockingWorkItemChan.SendAsync workItem

    member internal this.RescheduleBlockingWorkItems (workItemChan: InternalChannel<WorkItem>) = task {
        while blockingWorkItemChan.Count > 0 do
            let! item = blockingWorkItemChan.ReceiveAsync()
            do! workItemChan.SendAsync item
    }

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
        let! res = resChan.ReceiveAsync ()
        // Re-add the result to the queue to allow concurrent awaits
        do! resChan.SendAsync res
        match res with
        | Ok res -> return Ok (res :?> 'R)
        | Error err -> return Error (err :?> 'E)
    }

    member internal this.ToInternal () =
        InternalFiber(resChan, blockingWorkItemChan)

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
        resChan.SendAsync msg

    member internal this.ReceiveAsync () = task {
        let! res = resChan.ReceiveAsync()
        return res :?> 'R
    }

    member internal this.SendBlockingWorkItem workItem =
        blockingWorkItemChan.SendAsync workItem

    member internal this.RescheduleBlockingWorkItems (workItemChan: InternalChannel<WorkItem>) = task {
        if blockingWorkItemChan.Count > 0 then
            let! item = blockingWorkItemChan.ReceiveAsync()
            do! workItemChan.SendAsync item
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
    | Concurrent of eff: FIO<obj, obj> * fiber: obj * ifiber: InternalFiber
    | AwaitFiber of ifiber: InternalFiber
    | AwaitTask of task: Task<obj> * onError: (exn -> 'E)
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
        FIO<'R, exn>.FromFunc (func, fun exn -> exn)

    /// Converts an Option into an effect.
    static member inline FromOption (opt: Option<'R>) (err: 'E) : FIO<'R, 'E> =
        match opt with
        | Some res -> FIO.Succeed res
        | None -> FIO.Fail err

    /// Converts a Result into an effect.
    static member inline FromResult (res: Result<'R, 'E>) : FIO<'R, 'E> =
        match res with
        | Ok res -> FIO.Succeed res
        | Error err -> FIO.Fail err

    /// Converts a Choice into an effect.
    static member inline FromChoice (choice: Choice<'R, 'E>) : FIO<'R, 'E> =
        match choice with
        | Choice1Of2 res -> FIO.Succeed res
        | Choice2Of2 err -> FIO.Fail err

    /// Converts a generic Task into an effect.
    static member FromGenericTask (task: Task<'R>, onError: exn -> 'E) : FIO<'R, 'E> =
        AwaitTask (task.ContinueWith(fun (t: Task<'R>) -> 
            if t.IsFaulted then
                Task.FromException<obj> (t.Exception.GetBaseException())
            elif t.IsCanceled then
                Task.FromCanceled<obj> CancellationToken.None
            else 
                Task.FromResult (box t.Result)
        ).Unwrap(), onError)

    /// Converts a generic Task into an effect with a default onError.
    static member inline FromGenericTask (task: Task<'R>) : FIO<'R, exn> =
        FIO<'R, exn>.FromGenericTask(task, fun exn -> exn)

    /// Converts a Task into an effect.
    static member FromTask (task: Task, onError: exn -> 'E) : FIO<unit, 'E> =
        AwaitTask (task.ContinueWith(fun (_: Task) -> box ()), onError)

    /// Converts a Task into an effect with a default onError.
    static member inline FromTask (task: Task) : FIO<unit, exn> =
        FIO<unit, exn>.FromTask (task, fun exn -> exn)

    /// Converts an Async computation into an effect.
    static member inline FromAsync (async: Async<'R>, onError: exn -> 'E) : FIO<'R, 'E> =
        FIO<'R, 'E>.FromGenericTask (Async.StartAsTask async, onError)

    /// Converts an Async computation into an effect with a default onError.
    static member inline FromAsync (async: Async<'R>) : FIO<'R, exn> =
        FIO<'R, exn>.FromAsync (async, fun exn -> exn)

    /// Binds a continuation to the result of an effect.
    /// If the effect fails, the error is immediately returned.
    member this.FlatMap (cont: 'R -> FIO<'R1, 'E>) : FIO<'R1, 'E> =
        ChainSuccess (this.UpcastResult(), fun res -> cont (res :?> 'R))

    /// Binds a continuation to the error of an effect.
    /// If the effect succeeds, the result is immediately returned.
    member this.FlatMapError (cont: 'E -> FIO<'R, 'E1>) : FIO<'R, 'E1> =
        ChainError (this.Upcast(), fun err -> cont (err :?> 'E))

    /// Maps a function over the result of an effect.
    member inline this.Map (cont: 'R -> 'R1) : FIO<'R1, 'E> =
        this.FlatMap <| fun res ->
            FIO.Succeed <| cont res

    // Maps a function over the error of an effect.
    member inline this.MapError (cont: 'E -> 'E1) : FIO<'R, 'E1> =
        this.FlatMapError <| fun err ->
            FIO.Fail <| cont err

    /// Sequences two effects, ignoring the result of the first effect.
    /// If the first effect fails, the error is immediately returned.
    member inline this.Then (eff: FIO<'R1, 'E>) : FIO<'R1, 'E> =
        this.FlatMap <| fun _ -> eff

    /// Sequences two effects, ignoring the error of the first effect.
    /// If the first effect succeeds, the result is immediately returned.
    member inline this.ThenError (eff: FIO<'R, 'E1>) : FIO<'R, 'E1> =
        this.FlatMapError <| fun _ -> eff

    /// Combines two effects: one produces a result function and the other produces a result value.
    /// The function is applied to the value, and the result is returned.
    /// Errors are immediately returned if any effect fails.
    member inline this.Apply (eff: FIO<'R -> 'R1, 'E>) : FIO<'R1, 'E> =
        eff.FlatMap <| fun func ->
            this.Map func

    /// Combines two effects: one produces an error function and the other produces an error value.
    /// The function is applied to the value, and the error is returned.
    member inline this.ApplyError (eff: FIO<'R, 'E -> 'E1>) : FIO<'R, 'E1> =
        eff.FlatMapError <| fun func ->
            this.MapError func

    /// Combines two effects and succeeds with a tuple of their results when both complete.
    /// Errors are immediately returned if any effect fails.
    member inline this.Zip (eff: FIO<'R1, 'E>) : FIO<'R * 'R1, 'E> =
        this.FlatMap <| fun res ->
            eff.FlatMap <| fun res' ->
                FIO.Succeed (res, res')

    /// Combines two effects and succeeds with a tuple of their errors when both complete.
    member inline this.ZipError (eff: FIO<'R, 'E1>) : FIO<'R, 'E * 'E1> =
        this.FlatMapError <| fun err ->
            eff.FlatMapError <| fun err' ->
                FIO.Fail (err, err')

    /// Interprets an effect concurrently and returns the fiber that is interpreting it.
    /// The fiber can be awaited for the result of the effect.
    member this.Fork () : FIO<Fiber<'R, 'E>, 'E1> =
        let fiber = new Fiber<'R, 'E>()
        Concurrent (this.Upcast(), fiber, fiber.ToInternal())

    /// Interprets two effects concurrently and succeeds with a tuple of their results when both complete.
    /// If either effect fails, the error is immediately returned.
    member inline this.Parallel (eff: FIO<'R1, 'E>) : FIO<'R * 'R1, 'E> =
        eff.Fork().FlatMap <| fun fiber ->
            this.FlatMap <| fun res ->
                fiber.Await().FlatMap <| fun res' ->
                     FIO.Succeed (res, res')

    /// Interprets two effects concurrently and succeeds with a tuple of their errors when both complete.
    member inline this.ParallelError (eff: FIO<'R, 'E1>) : FIO<'R, 'E * 'E1> =
        eff.Fork().FlatMap <| fun fiber ->
            this.FlatMapError <| fun err ->
                fiber.Await().FlatMapError <| fun err' ->
                    FIO.Fail (err, err')

    member internal this.UpcastResult () : FIO<obj, 'E> =
        let castFunc (f: unit -> 'R) : unit -> obj =
            fun () -> f () :> obj
        match this with
        | Success res ->
            Success (res :> obj)
        | Failure err ->
            Failure err
        | Action (func, onError) ->
            Action(castFunc func, onError)
        | SendChan (msg, chan) ->
            SendChan (msg :> obj, chan.Upcast())
        | ReceiveChan chan ->
            ReceiveChan <| chan.Upcast()
        | Concurrent (eff, fiber, ifiber) ->
            Concurrent (eff, fiber, ifiber)
        | AwaitFiber ifiber ->
            AwaitFiber ifiber
        | AwaitTask (task, onError) ->
            AwaitTask (task, onError)
        | ChainSuccess (eff, cont) ->
            ChainSuccess (eff, fun res -> (cont res).UpcastResult())
        | ChainError (eff, cont) ->
            ChainError (eff, fun err -> (cont err).UpcastResult())

    member internal this.UpcastError () : FIO<'R, obj> =
        let castOnError (f: exn -> 'E) : (exn -> obj) =
            fun (exn: exn) -> f exn :> obj
        match this with
        | Success res ->
            Success res
        | Failure err ->
            Failure (err :> obj)
        | Action (func, onError) ->
            Action (func, castOnError onError)
        | SendChan (msg, chan) ->
            SendChan (msg, chan)
        | ReceiveChan chan ->
            ReceiveChan chan
        | Concurrent (eff, fiber, ifiber) ->
            Concurrent (eff, fiber, ifiber)
        | AwaitFiber ifiber ->
            AwaitFiber ifiber
        | AwaitTask (task, onError) ->
            AwaitTask (task, castOnError onError)
        | ChainSuccess (eff, cont) ->
            ChainSuccess (eff.UpcastError(), fun res -> (cont res).UpcastError())
        | ChainError (eff, cont) ->
            ChainError (eff.UpcastError(), fun err -> (cont err).UpcastError())

    member internal this.Upcast () : FIO<obj, obj> =
        this.UpcastResult().UpcastError()
