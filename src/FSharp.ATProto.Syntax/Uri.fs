namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

type Uri = private Uri of string

module Uri =
    // General URI validation based on RFC 3986 basics.
    // From test data:
    // - Must have a scheme (letters, digits, +, -, .) starting with a letter
    // - Scheme is followed by ":"
    // - Must have something after the scheme+colon
    // - No whitespace anywhere in the URI
    // - No leading/trailing whitespace
    // - Max 8KB
    // Valid schemes seen: https, dns, at, did, content-type, microsoft.windows.camera, go
    // Note: "http:" alone is invalid (nothing after colon)
    // Note: scheme must start with a letter (not . or -)
    let private pattern =
        Regex(@"^[a-zA-Z][a-zA-Z0-9+\-.]*:[^\s]+$", RegexOptions.Compiled)

    let value (Uri s) = s

    let parse (s: string) : Result<Uri, string> =
        if isNull s then Error "URI cannot be null"
        elif s.Length > 8192 then Error "URI exceeds max length of 8KB"
        elif not (pattern.IsMatch(s)) then Error (sprintf "Invalid URI: %s" s)
        else Ok (Uri s)
