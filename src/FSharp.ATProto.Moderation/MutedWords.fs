namespace FSharp.ATProto.Moderation

open System
open System.Text.RegularExpressions

/// Target for a muted word -- where should the match be checked.
[<RequireQualifiedAccess>]
type MutedWordTarget =
    /// Match against post text content
    | Content
    /// Match against hashtags (both inline and outline)
    | Tag

/// A word or phrase the user has muted.
type MutedWord =
    { /// The word or phrase to mute
      Value: string
      /// Where to check for this word (content, tags, or both)
      Targets: MutedWordTarget list
      /// Actor targeting: "all", "exclude-following", or None (defaults to "all")
      ActorTarget: string option
      /// ISO 8601 expiration timestamp, or None for permanent
      ExpiresAt: string option }

/// Languages that don't use spaces for word boundaries.
/// For these, we use simple string.Contains instead of word-boundary matching.
module private LanguageExceptions =
    let values = set [ "ja"; "zh"; "ko"; "th"; "vi" ]

module MutedWords =

    let private leadingTrailingPunctuation =
        Regex(@"(?:^\p{P}+|\p{P}+$)", RegexOptions.Compiled)

    let private wordBoundaryRegex =
        Regex(@"[\s\n\t\r\f\v]+", RegexOptions.Compiled)

    let private punctuationRegex =
        Regex(@"\p{P}+", RegexOptions.Compiled)

    let private spaceOrPunctuationRegex =
        Regex(@"(?:\s|\p{P})+", RegexOptions.Compiled)

    let private slashRegex =
        Regex(@"[/]+", RegexOptions.Compiled)

    let private stripLeadingTrailingPunctuation (s: string) =
        leadingTrailingPunctuation.Replace(s, "")

    /// Check whether a muted word has expired.
    let isExpired (mutedWord: MutedWord) : bool =
        match mutedWord.ExpiresAt with
        | None -> false
        | Some expiresAt ->
            match DateTimeOffset.TryParse(expiresAt) with
            | true, expiry -> expiry < DateTimeOffset.UtcNow
            | false, _ -> false

    /// Check whether a single muted word matches the given text content.
    /// The text and tags are checked based on the muted word's target configuration.
    /// Returns true if the muted word matches.
    let matchesMutedWord
        (mutedWord: MutedWord)
        (text: string)
        (tags: string list)
        (languages: string list)
        : bool =

        if isExpired mutedWord then
            false
        else
            let mutedValue = mutedWord.Value.ToLowerInvariant()
            let postText = text.ToLowerInvariant()
            let lowerTags = tags |> List.map (fun t -> t.ToLowerInvariant())

            // Check tags first -- both "tag" and "content" targets match against tags
            if lowerTags |> List.contains mutedValue then
                true
            else
                // Rest of the checks are for "content" targets only
                if not (mutedWord.Targets |> List.contains MutedWordTarget.Content) then
                    false
                else
                    let isLanguageException =
                        match languages with
                        | lang :: _ -> LanguageExceptions.values.Contains lang
                        | [] -> false

                    // Single character or language exception: simple contains
                    if (mutedValue.Length = 1 || isLanguageException) && postText.Contains(mutedValue) then
                        true
                    // Muted word longer than post text
                    elif mutedValue.Length > postText.Length then
                        false
                    // Exact match
                    elif mutedValue = postText then
                        true
                    // Phrase with space or punctuation: simple contains
                    elif spaceOrPunctuationRegex.IsMatch(mutedValue) && postText.Contains(mutedValue) then
                        true
                    else
                        // Check individual words split by whitespace
                        let words = wordBoundaryRegex.Split(postText)

                        let matchesWord =
                            words
                            |> Array.exists (fun word ->
                                if word = mutedValue then
                                    true
                                else
                                    let trimmed = stripLeadingTrailingPunctuation word

                                    if mutedValue = trimmed then
                                        true
                                    elif mutedValue.Length > trimmed.Length then
                                        false
                                    elif punctuationRegex.IsMatch(trimmed) then
                                        // Exit case for slashes -- "and/or" should not match "Andor"
                                        if slashRegex.IsMatch(trimmed) then
                                            false
                                        else
                                            let spacedWord = punctuationRegex.Replace(trimmed, " ")

                                            if spacedWord = mutedValue then
                                                true
                                            else
                                                let contiguousWord = spacedWord.Replace(" ", "")

                                                if contiguousWord = mutedValue then
                                                    true
                                                else
                                                    let wordParts = punctuationRegex.Split(trimmed)
                                                    wordParts |> Array.exists (fun part -> part = mutedValue)
                                    else
                                        false)

                        matchesWord

    /// Check whether any of the muted words match the given text and tags.
    /// Returns true if any muted word matches.
    let hasMutedWord
        (mutedWords: MutedWord list)
        (text: string)
        (tags: string list)
        (languages: string list)
        : bool =
        mutedWords
        |> List.exists (fun mw -> matchesMutedWord mw text tags languages)
