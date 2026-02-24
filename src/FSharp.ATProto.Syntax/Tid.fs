namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

type Tid = private Tid of string

module Tid =
    let value (Tid s) = s
    let parse (s: string) : Result<Tid, string> =
        Error "not implemented"
