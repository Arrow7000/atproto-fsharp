namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

/// <summary>
/// An AT Protocol datetime string, a strict subset of RFC 3339 / ISO 8601.
/// Format: <c>YYYY-MM-DDTHH:MM:SS[.fractional](Z|+HH:MM|-HH:MM)</c>.
/// </summary>
/// <remarks>
/// AT Protocol datetimes have stricter requirements than general RFC 3339:
/// exactly 4-digit year, uppercase <c>T</c> separator, uppercase <c>Z</c> for UTC,
/// seconds are required, and the offset <c>-00:00</c> is not allowed.
/// See the AT Protocol specification: https://atproto.com/specs/lexicon#datetime
/// </remarks>
type AtDateTime = private AtDateTime of string

/// <summary>
/// Functions for creating, validating, and extracting data from <see cref="AtDateTime"/> values.
/// </summary>
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

    /// <summary>
    /// Extract the string representation of an AT Protocol datetime.
    /// </summary>
    /// <param name="dateTime">The datetime to extract the value from.</param>
    /// <returns>The datetime string in RFC 3339 format (e.g. <c>"2023-11-23T12:34:56.789Z"</c>).</returns>
    let value (AtDateTime s) = s

    /// <summary>
    /// Parse and validate an AT Protocol datetime string.
    /// </summary>
    /// <param name="s">
    /// A datetime string in the format <c>YYYY-MM-DDTHH:MM:SS[.fractional](Z|+HH:MM|-HH:MM)</c>.
    /// Must use uppercase <c>T</c> separator and uppercase <c>Z</c> for UTC.
    /// Seconds are required. The offset <c>-00:00</c> is not allowed.
    /// </param>
    /// <returns>
    /// <c>Ok</c> with a validated <see cref="AtDateTime"/>, or <c>Error</c> with a message describing the validation failure.
    /// Validation failures include: null input, invalid format, or use of the prohibited <c>-00:00</c> offset.
    /// </returns>
    let parse (s: string) : Result<AtDateTime, string> =
        if isNull s then Error "DateTime cannot be null"
        elif not (pattern.IsMatch(s)) then Error (sprintf "Invalid ATProto datetime: %s" s)
        elif s.EndsWith("-00:00") then Error (sprintf "Invalid ATProto datetime: -00:00 offset not allowed: %s" s)
        else Ok (AtDateTime s)
