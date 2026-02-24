namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

type Did = private Did of string

module Did =
    let private pattern =
        Regex(@"^did:[a-z]+:[a-zA-Z0-9._:%-]*[a-zA-Z0-9._-]$", RegexOptions.Compiled)

    let value (Did s) = s

    let private hasValidPercentEncoding (s: string) =
        let mutable i = 0
        let mutable valid = true
        while valid && i < s.Length do
            if s.[i] = '%' then
                if i + 2 >= s.Length then valid <- false
                elif not (System.Uri.IsHexDigit(s.[i + 1]) && System.Uri.IsHexDigit(s.[i + 2])) then valid <- false
                else i <- i + 3
            else i <- i + 1
        valid

    let parse (s: string) : Result<Did, string> =
        if isNull s then Error "DID cannot be null"
        elif s.Length > 2048 then Error (sprintf "DID exceeds max length of 2048: %d" s.Length)
        elif not (pattern.IsMatch(s)) then Error (sprintf "Invalid DID syntax: %s" s)
        elif not (hasValidPercentEncoding s) then Error (sprintf "Invalid percent-encoding in DID: %s" s)
        else Ok (Did s)

    let method (Did s) =
        let i1 = s.IndexOf(':')
        let i2 = s.IndexOf(':', i1 + 1)
        s.Substring(i1 + 1, i2 - i1 - 1)
