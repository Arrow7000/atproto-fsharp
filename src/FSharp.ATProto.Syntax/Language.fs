namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

/// <summary>
/// A BCP 47 language tag used to identify the language of content in the AT Protocol.
/// Examples include <c>"en"</c>, <c>"pt-BR"</c>, <c>"zh-Hans"</c>, and grandfathered tags like <c>"i-default"</c>.
/// </summary>
/// <remarks>
/// This is a simplified/naive parser that validates the basic structure of BCP 47 tags:
/// a 2-3 letter primary subtag (or the special prefix <c>"i"</c> for grandfathered tags),
/// optionally followed by hyphen-separated alphanumeric subtags.
/// See https://www.rfc-editor.org/rfc/rfc5646.html for the full BCP 47 specification.
/// </remarks>
type Language = private Language of string

/// <summary>
/// Functions for creating, validating, and extracting data from <see cref="Language"/> values.
/// </summary>
module Language =
    // BCP 47 language tag (simplified/naive parser)
    // From test data analysis:
    // - Primary subtag: 2-3 lowercase letters, OR "i" (grandfathered)
    // - Subsequent subtags: alphanumeric, separated by hyphens
    // - No trailing hyphens
    // - Case sensitive: primary must be lowercase
    // Valid: ja, ban, pt-BR, i-default, i-navajo, zh-hakka, en-GB-boont-r-extended-sequence-x-private
    // Invalid: jaja (4 lowercase), JA (uppercase primary), j (single letter that's not 'i'),
    //          ja- (trailing hyphen), a-DE (single letter that's not 'i'), 123, .
    let private pattern =
        Regex(@"^(i|[a-z]{2,3})(-[a-zA-Z0-9]+)*$", RegexOptions.Compiled)

    /// <summary>
    /// Extract the string representation of a language tag.
    /// </summary>
    /// <param name="language">The language tag to extract the value from.</param>
    /// <returns>The BCP 47 language tag string (e.g. <c>"en"</c> or <c>"pt-BR"</c>).</returns>
    let value (Language s) = s

    /// <summary>
    /// Parse and validate a BCP 47 language tag string.
    /// </summary>
    /// <param name="s">
    /// A BCP 47 language tag. The primary subtag must be 2-3 lowercase letters
    /// or the special grandfathered prefix <c>"i"</c>. Optional subtags are separated
    /// by hyphens and must be alphanumeric. Trailing hyphens are not allowed.
    /// </param>
    /// <returns>
    /// <c>Ok</c> with a validated <see cref="Language"/>, or <c>Error</c> with a message describing the validation failure.
    /// Validation failures include: null input or invalid language tag syntax.
    /// </returns>
    let parse (s: string) : Result<Language, string> =
        if isNull s then Error "Language cannot be null"
        elif not (pattern.IsMatch(s)) then Error (sprintf "Invalid language tag: %s" s)
        else Ok (Language s)
