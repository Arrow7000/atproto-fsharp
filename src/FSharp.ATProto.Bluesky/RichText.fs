namespace FSharp.ATProto.Bluesky

open System.Text
open System.Text.RegularExpressions

/// <summary>
/// Rich text processing for Bluesky posts.
/// Detects mentions, links, and hashtags in text and resolves them to facets
/// with correct UTF-8 byte offsets as required by the AT Protocol.
/// </summary>
module RichText =

    /// <summary>
    /// A facet detected in rich text, with UTF-8 byte offsets and extracted content.
    /// Byte offsets are used rather than character indices because the AT Protocol
    /// specifies facet positions in UTF-8 byte coordinates.
    /// </summary>
    type DetectedFacet =
        /// <summary>A mention (@handle) detected in text.</summary>
        | DetectedMention of byteStart: int * byteEnd: int * handle: string
        /// <summary>A link (http:// or https://) detected in text.</summary>
        | DetectedLink of byteStart: int * byteEnd: int * uri: string
        /// <summary>A hashtag (#tag) detected in text.</summary>
        | DetectedTag of byteStart: int * byteEnd: int * tag: string

    let private charIndexToByteIndex (text: string) (charIndex: int) =
        Encoding.UTF8.GetByteCount(text, 0, charIndex)

    let private mentionRegex =
        Regex(@"(?:^|[\s(])(@([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)", RegexOptions.Compiled)

    let private linkRegex =
        Regex(@"(?:^|[\s(])(https?://[^\s)\]]*)", RegexOptions.Compiled)

    let private hashtagRegex =
        Regex(@"(?:^|[\s])[#\uFF03](\S*[^\d\s\p{P}]\S*)", RegexOptions.Compiled)

    let private trailingPunctuation = Regex(@"[.,;:!?]+$", RegexOptions.Compiled)

    let private detectMentions (text: string) =
        [ for m in mentionRegex.Matches(text) do
            // Find the @handle part within the match
            let fullMatch = m.Value
            let atIndex = fullMatch.IndexOf('@')
            let handle = fullMatch.Substring(atIndex + 1)
            let charStart = m.Index + atIndex
            let charEnd = charStart + handle.Length + 1 // +1 for @
            let byteStart = charIndexToByteIndex text charStart
            let byteEnd = charIndexToByteIndex text charEnd
            DetectedMention(byteStart, byteEnd, handle) ]

    let private detectLinks (text: string) =
        [ for m in linkRegex.Matches(text) do
            let rawUrl =
                let v = m.Groups.[1].Value
                trailingPunctuation.Replace(v, "")
            let charStart = m.Groups.[1].Index
            // Recalculate end based on cleaned URL
            let cleanCharEnd = charStart + rawUrl.Length
            let byteStart = charIndexToByteIndex text charStart
            let byteEnd = charIndexToByteIndex text cleanCharEnd
            DetectedLink(byteStart, byteEnd, rawUrl) ]

    let private detectHashtags (text: string) =
        [ for m in hashtagRegex.Matches(text) do
            let tag = m.Groups.[1].Value |> fun t -> trailingPunctuation.Replace(t, "")
            if tag.Length > 0 then
                // Find the # character position
                let fullMatch = m.Value
                let hashIndex = fullMatch.IndexOfAny([| '#'; '\uFF03' |])
                let charStart = m.Index + hashIndex
                let charEnd = charStart + 1 + tag.Length // +1 for # char
                // Adjust for fullwidth # which is 3 bytes but 1 char
                let byteStart = charIndexToByteIndex text charStart
                let byteEnd = charIndexToByteIndex text charEnd
                DetectedTag(byteStart, byteEnd, tag) ]

    /// <summary>
    /// Detect mentions, links, and hashtags in text.
    /// Returns facets with UTF-8 byte offsets, sorted by start position.
    /// </summary>
    /// <param name="text">The text to scan for rich text entities.</param>
    /// <returns>
    /// A list of <see cref="DetectedFacet"/> values sorted by byte start position.
    /// Mentions match <c>@handle.domain</c>, links match <c>http(s)://...</c>,
    /// and hashtags match <c>#tag</c> patterns.
    /// </returns>
    /// <remarks>
    /// This performs detection only. To resolve mentions to DIDs and produce
    /// <see cref="AppBskyRichtext.Facet.Facet"/> records suitable for the API,
    /// pass the result to <see cref="resolve"/> or use <see cref="parse"/> for a
    /// combined detect-and-resolve step.
    /// </remarks>
    /// <example>
    /// <code>
    /// let facets = RichText.detect "Hello @alice.bsky.social! #atproto"
    /// // Returns [DetectedMention(6, 27, "alice.bsky.social"); DetectedTag(29, 37, "atproto")]
    /// </code>
    /// </example>
    let detect (text: string) : DetectedFacet list =
        let mentions = detectMentions text
        let links = detectLinks text
        let tags = detectHashtags text
        mentions @ links @ tags
        |> List.sortBy (fun f ->
            match f with
            | DetectedMention (s, _, _) -> s
            | DetectedLink (s, _, _) -> s
            | DetectedTag (s, _, _) -> s)

    open System.Globalization
    open System.Text.Json
    open System.Threading.Tasks
    open FSharp.ATProto.Core

    let private makeFacet (byteStart: int) (byteEnd: int) (feature: JsonElement) : AppBskyRichtext.Facet.Facet =
        { Index = { ByteStart = int64 byteStart; ByteEnd = int64 byteEnd }
          Features = [ feature ] }

    let private serializeFeature (typeName: string) (fields: (string * obj) list) : JsonElement =
        let dict = System.Collections.Generic.Dictionary<string, obj>()
        dict.["$type"] <- typeName
        for (k, v) in fields do dict.[k] <- v
        JsonSerializer.SerializeToElement(dict)

    /// <summary>
    /// Resolve detected facets into API-ready facet records.
    /// Mentions are resolved to DIDs via <c>com.atproto.identity.resolveHandle</c>;
    /// unresolvable mentions are silently dropped.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="detected">The list of <see cref="DetectedFacet"/> values to resolve (typically from <see cref="detect"/>).</param>
    /// <returns>
    /// A list of <see cref="AppBskyRichtext.Facet.Facet"/> records with resolved features.
    /// Mentions whose handles cannot be resolved are omitted from the result.
    /// </returns>
    let resolve (agent: AtpAgent) (detected: DetectedFacet list) : Task<AppBskyRichtext.Facet.Facet list> =
        task {
            let results = System.Collections.Generic.List<AppBskyRichtext.Facet.Facet>()
            for facet in detected do
                match facet with
                | DetectedMention (s, e, handle) ->
                    let! result = ComAtprotoIdentity.ResolveHandle.query agent { Handle = handle }
                    match result with
                    | Ok output ->
                        let feature = serializeFeature "app.bsky.richtext.facet#mention" [ "did", output.Did ]
                        results.Add(makeFacet s e feature)
                    | Error _ -> ()
                | DetectedLink (s, e, uri) ->
                    let feature = serializeFeature "app.bsky.richtext.facet#link" [ "uri", uri ]
                    results.Add(makeFacet s e feature)
                | DetectedTag (s, e, tag) ->
                    let feature = serializeFeature "app.bsky.richtext.facet#tag" [ "tag", tag ]
                    results.Add(makeFacet s e feature)
            return results |> Seq.toList
        }

    /// <summary>
    /// Detect and resolve all rich text facets in a single step.
    /// Combines <see cref="detect"/> and <see cref="resolve"/>: scans the text for
    /// mentions, links, and hashtags, then resolves mentions to DIDs.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="text">The text to scan and resolve.</param>
    /// <returns>A list of resolved <see cref="AppBskyRichtext.Facet.Facet"/> records.</returns>
    /// <example>
    /// <code>
    /// let! facets = RichText.parse agent "Hello @alice.bsky.social! Check https://example.com #atproto"
    /// </code>
    /// </example>
    let parse (agent: AtpAgent) (text: string) : Task<AppBskyRichtext.Facet.Facet list> =
        task {
            let detected = detect text
            return! resolve agent detected
        }

    /// <summary>
    /// Count the number of grapheme clusters (user-perceived characters) in a string.
    /// Bluesky uses grapheme length for the 300-character post limit.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <returns>The number of extended grapheme clusters in the text.</returns>
    /// <remarks>
    /// Grapheme length differs from <see cref="System.String.Length"/> for multi-codepoint
    /// characters such as emoji (e.g., family emoji, flag emoji) and combining character sequences.
    /// </remarks>
    let graphemeLength (text: string) : int =
        let info = StringInfo(text)
        info.LengthInTextElements

    /// <summary>
    /// Count the UTF-8 byte length of a string.
    /// The AT Protocol specifies facet positions and text limits in UTF-8 bytes.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <returns>The number of bytes when the text is encoded as UTF-8.</returns>
    let byteLength (text: string) : int =
        Encoding.UTF8.GetByteCount(text)
