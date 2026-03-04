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
        | DetectedMention of byteStart : int * byteEnd : int * handle : string
        /// <summary>A link (http:// or https://) detected in text.</summary>
        | DetectedLink of byteStart : int * byteEnd : int * uri : string
        /// <summary>A hashtag (#tag) detected in text.</summary>
        | DetectedTag of byteStart : int * byteEnd : int * tag : string

    let private charIndexToByteIndex (text : string) (charIndex : int) = Encoding.UTF8.GetByteCount (text, 0, charIndex)

    let private mentionRegex =
        Regex (
            @"(?:^|[\s(])(@([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)",
            RegexOptions.Compiled
        )

    let private linkRegex =
        Regex (@"(?:^|[\s(])(https?://[^\s)\]]*)", RegexOptions.Compiled)

    let private hashtagRegex =
        Regex (@"(?:^|[\s])[#\uFF03](\S*[^\d\s\p{P}]\S*)", RegexOptions.Compiled)

    let private trailingPunctuation = Regex (@"[.,;:!?]+$", RegexOptions.Compiled)

    let private detectMentions (text : string) =
        [ for m in mentionRegex.Matches (text) do
              // Find the @handle part within the match
              let fullMatch = m.Value
              let atIndex = fullMatch.IndexOf ('@')
              let handle = fullMatch.Substring (atIndex + 1)
              let charStart = m.Index + atIndex
              let charEnd = charStart + handle.Length + 1 // +1 for @
              let byteStart = charIndexToByteIndex text charStart
              let byteEnd = charIndexToByteIndex text charEnd
              DetectedMention (byteStart, byteEnd, handle) ]

    let private detectLinks (text : string) =
        [ for m in linkRegex.Matches (text) do
              let rawUrl =
                  let v = m.Groups.[1].Value
                  trailingPunctuation.Replace (v, "")

              let charStart = m.Groups.[1].Index
              // Recalculate end based on cleaned URL
              let cleanCharEnd = charStart + rawUrl.Length
              let byteStart = charIndexToByteIndex text charStart
              let byteEnd = charIndexToByteIndex text cleanCharEnd
              DetectedLink (byteStart, byteEnd, rawUrl) ]

    let private detectHashtags (text : string) =
        [ for m in hashtagRegex.Matches (text) do
              let tag = m.Groups.[1].Value |> fun t -> trailingPunctuation.Replace (t, "")

              if tag.Length > 0 then
                  // Find the # character position
                  let fullMatch = m.Value
                  let hashIndex = fullMatch.IndexOfAny ([| '#'; '\uFF03' |])
                  let charStart = m.Index + hashIndex
                  let charEnd = charStart + 1 + tag.Length // +1 for # char
                  // Adjust for fullwidth # which is 3 bytes but 1 char
                  let byteStart = charIndexToByteIndex text charStart
                  let byteEnd = charIndexToByteIndex text charEnd
                  DetectedTag (byteStart, byteEnd, tag) ]

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
    /// let facets = RichText.detect "Hello @my-handle.bsky.social! #atproto"
    /// // Returns [DetectedMention(6, 28, "my-handle.bsky.social"); DetectedTag(30, 38, "atproto")]
    /// </code>
    /// </example>
    let detect (text : string) : DetectedFacet list =
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
    open FSharp.ATProto.Syntax

    let private makeFacet
        (byteStart : int)
        (byteEnd : int)
        (feature : AppBskyRichtext.Facet.FacetFeaturesItem)
        : AppBskyRichtext.Facet.Facet =
        { Index =
            { ByteStart = int64 byteStart
              ByteEnd = int64 byteEnd }
          Features = [ feature ] }

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
    let resolve (agent : AtpAgent) (detected : DetectedFacet list) : Task<AppBskyRichtext.Facet.Facet list> =
        task {
            let results = System.Collections.Generic.List<AppBskyRichtext.Facet.Facet> ()

            for facet in detected do
                match facet with
                | DetectedMention (s, e, handle) ->
                    match Handle.parse handle with
                    | Error _ -> () // Drop mentions with unparseable handles
                    | Ok handleTyped ->
                        let! result = ComAtprotoIdentity.ResolveHandle.query agent { Handle = handleTyped }

                        match result with
                        | Ok output ->
                            let feature = AppBskyRichtext.Facet.FacetFeaturesItem.Mention { Did = output.Did }
                            results.Add (makeFacet s e feature)
                        | Error _ -> () // Drop unresolvable mentions
                | DetectedLink (s, e, uri) ->
                    match FSharp.ATProto.Syntax.Uri.parse uri with
                    | Error _ -> () // Drop links with unparseable URIs
                    | Ok uriTyped ->
                        let feature = AppBskyRichtext.Facet.FacetFeaturesItem.Link { Uri = uriTyped }
                        results.Add (makeFacet s e feature)
                | DetectedTag (s, e, tag) ->
                    let feature = AppBskyRichtext.Facet.FacetFeaturesItem.Tag { Tag = tag }
                    results.Add (makeFacet s e feature)

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
    /// let! facets = RichText.parse agent "Hello @my-handle.bsky.social! Check https://example.com #atproto"
    /// </code>
    /// </example>
    let parse (agent : AtpAgent) (text : string) : Task<AppBskyRichtext.Facet.Facet list> =
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
    let graphemeLength (text : string) : int =
        let info = StringInfo (text)
        info.LengthInTextElements

    /// <summary>
    /// Count the UTF-8 byte length of a string.
    /// The AT Protocol specifies facet positions and text limits in UTF-8 bytes.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <returns>The number of bytes when the text is encoded as UTF-8.</returns>
    let byteLength (text : string) : int = Encoding.UTF8.GetByteCount (text)

    // ──────────────────────────────────────────────────────────────────
    //  Rich text value type and manipulation functions
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// A rich text value: text content paired with its resolved facets.
    /// All facet indices are UTF-8 byte offsets.
    /// </summary>
    type RichTextValue =
        { Text : string
          Facets : AppBskyRichtext.Facet.Facet list }

    /// <summary>
    /// A segment of rich text for rendering. Each segment has plain text content
    /// and an optional facet that applies to the entire segment.
    /// </summary>
    type RichTextSegment =
        { Text : string
          Facet : AppBskyRichtext.Facet.Facet option }

    /// <summary>
    /// Create a RichTextValue from text and facets.
    /// </summary>
    let create (text : string) (facets : AppBskyRichtext.Facet.Facet list) : RichTextValue =
        { Text = text; Facets = facets }

    /// <summary>
    /// Create a plain RichTextValue with no facets.
    /// </summary>
    let plain (text : string) : RichTextValue = { Text = text; Facets = [] }

    /// Helper to splice a UTF-8 encoded byte array at a character-level position
    /// derived from a byte index. We work with bytes to stay correct.
    let private spliceBytes (textBytes : byte array) (bytePos : int) (insertBytes : byte array) : byte array =
        let pos = max 0 (min bytePos textBytes.Length)
        let result = Array.zeroCreate (textBytes.Length + insertBytes.Length)
        System.Array.Copy (textBytes, 0, result, 0, pos)
        System.Array.Copy (insertBytes, 0, result, pos, insertBytes.Length)
        System.Array.Copy (textBytes, pos, result, pos + insertBytes.Length, textBytes.Length - pos)
        result

    /// Helper to delete a byte range from a UTF-8 byte array.
    let private deleteBytes (textBytes : byte array) (byteStart : int) (byteEnd : int) : byte array =
        let s = max 0 (min byteStart textBytes.Length)
        let e = max s (min byteEnd textBytes.Length)
        let deleteLen = e - s
        let result = Array.zeroCreate (textBytes.Length - deleteLen)
        System.Array.Copy (textBytes, 0, result, 0, s)
        System.Array.Copy (textBytes, e, result, s, textBytes.Length - e)
        result

    /// <summary>
    /// Insert text at a UTF-8 byte index. Facets that start at or after the insertion
    /// point are shifted forward. Facets that span the insertion point are expanded.
    /// </summary>
    /// <param name="bytePos">The UTF-8 byte offset at which to insert.</param>
    /// <param name="insertText">The text to insert.</param>
    /// <param name="rt">The rich text value to modify.</param>
    /// <returns>A new RichTextValue with the text inserted and facet indices adjusted.</returns>
    let insert (bytePos : int) (insertText : string) (rt : RichTextValue) : RichTextValue =
        let textBytes = Encoding.UTF8.GetBytes rt.Text
        let insertBytes = Encoding.UTF8.GetBytes insertText
        let insertLen = int64 insertBytes.Length
        let pos = int64 (max 0 (min bytePos textBytes.Length))

        let newBytes = spliceBytes textBytes bytePos insertBytes
        let newText = Encoding.UTF8.GetString newBytes

        let newFacets =
            rt.Facets
            |> List.map (fun facet ->
                let s = facet.Index.ByteStart
                let e = facet.Index.ByteEnd

                let newStart, newEnd =
                    if s >= pos then
                        // Facet is entirely at or after insertion point: shift both
                        s + insertLen, e + insertLen
                    elif e > pos then
                        // Facet spans the insertion point: expand end
                        s, e + insertLen
                    else
                        // Facet is entirely before insertion point: no change
                        s, e

                { facet with
                    Index = { ByteStart = newStart; ByteEnd = newEnd } })

        { Text = newText; Facets = newFacets }

    /// <summary>
    /// Delete a UTF-8 byte range from rich text. Facets are adjusted:
    /// facets entirely within the deleted range are removed, facets partially
    /// overlapping are truncated, and facets after the deleted range are shifted back.
    /// </summary>
    /// <param name="byteStart">The start of the byte range to delete (inclusive).</param>
    /// <param name="byteEnd">The end of the byte range to delete (exclusive).</param>
    /// <param name="rt">The rich text value to modify.</param>
    /// <returns>A new RichTextValue with the range deleted and facet indices adjusted.</returns>
    let delete (byteStart : int) (byteEnd : int) (rt : RichTextValue) : RichTextValue =
        let textBytes = Encoding.UTF8.GetBytes rt.Text
        let s = int64 (max 0 (min byteStart textBytes.Length))
        let e = int64 (max (int s) (min byteEnd textBytes.Length))
        let deleteLen = e - s

        let newBytes = deleteBytes textBytes byteStart byteEnd
        let newText = Encoding.UTF8.GetString newBytes

        let newFacets =
            rt.Facets
            |> List.choose (fun facet ->
                let fs = facet.Index.ByteStart
                let fe = facet.Index.ByteEnd

                if fe <= s then
                    // Facet is entirely before deleted range: no change
                    Some facet
                elif fs >= e then
                    // Facet is entirely after deleted range: shift back
                    Some
                        { facet with
                            Index =
                                { ByteStart = fs - deleteLen
                                  ByteEnd = fe - deleteLen } }
                elif fs >= s && fe <= e then
                    // Facet is entirely within deleted range: remove
                    None
                elif fs < s && fe > e then
                    // Facet spans the entire deleted range: shrink
                    Some
                        { facet with
                            Index =
                                { ByteStart = fs
                                  ByteEnd = fe - deleteLen } }
                elif fs < s then
                    // Facet starts before deletion, ends within: truncate end
                    let newEnd = s

                    if newEnd > fs then
                        Some
                            { facet with
                                Index = { ByteStart = fs; ByteEnd = newEnd } }
                    else
                        None
                else
                    // Facet starts within deletion, ends after: truncate start
                    let newStart = s
                    let newEnd = fe - deleteLen

                    if newEnd > newStart then
                        Some
                            { facet with
                                Index =
                                    { ByteStart = newStart
                                      ByteEnd = newEnd } }
                    else
                        None)

        { Text = newText; Facets = newFacets }

    /// <summary>
    /// Split rich text into segments by facet boundaries for rendering.
    /// Each segment has plain text and an optional facet. Non-faceted text
    /// between facets becomes segments with <c>Facet = None</c>.
    /// Segments are returned in text order.
    /// </summary>
    /// <param name="rt">The rich text value to segment.</param>
    /// <returns>A list of <see cref="RichTextSegment"/> values covering the entire text.</returns>
    let segments (rt : RichTextValue) : RichTextSegment list =
        let textBytes = Encoding.UTF8.GetBytes rt.Text

        // Sort facets by start position; filter out invalid facets
        let sortedFacets =
            rt.Facets
            |> List.filter (fun f ->
                f.Index.ByteStart >= 0L
                && f.Index.ByteEnd > f.Index.ByteStart
                && f.Index.ByteStart < int64 textBytes.Length)
            |> List.sortBy (fun f -> f.Index.ByteStart)

        if sortedFacets.IsEmpty then
            if rt.Text.Length > 0 then
                [ { Text = rt.Text; Facet = None } ]
            else
                []
        else
            let result = System.Collections.Generic.List<RichTextSegment> ()
            let mutable cursor = 0L

            for facet in sortedFacets do
                let facetStart = facet.Index.ByteStart
                let facetEnd = min facet.Index.ByteEnd (int64 textBytes.Length)

                // Add gap segment before this facet
                if facetStart > cursor then
                    let gapText =
                        Encoding.UTF8.GetString (textBytes, int cursor, int (facetStart - cursor))

                    result.Add { Text = gapText; Facet = None }

                // Add faceted segment
                let facetText =
                    Encoding.UTF8.GetString (textBytes, int facetStart, int (facetEnd - facetStart))

                result.Add
                    { Text = facetText
                      Facet = Some facet }

                cursor <- facetEnd

            // Add trailing gap segment
            if cursor < int64 textBytes.Length then
                let trailText =
                    Encoding.UTF8.GetString (textBytes, int cursor, textBytes.Length - int cursor)

                result.Add { Text = trailText; Facet = None }

            result |> Seq.toList

    let private whitespaceRunRegex = Regex (@"[^\S\n]{2,}", RegexOptions.Compiled)
    let private newlineRunRegex = Regex (@"\n{3,}", RegexOptions.Compiled)

    /// <summary>
    /// Sanitize rich text by trimming leading/trailing whitespace, collapsing
    /// runs of spaces/tabs to a single space, normalizing \r\n to \n, and
    /// limiting consecutive newlines to two. Facet indices are recomputed
    /// after sanitization by re-detecting facets from the cleaned text.
    /// </summary>
    /// <param name="rt">The rich text value to sanitize.</param>
    /// <returns>A new RichTextValue with cleaned text and adjusted facets.</returns>
    /// <remarks>
    /// Because sanitization can change byte offsets in non-trivial ways
    /// (e.g., collapsing multiple spaces to one), facet positions are
    /// recalculated by mapping each original facet's byte range through
    /// the transformation. Facets whose ranges become empty or invalid
    /// after sanitization are removed.
    /// </remarks>
    let sanitize (rt : RichTextValue) : RichTextValue =
        // Normalize \r\n -> \n, then \r -> \n
        let text1a = rt.Text.Replace("\r\n", "\n")
        let text1 = text1a.Replace("\r", "\n")
        // Collapse runs of non-newline whitespace to single space
        let text2 = whitespaceRunRegex.Replace (text1, " ")
        // Collapse 3+ newlines to 2
        let text3 = newlineRunRegex.Replace (text2, "\n\n")
        // Trim
        let text4 = text3.Trim ()

        let origBytes = Encoding.UTF8.GetBytes rt.Text

        if rt.Facets.IsEmpty then
            { Text = text4; Facets = [] }
        else
            // Build a char-index to byte-offset map for original text
            let origCharToByteStart =
                Array.init (rt.Text.Length + 1) (fun i -> Encoding.UTF8.GetByteCount (rt.Text, 0, i))

            // Build char-level mapping from original -> text1 (CRLF -> LF, CR -> LF)
            let mapCrLf (s : string) =
                let sb = System.Text.StringBuilder ()
                let map = Array.create (s.Length + 1) 0
                let mutable outIdx = 0
                let mutable i = 0

                while i < s.Length do
                    if i + 1 < s.Length && s.[i] = '\r' && s.[i + 1] = '\n' then
                        sb.Append '\n' |> ignore
                        map.[i] <- outIdx
                        map.[i + 1] <- outIdx
                        outIdx <- outIdx + 1
                        i <- i + 2
                    elif s.[i] = '\r' then
                        sb.Append '\n' |> ignore
                        map.[i] <- outIdx
                        outIdx <- outIdx + 1
                        i <- i + 1
                    else
                        sb.Append s.[i] |> ignore
                        map.[i] <- outIdx
                        outIdx <- outIdx + 1
                        i <- i + 1

                map.[s.Length] <- outIdx
                sb.ToString (), map

            // Build char-level mapping for regex replacement
            let mapRegex (pattern : Regex) (replacement : string) (s : string) =
                let matches = pattern.Matches s
                let sb = System.Text.StringBuilder ()
                let map = Array.create (s.Length + 1) 0
                let mutable prevEnd = 0
                let mutable outIdx = 0

                for m in matches do
                    for i in prevEnd .. m.Index - 1 do
                        sb.Append s.[i] |> ignore
                        map.[i] <- outIdx
                        outIdx <- outIdx + 1

                    sb.Append replacement |> ignore

                    for i in m.Index .. m.Index + m.Length - 1 do
                        if i = m.Index then
                            map.[i] <- outIdx
                        else
                            map.[i] <- outIdx + replacement.Length - 1

                    outIdx <- outIdx + replacement.Length
                    prevEnd <- m.Index + m.Length

                for i in prevEnd .. s.Length - 1 do
                    sb.Append s.[i] |> ignore
                    map.[i] <- outIdx
                    outIdx <- outIdx + 1

                map.[s.Length] <- outIdx
                sb.ToString (), map

            // Build char-level mapping for trim
            let mapTrim (s : string) =
                let trimmed = s.Trim ()
                let leadingTrim = s.Length - s.TrimStart().Length
                let map = Array.create (s.Length + 1) 0

                for i in 0 .. s.Length do
                    map.[i] <- max 0 (min (i - leadingTrim) trimmed.Length)

                trimmed, map

            // Apply transforms and collect char-level mappings
            let t1, map1 = mapCrLf rt.Text
            let t2, map2 = mapRegex whitespaceRunRegex " " t1
            let t3, map3 = mapRegex newlineRunRegex "\n\n" t2
            let _t4, map4 = mapTrim t3

            // Compose char mappings: origCharIdx -> finalCharIdx
            let composeMap (origLen : int) (m1 : int array) (m1OutLen : int) (m2 : int array) =
                Array.init (origLen + 1) (fun i ->
                    let clamped = min m1.[i] m1OutLen
                    m2.[clamped])

            let composed12 = composeMap rt.Text.Length map1 t1.Length map2
            let composed123 = composeMap rt.Text.Length composed12 t2.Length map3
            let composed1234 = composeMap rt.Text.Length composed123 t3.Length map4

            // Build byte-to-char and char-to-byte maps for the final text
            let newCharToByteStart =
                Array.init (text4.Length + 1) (fun i -> Encoding.UTF8.GetByteCount (text4, 0, i))

            // Build reverse map: original byte offset -> original char index
            let origByteToCharStart =
                let arr = Array.create (origBytes.Length + 1) rt.Text.Length

                for ci in 0 .. rt.Text.Length do
                    let bo = origCharToByteStart.[ci]

                    if bo <= origBytes.Length then
                        arr.[bo] <- min arr.[bo] ci

                // Fill gaps: bytes within a multi-byte char map to that char's index
                let mutable lastCharIdx = 0

                for bo in 0 .. origBytes.Length do
                    if arr.[bo] <= rt.Text.Length && arr.[bo] >= lastCharIdx then
                        lastCharIdx <- arr.[bo]
                    else
                        arr.[bo] <- lastCharIdx

                arr

            // Map each facet's byte range through the composed char mapping
            let newFacets =
                rt.Facets
                |> List.choose (fun facet ->
                    let clampedStart = max 0 (min (int facet.Index.ByteStart) origBytes.Length)
                    let clampedEnd = max clampedStart (min (int facet.Index.ByteEnd) origBytes.Length)

                    let origCharStart = origByteToCharStart.[clampedStart]
                    let origCharEnd = origByteToCharStart.[clampedEnd]

                    let newCharStart = composed1234.[min origCharStart rt.Text.Length]
                    let newCharEnd = composed1234.[min origCharEnd rt.Text.Length]

                    let newByteStart = newCharToByteStart.[min newCharStart text4.Length]
                    let newByteEnd = newCharToByteStart.[min newCharEnd text4.Length]

                    if newByteEnd > newByteStart then
                        Some
                            { facet with
                                Index =
                                    { ByteStart = int64 newByteStart
                                      ByteEnd = int64 newByteEnd } }
                    else
                        None)

            { Text = text4; Facets = newFacets }

    /// <summary>
    /// Truncate rich text to a maximum UTF-8 byte length while preserving facet integrity.
    /// The text is truncated at the byte limit (on a valid UTF-8 boundary), and any
    /// facets that extend beyond the limit are removed entirely.
    /// </summary>
    /// <param name="maxBytes">The maximum number of UTF-8 bytes to keep.</param>
    /// <param name="rt">The rich text value to truncate.</param>
    /// <returns>
    /// A new RichTextValue truncated to the byte limit. Facets that would extend
    /// beyond the truncated text are removed (not partially preserved).
    /// </returns>
    let truncate (maxBytes : int) (rt : RichTextValue) : RichTextValue =
        let textBytes = Encoding.UTF8.GetBytes rt.Text

        if textBytes.Length <= maxBytes then
            rt
        else
            // Find the last valid UTF-8 boundary at or before maxBytes.
            // In UTF-8, continuation bytes have the form 10xxxxxx (0x80-0xBF).
            // We back up from maxBytes until we're not on a continuation byte.
            let mutable cp = min maxBytes textBytes.Length

            while cp > 0 && cp < textBytes.Length && textBytes.[cp] &&& 0xC0uy = 0x80uy do
                cp <- cp - 1

            let newText = Encoding.UTF8.GetString (textBytes, 0, cp)
            let limit = int64 cp

            // Remove facets that extend beyond the cut point
            let newFacets =
                rt.Facets
                |> List.filter (fun facet -> facet.Index.ByteEnd <= limit && facet.Index.ByteStart < limit)

            { Text = newText; Facets = newFacets }
