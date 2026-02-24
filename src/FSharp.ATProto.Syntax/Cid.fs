namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

type Cid = private Cid of string

module Cid =
    let private pattern = Regex(@"^[a-zA-Z0-9+/=]{8,256}$", RegexOptions.Compiled)

    let value (Cid s) = s

    let parse (s: string) : Result<Cid, string> =
        if isNull s then Error "CID cannot be null"
        elif s.StartsWith("Qmb") then Error "CIDv0 is not supported"
        elif not (pattern.IsMatch(s)) then Error (sprintf "Invalid CID syntax: %s" s)
        else Ok (Cid s)

    let internal fromValidated (s: string) = Cid s
