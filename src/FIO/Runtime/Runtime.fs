(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(************************************************************************************)

namespace FIO.Runtime

open FIO.Core

[<AbstractClass>]
type Runtime() =
    abstract member Run : FIO<'R, 'E> -> Fiber<'R, 'E>
