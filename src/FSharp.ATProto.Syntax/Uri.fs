namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

/// <summary>
/// A general URI as defined by RFC 3986, used in the AT Protocol for links and references.
/// Must have a valid scheme (starting with a letter, followed by letters, digits, <c>+</c>, <c>-</c>, or <c>.</c>)
/// followed by <c>:</c> and a non-empty scheme-specific part with no whitespace.
/// Maximum length is 8192 characters.
/// </summary>
/// <remarks>
/// This performs basic syntactic validation only. Valid scheme examples include
/// <c>https</c>, <c>dns</c>, <c>at</c>, <c>did</c>, and <c>content-type</c>.
/// See https://www.rfc-editor.org/rfc/rfc3986 for the full URI specification.
/// </remarks>
type Uri = private Uri of string

/// <summary>
/// Functions for creating, validating, and extracting data from <see cref="Uri"/> values.
/// </summary>
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

    /// <summary>
    /// Extract the string representation of a URI.
    /// </summary>
    /// <param name="uri">The URI to extract the value from.</param>
    /// <returns>The full URI string.</returns>
    let value (Uri s) = s

    /// <summary>
    /// Parse and validate a URI string.
    /// </summary>
    /// <param name="s">
    /// A URI string with a valid scheme followed by <c>:</c> and a non-empty, whitespace-free body.
    /// The scheme must start with a letter and may contain letters, digits, <c>+</c>, <c>-</c>, and <c>.</c>.
    /// </param>
    /// <returns>
    /// <c>Ok</c> with a validated <see cref="Uri"/>, or <c>Error</c> with a message describing the validation failure.
    /// Validation failures include: null input, exceeding 8192 characters, or invalid URI syntax.
    /// </returns>
    let parse (s: string) : Result<Uri, string> =
        if isNull s then Error "URI cannot be null"
        elif s.Length > 8192 then Error "URI exceeds max length of 8KB"
        elif not (pattern.IsMatch(s)) then Error (sprintf "Invalid URI: %s" s)
        else Ok (Uri s)
