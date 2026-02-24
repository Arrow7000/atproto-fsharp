namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

type Nsid = private Nsid of string

module Nsid =
    let private pattern =
        Regex(@"^[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)+(\.[a-zA-Z]([a-zA-Z0-9]{0,62})?)$", RegexOptions.Compiled)

    let value (Nsid s) = s

    let parse (s: string) : Result<Nsid, string> =
        if isNull s then Error "NSID cannot be null"
        elif s.Length > 317 then Error (sprintf "NSID exceeds max length of 317: %d" s.Length)
        elif not (pattern.IsMatch(s)) then Error (sprintf "Invalid NSID syntax: %s" s)
        else Ok (Nsid s)

    let authority (Nsid s) = let i = s.LastIndexOf('.') in s.Substring(0, i)
    let name (Nsid s) = let i = s.LastIndexOf('.') in s.Substring(i + 1)
