(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module FIO.Library.Console

open System

open FIO.Core

let printfnf format : FIO<'R, 'E> =
    !+ printfn(format)

let printff format : FIO<'R, 'E> =
    !+ printf(format)

let sprintff format : FIO<'R, 'E> =
    !+ sprintf(format)

let readLine () : FIO<string, 'E> =
    !+ Console.ReadLine()