namespace FSharp.ATProto.Bluesky

open System.Text
open System.Text.RegularExpressions

module RichText =

    type DetectedFacet =
        | DetectedMention of byteStart: int * byteEnd: int * handle: string
        | DetectedLink of byteStart: int * byteEnd: int * uri: string
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
