(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module FIO.Library.Console

open FIO.Core

open System

let write format : FIO<'R, 'E> =
    FIO.Succeed <| printf format

let writeln format : FIO<'R, 'E> =
    FIO.Succeed <| printfn format

let writestr format : FIO<'R, 'E> =
    FIO.Succeed <| sprintf format

let readln () : FIO<string, 'E> =
    FIO.Succeed <| Console.ReadLine()
