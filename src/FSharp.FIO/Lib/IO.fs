(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module FSharp.FIO.Lib.IO

open FSharp.FIO.DSL

/// Functional Console
type FConsole private =

    static member inline Print<'E> (format, onError: exn -> 'E) : FIO<unit, 'E> =
        !<<< (fun () -> printf format) onError

    static member inline Print format : FIO<unit, exn> =
        FConsole.Print<exn> (format, id)

    static member inline PrintLine<'E> (format, onError: exn -> 'E) : FIO<unit, 'E> =
        !<<< (fun () -> printfn format) onError

    static member inline PrintLine format : FIO<unit, exn> =
        FConsole.PrintLine<exn> (format, id)

    static member inline WriteLine<'E> (format, onError: exn -> 'E) : FIO<string, 'E> =
        !<<< (fun () -> sprintf format) onError

    static member inline WriteLine format : FIO<string, exn> =
        FConsole.WriteLine<exn> (format, id)

    static member inline ReadLine<'E> (onError: exn -> 'E) : FIO<string, 'E> =
        !<<< System.Console.ReadLine onError

    static member inline ReadLine () : FIO<string, exn> =
        FConsole.ReadLine<exn> id
