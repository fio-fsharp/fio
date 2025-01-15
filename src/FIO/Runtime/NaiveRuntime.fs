(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module FIO.Runtime.Naive

open FIO.Core

type Runtime() =
    inherit FIORuntime()

    member private this.InternalRun (effect: FIO<obj, obj>) (stack: ContinuationStack) : Result<obj, obj> =

        let rec handleSuccess result stack =
            match stack with
            | [] -> Ok result
            | (SuccessKind, cont) :: ss -> interpret (cont result) ss
            | (ErrorKind, _) :: ss -> handleSuccess result ss

        and handleError error stack =
            match stack with
            | [] -> Error error
            | (SuccessKind, _) :: ss -> handleError error ss
            | (ErrorKind, cont) :: ss -> interpret (cont error) ss

        and handleResult result stack =
            match result with
            | Ok result' -> handleSuccess result' stack
            | Error error -> handleError error stack

        and interpret effect' stack' =
            match effect' with
            | Send (message, channel) ->
                channel.Add message
                handleSuccess message stack'

            | Receive channel ->
                handleSuccess (channel.Take()) stack'

            | Concurrent (effect, fiber, internalFiber) ->
                async {
                    internalFiber.Complete
                    <| interpret effect ContinuationStack.Empty
                }
                |> Async.Start
                handleSuccess fiber stack'

            | Await internalFiber ->
                handleResult (internalFiber.AwaitResult()) stack'

            | ChainSuccess (effect, continuation) ->
                interpret effect ((SuccessKind, continuation) :: stack')

            | ChainError (effect, continuation) ->
                interpret effect ((ErrorKind, continuation) :: stack')

            | Success result ->
                handleSuccess result stack'

            | Failure error ->
                handleError error stack'

        interpret effect stack

    override this.Run (effect: FIO<'R, 'E>) : Fiber<'R, 'E> =
        let fiber = new Fiber<'R, 'E>()
        async {
            fiber.ToInternal().Complete
            <| this.InternalRun (effect.Upcast()) ContinuationStack.Empty
        }
        |> Async.Start
        fiber
