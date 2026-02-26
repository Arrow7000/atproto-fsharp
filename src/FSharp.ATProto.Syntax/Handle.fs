namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

/// <summary>
/// A handle (domain name) used as a human-readable identifier in the AT Protocol.
/// Handles are DNS-based names (e.g. <c>alice.bsky.social</c>) that resolve to a <see cref="Did"/>.
/// They must be valid domain names with at least two segments and a maximum length of 253 characters.
/// </summary>
/// <remarks>
/// See the AT Protocol specification: https://atproto.com/specs/handle
/// </remarks>
type Handle =
    private
    | Handle of string

    override this.ToString() = let (Handle s) = this in s

/// <summary>
/// Functions for creating, validating, and extracting data from <see cref="Handle"/> values.
/// </summary>
module Handle =
    let private pattern =
        Regex(@"^([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$", RegexOptions.Compiled)

    /// <summary>
    /// Extract the string representation of a handle.
    /// </summary>
    /// <param name="handle">The handle to extract the value from.</param>
    /// <returns>The full handle string (e.g. <c>"alice.bsky.social"</c>).</returns>
    let value (Handle s) = s

    /// <summary>
    /// Parse and validate a handle string.
    /// </summary>
    /// <param name="s">
    /// A handle string in domain-name format (e.g. <c>"alice.bsky.social"</c>).
    /// Must be a valid hostname with at least two segments, each segment starting and
    /// ending with an alphanumeric character, and the TLD starting with a letter.
    /// </param>
    /// <returns>
    /// <c>Ok</c> with a validated <see cref="Handle"/>, or <c>Error</c> with a message describing the validation failure.
    /// Validation failures include: null input, exceeding the 253-character limit,
    /// or invalid hostname syntax.
    /// </returns>
    /// <example>
    /// <code>
    /// match Handle.parse "alice.bsky.social" with
    /// | Ok handle -> printfn "Valid: %s" (Handle.value handle)
    /// | Error e -> printfn "Invalid: %s" e
    /// </code>
    /// </example>
    let parse (s: string) : Result<Handle, string> =
        if isNull s then Error "Handle cannot be null"
        elif s.Length > 253 then Error (sprintf "Handle exceeds max length of 253: %d" s.Length)
        elif not (pattern.IsMatch(s)) then Error (sprintf "Invalid handle syntax: %s" s)
        else Ok (Handle s)
