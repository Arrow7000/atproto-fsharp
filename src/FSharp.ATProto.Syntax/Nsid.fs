namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

/// <summary>
/// A Namespaced Identifier (NSID) used to reference Lexicon schemas in the AT Protocol.
/// NSIDs follow the pattern <c>authority.name</c> where the authority is a reversed domain name
/// and the name identifies the specific schema (e.g. <c>app.bsky.feed.post</c>).
/// Maximum length is 317 characters.
/// </summary>
/// <remarks>
/// See the AT Protocol specification: https://atproto.com/specs/nsid
/// </remarks>
type Nsid =
    private
    | Nsid of string

    override this.ToString() = let (Nsid s) = this in s

/// <summary>
/// Functions for creating, validating, and extracting data from <see cref="Nsid"/> values.
/// </summary>
module Nsid =
    let private pattern =
        Regex(@"^[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)+(\.[a-zA-Z]([a-zA-Z0-9]{0,62})?)$", RegexOptions.Compiled)

    /// <summary>
    /// Extract the string representation of an NSID.
    /// </summary>
    /// <param name="nsid">The NSID to extract the value from.</param>
    /// <returns>The full NSID string (e.g. <c>"app.bsky.feed.post"</c>).</returns>
    let value (Nsid s) = s

    /// <summary>
    /// Parse and validate an NSID string.
    /// </summary>
    /// <param name="s">
    /// An NSID string in the format <c>domain.segments.name</c>
    /// (e.g. <c>"app.bsky.feed.post"</c>). Must have at least three segments,
    /// where the final segment (name) starts with a letter and the preceding segments
    /// form a valid reversed domain authority.
    /// </param>
    /// <returns>
    /// <c>Ok</c> with a validated <see cref="Nsid"/>, or <c>Error</c> with a message describing the validation failure.
    /// Validation failures include: null input, exceeding the 317-character limit,
    /// or invalid NSID syntax.
    /// </returns>
    /// <example>
    /// <code>
    /// match Nsid.parse "app.bsky.feed.post" with
    /// | Ok nsid -> printfn "Authority: %s, Name: %s" (Nsid.authority nsid) (Nsid.name nsid)
    /// | Error e -> printfn "Invalid: %s" e
    /// </code>
    /// </example>
    let parse (s: string) : Result<Nsid, string> =
        if isNull s then Error "NSID cannot be null"
        elif s.Length > 317 then Error (sprintf "NSID exceeds max length of 317: %d" s.Length)
        elif not (pattern.IsMatch(s)) then Error (sprintf "Invalid NSID syntax: %s" s)
        else Ok (Nsid s)

    /// <summary>
    /// Extract the authority (reversed domain name) portion of an NSID.
    /// </summary>
    /// <param name="nsid">The NSID to extract the authority from.</param>
    /// <returns>The authority string (e.g. <c>"app.bsky.feed"</c> for NSID <c>"app.bsky.feed.post"</c>).</returns>
    let authority (Nsid s) = let i = s.LastIndexOf('.') in s.Substring(0, i)

    /// <summary>
    /// Extract the name (final segment) of an NSID.
    /// </summary>
    /// <param name="nsid">The NSID to extract the name from.</param>
    /// <returns>The name string (e.g. <c>"post"</c> for NSID <c>"app.bsky.feed.post"</c>).</returns>
    let name (Nsid s) = let i = s.LastIndexOf('.') in s.Substring(i + 1)
