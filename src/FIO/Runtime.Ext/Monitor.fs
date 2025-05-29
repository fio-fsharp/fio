(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module internal FIO.Runtime.Tools

open FIO.DSL

open System.Threading

// TODO: This implementation is currently wrong. It currently takes data from the channels, which is not correct.
// Rather, it should take a copy of the current contents of the channels, without removing anything.
// Perhaps the channel could hold a buffer when compilation directives are present.
type internal Monitor (
    activeWorkItemChan: InternalChannel<WorkItem>,
    activeBlockingDataChanOpt: InternalChannel<BlockingData> option,
    activeBlockingEventChan: InternalChannel<Channel<obj>> option) as this =
    
    let startMonitor () =
        task {
            while true do
                printfn "\n\n"
                do! this.PrintActiveWorkItemChanInfo activeWorkItemChan
                printfn "\n"
                
                match activeBlockingDataChanOpt with
                | Some chan ->
                    do! this.PrintActiveBlockingDataChanInfo chan
                    printfn "\n"
                | _ -> ()
                
                match activeBlockingEventChan with
                | Some chan ->
                    do! this.PrintBlockingEventChanInfo chan
                    printfn "\n"
                | _ -> ()

                Thread.Sleep 1000
        } |> ignore

    do startMonitor ()

    member private _.PrintActiveWorkItemChanInfo chan =
        task {
            printfn "MONITOR: ------------ activeWorkItemChan monitoring information start ------------"
            printfn $"MONITOR: activeWorkItemChan count: %i{chan.Count}"

            let mutable loop = true
            while loop do
                let! hasWorkItem = chan.WaitToTakeAsync()
                if not hasWorkItem then
                    loop <- false
                else
                    let! workItem = chan.TakeAsync()
                    printfn $"MONITOR:    ------------ workItem start ------------"
                    printfn $"MONITOR:      WorkItem IFiber Id: %A{workItem.IFiber.Id}"
                    printfn $"MONITOR:      WorkItem IFiber count: %i{workItem.IFiber.Count}"
                    printfn $"MONITOR:      WorkItem IFiber completed: %b{workItem.IFiber.Completed}"
                    printfn $"MONITOR:      WorkItem IFiber blocking items count: %i{workItem.IFiber.BlockingWorkItemCount}"
                    printfn $"MONITOR:      WorkItem PrevAction: %A{workItem.PrevAction}"
                    printfn $"MONITOR:      WorkItem Eff: %A{workItem.Eff}"
                    printfn $"MONITOR:    ------------ workItem end ------------"

            printfn "MONITOR: ------------ activeWorkItemChan monitoring information end ------------"
        }
        
    member private _.PrintActiveBlockingDataChanInfo chan =
        task {
            printfn "MONITOR: ------------ activeBlockingDataChan monitoring information start ------------"
            printfn $"MONITOR: activeBlockingDataChan count: %i{chan.Count}"

            let mutable loop = true
            while loop do
                let! hasWorkItem = chan.WaitToTakeAsync()
                if not hasWorkItem then
                    loop <- false
                else
                    // BlockingItem
                    // and WaitingWorkItem
                    let! blockingData = chan.TakeAsync()
                    printfn $"MONITOR:    ------------ waitingWorkItem start ------------"
                    printfn $"MONITOR:      WorkItem IFiber Id: %A{blockingData.WaitingWorkItem.IFiber.Id}"
                    printfn $"MONITOR:      WorkItem IFiber count: %i{blockingData.WaitingWorkItem.IFiber.Count}"
                    printfn $"MONITOR:      WorkItem IFiber completed: %b{blockingData.WaitingWorkItem.IFiber.Completed}"
                    printfn $"MONITOR:      WorkItem IFiber blocking items count: %i{blockingData.WaitingWorkItem.IFiber.BlockingWorkItemCount}"
                    printfn $"MONITOR:      WorkItem PrevAction: %A{blockingData.WaitingWorkItem.PrevAction}"
                    printfn $"MONITOR:      WorkItem Eff: %A{blockingData.WaitingWorkItem.Eff}"
                    printfn $"MONITOR:    ------------ waitingWorkItem end ------------"
                    
                    match blockingData.BlockingItem with
                    | BlockingChannel chan ->
                        printfn $"MONITOR:    ------------ blockingChan start ------------"
                        printfn $"MONITOR:      Id: %%{chan.Id}"
                        printfn $"MONITOR:      Count: %i{chan.Count}"
                        printfn $"MONITOR:      BlockingWorkItemCount: %i{chan.BlockingWorkItemCount}"
                        printfn $"MONITOR:    ------------ blockingChan end ------------"
                    | BlockingIFiber ifiber ->
                        printfn $"MONITOR:    ------------ blockingIFiber start ------------"
                        printfn $"MONITOR:      IFiber Id: %A{ifiber.Id}"
                        printfn $"MONITOR:      IFiber count: %i{ifiber.Count}"
                        printfn $"MONITOR:      IFiber completed: %b{ifiber.Completed}"
                        printfn $"MONITOR:      IFiber blocking items count: %i{ifiber.BlockingWorkItemCount}"
                        printfn $"MONITOR:    ------------ blockingIFiber end ------------"

            printfn "MONITOR: ------------ activeBlockingDataChan monitoring information end ------------"
        }

    member private _.PrintBlockingEventChanInfo chan =
        task {
            printfn $"MONITOR: ------------ activeBlockingEventChan monitoring information start ------------"
            printfn $"MONITOR: activeBlockingEventChan count: %i{chan.Count}"

            let mutable loop = true
            while loop do
                let! hasWorkItem = chan.WaitToTakeAsync()
                if not hasWorkItem then
                    loop <- false
                else
                    let! eventChan = chan.TakeAsync()
                    printfn $"MONITOR:    ------------ eventChan start ------------"
                    printfn $"MONITOR:      Id: %%{eventChan.Id}"
                    printfn $"MONITOR:      Count: %i{eventChan.Count}"
                    printfn $"MONITOR:      BlockingWorkItemCount: %i{eventChan.BlockingWorkItemCount}"
                    printfn $"MONITOR:    ------------ eventChan end ------------"
                        
            printfn "MONITOR: ------------ activeBlockingEventChan monitoring information end ------------"
        }
