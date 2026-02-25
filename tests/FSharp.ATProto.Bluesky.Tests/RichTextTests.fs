module FSharp.ATProto.Bluesky.Tests.RichTextTests

open Expecto
open FSharp.ATProto.Bluesky

[<Tests>]
let detectTests =
    testList "RichText.detect" [
        testCase "detects mention in text" <| fun _ ->
            let facets = RichText.detect "Hello @alice.bsky.social!"
            Expect.equal facets.Length 1 "should find one facet"
            match facets.[0] with
            | RichText.DetectedMention (s, e, h) ->
                Expect.equal s 6 "byteStart"
                Expect.equal e 24 "byteEnd"
                Expect.equal h "alice.bsky.social" "handle"
            | _ -> failtest "expected mention"

        testCase "detects link in text" <| fun _ ->
            let facets = RichText.detect "Check https://example.com ok"
            Expect.equal facets.Length 1 "should find one facet"
            match facets.[0] with
            | RichText.DetectedLink (s, e, u) ->
                Expect.equal s 6 "byteStart"
                Expect.equal e 25 "byteEnd"
                Expect.equal u "https://example.com" "uri"
            | _ -> failtest "expected link"

        testCase "detects hashtag in text" <| fun _ ->
            let facets = RichText.detect "Hello #atproto world"
            Expect.equal facets.Length 1 "should find one facet"
            match facets.[0] with
            | RichText.DetectedTag (s, e, t) ->
                Expect.equal s 6 "byteStart"
                Expect.equal e 14 "byteEnd"
                Expect.equal t "atproto" "tag"
            | _ -> failtest "expected tag"

        testCase "detects multiple facets" <| fun _ ->
            let facets = RichText.detect "Hi @alice.bsky.social check #atproto"
            Expect.equal facets.Length 2 "should find two facets"

        testCase "no facets in plain text" <| fun _ ->
            let facets = RichText.detect "Hello world"
            Expect.equal facets.Length 0 "should find no facets"

        testCase "mention must have dot (no bare @word)" <| fun _ ->
            let facets = RichText.detect "Hello @alice"
            Expect.equal facets.Length 0 "bare @word is not a mention"

        testCase "correct byte offsets with emoji" <| fun _ ->
            // 👋 is 4 bytes in UTF-8
            let facets = RichText.detect "👋 @alice.bsky.social"
            Expect.equal facets.Length 1 "should find one facet"
            match facets.[0] with
            | RichText.DetectedMention (s, e, _) ->
                Expect.equal s 5 "byteStart (4 bytes emoji + 1 byte space)"
                Expect.equal e 23 "byteEnd (5 + 18 = 23)"
            | _ -> failtest "expected mention"

        testCase "correct byte offsets with accented chars" <| fun _ ->
            // "Posição " has multibyte chars: ç (2 bytes), ã (2 bytes)
            let facets = RichText.detect "Posição @alice.bsky.social"
            Expect.equal facets.Length 1 "should find one facet"
            match facets.[0] with
            | RichText.DetectedMention (s, e, _) ->
                // P(1)+o(1)+s(1)+i(1)+ç(2)+ã(2)+o(1)+ (1) = 10 bytes
                Expect.equal s 10 "byteStart"
                Expect.equal e 28 "byteEnd (10 + 18 = 28)"
            | _ -> failtest "expected mention"

        testCase "strips trailing punctuation from links" <| fun _ ->
            let facets = RichText.detect "See https://example.com."
            match facets.[0] with
            | RichText.DetectedLink (_, _, u) ->
                Expect.equal u "https://example.com" "trailing period stripped"
            | _ -> failtest "expected link"

        testCase "hashtag excludes pure numeric" <| fun _ ->
            let facets = RichText.detect "Test #123"
            Expect.equal facets.Length 0 "pure numeric hashtag excluded"

        testCase "mention at start of text" <| fun _ ->
            let facets = RichText.detect "@alice.bsky.social hello"
            Expect.equal facets.Length 1 "mention at start"
            match facets.[0] with
            | RichText.DetectedMention (s, _, _) ->
                Expect.equal s 0 "byteStart at 0"
            | _ -> failtest "expected mention"

        testCase "link with path and query" <| fun _ ->
            let facets = RichText.detect "Go to https://example.com/path?q=1 now"
            match facets.[0] with
            | RichText.DetectedLink (_, _, u) ->
                Expect.equal u "https://example.com/path?q=1" "full URL preserved"
            | _ -> failtest "expected link"

        testCase "hashtag with fullwidth hash" <| fun _ ->
            // ＃ (U+FF03) is 3 bytes in UTF-8
            let facets = RichText.detect "Hello ＃atproto"
            Expect.equal facets.Length 1 "fullwidth hash detected"
            match facets.[0] with
            | RichText.DetectedTag (_, _, t) ->
                Expect.equal t "atproto" "tag without hash prefix"
            | _ -> failtest "expected tag"
    ]

open System.Text
open FsCheck

[<Tests>]
let propertyTests =
    testList "RichText.detect properties" [
        testProperty "byte ranges within text bounds" <| fun (text: NonNull<string>) ->
            let facets = RichText.detect text.Get
            let totalBytes = Encoding.UTF8.GetByteCount(text.Get)
            facets |> List.iter (fun f ->
                let s, e = match f with
                           | RichText.DetectedMention (s, e, _) -> s, e
                           | RichText.DetectedLink (s, e, _) -> s, e
                           | RichText.DetectedTag (s, e, _) -> s, e
                Expect.isLessThanOrEqual s totalBytes "start within bounds"
                Expect.isLessThanOrEqual e totalBytes "end within bounds"
                Expect.isLessThan s e "start < end")

        testProperty "detected facets are non-overlapping and sorted" <| fun (text: NonNull<string>) ->
            let facets = RichText.detect text.Get
            let ranges = facets |> List.map (fun f ->
                match f with
                | RichText.DetectedMention (s, e, _) -> s, e
                | RichText.DetectedLink (s, e, _) -> s, e
                | RichText.DetectedTag (s, e, _) -> s, e)
            ranges |> List.pairwise |> List.iter (fun ((_, e1), (s2, _)) ->
                Expect.isLessThanOrEqual e1 s2 "non-overlapping")
    ]
