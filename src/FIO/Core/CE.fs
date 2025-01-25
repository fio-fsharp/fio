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

    member inline this.Bind (eff: FIO<'R1, 'E>, cont: 'R1 -> FIO<'R, 'E>) : FIO<'R, 'E> =
        eff.Bind cont

    member inline this.Combine (eff: FIO<'R, 'E>, eff': FIO<'R1, 'E>) : FIO<'R1, 'E> =
        eff.Then eff'

    member inline this.Run (eff: FIO<'R, 'E>) : FIO<'R, 'E> =
        eff

    member inline this.Zero () : FIO<unit, 'E> =
        FIO.Succeed ()

    member inline this.Return (res: 'R) : FIO<'R, 'E> =
        FIO.Succeed res

    member inline this.ReturnFrom (eff: FIO<'R, 'E>) : FIO<'R, 'E> =
        eff

    member inline this.Yield (res: 'R) : FIO<'R, 'E> =
        FIO.Succeed res

    member inline this.YieldFrom (eff: FIO<'R, 'E>) : FIO<'R, 'E> =
        eff

    member inline this.TryWith (eff: FIO<'R, 'E>, handler: 'E -> FIO<'R, 'E>) : FIO<'R, 'E> =
        eff.BindError handler

    member inline this.TryFinally (eff: FIO<'R, 'E>, finalizer: unit -> unit) : FIO<'R, 'E> =
        eff.Bind <| fun res ->
            try
                finalizer ()
                FIO.Succeed res
            with exn ->
                FIO.Fail (exn :?> 'E)

    member inline this.Delay (func: unit -> FIO<'R, 'E>) : FIO<'R, 'E> =
       (FIO.Succeed ()).Bind func

    member inline this.For (sequence: seq<'T>, body: 'T -> FIO<unit, 'E>) : FIO<unit, 'E> =
        let rec loop (enumerator: IEnumerator<'T>) =
            if enumerator.MoveNext() then
                (body enumerator.Current).Then <| loop enumerator
            else
                FIO.Succeed ()
        sequence.GetEnumerator() |> loop

    member inline this.While (guard: unit -> bool, eff: FIO<'R, 'E>) : FIO<unit, 'E> =
        let rec loop () =
            if guard () then
                this.Delay <| fun () -> eff.Bind <| fun _ -> loop ()
            else
                FIO.Succeed ()
        loop ()

    member inline this.Using (resource: #IDisposable, body: 'T -> FIO<'R, 'E>) : FIO<'R, 'E> =
        this.TryFinally (body resource, fun () -> resource.Dispose())

    member inline this.Match (value: 'T, cases: 'T -> FIO<'R, 'E>) : FIO<'R, 'E> =
        cases value

let fio = FIOBuilder()
