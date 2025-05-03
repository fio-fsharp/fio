(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

[<AutoOpen>]
module FIO.Core.Ops

open System.Threading.Tasks

/// An alias for `Succeed` which succeeds immediately with the result.
let inline ( !+ ) (res: 'R) : FIO<'R, 'E> =
    FIO.Succeed res

/// An alias for `Fail` which fails immediately with the error.
let inline ( !- ) (err: 'E) : FIO<'R, 'E> =
    FIO.Fail err

/// An alias for 'FromFunc' which succeeds with the result of a function and applies error handling if necessary.
let inline ( !<<< ) (func: unit -> 'R) (onError: exn -> 'E) : FIO<'R, 'E> =
    FIO.FromFunc<'R, 'E> (func, onError)
    
/// An alias for 'FromFunc' which succeeds with the result of a function and fails with Exception.
let inline ( !<< ) (func: unit -> 'R) : FIO<'R, exn> =
    FIO.FromFunc<'R, exn> func

/// An alias for `Send`, which puts the message on the channel and succeeds with the message.
let inline ( --> ) (msg: 'R) (chan: Channel<'R>) : FIO<'R, 'E> =
    chan.Send msg

/// An alias for `Send`, which puts the message on the channel and succeeds with the message.
let inline ( <-- ) (chan: Channel<'R>) (msg: 'R) : FIO<'R, 'E> =
    chan.Send msg

/// An alias for `Send`, which puts the message on the channel and succeeds with unit.
let inline ( --!> ) (msg: 'R) (chan: Channel<'R>) : FIO<unit, 'E> =
    chan.Send msg |> _.Then <| FIO.Succeed ()

/// An alias for `Send`, which puts the message on the channel and succeeds with unit.
let inline ( <!-- ) (chan: Channel<'R>) (msg: 'R) : FIO<unit, 'E> =
    chan.Send msg |> _.Then <| FIO.Succeed ()

/// An alias for `Receive`, which receives a message from the channel and succeeds with it.
let inline ( !--> ) (chan: Channel<'R>) : FIO<'R, 'E> =
    chan.Receive()

/// An alias for `Receive`, which receives a message from the channel and succeeds with it.
let inline ( !<-- ) (chan: Channel<'R>) : FIO<'R, 'E> =
    chan.Receive()

/// An alias for `Receive`, which receives a message from the channel and succeeds with unit.
let inline ( !--!> ) (chan: Channel<'R>) : FIO<unit, 'E> =
    chan.Receive().Then <| FIO.Succeed ()

/// An alias for `Receive`, which receives a message from the channel and succeeds with unit.
let inline ( !<!-- ) (chan: Channel<'R>) : FIO<unit, 'E> =
    chan.Receive().Then <| FIO.Succeed ()

/// An alias for `Fork`, which interprets an effect concurrently and returns the fiber that is interpreting it.
/// The fiber can be awaited for the result of the effect.
let inline ( !<~ ) (eff: FIO<'R, 'E>) : FIO<Fiber<'R, 'E>, 'E1> =
    eff.Fork()

/// An alias for `Fork`, which interprets an effect concurrently and returns the fiber that is interpreting it.
/// The fiber can be awaited for the result of the effect.
let inline ( !~> ) (eff: FIO<'R, 'E>) : FIO<Fiber<'R, 'E>, 'E1> =
    eff.Fork()

/// An alias for `Fork`, which interprets an effect concurrently and returns `unit` when interpreted.
let inline ( !!<~ ) (eff: FIO<'R, 'E>) : FIO<unit, 'E1> =
    eff.Fork().Then <| FIO.Succeed ()

/// An alias for `Fork`, which interprets an effect concurrently and returns `unit` when interpreted.
let inline ( !!~> ) (eff: FIO<'R, 'E>) : FIO<unit, 'E1> =
    eff.Fork().Then <| FIO.Succeed ()

/// An alias for `Parallel`, which interprets two effects concurrently and succeeds with a tuple of their results when both complete.
/// If either effect fails, the error is immediately returned.
let inline ( <!> ) (eff: FIO<'R, 'E>) (eff': FIO<'R1, 'E>) : FIO<'R * 'R1, 'E> =
    eff.Parallel eff'

/// An alias for `Parallel`, which interprets two effects concurrently and succeeds with `unit` when completed.
/// If either effect fails, the error is immediately returned.
let inline ( <~> ) (eff: FIO<'R, 'E>) (eff': FIO<'R1, 'E>) : FIO<unit, 'E> =
    eff.Parallel eff' |> _.Then <| FIO.Succeed ()

/// An alias for `ParallelError`, which interprets two effects concurrently and succeeds with a tuple of their errors when both fail.
let inline ( <!!> ) (eff: FIO<'R, 'E>) (eff': FIO<'R, 'E1>) : FIO<'R, 'E * 'E1> =
    eff.ParallelError eff'

/// An alias for `Await`, which waits for the result of the given fiber and succeeds with it.
let inline ( !<~~ ) (fiber: Fiber<'R, 'E>) : FIO<'R, 'E> =
    fiber.Await()

/// An alias for `Await`, which waits for the result of the given fiber and succeeds with it.
let inline ( !~~> ) (fiber: Fiber<'R, 'E>) : FIO<'R, 'E> =
    fiber.Await()

/// An alias for `Await`, which waits for the completion of the fiber and returns `unit`.
let inline ( !!<~~ ) (fiber: Fiber<'R, 'E>) : FIO<unit, 'E> =
    fiber.Await().Then <| FIO.Succeed ()

/// An alias for `Await`, which waits for the completion of the fiber and returns `unit`.
let inline ( !!~~> ) (fiber: Fiber<'R, 'E>) : FIO<unit, 'E> =
    fiber.Await().Then <| FIO.Succeed ()

// An alias for 'AwaitTask', which awaits a Task and turns it into an effect.
// Applies error handling if necessary.
let inline ( !<<~ ) (task: Task) (onError: exn -> 'E) : FIO<unit, 'E> =
    FIO.AwaitTask<unit, 'E> (task, onError)

// An alias for 'AwaitTask', which awaits a Task and turns it into an effect.
let inline ( !!<<~ ) (task: Task) : FIO<unit, exn> =
    FIO.AwaitTask<unit, exn> task

// An alias for 'AwaitGenericTask', which awaits a generic Task and turns it into an effect.
// Applies error handling if necessary.
let inline ( !<<~~ ) (task: Task<'R>) (onError: exn -> 'E) : FIO<'R, 'E> =
    FIO.AwaitGenericTask<'R, 'E> (task, onError)

// An alias for 'AwaitGenericTask', which awaits a generic Task and turns it into an effect.
let inline ( !!<<~~ ) (task: Task<'R>) : FIO<'R, exn> =
    FIO.AwaitGenericTask<'R, exn> task

// An alias for 'AwaitAsync', which awaits an Async computation and turns it into an effect
// with a default onError.
let inline ( !<<<~ ) (async: Async<'R>) : FIO<'R, exn> =
    FIO.AwaitAsync<'R, exn> async

// An alias for 'AwaitAsync', which awaits an Async computation and turns it into an effect.
// Applies error handling if necessary.
let inline ( !!<<<~ ) (async: Async<'R>) (onError: exn -> 'E) : FIO<'R, 'E> =
    FIO.AwaitAsync<'R, 'E> (async, onError)

/// An alias for `FlatMap`, which chains the success result of the effect to the continuation function.
let inline ( >>= ) (eff: FIO<'R, 'E>) (cont: 'R -> FIO<'R1, 'E>) : FIO<'R1, 'E> =
    eff.Bind cont

/// An alias for `FlatMap`, which chains the success result of the effect to the continuation function.
let inline ( =<< ) (cont: 'R -> FIO<'R1, 'E>) (eff: FIO<'R, 'E>)  : FIO<'R1, 'E> =
    eff.Bind cont

/// An alias for `FlatMapError`, which chains the error result of the effect to the continuation function.
let inline ( >>=? ) (eff: FIO<'R, 'E>) (cont: 'E -> FIO<'R, 'E1>) : FIO<'R, 'E1> =
    eff.BindError cont

/// An alias for `FlatMapError`, which chains the error result of the effect to the continuation function.
let inline ( ?=<< ) (cont: 'E -> FIO<'R, 'E1>) (eff: FIO<'R, 'E>) : FIO<'R, 'E1> =
    eff.BindError cont

/// An alias for `Map`, which maps a function over the result of an effect.
let inline ( *> ) (eff: FIO<'R, 'E>) (cont: 'R -> 'R1) : FIO<'R1, 'E> =
    eff.Map cont

/// An alias for `Map`, which maps a function over the result of an effect.
let inline ( <* ) (cont: 'R -> 'R1) (eff: FIO<'R, 'E>) : FIO<'R1, 'E> =
    eff.Map cont

/// An alias for `MapError`, which maps a function over the error of an effect.
let inline ( *>? ) (eff: FIO<'R, 'E>) (cont: 'E -> 'E1) : FIO<'R, 'E1> =
    eff.MapError cont

/// An alias for `MapError`, which maps a function over the error of an effect.
let inline ( ?<* ) (cont: 'E -> 'E1) (eff: FIO<'R, 'E>) : FIO<'R, 'E1> =
    eff.MapError cont

/// An alias for `Then`, which sequences two effects, ignoring the result of the first one.
let inline ( >> ) (eff: FIO<'R, 'E>) (eff': FIO<'R1, 'E>) : FIO<'R1, 'E> =
    eff.Then eff'

/// An alias for `Then`, which sequences two effects, ignoring the result of the first one.
let inline ( << ) (eff: FIO<'R, 'E>) (eff': FIO<'R1, 'E>) : FIO<'R, 'E> =
    eff'.Then eff

/// An alias for `ThenError`, which sequences two effects, ignoring the error of the first one.
let inline ( >>? ) (eff: FIO<'R, 'E>) (eff': FIO<'R, 'E1>) : FIO<'R, 'E1> =
    eff.ThenError eff'

/// An alias for `ThenError`, which sequences two effects, ignoring the error of the first one.
let inline ( ?<< ) (eff: FIO<'R, 'E1>) (eff': FIO<'R, 'E>) : FIO<'R, 'E1> =
    eff'.ThenError eff

/// An alias for `Apply`, which combines two effects: one producing a function and the other a value, 
/// and applies the function to the value.
let inline ( <*> ) (eff: FIO<'R, 'E>) (eff': FIO<'R -> 'R1, 'E>) : FIO<'R1, 'E> =
    eff.Apply eff'

/// An alias for `ApplyError`, which combines two effects: one producing a function and the other a value, 
/// and applies the function to the value.
let inline ( <**> ) (eff: FIO<'R, 'E>) (eff': FIO<'R, 'E -> 'E1>) : FIO<'R, 'E1> =
    eff.ApplyError eff'

/// An alias for `Zip`, which combines the results of two effects into a tuple when both succeed.
/// If either effect fails, the error is immediately returned.
let inline ( <^> ) (eff: FIO<'R, 'E>) (eff': FIO<'R1, 'E>) : FIO<'R * 'R1, 'E> =
    eff.Zip eff'

/// An alias for `ZipError`, which combines the errors of two effects into a tuple when both fail.
let inline ( <^^> ) (eff: FIO<'R, 'E>) (eff': FIO<'R, 'E1>) : FIO<'R, 'E * 'E1> =
    eff.ZipError eff'
