namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

type AtDateTime = private AtDateTime of string

module AtDateTime =
    // ATProto datetime: strict subset of RFC 3339 / ISO 8601
    // Format: YYYY-MM-DDTHH:MM:SS[.fractional]Z or YYYY-MM-DDTHH:MM:SS[.fractional]+HH:MM/-HH:MM
    // Requirements from test data:
    // - Exactly 4-digit year (no 5-digit, no leading +/-)
    // - Uppercase T separator (no space, no underscore, no lowercase t)
    // - Uppercase Z for UTC (no lowercase z)
    // - Seconds are required (no HH:MMZ)
    // - If fractional seconds present, must have at least one digit after dot
    // - Timezone offset: +HH:MM or -HH:MM (not -00:00, not +0000, not +00)
    // - No leading/trailing whitespace
    let private pattern =
        Regex(
            @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$",
            RegexOptions.Compiled
        )

    let value (AtDateTime s) = s

    let parse (s: string) : Result<AtDateTime, string> =
        if isNull s then Error "DateTime cannot be null"
        elif not (pattern.IsMatch(s)) then Error (sprintf "Invalid ATProto datetime: %s" s)
        elif s.EndsWith("-00:00") then Error (sprintf "Invalid ATProto datetime: -00:00 offset not allowed: %s" s)
        else Ok (AtDateTime s)
