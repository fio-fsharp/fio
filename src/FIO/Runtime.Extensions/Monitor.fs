(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module internal FIO.Runtime.Tools

open FIO.Core

open System.Threading
(*
type internal Monitor internal (
    workItemQueue: BlockingQueue<WorkItem>,
    blockingItemQueue: Option<BlockingQueue<BlockingItem>>
) as this =
    let _ =
        (async {
            while true do
                printfn "\n\n"
                this.PrintWorkItemQueueInfo workItemQueue
                printfn "\n"


                match blockingItemQueue with
                | Some queue ->
                    this.PrintBlockingItemQueueInfo queue
                    printfn "\n"
                | _ -> ()

                Thread.Sleep(1000)
         }
         |> Async.StartAsTask
         |> ignore)

    member private this.PrintWorkItemQueueInfo(queue: BlockingQueue<WorkItem>) =
        printfn $"MONITOR: workItemQueue count: %i{queue.Count}"
        printfn "MONITOR: ------------ workItemQueue information start ------------"

        for workItem in queue.ToArray() do
            let ifiber = workItem.IFiber
            printfn $"MONITOR:    ------------ workItem start ------------"
            printfn $"MONITOR:      WorkItem IFiber completed: %b{ifiber.Completed()}"
            printfn $"MONITOR:      WorkItem IFiber blocking items count: %i{ifiber.BlockingWorkItemsCount()}"
            printfn $"MONITOR:      WorkItem PrevAction: %A{workItem.PrevAction}"
            printfn $"MONITOR:      WorkItem Eff: %A{workItem.Eff}"
            printfn $"MONITOR:    ------------ workItem end ------------"

        printfn "MONITOR: ------------ workItemQueue information end ------------"


    member private _.PrintBlockingItemQueueInfo(queue: BlockingQueue<BlockingItem>) =
        printfn $"MONITOR: ------------ blockingItemQueue debugging information start ------------"
        printfn $"MONITOR: blockingItemQueue count: %i{queue.Count}"

        for blockingItem in queue.ToArray() do
            match blockingItem with
            | BlockingIFiber ifiber ->
                printfn $"MONITOR:    ------------ blockingIFiber start ------------"
                printfn $"MONITOR:      IFiber completed: %b{ifiber.Completed()}"
                printfn $"MONITOR:      IFiber blocking items count: %i{ifiber.BlockingWorkItemsCount()}"
                printfn $"MONITOR:    ------------ blockingIFiber end ------------"
            | BlockingChannel blockingChan ->
                printfn $"MONITOR:    ------------ blockingChan start ------------"
                printfn $"MONITOR:      Count: %i{blockingChan.Count()}"
                printfn $"MONITOR:    ------------ blockingChan end ------------"
            | BlockingTask blockingTask ->
                printfn $"MONITOR:    ------------ blockingTask start ------------"
                printfn $"MONITOR:      Id: %i{blockingTask.Task().Id}"
                printfn $"MONITOR:      Name: %s{blockingTask.Name}"
                printfn $"MONITOR:      IsCanceled: %b{blockingTask.Task().IsCanceled}"
                printfn $"MONITOR:      IsCompleted: %b{blockingTask.Task().IsCompleted}"
                printfn $"MONITOR:      IsCompletedSuccessfully: %b{blockingTask.Task().IsCompletedSuccessfully}"
                printfn $"MONITOR:      IsFaulted: %b{blockingTask.Task().IsFaulted}"
                printfn $"MONITOR:      Status: %A{blockingTask.Task().Status}"
                printfn $"MONITOR:    ------------ blockingTask end ------------"

        printfn "MONITOR: ------------ blockingEventQueue debugging information end ------------"
*)