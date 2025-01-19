module DeadlockDetector

open FIO.Core

open System.Collections.Concurrent

[<AbstractClass>]
type internal Worker() =
    abstract Working: unit -> bool

type internal DeadlockDetector<'B, 'E when 'B :> Worker and 'E :> Worker>(workItemQueue: InternalQueue<WorkItem>, intervalMs: int) as self =
    let blockingItems = ConcurrentDictionary<BlockingItem, Unit>()
    let mutable blockingWorkers: List<'B> = []
    let mutable evalWorkers: List<'E> = []
    let mutable countDown = 10

    let _ =
        (async {
            while true do
                (*
                * If there's no work left in the work queue and no eval workers are working,
                * BUT there are still blocking items, then we know we have a deadlock.
                *)
                if workItemQueue.Count <= 0 && self.AllEvalWorkersIdle() && blockingItems.Count > 0 then
                    if countDown <= 0 then
                        printfn "DEADLOCK_DETECTOR: ############ WARNING: Potential deadlock detected! ############"
                        printfn "DEADLOCK_DETECTOR:     Suspicion: No work items left, All EvalWorkers idling, Existing blocking items"
                    else
                        countDown <- countDown - 1
                else
                    countDown <- 10

                System.Threading.Thread.Sleep(intervalMs)
         }
         |> Async.StartAsTask
         |> ignore)

    member internal _.AddBlockingItem blockingItem =
        blockingItems.TryAdd(blockingItem, ()) |> ignore

    member internal _.RemoveBlockingItem(blockingItem: BlockingItem) =
        blockingItems.TryRemove blockingItem |> ignore

    member private _.AllEvalWorkersIdle() =
        not (
            List.contains true
            <| List.map (fun (evalWorker: 'E) -> evalWorker.Working()) evalWorkers
        )

    member private _.AllBlockingWorkersIdle() =
        not (
            List.contains true
            <| List.map (fun (evalWorker: 'B) -> evalWorker.Working()) blockingWorkers
        )

    member internal _.SetEvalWorkers workers = evalWorkers <- workers

    member internal _.SetBlockingWorkers workers = blockingWorkers <- workers