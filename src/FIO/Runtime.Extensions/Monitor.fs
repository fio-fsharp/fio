(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module internal FIO.Runtime.Tools

open FIO.Core

open System.Collections.Concurrent

type internal Monitor
    (
        workItemQueue: BlockingQueue<WorkItem>,
        blockingItemQueue: Option<BlockingQueue<BlockingItem * WorkItem>>,
        blockingEventQueue: Option<BlockingQueue<Channel<obj>>>,
        blockingWorkItemMap: Option<ConcurrentDictionary<BlockingItem, BlockingQueue<WorkItem>>>
    ) as self =
    let _ =
        (async {
            while true do
                printfn "\n\n"
                self.PrintWorkItemQueueInfo workItemQueue
                printfn "\n"

                match blockingItemQueue with
                | Some queue ->
                    self.PrintBlockingItemQueueInfo queue
                    printfn "\n"
                | _ -> ()

                match blockingEventQueue with
                | Some queue ->
                    self.PrintBlockingEventQueueInfo queue
                    printfn "\n"
                | _ -> ()

                match blockingWorkItemMap with
                | Some map ->
                    self.PrintBlockingWorkItemMapInfo map
                    printfn "\n"
                | _ -> ()

                System.Threading.Thread.Sleep(1000)
         }
         |> Async.StartAsTask
         |> ignore)

    member private this.PrintWorkItemQueueInfo(queue: BlockingQueue<WorkItem>) =
        printfn $"MONITOR: workItemQueue count: %i{queue.Count}"
        printfn "MONITOR: ------------ workItemQueue information start ------------"

        for workItem in queue.ToArray() do
            let ifiber = workItem.IFiber
            printfn $"MONITOR:    ------------ workItem start ------------"
            printfn $"MONITOR:      WorkItem IFiber completed: %A{ifiber.Completed()}"
            printfn $"MONITOR:      WorkItem IFiber blocking items count: %A{ifiber.BlockingWorkItemsCount()}"
            printfn $"MONITOR:      WorkItem PrevAction: %A{workItem.PrevAction}"
            printfn $"MONITOR:      WorkItem Eff: %A{workItem.Eff}"
            printfn $"MONITOR:    ------------ workItem end ------------"

        printfn "MONITOR: ------------ workItemQueue information end ------------"

    member private this.PrintBlockingItemQueueInfo(queue: BlockingQueue<BlockingItem * WorkItem>) =
        printfn $"MONITOR: blockingItemQueue count: %i{queue.Count}"
        printfn "MONITOR: ------------ blockingItemQueue information start ------------"

        for blockingItem, workItem in queue.ToArray() do
            printfn $"MONITOR:    ------------ BlockingItem * WorkItem start ------------"

            match blockingItem with
            | BlockingChannel chan -> printfn $"MONITOR:      Blocking Channel count: %A{chan.Count}"
            | BlockingIFiber ifiber ->
                printfn $"MONITOR:      Blocking IFiber completed: %A{ifiber.Completed()}"
                printfn $"MONITOR:      Blocking IFiber blocking items count: %A{ifiber.BlockingWorkItemsCount()}"

            let ifiber = workItem.IFiber
            printfn $"MONITOR:      WorkItem IFiber completed: %A{ifiber.Completed()}"
            printfn $"MONITOR:      WorkItem IFiber blocking items count: %A{ifiber.BlockingWorkItemsCount()}"
            printfn $"MONITOR:      WorkItem PrevAction: %A{workItem.PrevAction}"
            printfn $"MONITOR:      WorkItem Eff: %A{workItem.Eff}"
            printfn $"MONITOR:    ------------ BlockingItem * WorkItem end ------------"

        printfn "MONITOR: ------------ workItemQueue information end ------------"

    member private _.PrintBlockingEventQueueInfo(queue: BlockingQueue<Channel<obj>>) =
        printfn $"MONITOR: blockingEventQueue count: %i{queue.Count}"
        printfn "MONITOR: ------------ blockingEventQueue information start ------------"

        for blockingChan in queue.ToArray() do
            printfn $"MONITOR:    ------------ blockingChan start ------------"
            printfn $"MONITOR:      Count: %A{blockingChan.Count()}"
            printfn $"MONITOR:    ------------ blockingChan end ------------"

        printfn "MONITOR: ------------ blockingEventQueue information end ------------"

    member private _.PrintBlockingWorkItemMapInfo(map: ConcurrentDictionary<BlockingItem, BlockingQueue<WorkItem>>) =
        printfn $"MONITOR: blockingWorkItemMap count: %i{map.Count}"
