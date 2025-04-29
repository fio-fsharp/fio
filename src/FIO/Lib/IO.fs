(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module FIO.Lib.IO

open FIO.Core

/// Functional Console
type FConsole private =

    static member inline Print (format, onError: exn -> 'E) : FIO<unit, 'E> =
        !<<< (fun () -> printf format) onError

    static member inline Print format : FIO<unit, exn> =
        FConsole.Print (format, id)

    static member inline PrintLine (format, onError: exn -> 'E) : FIO<unit, 'E> =
        !<<< (fun () -> printfn format) onError

    static member inline PrintLine format: FIO<unit, exn> =
        FConsole.PrintLine (format, id)

    static member inline WriteLine (format, onError: exn -> 'E) : FIO<string, 'E> =
        !<<< (fun () -> sprintf format) onError

    static member inline WriteLine format : FIO<string, exn> =
        FConsole.WriteLine (format, id)

    static member inline ReadLine (onError: exn -> 'E) : FIO<string, 'E> =
        !<<< System.Console.ReadLine onError

    static member inline ReadLine () : FIO<string, exn> =
        FConsole.ReadLine id
