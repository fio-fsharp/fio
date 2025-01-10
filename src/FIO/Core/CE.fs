(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(************************************************************************************)

[<AutoOpen>]
module FIO.Core.CE

module internal FIOBuilderHelper =

    let inline internal Bind(effect: FIO<'R1, 'E>) (continuation: 'R1 -> FIO<'R, 'E>) : FIO<'R, 'E> =
        effect >>= continuation

    let inline internal Return(result: 'R) : FIO<'R, 'E> =
        !+ result

    let inline internal ReturnFrom(effect: FIO<'R, 'E>) : FIO<'R, 'E> =
        effect

    let inline internal Yield(result: 'R) : FIO<'R, 'E> =
        Return result

    let inline internal YieldFrom(effect: FIO<'R, 'E>) : FIO<'R, 'E> =
        ReturnFrom effect

    let inline internal Combine(firstEffect: FIO<'R, 'E>) (secondEffect: FIO<'R1, 'E>) : FIO<'R1, 'E> =
        firstEffect >> secondEffect

    let inline internal Zero() : FIO<unit, 'E> =
        !+ ()

    let inline internal Delay(factory: unit -> FIO<'R, 'E>) : FIO<'R, 'E> =
        NonBlocking (fun () -> Ok()) >>= fun _ -> factory ()

    let inline internal Run(effect: FIO<'R, 'E>) : FIO<'R, 'E> =
        effect

    let inline internal TryWith(effect: FIO<'R, 'E>) (handler: 'E -> FIO<'R, 'E>) : FIO<'R, 'E> =
        effect >>? handler

    let inline internal TryFinally(effect: FIO<'R, 'E>) (finalizer: unit -> unit) : FIO<'R, 'E> =
        effect >>= fun result ->
            try
                finalizer ()
                !+ result
            with exn ->
                !- (exn :?> 'E)

    let inline internal While(guard: unit -> bool) (effect: FIO<'R, 'E>) : FIO<unit, 'E> =
        let rec loop () =
            if guard () then
                Delay <| fun () -> effect >> loop ()
            else
                !+ ()
        loop ()

type FIOBuilder() =

    member this.Bind(effect: FIO<'R1, 'E>, continuation: 'R1 -> FIO<'R, 'E>) : FIO<'R, 'E> =
        FIOBuilderHelper.Bind effect continuation

    member this.Return(result: 'R) : FIO<'R, 'E> =
        FIOBuilderHelper.Return result

    member this.ReturnFrom(effect: FIO<'R, 'E>) : FIO<'R, 'E> =
        FIOBuilderHelper.ReturnFrom effect

    member this.Yield(result: 'R) : FIO<'R, 'E> =
        FIOBuilderHelper.Yield result

    member this.YieldFrom(effect: FIO<'R, 'E>) : FIO<'R, 'E> =
        FIOBuilderHelper.YieldFrom effect

    member this.Combine(firstEffect: FIO<'R, 'E>, secondEffect: FIO<'R1, 'E>) : FIO<'R1, 'E> = 
        FIOBuilderHelper.Combine firstEffect secondEffect

    member this.Zero() : FIO<unit, 'E> =
        FIOBuilderHelper.Zero()

    member this.Delay(factory: unit -> FIO<'R, 'E>) : FIO<'R, 'E> =
        FIOBuilderHelper.Delay factory

    member this.Run(effect: FIO<'R, 'E>) : FIO<'R, 'E> =
        FIOBuilderHelper.Run effect

    member this.TryWith(effect: FIO<'R, 'E>, handler: 'E -> FIO<'R, 'E>) : FIO<'R, 'E> = 
        FIOBuilderHelper.TryWith effect handler

    member this.TryFinally(effect: FIO<'R, 'E>, finalizer: unit -> unit) : FIO<'R, 'E> =
        FIOBuilderHelper.TryFinally effect finalizer

    member this.While(guard: unit -> bool, effect: FIO<'R, 'E>) : FIO<unit, 'E> =
        FIOBuilderHelper.While guard effect

let fio = FIOBuilder()
