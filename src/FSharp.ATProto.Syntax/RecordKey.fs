namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

/// <summary>
/// A record key (rkey) used to identify individual records within a collection in the AT Protocol.
/// Record keys are strings of 1-512 characters from the set <c>[a-zA-Z0-9._~:-]</c>.
/// The special values <c>"."</c> and <c>".."</c> are not allowed.
/// Common record key formats include TIDs (for time-ordered records) and <c>"self"</c> (for singleton records).
/// </summary>
/// <remarks>
/// See the AT Protocol specification: https://atproto.com/specs/record-key
/// </remarks>
type RecordKey = private RecordKey of string

/// <summary>
/// Functions for creating, validating, and extracting data from <see cref="RecordKey"/> values.
/// </summary>
module RecordKey =
    let private pattern = Regex(@"^[a-zA-Z0-9._~:-]{1,512}$", RegexOptions.Compiled)

    /// <summary>
    /// Extract the string representation of a record key.
    /// </summary>
    /// <param name="recordKey">The record key to extract the value from.</param>
    /// <returns>The record key string.</returns>
    let value (RecordKey s) = s

    /// <summary>
    /// Parse and validate a record key string.
    /// </summary>
    /// <param name="s">
    /// A record key string of 1-512 characters using only <c>[a-zA-Z0-9._~:-]</c>.
    /// The values <c>"."</c> and <c>".."</c> are reserved and not allowed.
    /// </param>
    /// <returns>
    /// <c>Ok</c> with a validated <see cref="RecordKey"/>, or <c>Error</c> with a message describing the validation failure.
    /// Validation failures include: null input, reserved values (<c>"."</c> or <c>".."</c>),
    /// or characters outside the allowed set.
    /// </returns>
    let parse (s: string) : Result<RecordKey, string> =
        if isNull s then Error "RecordKey cannot be null"
        elif s = "." || s = ".." then Error (sprintf "RecordKey cannot be '%s'" s)
        elif not (pattern.IsMatch(s)) then Error (sprintf "Invalid RecordKey: %s" s)
        else Ok (RecordKey s)
