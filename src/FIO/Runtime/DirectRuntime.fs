(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module FIO.Runtime.Direct

open FIO.DSL

open System
open System.Threading
open System.Threading.Tasks

type Runtime () =
    inherit FRuntime ()

    override this.Name =
        "Direct"

    [<TailCall>]
    member private this.InterpretAsync eff =
        let mutable currentEff = eff
        let mutable contStack = []
        let mutable resultOpt = None

        let handleSuccess res =
            let mutable loop = true
            while loop do
                match contStack with
                | [] ->
                    resultOpt <- Some <| Ok res
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
                    resultOpt <- Some <| Error err
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
            while resultOpt.IsNone do
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
                        handleError
                        <| onError exn
                | SendChan (msg, chan) ->
                    do! chan.SendAsync msg
                    handleSuccess msg
                | ReceiveChan chan ->
                    let! res = chan.ReceiveAsync()
                    handleSuccess res
                | ConcurrentEffect (eff, fiber, ifiber) ->
                    // This runs the task on a separate thread pool.
                    Task.Run(fun () ->
                        task {
                            let! res = this.InterpretAsync eff
                            do! ifiber.Complete res
                        } :> Task
                    )
                    |> ignore
                    handleSuccess fiber
                | ConcurrentTPLTask (task, onError, fiber, ifiber) ->
                    Task.Run(fun () ->
                        (task ()).ContinueWith((fun (t: Task<obj>) ->
                            if t.IsFaulted then
                                ifiber.Complete
                                <| Error (onError t.Exception.InnerException)
                            elif t.IsCanceled then
                                ifiber.Complete
                                <| Error (onError <| TaskCanceledException "Task has been cancelled.")
                            elif t.IsCompleted then
                                ifiber.Complete
                                <| Ok t.Result
                            else
                                ifiber.Complete
                                <| Error (onError <| InvalidOperationException "Task not completed.")),
                            CancellationToken.None,
                            TaskContinuationOptions.RunContinuationsAsynchronously,
                            TaskScheduler.Default) :> Task
                    ) |> ignore
                    handleSuccess fiber
                | AwaitFiber ifiber ->
                    let! res = ifiber.AwaitAsync()
                    handleResult res
                | AwaitGenericTPLTask (task, onError) ->
                    try
                        let! res = task
                        handleSuccess res
                    with exn ->
                        handleError <| onError exn
                | ChainSuccess (eff, cont) ->
                    currentEff <- eff
                    contStack <- (SuccessCont, cont) :: contStack
                | ChainError (eff, cont) ->
                    currentEff <- eff
                    contStack <- (FailureCont, cont) :: contStack

            return resultOpt.Value
        }

    override this.Run<'R, 'E> (eff: FIO<'R, 'E>) : Fiber<'R, 'E> =
        let fiber = Fiber<'R, 'E>()
        task {
            let! res = this.InterpretAsync <| eff.Upcast()
            do! fiber.Internal.Complete res
        } |> ignore
        fiber
