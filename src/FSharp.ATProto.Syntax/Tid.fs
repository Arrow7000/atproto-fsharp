namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

type Tid = private Tid of string

module Tid =
    let private pattern = Regex(@"^[234567abcdefghij][234567abcdefghijklmnopqrstuvwxyz]{12}$", RegexOptions.Compiled)

    let value (Tid s) = s

    let parse (s: string) : Result<Tid, string> =
        if isNull s then
            Error "TID cannot be null"
        elif s.Length <> 13 then
            Error (sprintf "TID must be exactly 13 characters, got %d" s.Length)
        elif not (pattern.IsMatch(s)) then
            Error (sprintf "Invalid TID: %s" s)
        else
            Ok (Tid s)
