namespace FSharp.ATProto.Syntax

/// <summary>
/// An AT-URI that identifies a resource in the AT Protocol network.
/// AT-URIs use the scheme <c>at://</c> followed by an authority (DID or handle),
/// an optional collection (NSID), and an optional record key.
/// Format: <c>at://&lt;authority&gt;[/&lt;collection&gt;[/&lt;rkey&gt;]]</c>.
/// Maximum length is 8192 characters.
/// </summary>
/// <remarks>
/// See the AT Protocol specification: https://atproto.com/specs/at-uri-scheme
/// </remarks>
type AtUri = private AtUri of string

/// <summary>
/// Functions for creating, validating, and extracting data from <see cref="AtUri"/> values.
/// </summary>
module AtUri =
    /// <summary>
    /// Extract the string representation of an AT-URI.
    /// </summary>
    /// <param name="atUri">The AT-URI to extract the value from.</param>
    /// <returns>The full AT-URI string (e.g. <c>"at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/3k2la3b"</c>).</returns>
    let value (AtUri s) = s

    /// Split the path portion of a validated AT-URI into its segments.
    let private segments (AtUri s) =
        s.Substring(5).Split('/', 3)

    /// <summary>
    /// Extract the authority segment (DID or handle) from an AT-URI.
    /// </summary>
    /// <param name="atUri">The AT-URI to extract the authority from.</param>
    /// <returns>The authority string (e.g. <c>"did:plc:z72i7hdynmk6r22z27h6tvur"</c> or <c>"alice.bsky.social"</c>).</returns>
    let authority atUri =
        (segments atUri).[0]

    /// <summary>
    /// Extract the collection NSID segment from an AT-URI, if present.
    /// </summary>
    /// <param name="atUri">The AT-URI to extract the collection from.</param>
    /// <returns><c>Some</c> with the collection NSID string (e.g. <c>"app.bsky.feed.post"</c>), or <c>None</c> if the AT-URI has no collection segment.</returns>
    let collection atUri =
        let parts = segments atUri
        if parts.Length >= 2 then Some parts.[1] else None

    /// <summary>
    /// Extract the record key segment from an AT-URI, if present.
    /// </summary>
    /// <param name="atUri">The AT-URI to extract the record key from.</param>
    /// <returns><c>Some</c> with the record key string (e.g. <c>"3k2la3b"</c>), or <c>None</c> if the AT-URI has no record key segment.</returns>
    let rkey atUri =
        let parts = segments atUri
        if parts.Length >= 3 then Some parts.[2] else None

    /// <summary>
    /// Parse and validate an AT-URI string.
    /// </summary>
    /// <param name="s">
    /// An AT-URI string starting with <c>"at://"</c> followed by an authority (DID or handle),
    /// and optionally a collection (NSID) and record key path segments.
    /// Query parameters and fragments are not allowed.
    /// </param>
    /// <returns>
    /// <c>Ok</c> with a validated <see cref="AtUri"/>, or <c>Error</c> with a message describing the validation failure.
    /// Validation failures include: null input, exceeding 8192 characters, missing <c>at://</c> prefix,
    /// presence of query/fragment components, invalid authority (must be a valid DID or handle),
    /// invalid collection (must be a valid NSID), invalid record key, or trailing slash.
    /// </returns>
    /// <example>
    /// <code>
    /// match AtUri.parse "at://alice.bsky.social/app.bsky.feed.post/3k2la3b" with
    /// | Ok uri -> printfn "Valid: %s" (AtUri.value uri)
    /// | Error e -> printfn "Invalid: %s" e
    /// </code>
    /// </example>
    let parse (s: string) : Result<AtUri, string> =
        if isNull s then Error "AT-URI cannot be null"
        elif s.Length > 8192 then Error "AT-URI exceeds max length of 8KB"
        elif not (s.StartsWith("at://")) then Error "AT-URI must start with 'at://'"
        elif s.Contains('?') || s.Contains('#') then Error "AT-URI must not contain query or fragment"
        else
            let rest = s.Substring(5)
            if rest.Length = 0 then Error "AT-URI must have an authority"
            elif rest.EndsWith("/") then Error "AT-URI must not have a trailing slash"
            else
                let parts = rest.Split('/', 3)
                let authorityStr = parts.[0]
                let authorityResult =
                    if authorityStr.StartsWith("did:") then Did.parse authorityStr |> Result.map ignore
                    else Handle.parse authorityStr |> Result.map ignore
                match authorityResult with
                | Error e -> Error (sprintf "Invalid AT-URI authority: %s" e)
                | Ok _ ->
                    if parts.Length >= 2 then
                        match Nsid.parse parts.[1] with
                        | Error e -> Error (sprintf "Invalid AT-URI collection: %s" e)
                        | Ok _ ->
                            if parts.Length >= 3 then
                                match RecordKey.parse parts.[2] with
                                | Error e -> Error (sprintf "Invalid AT-URI record key: %s" e)
                                | Ok _ -> Ok (AtUri s)
                            else Ok (AtUri s)
                    else Ok (AtUri s)
