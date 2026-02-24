namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

type RecordKey = private RecordKey of string

module RecordKey =
    let private pattern = Regex(@"^[a-zA-Z0-9._~:-]{1,512}$", RegexOptions.Compiled)

    let value (RecordKey s) = s

    let parse (s: string) : Result<RecordKey, string> =
        if isNull s then Error "RecordKey cannot be null"
        elif s = "." || s = ".." then Error (sprintf "RecordKey cannot be '%s'" s)
        elif not (pattern.IsMatch(s)) then Error (sprintf "Invalid RecordKey: %s" s)
        else Ok (RecordKey s)
