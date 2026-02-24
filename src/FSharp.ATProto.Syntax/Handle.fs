namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

type Handle = private Handle of string

module Handle =
    let private pattern =
        Regex(@"^([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$", RegexOptions.Compiled)

    let value (Handle s) = s

    let parse (s: string) : Result<Handle, string> =
        if isNull s then Error "Handle cannot be null"
        elif s.Length > 253 then Error (sprintf "Handle exceeds max length of 253: %d" s.Length)
        elif not (pattern.IsMatch(s)) then Error (sprintf "Invalid handle syntax: %s" s)
        else Ok (Handle s)
