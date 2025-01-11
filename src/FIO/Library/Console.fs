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