(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

[<AutoOpen>]
module FIO.Core.DSL

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
    | RescheduleForRunning
    | RescheduleForBlocking of BlockingItem
    | Evaluated

and internal BlockingItem =
    | BlockingChannel of Channel<obj>
    | BlockingIFiber of InternalFiber

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

    member internal this.Complete res : Task<unit> =
        this.IFiber.Complete res

    member internal this.CompleteAndReschedule res workItemChan : Task<unit> =
        this.IFiber.CompleteAndReschedule res workItemChan

and internal InternalChannel<'R> (id: Guid) =
    let chan = Channel.CreateUnbounded<'R>()
    let mutable count = 0
    
    new() = InternalChannel (Guid.NewGuid())

    member internal this.AddAsync (msg: 'R) : Task<unit> =
        (chan.Writer.WriteAsync msg)
            .AsTask()
            .ContinueWith(fun _ ->
                Interlocked.Increment &count
                |> ignore
                Task.FromResult ())
            .Unwrap()
    
    member internal this.TakeAsync () : Task<'R> =
        chan.Reader.ReadAsync()
            .AsTask()
            .ContinueWith(fun (task: Task<'R>) ->
                Interlocked.Decrement &count
                |> ignore
                task.Result)

    member internal this.WaitToReceiveAsync () : Task<bool> =
        chan.Reader.WaitToReadAsync().AsTask()

    member internal this.Count : int =
        Volatile.Read &count

    member internal this.Id : Guid =
        id

and internal InternalFiber
    (id: Guid,
     resChan: InternalChannel<Result<obj, obj>>,
     blockingWorkItemChan: InternalChannel<WorkItem>) =

    let completeAlreadyCalledFail () =
        invalidOp $"InternalFiber: Complete was called on an already
            completed InternalFiber! Result channel count: %i{resChan.Count}"

    member internal this.Complete res : Task<unit> =
        if resChan.Count = 0 then
            resChan.AddAsync res
        else
            completeAlreadyCalledFail ()
    
    member internal this.CompleteAndReschedule res workItemChan : Task<unit> =
        if resChan.Count = 0 then
            resChan.AddAsync res |> ignore
            this.RescheduleBlockingWorkItems workItemChan
        else
            completeAlreadyCalledFail ()

    member internal this.AwaitAsync () : Task<Result<obj, obj>> =
        task {
            let! res = resChan.TakeAsync ()
            // Re-add the result to the channel to allow concurrent awaits
            // (the channel is never depleted)
            do! resChan.AddAsync res
            return res
        }
    
    member internal this.AddBlockingWorkItem workItem : Task<unit> =
        blockingWorkItemChan.AddAsync workItem

    member internal this.Completed () : bool =
        resChan.Count > 0

    member internal this.Id : Guid =
        id

    member private this.RescheduleBlockingWorkItems (activeWorkItemChan: InternalChannel<WorkItem>) : Task<unit> =
        task {
            while blockingWorkItemChan.Count > 0 do
                let! workItem = blockingWorkItemChan.TakeAsync ()
                do! activeWorkItemChan.AddAsync workItem
        }

/// A Fiber is a construct that represents a lightweight-thread.
/// Fibers are used to interpret multiple effects in parallel and
/// can be awaited to retrieve the result of the effect.
and Fiber<'R, 'E> private
    (id: Guid,
     resChan: InternalChannel<Result<obj, obj>>,
     blockingWorkItemChan: InternalChannel<WorkItem>) =

    let ifiber = InternalFiber (id, resChan, blockingWorkItemChan)

    new() = Fiber(Guid.NewGuid(), InternalChannel<Result<obj, obj>>(), InternalChannel<WorkItem>())

    /// Creates an effect that waits for the fiber and succeeds with its result.
    member this.Await () : FIO<'R, 'E> =
        AwaitFiber ifiber

    /// Waits for the fiber and succeeds with its result.
    member this.AwaitAsync () : Task<Result<'R, 'E>> =
        task {
            match! ifiber.AwaitAsync() with
            | Ok res -> return Ok (res :?> 'R)
            | Error err -> return Error (err :?> 'E)
        }

    member this.Id : Guid =
        id

     member internal this.Internal : InternalFiber =
        ifiber
 
/// A channel represents a communication queue that holds
/// data of the type 'R. Data can be both be sent and
/// received (blocking) on a channel.
and Channel<'R> private
    (id: Guid,
     resChan: InternalChannel<obj>,
     blockingWorkItemChan: InternalChannel<WorkItem>,
     count: int64 ref) =

    new() = Channel(Guid.NewGuid(), InternalChannel<obj>(), InternalChannel<WorkItem>(), ref 0)

    /// Send puts the message on the channel and succeeds with the message.
    member this.Send msg : FIO<'R, 'E> =
        SendChan (msg, this)

    /// Receive retrieves a message from the channel and succeeds with it.
    member this.Receive () : FIO<'R, 'E> =
        ReceiveChan this

    member this.Count : int =
        resChan.Count
        
    member this.Id : Guid =
        id

    member internal this.SendAsync (msg: 'R) : Task<unit> =
        Interlocked.Increment count
        |> ignore
        resChan.AddAsync msg

    member internal this.ReceiveAsync () : Task<'R> =
        task {
            let! res = resChan.TakeAsync()
            return res :?> 'R
        }

    member internal this.AddBlockingWorkItem workItem : Task<unit> =
        blockingWorkItemChan.AddAsync workItem

    member internal this.RescheduleNextBlockingWorkItem (activeWorkItemChan: InternalChannel<WorkItem>) : Task<unit> =
        task {
            if blockingWorkItemChan.Count > 0 then
                let! workItem = blockingWorkItemChan.TakeAsync()
                do! activeWorkItemChan.AddAsync workItem
        }

    member internal this.BlockingWorkItemCount : int =
        blockingWorkItemChan.Count

    member internal this.UseAvailableData () : unit =
        Interlocked.Decrement count
        |> ignore

    member internal this.HasDataAvailable () : bool =
        let value = count.Value
        Interlocked.Read &value > 0

    member internal this.Upcast () : Channel<obj> =
        Channel<obj> (id, resChan, blockingWorkItemChan, count)

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
    | ConcurrentTask of task: (unit -> Task<obj>) * onError: (exn -> 'E) * fiber: obj * ifiber: InternalFiber
    | AwaitFiber of ifiber: InternalFiber
    | AwaitGenericTPLTask of task: Task<obj> * onError: (exn -> 'E)
    | ChainSuccess of eff: FIO<obj, 'E> * cont: (obj -> FIO<'R, 'E>)
    | ChainError of eff: FIO<'R, obj> * cont: (obj -> FIO<'R, 'E>)

    /// Succeeds immediately with the result.
    static member Succeed res : FIO<'R, 'E> =
        Success res

    /// Fails immediately with the error.
    static member Fail err : FIO<'R, 'E> =
        Failure err

    /// Converts a function into an effect.
    static member FromFunc<'R, 'E> (func: unit -> 'R, onError: exn -> 'E) : FIO<'R, 'E> =
        Action (func, onError)

    /// Converts a function into an effect with a default onError.
    static member inline FromFunc<'R, 'E> (func: unit -> 'R) : FIO<'R, exn> =
        FIO.FromFunc<'R, exn> (func, id)

    /// Converts a Result into an effect.
    static member inline FromResult (res: Result<'R, 'E>) : FIO<'R, 'E> =
        match res with
        | Ok res -> FIO.Succeed res
        | Error err -> FIO.Fail err
    
    /// Converts an Option into an effect.
    static member inline FromOption (opt: Option<'R>) (onNone: unit -> 'E) : FIO<'R, 'E> =
        match opt with
        | Some res -> FIO.Succeed res
        | None -> FIO.Fail <| onNone ()

    /// Converts a Choice into an effect.
    static member inline FromChoice (choice: Choice<'R, 'E>) : FIO<'R, 'E> =
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
    static member FromTask<'R, 'E> (task: unit -> Task, onError: exn -> 'E) : FIO<Fiber<unit, exn>, 'E> =
        let fiber = Fiber<unit, exn>()
        ConcurrentTask ((fun () -> (task ()).ContinueWith(fun _ -> box ())), onError, fiber, fiber.Internal)

    // Converts a Task into a Fiber with a default onError.
    static member inline FromTask<'R, 'E> (task: unit -> Task) : FIO<Fiber<unit, exn>, exn> =
        FIO.FromTask<Fiber<unit, exn>, exn> (task, id)
    
    // Converts a generic Task into a Fiber.
    static member FromGenericTask<'R, 'E> (task: unit -> Task<'R>, onError: exn -> 'E) : FIO<Fiber<'R, exn>, 'E> =
        let fiber = Fiber<'R, exn>()
        let task = fun () ->
                (task ()).ContinueWith(fun (outerTask: Task<'R>) ->
                    if outerTask.IsFaulted then
                        Task.FromException<obj> (outerTask.Exception.GetBaseException())
                    elif outerTask.IsCanceled then
                        Task.FromCanceled<obj> CancellationToken.None
                    else 
                        Task.FromResult (box outerTask.Result)
                   ).Unwrap()
        ConcurrentTask (task, onError, fiber, fiber.Internal)

    // Converts a generic Task into a Fiber with a default onError.       
    static member inline FromGenericTask<'R, 'E> (task: unit -> Task<'R>) : FIO<Fiber<'R, exn>, exn> =
        FIO.FromGenericTask<Fiber<'R, exn>, exn> (task, id)
        
    /// Interprets an effect concurrently and returns the fiber interpreting it.
    /// The fiber can be awaited for the result of the effect.
    member this.Fork () : FIO<Fiber<'R, 'E>, 'E1> =
        let fiber = new Fiber<'R, 'E>()
        ConcurrentEffect (this.Upcast(), fiber, fiber.Internal)

    /// Binds a continuation to the result of an effect.
    /// If the effect fails, the error is immediately returned.
    member this.Bind (cont: 'R -> FIO<'R1, 'E>) : FIO<'R1, 'E> =
        ChainSuccess (this.UpcastResult(), fun res -> cont (res :?> 'R))

    /// Binds a continuation to the error of an effect.
    /// If the effect succeeds, the result is immediately returned.       
    member this.BindError (cont: 'E -> FIO<'R, 'E1>) : FIO<'R, 'E1> =
        ChainError (this.UpcastError(), fun err -> cont (err :?> 'E))

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
        | ConcurrentTask (task, onError, fiber, ifiber) ->
            ConcurrentTask (task, onError, fiber, ifiber)
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
        | ConcurrentTask (task, onError, fiber, ifiber) ->
            ConcurrentTask (task, upcastOnError onError, fiber, ifiber)
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
