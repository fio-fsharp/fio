(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

[<AutoOpen>]
module FIO.Core.CE

open System
open System.Collections.Generic

type FIOBuilder internal () =

    member inline this.Bind<'R, 'R1, 'E> (eff: FIO<'R, 'E>, cont: 'R -> FIO<'R1, 'E>) : FIO<'R1, 'E> =
        eff.Bind cont
        
    member inline this.BindReturn<'R, 'R1, 'E> (eff: FIO<'R, 'E>, cont: 'R -> 'R1) : FIO<'R1, 'E> =
        eff.Map cont

    member inline this.Combine<'R, 'R1, 'E> (eff: FIO<'R, 'E>, eff': FIO<'R1, 'E>) : FIO<'R1, 'E> =
        eff.Then eff'

    member inline this.Run<'R, 'E> (eff: FIO<'R, 'E>) : FIO<'R, 'E> =
        eff

    member inline this.Zero<'E> () : FIO<unit, 'E> =
        FIO.Succeed ()

    member inline this.Return<'R, 'E> (res: 'R) : FIO<'R, 'E> =
        FIO.Succeed res

    member inline this.ReturnFrom<'R, 'E> (eff: FIO<'R, 'E>) : FIO<'R, 'E> =
        eff

    member inline this.Yield<'R, 'E> (res: 'R) : FIO<'R, 'E> =
        this.Return res

    member inline this.YieldFrom<'R, 'E> (eff: FIO<'R, 'E>) : FIO<'R, 'E> =
        this.ReturnFrom eff

    member inline this.TryWith<'R, 'E, 'E1> (eff: FIO<'R, 'E>, cont: 'E -> FIO<'R, 'E1>) : FIO<'R, 'E1> =
        eff.BindError cont

    member inline this.TryFinally<'R, 'E when 'E :> exn> (eff: FIO<'R, 'E>, finalizer: unit -> unit) : FIO<'R, 'E> =
        eff.Bind <| fun res ->
            try
                finalizer ()
                this.Return res
            with exn ->
                FIO.Fail (exn :?> 'E)

    member inline this.Delay<'R1, 'E> (cont: unit -> FIO<'R1, 'E>) : FIO<'R1, 'E> =
       this.Zero().Bind cont

    member inline this.For<'T, 'E when 'E :> exn> (sequence: seq<'T>, body: 'T -> FIO<unit, 'E>) : FIO<unit, 'E> =
        let rec loop (enumerator: IEnumerator<'T>) =
            if enumerator.MoveNext () then
                this.Delay <| fun () -> 
                    body(enumerator.Current).Then(loop enumerator)
            else
                try
                    enumerator.Dispose ()
                    this.Zero ()
                with exn ->
                    FIO.Fail (exn :?> 'E)
                
        sequence.GetEnumerator()
        |> loop

    member inline this.While<'R, 'E> (guard: unit -> bool, body: FIO<'R, 'E>) : FIO<unit, 'E> =
        let rec loop () =
            if guard () then
                this.Delay <| fun () -> body.Bind <| fun _ -> loop ()
            else
                this.Zero ()
        loop ()

    member inline this.Using (resource: #IDisposable, body: 'T -> FIO<'R, 'E>) : FIO<'R, 'E> =
        this.TryFinally (body resource, fun () -> resource.Dispose())

    member inline this.Match<'R, 'E, 'T> (value: 'T, cases: 'T -> FIO<'R, 'E>) : FIO<'R, 'E> =
        cases value

    member inline this.MergeSources<'R, 'R1, 'E> (eff: FIO<'R, 'E>, eff': FIO<'R1, 'E>) : FIO<'R * 'R1, 'E> =
        eff.Zip eff'

/// The FIO computation expression builder.
let fio = FIOBuilder()
