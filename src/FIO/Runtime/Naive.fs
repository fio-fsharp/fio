(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(************************************************************************************)

module FIO.Runtime.Naive

open FIO.Core

type NaiveRuntime() =
    inherit Runtime()

    [<TailCall>]
    member internal this.InternalRun effect stack : Result<obj, obj> =
        let rec handleSuccess result stack =
            match stack with
            | [] -> Ok result
            | s :: ss ->
                match s with
                | SuccConts succCont -> this.InternalRun (succCont result) ss
                | ErrorConts _ -> handleSuccess result ss

        let rec handleError error stack =
            match stack with
            | [] -> Error error
            | s :: ss ->
                match s with
                | SuccConts _ -> handleError error ss
                | ErrorConts errCont -> this.InternalRun (errCont error) ss

        let handleResult result stack =
            match result with
            | Ok result -> handleSuccess result stack
            | Error error -> handleError error stack

        match effect with
        | NonBlocking action -> handleResult (action ()) stack
        | Blocking channel ->
            let result = channel.Take()
            handleSuccess result stack
        | Send(message, channel) ->
            channel.Add message
            handleSuccess message stack
        | Concurrent(effect, fiber, ifiber) ->
            async { ifiber.Complete <| this.InternalRun effect [] }
            |> Async.StartAsTask
            |> ignore

            handleSuccess fiber stack
        | Await ifiber -> handleResult (ifiber.AwaitResult()) stack
        | ChainSuccess(effect, continuation) -> this.InternalRun effect (SuccConts continuation :: stack)
        | ChainError(effect, continuation) -> this.InternalRun effect (ErrorConts continuation :: stack)
        | Success result -> handleSuccess result stack
        | Failure result -> handleError result stack

    override this.Run<'R, 'E>(effect: FIO<'R, 'E>) : Fiber<'R, 'E> =
        let fiber = new Fiber<'R, 'E>()

        async { fiber.ToInternal().Complete <| this.InternalRun (effect.Upcast()) [] }
        |> Async.StartAsTask
        |> ignore

        fiber
