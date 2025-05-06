module DeadlockDetector

open FIO.Core

open System.Threading
open System.Collections.Concurrent

[<AbstractClass>]
type internal Worker () =
    abstract Working: unit -> bool
    
type internal DeadlockDetector<'B, 'E when 'B :> Worker and 'E :> Worker>(activeWorkItemChan: InternalChannel<WorkItem>, intervalMs: int) as this =
    let blockingItems = ConcurrentDictionary<BlockingItem, unit>()
    let mutable blockingWorkers: List<'B> = []
    let mutable evalWorkers: List<'E> = []
    let mutable countDown = 10
    
    let startMonitor () =
        task {
            while true do
                (*
                * If there's no work left in the work queue and no eval workers are working,
                * BUT there are still blocking items, then we know we have a deadlock.
                *)
                if activeWorkItemChan.Count <= 0 && this.AllEvalWorkersIdle() && blockingItems.Count > 0 then
                    if countDown <= 0 then
                        printfn "DEADLOCK_DETECTOR: ############ WARNING: Potential deadlock detected! ############"
                        printfn "DEADLOCK_DETECTOR:     Suspicion: No work items left, All EvalWorkers idling, Existing blocking items"
                    else
                        countDown <- countDown - 1
                else
                    countDown <- 10

                Thread.Sleep intervalMs
        } |> ignore

    do startMonitor ()

    member internal this.AddBlockingItem blockingItem =
        blockingItems.TryAdd (blockingItem, ())

    member internal this.RemoveBlockingItem blockingItem =
        blockingItems.TryRemove blockingItem |> ignore

    member private this.AllEvalWorkersIdle () =
        not (
            List.contains true
            <| List.map (fun (evalWorker: 'E) -> evalWorker.Working ()) evalWorkers
        )

    member private this.AllBlockingWorkersIdle () =
        not (
            List.contains true
            <| List.map (fun (evalWorker: 'B) -> evalWorker.Working ()) blockingWorkers
        )

    member internal this.SetEvalWorkers workers =
        evalWorkers <- workers

    member internal this.SetBlockingWorkers workers =
        blockingWorkers <- workers
