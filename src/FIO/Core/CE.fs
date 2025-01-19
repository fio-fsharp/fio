(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

[<AutoOpen>]
module FIO.Core.CE

open System
open System.Collections.Generic

type FIOBuilder() =

    member inline this.Bind(effect: FIO<'R1, 'E>, continuation: 'R1 -> FIO<'R, 'E>) : FIO<'R, 'E> =
        effect.Bind continuation

    member inline this.Combine(effect: FIO<'R, 'E>, effect': FIO<'R1, 'E>) : FIO<'R1, 'E> = 
        effect.Then effect'

    member inline this.Run(effect: FIO<'R, 'E>) : FIO<'R, 'E> =
        effect

    member inline this.Zero() : FIO<unit, 'E> =
        succeed ()

    member inline this.Return(result: 'R) : FIO<'R, 'E> =
        succeed result

    member inline this.ReturnFrom(effect: FIO<'R, 'E>) : FIO<'R, 'E> =
        effect

    member inline this.Yield(result: 'R) : FIO<'R, 'E> =
        succeed result

    member inline this.YieldFrom(effect: FIO<'R, 'E>) : FIO<'R, 'E> =
        effect

    member inline this.TryWith(effect: FIO<'R, 'E>, handler: 'E -> FIO<'R, 'E>) : FIO<'R, 'E> = 
        effect.BindError handler

    member inline this.TryFinally(effect: FIO<'R, 'E>, finalizer: unit -> unit) : FIO<'R, 'E> =
        effect.Bind <| fun result ->
            try
                finalizer ()
                succeed result
            with exn ->
                fail (exn :?> 'E)

    member inline this.Delay(func: unit -> FIO<'R, 'E>) : FIO<'R, 'E> =
        (succeed ()).Bind func

    member inline this.For(sequence: seq<'T>, body: 'T -> FIO<unit, 'E>) : FIO<unit, 'E> =
        let rec loop (enumerator: IEnumerator<'T>) =
            if enumerator.MoveNext() then
                (body enumerator.Current).Then <| loop enumerator
            else
                succeed ()
        sequence.GetEnumerator() |> loop

    member inline this.While(guard: unit -> bool, effect: FIO<'R, 'E>) : FIO<unit, 'E> =
        let rec loop () =
            if guard () then
                this.Delay <| fun () -> effect.Bind <| fun _ -> loop ()
            else
                succeed ()
        loop ()

    member inline this.Using(resource: #IDisposable, body: 'T -> FIO<'R, 'E>) : FIO<'R, 'E> =
        this.TryFinally(body resource, fun () -> resource.Dispose())

    member inline this.Match(value: 'T, cases: 'T -> FIO<'R, 'E>) : FIO<'R, 'E> =
        cases value

let fio = FIOBuilder()
