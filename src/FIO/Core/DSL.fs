(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(************************************************************************************)

[<AutoOpen>]
module FIO.Core.DSL

open System.Threading
open System.Collections.Concurrent

type internal Continuation =
    obj -> FIO<obj, obj>

and internal ContinuationKind =
    | SuccessKind
    | ErrorKind

and internal ContinuationStackFrame =
    ContinuationKind * Continuation

and internal ContinuationStack =
    ContinuationStackFrame list

and internal InternalQueue<'T> = BlockingCollection<'T>

and internal RuntimeAction =
    | RescheduleForRunning
    | RescheduleForBlocking of BlockingItem
    | Evaluated

and internal WorkItem =
    { Effect: FIO<obj, obj>
      InternalFiber: InternalFiber
      Stack: ContinuationStack
      PrevAction: RuntimeAction }

    static member Create(effect, internalFiber, stack, lastAction) =
        { Effect = effect
          InternalFiber = internalFiber
          Stack = stack
          PrevAction = lastAction }

    member internal this.Complete(result) =
        this.InternalFiber.Complete result

and internal BlockingItem =
    | BlockingChannel of Channel<obj>
    | BlockingFiber of InternalFiber

and internal InternalFiber internal (
        resultQueue: InternalQueue<Result<obj, obj>>,
        blockingWorkItemQueue: InternalQueue<WorkItem>
    ) =

    // Use semaphore instead?
    member internal this.Complete(result) =
        if resultQueue.Count = 0 then
            resultQueue.Add result
        else
            failwith "InternalFiber: Complete was called on an already completed InternalFiber!"

    member internal this.AwaitResult() =
        let result = resultQueue.Take()
        resultQueue.Add result
        result

    member internal this.Completed() =
        resultQueue.Count > 0

    member internal this.AddBlockingWorkItem(workItem) =
        blockingWorkItemQueue.Add workItem

    member internal this.BlockingWorkItemsCount() =
        blockingWorkItemQueue.Count

    member internal this.RescheduleBlockingWorkItems(workItemQueue: InternalQueue<WorkItem>) =
        while blockingWorkItemQueue.Count > 0 do
            workItemQueue.Add <| blockingWorkItemQueue.Take()

/// A Fiber is a construct that represents a lightweight-thread.
/// Fibers are used to execute multiple effects in parallel and
/// can be awaited to retrieve the result of the effect.
and Fiber<'R, 'E> private (
        resultQueue: InternalQueue<Result<obj, obj>>,
        blockingWorkItemQueue: InternalQueue<WorkItem>
    ) =

    new() = Fiber(new InternalQueue<Result<obj, obj>>(), new InternalQueue<WorkItem>())

    member internal this.ToInternal() =
        InternalFiber(resultQueue, blockingWorkItemQueue)

    member this.AwaitResult() =
        let result = resultQueue.Take()
        resultQueue.Add result
        match result with
        | Ok result -> Ok (result :?> 'R)
        | Error error -> Error (error :?> 'E)

    /// Await waits for the result of the fiber and succeeds with it.
    member this.Await() : FIO<'R, 'E> =
        Await <| this.ToInternal()

/// A channel represents a communication queue that holds
/// data of the type 'D. Data can be both be sent and
/// retrieved (blocking) on a channel.
and Channel<'R> private (
        dataQueue: InternalQueue<obj>,
        blockingWorkItemQueue: InternalQueue<WorkItem>,
        dataCounter: int64 ref // TODO: Use semaphore instead?
    ) =

    new() = Channel(new InternalQueue<obj>(), new InternalQueue<WorkItem>(), ref 0)
    
    member internal this.AddBlockingWorkItem workItem =
        blockingWorkItemQueue.Add workItem

    member internal this.RescheduleBlockingWorkItem(workItemQueue: InternalQueue<WorkItem>) =
        if blockingWorkItemQueue.Count > 0 then
            workItemQueue.Add <| blockingWorkItemQueue.Take()

    member internal this.HasBlockingWorkItems() =
        blockingWorkItemQueue.Count > 0

    member internal this.Upcast() =
        Channel<obj>(dataQueue, blockingWorkItemQueue, dataCounter)

    member internal this.UseAvailableData() =
        Interlocked.Decrement dataCounter |> ignore

    member internal this.DataAvailable() =
        let mutable temp = dataCounter.Value
        Interlocked.Read &temp > 0

    member this.Add(message: 'R) =
        Interlocked.Increment dataCounter |> ignore
        dataQueue.Add message

    member this.Take() =
        dataQueue.Take() :?> 'R

    member this.Count() =
        dataQueue.Count

    /// Send puts the message on the given channel and succeeds with the message.
    member this.Send(message: 'R) : FIO<'R, 'E> =
        Send (message, this)

    /// Receive retrieves a message from the channel and succeeds with it.
    member this.Receive() : FIO<'R, 'E> =
        Receive this

and channel<'R> = Channel<'R>

/// The FIO type models a functional effect that can either succeed
/// with a result or fail with an error when executed.
and FIO<'R, 'E> =
    internal
    | Send of message: 'R * channel: Channel<'R>
    | Receive of channel: Channel<'R>
    | Concurrent of effect: FIO<obj, obj> * fiber: obj * internalFiber: InternalFiber
    | Await of internalFiber: InternalFiber
    | ChainSuccess of effect: FIO<obj, 'E> * continuation: (obj -> FIO<'R, 'E>)
    | ChainError of effect: FIO<obj, obj> * continuation: (obj -> FIO<'R, 'E>)
    | Success of result: 'R
    | Failure of error: 'E

    /// Fork executes an effect concurrently and returns the fiber that executes it.
    /// The fiber can be awaited for the result of the effect.
    member this.Fork() : FIO<Fiber<'R, 'E>, 'E1> =
        let fiber = new Fiber<'R, 'E>()
        Concurrent (this.Upcast(), fiber, fiber.ToInternal())

    /// Bind binds a continuation to the success result of an effect.
    /// If the effect fails, the error is immediately returned.
    member this.Bind(continuation: 'R -> FIO<'R1, 'E>) : FIO<'R1, 'E> =
        ChainSuccess (this.UpcastResult(), fun result -> continuation (result :?> 'R))

    /// BindError binds a continuation to the error result of an effect.
    /// If the effect succeeds, the result is immediately returned.
    member this.BindError(continuation: 'E -> FIO<'R, 'E1>) : FIO<'R, 'E1> =
        ChainError (this.Upcast(), fun error -> continuation (error :?> 'E))

    /// Then sequences two effects, ignoring the result of the first effect.
    /// If the first effect fails, the error is immediately returned.
    member inline this.Then(effect: FIO<'R1, 'E>) : FIO<'R1, 'E> =
        this.Bind <| fun _ -> effect

    /// ThenError sequences two effects, ignoring the error of the first effect.
    /// If the first effect succeeds, the result is immediately returned.
    member inline this.ThenError(effect: FIO<'R, 'E1>) : FIO<'R, 'E1> =
        this.BindError <| fun _ -> effect

    /// ApplyWith combines two effects: one produces a function and the other produces a value.
    /// The function is applied to the value, and the result is returned.
    /// Errors are immediately returned if any effect fails.
    member this.ApplyWith(effect: FIO<'R -> 'R1, 'E>) : FIO<'R1, 'E> =
        effect.Bind <| fun func ->
            this.Bind <| fun result ->
                Success <| func result

    /// InParallelWith executes two effects concurrently and succeeds with a tuple of their results when both complete.
    /// If either effect fails, the error is immediately returned.
    member this.InParallelWith(effect: FIO<'R1, 'E>) : FIO<'R * 'R1, 'E> =
        effect.Fork().Bind <| fun fiber ->
            this.Bind <| fun thisResult ->
                fiber.Await().Bind <| fun result ->
                    Success (thisResult, result)

    /// ZipWith combines two effects and succeeds with a tuple of their results when both complete.
    /// Errors are immediately returned if any effect fails.
    member this.ZipWith(effect: FIO<'R1, 'E>) : FIO<'R * 'R1, 'E> =
        this.Bind <| fun thisResult ->
            effect.Bind <| fun result ->
                Success (thisResult, result)

    /// RaceWith executes two effects concurrently and succeeds with the result of the first effect that completes.
    /// If both effects fail, the first error is returned.
    member this.RaceWith(effect: FIO<'R, 'E>) : FIO<'R, 'E> =
        let rec loop (thisFiber: InternalFiber) (fiber: InternalFiber) =
            if thisFiber.Completed() then thisFiber
            else if fiber.Completed() then fiber
            else loop thisFiber fiber
        this.Fork().Bind <| fun thisFiber ->
            effect.Fork().Bind <| fun fiber ->
                match (loop (thisFiber.ToInternal()) (fiber.ToInternal())).AwaitResult() with
                | Ok result -> Success (result :?> 'R)
                | Error error -> Failure (error :?> 'E)

    member internal this.UpcastResult() : FIO<obj, 'E> =
        match this with
        | Send (message, channel) ->
            Send (message :> obj, channel.Upcast())

        | Receive channel ->
            Receive <| channel.Upcast()

        | Concurrent (effect, fiber, internalFiber) ->
            Concurrent (effect, fiber, internalFiber)

        | Await internalFiber ->
            Await internalFiber

        | ChainSuccess (effect, continuation) ->
            ChainSuccess (effect, fun result -> (continuation result).UpcastResult())

        | ChainError (effect, continuation) ->
            ChainError (effect, fun result -> (continuation result).UpcastResult())

        | Success result ->
            Success (result :> obj)

        | Failure error ->
            Failure error

    member internal this.UpcastError() : FIO<'R, obj> =
        match this with
        | Send (message, channel) ->
            Send (message, channel)

        | Receive channel ->
            Receive channel

        | Concurrent (effect, fiber, internalFiber) ->
            Concurrent (effect, fiber, internalFiber)

        | Await internalFiber ->
            Await internalFiber

        | ChainSuccess (effect, continuation) ->
            ChainSuccess (effect.UpcastError(), fun result -> (continuation result).UpcastError())

        | ChainError (effect, continuation) ->
            ChainError (effect.UpcastError(), fun result -> (continuation result).UpcastError())

        | Success result ->
            Success result

        | Failure error ->
            Failure (error :> obj)

    member internal this.Upcast() : FIO<obj, obj> =
        this.UpcastResult().UpcastError()

/// Creates an effect that succeeds immediately with the given result.
let succeed (result: 'R) : FIO<'R, 'E> =
    Success result

/// Creates an effect that fails immediately with the given error.
let fail (error: 'E) : FIO<'R, 'E> =
    Failure error

[<AutoOpen>]
module Operators =

    /// An alias for `succeed`, which succeeds immediately with the given result.
    let inline ( !+ ) (result: 'R) : FIO<'R, 'E> =
        succeed result

    /// An alias for `fail`, which fails with the error argument when executed.
    let inline ( !- ) (error: 'E) : FIO<'R, 'E> =
        fail error

    /// An alias for `Send`, which puts the message on the given channel and succeeds with the message.
    let inline ( --> ) (message: 'R) (channel: Channel<'R>) : FIO<'R, 'E> =
        channel.Send message

    /// An alias for `Send`, which puts the message on the given channel and succeeds with the message.
    let inline ( <-- ) (channel: Channel<'R>) (message: 'R) : FIO<'R, 'E> =
        channel.Send message

    /// An alias for `Send`, which puts the message on the given channel and succeeds with unit.
    let inline ( -!> ) (message: 'R) (channel: Channel<'R>) : FIO<unit, 'E> =
        (channel.Send message).Then <| succeed ()

    /// An alias for `Send`, which puts the message on the given channel and succeeds with unit.
    let inline ( <!- ) (channel: Channel<'R>) (message: 'R) : FIO<unit, 'E> =
        (channel.Send message).Then <| succeed ()

    /// An alias for `Receive`, which retrieves a message from the channel and succeeds with it.
    let inline ( !--> ) (channel: Channel<'R>) : FIO<'R, 'E> =
        channel.Receive()

    /// An alias for `Receive`, which retrieves a message from the channel and succeeds with it.
    let inline ( !<-- ) (channel: Channel<'R>) : FIO<'R, 'E> =
        channel.Receive()

    /// An alias for `Receive`, which retrieves a message from the channel and succeeds with unit.
    let inline ( !-!> ) (channel: Channel<'R>) : FIO<unit, 'E> =
        channel.Receive().Then <| succeed ()

    /// An alias for `Receive`, which retrieves a message from the channel and succeeds with unit.
    let inline ( !<!- ) (channel: Channel<'R>) : FIO<unit, 'E> =
        channel.Receive().Then <| succeed ()

    /// An alias for `Fork`, which executes an effect concurrently and returns the fiber that executes it.
    /// The fiber can be awaited for the result of the effect.
    let inline ( ! ) (effect: FIO<'R, 'E>) : FIO<Fiber<'R, 'E>, 'E1> =
        effect.Fork()

    /// An alias for `Fork`, which executes an effect concurrently and returns `unit` when executed.
    let inline ( !! ) (effect: FIO<'R, 'E>) : FIO<unit, 'E1> =
        effect.Fork().Bind <| fun _ -> succeed ()

    /// An alias for `Await`, which waits for the result of the given fiber and succeeds with it.
    let inline ( !? ) (fiber: Fiber<'R, 'E>) : FIO<'R, 'E> =
        fiber.Await()

    /// An alias for `Bind`, which chains the success result of the effect to the continuation function.
    let inline ( >>= ) (effect: FIO<'R, 'E>) (continuation: 'R -> FIO<'R1, 'E>) : FIO<'R1, 'E> =
        effect.Bind continuation

    /// An alias for `Bind`, which chains the success result of the effect to the continuation function.
    let inline ( =<< ) (continuation: 'R -> FIO<'R1, 'E>) (effect: FIO<'R, 'E>)  : FIO<'R1, 'E> =
        effect.Bind continuation

    /// An alias for `BindError`, which chains the error result of the effect to the continuation function.
    let inline ( >>? ) (effect: FIO<'R, 'E>) (continuation: 'E -> FIO<'R, 'E1>) : FIO<'R, 'E1> =
        effect.BindError continuation

    /// An alias for `BindError`, which chains the error result of the effect to the continuation function.
    let inline ( ?<< ) (continuation: 'E -> FIO<'R, 'E1>) (effect: FIO<'R, 'E>) : FIO<'R, 'E1> =
        effect.BindError continuation

    /// An alias for `Then`, which sequences two effects, ignoring the result of the first one.
    let inline ( >> ) (effect: FIO<'R, 'E>) (effect': FIO<'R1, 'E>) : FIO<'R1, 'E> =
        effect.Then effect'

    /// An alias for `Then`, which sequences two effects, ignoring the result of the first one.
    let inline ( << ) (effect: FIO<'R, 'E>) (effect': FIO<'R1, 'E>) : FIO<'R, 'E> =
        effect'.Then effect

    /// An alias for `ThenError`, which sequences two effects, ignoring the error of the first one.
    let inline ( >? ) (effect: FIO<'R, 'E>) (effect': FIO<'R, 'E1>) : FIO<'R, 'E1> =
        effect.ThenError effect'

    /// An alias for `ThenError`, which sequences two effects, ignoring the error of the first one.
    let inline ( ?< ) (effect: FIO<'R, 'E1>) (effect': FIO<'R, 'E>) : FIO<'R, 'E1> =
        effect'.ThenError effect

    /// An alias for `ApplyWith`, which combines two effects: one producing a function and the other a value, 
    /// and applies the function to the value.
    let inline ( >>> ) (effect: FIO<'R,' E>) (effect': FIO<'R -> 'R1, 'E>) : FIO<'R1, 'E> =
        effect.ApplyWith effect'

    /// An alias for `ApplyWith`, which combines two effects: one producing a function and the other a value, 
    /// and applies the function to the value.
    let inline ( <<< ) (effect: FIO<'R -> 'R1, 'E>) (effect': FIO<'R,' E>) : FIO<'R1, 'E> =
        effect'.ApplyWith effect

    /// An alias for `InParallelWith`, which executes two effects concurrently and succeeds with a tuple of their results when both complete.
    /// If either effect fails, the error is immediately returned.
    let inline ( <*> ) (effect: FIO<'R, 'E>) (effect': FIO<'R1, 'E>) : FIO<'R * 'R1, 'E> =
        effect.InParallelWith effect'

    /// An alias for `InParallelWith`, which executes two effects concurrently and succeeds with `unit` when completed.
    /// If either effect fails, the error is immediately returned.
    let inline ( <!> ) (effect: FIO<'R, 'E>) (effect': FIO<'R1, 'E>) : FIO<unit, 'E> =
        (effect.InParallelWith effect').Then <| succeed ()

    /// An alias for `ZipWith`, which combines the results of two effects into a tuple when both succeed.
    /// If either effect fails, the error is immediately returned.
    let inline ( <^> ) (effect: FIO<'R, 'E>) (effect': FIO<'R1, 'E>) : FIO<'R * 'R1, 'E> =
        effect.ZipWith effect'

    /// An alias for `RaceWith`, which succeeds with the result of the effect that completes first.
    let inline ( <?> ) (effect: FIO<'R, 'E>) (effect': FIO<'R, 'E>) : FIO<'R, 'E> =
        effect.RaceWith effect'
