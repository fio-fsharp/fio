(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(************************************************************************************)

module FIO.Runtime.Naive

open FIO.Core

type Runtime() =
    inherit FIORuntime()

    [<TailCall>]
    member private this.InternalRun (effect: FIO<obj, obj>) (stack: ContinuationStack) : Result<obj, obj> =

        let rec handleSuccess result stack =
            match stack with
            | [] -> Ok result
            | (SuccessKind, cont) :: ss -> this.InternalRun (cont result) ss
            | (ErrorKind, _) :: ss -> handleSuccess result ss

        let rec handleError error stack =
            match stack with
            | [] -> Error error
            | (SuccessKind, _) :: ss -> handleError error ss
            | (ErrorKind, cont) :: ss -> this.InternalRun (cont error) ss

        let handleResult result stack =
            match result with
            | Ok result' -> handleSuccess result' stack
            | Error error -> handleError error stack

        match effect with
        | NonBlocking action ->
            handleResult (action ()) stack

        | Blocking channel ->
            handleSuccess (channel.Take()) stack

        | Send (message, channel) ->
            channel.Add message
            handleSuccess message stack

        | Concurrent (effect, fiber, internalFiber) ->
            async {
                internalFiber.Complete
                <| this.InternalRun effect ContinuationStack.Empty
            }
            |> Async.Start
            handleSuccess fiber stack

        | Await internalFiber ->
            handleResult (internalFiber.AwaitResult()) stack

        | ChainSuccess (effect, continuation) ->
            this.InternalRun effect ((SuccessKind, continuation) :: stack)

        | ChainError (effect, continuation) ->
            this.InternalRun effect ((ErrorKind, continuation) :: stack)

        | Success result ->
            handleSuccess result stack

        | Failure result ->
            handleError result stack

    override this.Run (effect: FIO<'R, 'E>) : Fiber<'R, 'E> =
        let fiber = new Fiber<'R, 'E>()
        async {
            fiber.ToInternal().Complete
            <| this.InternalRun (effect.Upcast()) ContinuationStack.Empty
        }
        |> Async.Start
        fiber
