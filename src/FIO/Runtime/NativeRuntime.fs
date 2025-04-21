(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module FIO.Runtime.Native

open FIO.Core

open System.Threading.Tasks

type Runtime() =
    inherit FIORuntime()

    [<TailCall>]
    member private this.InternalRunAsync eff =
        let mutable currentEff = eff
        let mutable contStack = []
        let mutable result = None

        let handleSuccess res =
            let mutable loop = true
            while loop do
                match contStack with
                | [] ->
                    result <- Some <| Ok res
                    loop <- false
                | (SuccessCont, cont) :: ss ->
                    currentEff <- cont res
                    contStack <- ss
                    loop <- false
                | (FailureCont, _) :: ss ->
                    contStack <- ss

        let handleError err =
            let mutable loop = true
            while loop do
                match contStack with
                | [] ->
                    result <- Some <| Error err
                    loop <- false
                | (SuccessCont, _) :: ss ->
                    contStack <- ss
                | (FailureCont, cont) :: ss ->
                    currentEff <- cont err
                    contStack <- ss
                    loop <- false

        let handleResult res =
            match res with
            | Ok res ->
                handleSuccess res
            | Error err ->
                handleError err

        task {
            while result.IsNone do
                match currentEff with
                | Success res ->
                    handleSuccess res
                | Failure err ->
                    handleError err
                | Action (func, onError) ->
                    try 
                        let res = func ()
                        handleSuccess res
                    with exn ->
                        handleResult (Error <| onError exn)
                | SendChan (msg, chan) ->
                    do! chan.SendAsync msg
                    handleSuccess msg
                | ReceiveChan chan ->
                    let! res = chan.ReceiveAsync()
                    handleSuccess res
                | Concurrent (eff, fiber, ifiber) ->
                    // This runs the task on a separate thread pool.
                    Task.Run(fun () ->
                        task {
                            let! res = this.InternalRunAsync eff
                            do! ifiber.Complete res
                        } :> Task
                    )
                    |> ignore
                    handleSuccess fiber
                | AwaitFiber ifiber ->
                    let! res = ifiber.AwaitAsync()
                    handleResult res
                | AwaitTask (task, onError) ->
                    try
                        let! res = task
                        handleResult <| Ok res
                    with exn ->
                        handleResult (Error <| onError exn)
                | ChainSuccess (eff, cont) ->
                    currentEff <- eff
                    contStack <- (SuccessCont, cont) :: contStack
                | ChainError (eff, cont) ->
                    currentEff <- eff
                    contStack <- (FailureCont, cont) :: contStack

            return result.Value
        }

    override this.Run (eff: FIO<'R, 'E>) : Fiber<'R, 'E> =
        let fiber = Fiber<'R, 'E>()
        task {
            let! res = this.InternalRunAsync <| eff.Upcast()
            do! fiber.ToInternal().Complete res
        } |> ignore
        fiber

    override this.Name () =
        "Native"
