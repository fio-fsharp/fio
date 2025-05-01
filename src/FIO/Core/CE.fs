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
        
    member inline this.BindReturn (eff: FIO<'R1, 'E>, cont: 'R1 -> 'R) : FIO<'R, 'E> =
        eff.Map cont

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
        this.Return res

    member inline this.YieldFrom (eff: FIO<'R, 'E>) : FIO<'R, 'E> =
        this.ReturnFrom eff

    member inline this.TryWith (eff: FIO<'R, 'E>, cont: 'E -> FIO<'R, 'E>) : FIO<'R, 'E> =
        eff.BindError cont

    member inline this.TryFinally (eff: FIO<'R, 'E>, finalizer: unit -> unit) : FIO<'R, 'E> =
        eff.Bind <| fun res ->
            try
                finalizer ()
                this.Return res
            with exn ->
                FIO.Fail (exn :?> 'E)

    member inline this.Delay (cont: unit -> FIO<'R, 'E>) : FIO<'R, 'E> =
       this.Zero().Bind cont

    member inline this.For (sequence: seq<'T>, body: 'T -> FIO<unit, 'E>) : FIO<unit, 'E> =
        let rec loop (enumerator: IEnumerator<'T>) =
            if enumerator.MoveNext() then
                this.Delay <| fun () -> 
                    body(enumerator.Current).Then(loop enumerator)
            else
                try
                    enumerator.Dispose()
                    this.Zero()
                with exn ->
                    FIO.Fail (exn :?> 'E)
                
        sequence.GetEnumerator()
        |> loop

    member inline this.While (guard: unit -> bool, body: FIO<'R, 'E>) : FIO<unit, 'E> =
        let rec loop () =
            if guard () then
                this.Delay <| fun () -> body.Bind <| fun _ -> loop ()
            else
                this.Zero()
        loop ()

    member inline this.Using (resource: #IDisposable, body: 'T -> FIO<'R, 'E>) : FIO<'R, 'E> =
        this.TryFinally (body resource, fun () -> resource.Dispose())

    member inline this.Match (value: 'T, cases: 'T -> FIO<'R, 'E>) : FIO<'R, 'E> =
        cases value

    member inline this.MergeSources (eff: FIO<'R, 'E>, eff': FIO<'R1, 'E>) : FIO<'R * 'R1, 'E> =
        eff.Zip eff'

/// The FIO computation expression builder.
let fio = FIOBuilder()
