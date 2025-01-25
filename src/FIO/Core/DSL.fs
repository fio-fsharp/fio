(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

[<AutoOpen>]
module FIO.Core.DSL

open System
open System.Threading
open System.Collections.Concurrent

type internal Cont =
    obj -> FIO<obj, obj>

and internal ContType =
    | SuccessCont
    | FailureCont

and internal ContStackFrame =
    ContType * Cont

and internal ContStack =
    ContStackFrame list

and internal BlockingQueue<'T> =
    BlockingCollection<'T>

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

and internal InternalFiber internal (resQueue: BlockingQueue<Result<obj, obj>>, blockingWorkItemQueue: BlockingQueue<WorkItem>) =

    // Use semaphore instead?
    member internal this.Complete res =
        if resQueue.Count = 0 then
            resQueue.Add res
        else
            invalidOp "InternalFiber: Complete was called on an already completed InternalFiber!"

    member internal this.AwaitResult () =
        let res = resQueue.Take ()
        // Re-add the result to the queue to allow concurrent awaits
        resQueue.Add res
        res

    member internal this.Completed () =
        resQueue.Count > 0

    member internal this.AddBlockingWorkItem workItem =
        blockingWorkItemQueue.Add workItem

    member internal this.RescheduleBlockingWorkItems (workItemQueue: BlockingQueue<WorkItem>) =
        while blockingWorkItemQueue.Count > 0 do
            workItemQueue.Add <| blockingWorkItemQueue.Take ()

    member internal this.BlockingWorkItemsCount () =
        blockingWorkItemQueue.Count

/// A Fiber is a construct that represents a lightweight-thread. Fibers are used to interpret multiple effects in parallel and
/// can be awaited to retrieve the result of the effect.
and Fiber<'R, 'E> private (resQueue: BlockingQueue<Result<obj, obj>>, blockingWorkItemQueue: BlockingQueue<WorkItem>) =

    new() = Fiber(new BlockingQueue<Result<obj, obj>>(), new BlockingQueue<WorkItem>())

    /// Creates an effect that waits for the fiber and succeeds with its result.
    member this.Await () : FIO<'R, 'E> =
        Await <| this.ToInternal ()

    /// Waits for the fiber and succeeds with its result.
    member this.AwaitResult () =
        let res = resQueue.Take ()
        // Re-add the result to the queue to allow concurrent awaits
        resQueue.Add res
        match res with
        | Ok res -> Ok (res :?> 'R)
        | Error err -> Error (err :?> 'E)

    member internal this.ToInternal () =
        InternalFiber(resQueue, blockingWorkItemQueue)

/// A channel represents a communication queue that holds data of the type 'R. Data can be both be sent and
/// received (blocking) on a channel.
and Channel<'R> private (resQueue: BlockingQueue<obj>, blockingWorkItemQueue: BlockingQueue<WorkItem>, dataCounter: int64 ref) =

    new() = Channel(new BlockingQueue<obj>(), new BlockingQueue<WorkItem>(), ref 0)

    /// Send puts the message on the channel and succeeds with the message.
    member this.Send msg : FIO<'R, 'E> =
        Send (msg, this)

    /// Receive retrieves a message from the channel and succeeds with it.
    member this.Receive () : FIO<'R, 'E> =
        Receive this

    member this.Count () =
        resQueue.Count

    member internal this.Add (msg: 'R) =
        Interlocked.Increment dataCounter |> ignore
        resQueue.Add msg

    member internal this.Take () =
        resQueue.Take() :?> 'R

    member internal this.AddBlockingWorkItem workItem =
        blockingWorkItemQueue.Add workItem

    member internal this.RescheduleBlockingWorkItems (workItemQueue: BlockingQueue<WorkItem>) =
        if blockingWorkItemQueue.Count > 0 then
            workItemQueue.Add <| blockingWorkItemQueue.Take ()

    member internal this.HasBlockingWorkItems () =
        blockingWorkItemQueue.Count > 0

    member internal this.UseAvailableData () =
        Interlocked.Decrement dataCounter |> ignore

    member internal this.DataAvailable () =
        let mutable temp = dataCounter.Value
        Interlocked.Read &temp > 0

    member internal this.Upcast () =
        Channel<obj>(resQueue, blockingWorkItemQueue, dataCounter)

and channel<'R> = Channel<'R>

/// The FIO type models a functional effect that can either succeed
/// with a result or fail with an error when interpreted.
and FIO<'R, 'E> =
    internal
    | Success of res: 'R
    | Failure of err: 'E
    | Concurrent of eff: FIO<obj, obj> * fiber: obj * ifiber: InternalFiber
    | Await of ifiber: InternalFiber
    | ChainSuccess of eff: FIO<obj, 'E> * cont: (obj -> FIO<'R, 'E>)
    | ChainError of eff: FIO<obj, obj> * cont: (obj -> FIO<'R, 'E>)
    | Send of msg: 'R * chan: Channel<'R>
    | Receive of chan: Channel<'R>

    /// Succeed succeeds immediately with the result.
    static member Succeed res : FIO<'R, 'E> =
        Success res

    /// Fail fails immediately with the error.
    static member Fail err : FIO<'R, 'E> =
        Failure err

    /// Converts a Result into a FIO effect.
    static member FromResult (res: Result<'R, 'E>) : FIO<'R, 'E> =
        match res with
        | Ok res -> FIO.Succeed res
        | Error err -> FIO.Fail err

    /// Converts an Option into a FIO effect.
    static member FromOption (opt: Option<'R>) (err: 'E) : FIO<'R, 'E> =
        match opt with
        | Some res -> FIO.Succeed res
        | None -> FIO.Fail err

    /// Converts a Choice into a FIO effect.
    static member FromChoice (choice: Choice<'R, 'E>) : FIO<'R, 'E> =
        match choice with
        | Choice1Of2 res -> FIO.Succeed res
        | Choice2Of2 err -> FIO.Fail err

    /// Fork interprets an effect concurrently and returns the fiber that is interpreting it.
    /// The fiber can be awaited for the result of the effect.
    member this.Fork () : FIO<Fiber<'R, 'E>, 'E1> =
        let fiber = new Fiber<'R, 'E>()
        Concurrent (this.Upcast(), fiber, fiber.ToInternal())

    /// Bind binds a continuation to the result of an effect.
    /// If the effect fails, the error is immediately returned.
    member this.Bind (cont: 'R -> FIO<'R1, 'E>) : FIO<'R1, 'E> =
        ChainSuccess (this.UpcastResult(), fun res -> cont (res :?> 'R))

    /// BindError binds a continuation to the error of an effect.
    /// If the effect succeeds, the result is immediately returned.
    member this.BindError (cont: 'E -> FIO<'R, 'E1>) : FIO<'R, 'E1> =
        ChainError (this.Upcast(), fun err -> cont (err :?> 'E))

    /// Then sequences two effects, ignoring the result of the first effect.
    /// If the first effect fails, the error is immediately returned.
    member inline this.Then (eff: FIO<'R1, 'E>) : FIO<'R1, 'E> =
        this.Bind <| fun _ -> eff

    /// ThenError sequences two effects, ignoring the error of the first effect.
    /// If the first effect succeeds, the result is immediately returned.
    member inline this.ThenError (eff: FIO<'R, 'E1>) : FIO<'R, 'E1> =
        this.BindError <| fun _ -> eff

    /// Apply combines two effects: one produces a result function and the other produces a result value.
    /// The function is applied to the value, and the result is returned.
    /// Errors are immediately returned if any effect fails.
    member inline this.Apply (eff: FIO<'R -> 'R1, 'E>) : FIO<'R1, 'E> =
        this.Bind <| fun res ->
            eff.Bind <| fun func ->
                FIO.Succeed <| func res

    /// ApplyError combines two effects: one produces an error function and the other produces an error value.
    /// The function is applied to the value, and the error is returned.
    member inline this.ApplyError (eff: FIO<'R, 'E -> 'E1>) : FIO<'R, 'E1> =
        this.BindError <| fun err ->
            eff.BindError <| fun func ->
                FIO.Fail <| func err

    /// Parallel interprets two effects concurrently and succeeds with a tuple of their results when both complete.
    /// If either effect fails, the error is immediately returned.
    member inline this.Parallel (eff: FIO<'R1, 'E>) : FIO<'R * 'R1, 'E> =
        eff.Fork().Bind <| fun fiber ->
            this.Bind <| fun res ->
                fiber.Await().Bind <| fun res' ->
                     FIO.Succeed (res, res')

    /// ParallelError interprets two effects concurrently and succeeds with a tuple of their errors when both complete.
    member inline this.ParallelError (eff: FIO<'R, 'E1>) : FIO<'R, 'E * 'E1> =
        eff.Fork().Bind <| fun fiber ->
            this.BindError <| fun err ->
                fiber.Await().BindError <| fun err' ->
                    FIO.Fail (err, err')

    /// Zip combines two effects and succeeds with a tuple of their results when both complete.
    /// Errors are immediately returned if any effect fails.
    member inline this.Zip (eff: FIO<'R1, 'E>) : FIO<'R * 'R1, 'E> =
        this.Bind <| fun res ->
            eff.Bind <| fun res' ->
                FIO.Succeed (res, res')

    /// ZipError combines two effects and succeeds with a tuple of their errors when both complete.
    member inline this.ZipError (eff: FIO<'R, 'E1>) : FIO<'R, 'E * 'E1> =
        this.BindError <| fun err ->
            eff.BindError <| fun err' ->
                FIO.Fail (err, err')

    /// Race interprets two effects concurrently and succeeds with the result of the first effect that completes.
    /// If both effects fail, the first error is returned.
    member this.Race (eff: FIO<'R, 'E>) : FIO<'R, 'E> =
        let resChan = new Channel<Result<'R, 'E>>()
        let interpret (eff: FIO<'R, 'E>) (chan: Channel<Result<'R, 'E>>) =
            eff.Fork().Bind <| fun fiber ->
                chan.Add <| fiber.AwaitResult()
                FIO.Succeed ()
        (interpret this resChan).Parallel (interpret eff resChan) |> _.Then <|
        match resChan.Take() with
        | Ok res -> FIO.Succeed res
        | Error err -> FIO.Fail err

    member internal this.UpcastResult () : FIO<obj, 'E> =
        match this with
        | Success res ->
            Success (res :> obj)

        | Failure err ->
            Failure err

        | Concurrent (eff, fiber, ifiber) ->
            Concurrent (eff, fiber, ifiber)

        | Await ifiber ->
            Await ifiber

        | ChainSuccess (eff, cont) ->
            ChainSuccess (eff, fun res -> (cont res).UpcastResult())

        | ChainError (eff, cont) ->
            ChainError (eff, fun err -> (cont err).UpcastResult())

        | Send (msg, chan) ->
            Send (msg :> obj, chan.Upcast())

        | Receive chan ->
            Receive <| chan.Upcast()

    member internal this.UpcastError () : FIO<'R, obj> =
        match this with
        | Success res ->
            Success res

        | Failure err ->
            Failure (err :> obj)

        | Concurrent (eff, fiber, ifiber) ->
            Concurrent (eff, fiber, ifiber)

        | Await ifiber ->
            Await ifiber

        | ChainSuccess (eff, cont) ->
            ChainSuccess (eff.UpcastError(), fun res -> (cont res).UpcastError())

        | ChainError (eff, cont) ->
            ChainError (eff.UpcastError(), fun err -> (cont err).UpcastError())

        | Send (msg, chan) ->
            Send (msg, chan)

        | Receive chan ->
            Receive chan

    member internal this.Upcast () : FIO<obj, obj> =
        this.UpcastResult().UpcastError()
