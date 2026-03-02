namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

/// <summary>
/// A timestamp-based identifier (TID) used as record keys in the AT Protocol.
/// TIDs are 13-character strings encoded in a base32-sortable format that embeds a microsecond
/// timestamp and a clock identifier. They are lexicographically sortable by creation time.
/// </summary>
/// <remarks>
/// See the AT Protocol specification: https://atproto.com/specs/record-key
/// The first character is restricted to the range <c>[234567abcdefghij]</c> to ensure
/// the encoded timestamp fits in a 64-bit integer.
/// </remarks>
type Tid =
    private
    | Tid of string

    override this.ToString () = let (Tid s) = this in s

/// <summary>
/// Functions for creating, validating, and extracting data from <see cref="Tid"/> values.
/// </summary>
module Tid =
    let private pattern =
        Regex (@"^[234567abcdefghij][234567abcdefghijklmnopqrstuvwxyz]{12}$", RegexOptions.Compiled)

    /// <summary>
    /// Extract the string representation of a TID.
    /// </summary>
    /// <param name="tid">The TID to extract the value from.</param>
    /// <returns>The 13-character TID string.</returns>
    let value (Tid s) = s

    /// <summary>
    /// Parse and validate a TID string.
    /// </summary>
    /// <param name="s">
    /// A TID string that must be exactly 13 characters long, using the base32-sortable
    /// alphabet (<c>234567abcdefghijklmnopqrstuvwxyz</c>), with the first character
    /// restricted to <c>[234567abcdefghij]</c>.
    /// </param>
    /// <returns>
    /// <c>Ok</c> with a validated <see cref="Tid"/>, or <c>Error</c> with a message describing the validation failure.
    /// Validation failures include: null input, incorrect length (must be exactly 13), or invalid characters.
    /// </returns>
    let parse (s : string) : Result<Tid, string> =
        if isNull s then
            Error "TID cannot be null"
        elif s.Length <> 13 then
            Error (sprintf "TID must be exactly 13 characters, got %d" s.Length)
        elif not (pattern.IsMatch (s)) then
            Error (sprintf "Invalid TID: %s" s)
        else
            Ok (Tid s)
