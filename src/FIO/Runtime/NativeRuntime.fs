(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module FIO.Runtime.Native

open FIO.Core

type Runtime() =
    inherit FIORuntime()

    member private this.InternalRun eff stack =

        let rec handleSuccess res stack =
            match stack with
            | [] -> Ok res
            | (SuccessCont, cont) :: ss -> interpret (cont res) ss
            | (FailureCont, _) :: ss -> handleSuccess res ss

        and handleError err stack =
            match stack with
            | [] -> Error err
            | (SuccessCont, _) :: ss -> handleError err ss
            | (FailureCont, cont) :: ss -> interpret (cont err) ss

        and handleResult res stack =
            match res with
            | Ok res -> handleSuccess res stack
            | Error err -> handleError err stack

        and interpret eff stack =
            match eff with
            | Success res ->
                handleSuccess res stack

            | Failure err ->
                handleError err stack

            | Concurrent (eff, fiber, ifiber) ->
                async {
                    ifiber.Complete
                    <| interpret eff ContStack.Empty
                }
                |> Async.Start
                handleSuccess fiber stack

            | Await ifiber ->
                handleResult (ifiber.AwaitResult()) stack

            | ChainSuccess (eff, cont) ->
                interpret eff ((SuccessCont, cont) :: stack)

            | ChainError (eff, cont) ->
                interpret eff ((FailureCont, cont) :: stack)

            | Send (msg, chan) ->
                chan.Add msg
                handleSuccess msg stack

            | Receive chan ->
                handleSuccess (chan.Take()) stack

        interpret eff stack

    override this.Run (eff: FIO<'R, 'E>) : Fiber<'R, 'E> =
        let fiber = new Fiber<'R, 'E>()
        async {
            fiber.ToInternal().Complete
            <| this.InternalRun (eff.Upcast()) ContStack.Empty
        }
        |> Async.Start
        fiber
