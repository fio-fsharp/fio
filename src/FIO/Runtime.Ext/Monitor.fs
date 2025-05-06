(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module internal FIO.Runtime.Tools

open FIO.Core

open System.Threading

type internal Monitor (activeWorkItemChan: InternalChannel<WorkItem>, activeBlockingItemChanOpt: Option<InternalChannel<BlockingItem>>) as this =
    
    let startMonitor () =
        task {
            while true do
                printfn "\n\n"
                do! this.PrintWorkItemChanInfo activeWorkItemChan
                printfn "\n"
                
                match activeBlockingItemChanOpt with
                | Some chan ->
                    do! this.PrintBlockingItemChanInfo chan
                    printfn "\n"
                | _ -> ()

                Thread.Sleep 1000
        } |> ignore

    do startMonitor ()

    member private this.PrintWorkItemChanInfo chan =
        task {
            printfn $"MONITOR: workItemQueue count: %i{chan.Count}"
            printfn "MONITOR: ------------ workItemQueue information start ------------"

            let mutable loop = true
            while loop do
                let! hasWorkItem = chan.WaitToTakeAsync()
                if not hasWorkItem then
                    loop <- false
                else
                    let! workItem = chan.TakeAsync()
                    let ifiber = workItem.IFiber
                    printfn $"MONITOR:    ------------ workItem start ------------"
                    printfn $"MONITOR:      WorkItem IFiber completed: %b{ifiber.Completed}"
                    printfn $"MONITOR:      WorkItem IFiber blocking items count: %i{ifiber.BlockingWorkItemCount}"
                    printfn $"MONITOR:      WorkItem PrevAction: %A{workItem.PrevAction}"
                    printfn $"MONITOR:      WorkItem Eff: %A{workItem.Eff}"
                    printfn $"MONITOR:    ------------ workItem end ------------"

            printfn "MONITOR: ------------ workItemQueue information end ------------"
        }


    member private this.PrintBlockingItemChanInfo chan =
        task {
            printfn $"MONITOR: ------------ blockingItemQueue debugging information start ------------"
            printfn $"MONITOR: blockingItemQueue count: %i{chan.Count}"

            let mutable loop = true
            while loop do
                let! hasWorkItem = chan.WaitToTakeAsync()
                if not hasWorkItem then
                    loop <- false
                else
                    let! blockingItem = chan.TakeAsync()
                    match blockingItem with
                    | BlockingIFiber ifiber ->
                        printfn $"MONITOR:    ------------ blockingIFiber start ------------"
                        printfn $"MONITOR:      IFiber completed: %b{ifiber.Completed}"
                        printfn $"MONITOR:      IFiber blocking items count: %i{ifiber.BlockingWorkItemCount}"
                        printfn $"MONITOR:    ------------ blockingIFiber end ------------"
                    | BlockingChannel blockingChan ->
                        printfn $"MONITOR:    ------------ blockingChan start ------------"
                        printfn $"MONITOR:      Count: %i{blockingChan.Count}"
                        printfn $"MONITOR:    ------------ blockingChan end ------------"
                        
            printfn "MONITOR: ------------ blockingEventQueue debugging information end ------------"
        }
