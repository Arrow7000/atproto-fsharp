module FSharp.ATProto.Bluesky.Tests.RichTextManipulationTests

open System.Text
open Expecto
open FSharp.ATProto.Bluesky

/// Helper to create a facet with a tag feature at given byte range
let private tagFacet (byteStart : int) (byteEnd : int) (tag : string) : AppBskyRichtext.Facet.Facet =
    { Index =
        { ByteStart = int64 byteStart
          ByteEnd = int64 byteEnd }
      Features = [ AppBskyRichtext.Facet.FacetFeaturesItem.Tag { Tag = tag } ] }

/// Helper to create a facet with a link feature at given byte range
let private linkFacet (byteStart : int) (byteEnd : int) (uri : string) : AppBskyRichtext.Facet.Facet =
    { Index =
        { ByteStart = int64 byteStart
          ByteEnd = int64 byteEnd }
      Features =
        [ AppBskyRichtext.Facet.FacetFeaturesItem.Link
              { Uri = FSharp.ATProto.Syntax.Uri.parse uri |> Result.defaultWith failwith } ] }

/// Helper to create a facet with a mention feature at given byte range
let private mentionFacet (byteStart : int) (byteEnd : int) (did : string) : AppBskyRichtext.Facet.Facet =
    { Index =
        { ByteStart = int64 byteStart
          ByteEnd = int64 byteEnd }
      Features =
        [ AppBskyRichtext.Facet.FacetFeaturesItem.Mention
              { Did = FSharp.ATProto.Syntax.Did.parse did |> Result.defaultWith failwith } ] }

// ──────────────────────────────────────────────────────────────────
//  RichText.create / RichText.plain
// ──────────────────────────────────────────────────────────────────

[<Tests>]
let createTests =
    testList
        "RichText.create and plain"
        [ testCase "create bundles text and facets"
          <| fun _ ->
              let facet = tagFacet 6 14 "atproto"
              let rt = RichText.create "Hello #atproto" [ facet ]
              Expect.equal rt.Text "Hello #atproto" "text preserved"
              Expect.equal rt.Facets.Length 1 "one facet"

          testCase "plain creates RichTextValue with no facets"
          <| fun _ ->
              let rt = RichText.plain "Hello world"
              Expect.equal rt.Text "Hello world" "text preserved"
              Expect.equal rt.Facets [] "no facets" ]

// ──────────────────────────────────────────────────────────────────
//  RichText.insert
// ──────────────────────────────────────────────────────────────────

[<Tests>]
let insertTests =
    testList
        "RichText.insert"
        [ testCase "insert at beginning shifts all facets forward"
          <| fun _ ->
              let rt =
                  RichText.create "Hello #test" [ tagFacet 6 11 "test" ]

              let result = RichText.insert 0 "Hey " rt
              Expect.equal result.Text "Hey Hello #test" "text inserted"
              Expect.equal result.Facets.[0].Index.ByteStart 10L "facet start shifted by 4"
              Expect.equal result.Facets.[0].Index.ByteEnd 15L "facet end shifted by 4"

          testCase "insert at end does not affect facets"
          <| fun _ ->
              let rt =
                  RichText.create "Hello #test" [ tagFacet 6 11 "test" ]

              let result = RichText.insert 11 "!" rt
              Expect.equal result.Text "Hello #test!" "text appended"
              Expect.equal result.Facets.[0].Index.ByteStart 6L "facet start unchanged"
              Expect.equal result.Facets.[0].Index.ByteEnd 11L "facet end unchanged"

          testCase "insert before facet shifts it"
          <| fun _ ->
              // "ab #tag cd" -- tag facet at bytes 3..7
              let rt = RichText.create "ab #tag cd" [ tagFacet 3 7 "tag" ]
              let result = RichText.insert 2 "XX" rt
              // "abXX #tag cd"
              Expect.equal result.Text "abXX #tag cd" "text correct"
              Expect.equal result.Facets.[0].Index.ByteStart 5L "start shifted by 2"
              Expect.equal result.Facets.[0].Index.ByteEnd 9L "end shifted by 2"

          testCase "insert within facet expands it"
          <| fun _ ->
              // "Hello #test world" -- tag facet at bytes 6..11 (#test)
              let rt =
                  RichText.create "Hello #test world" [ tagFacet 6 11 "test" ]

              // Insert "XX" at byte 8 (within the facet)
              let result = RichText.insert 8 "XX" rt
              Expect.equal result.Facets.[0].Index.ByteStart 6L "start unchanged"
              Expect.equal result.Facets.[0].Index.ByteEnd 13L "end expanded by 2"

          testCase "insert at facet start shifts the facet"
          <| fun _ ->
              let rt = RichText.create "Hello #tag" [ tagFacet 6 10 "tag" ]
              let result = RichText.insert 6 "XX" rt
              Expect.equal result.Facets.[0].Index.ByteStart 8L "start shifted"
              Expect.equal result.Facets.[0].Index.ByteEnd 12L "end shifted"

          testCase "insert with multiple facets adjusts all correctly"
          <| fun _ ->
              // "Hey #a and #b"
              //      ^4-6     ^11-13
              let rt =
                  RichText.create "Hey #a and #b" [ tagFacet 4 6 "a"; tagFacet 11 13 "b" ]

              // Insert "XX" at byte 7 (between the two facets)
              let result = RichText.insert 7 "XX" rt
              Expect.equal result.Facets.[0].Index.ByteStart 4L "first facet start unchanged"
              Expect.equal result.Facets.[0].Index.ByteEnd 6L "first facet end unchanged"
              Expect.equal result.Facets.[1].Index.ByteStart 13L "second facet start shifted by 2"
              Expect.equal result.Facets.[1].Index.ByteEnd 15L "second facet end shifted by 2"

          testCase "insert with emoji text computes byte offsets correctly"
          <| fun _ ->
              // "Hello #tag" -- tag at bytes 6..10
              let rt = RichText.create "Hello #tag" [ tagFacet 6 10 "tag" ]
              // Insert emoji (4 bytes) at start
              let result = RichText.insert 0 "\U0001F44B" rt
              Expect.equal result.Text "\U0001F44BHello #tag" "text with emoji"
              Expect.equal result.Facets.[0].Index.ByteStart 10L "shifted by 4 bytes (emoji)"
              Expect.equal result.Facets.[0].Index.ByteEnd 14L "end shifted by 4"

          testCase "insert empty string is identity"
          <| fun _ ->
              let rt = RichText.create "Hello #tag" [ tagFacet 6 10 "tag" ]
              let result = RichText.insert 3 "" rt
              Expect.equal result.Text "Hello #tag" "text unchanged"
              Expect.equal result.Facets.[0].Index.ByteStart 6L "start unchanged"
              Expect.equal result.Facets.[0].Index.ByteEnd 10L "end unchanged"

          testCase "insert into empty text"
          <| fun _ ->
              let rt = RichText.plain ""
              let result = RichText.insert 0 "Hello" rt
              Expect.equal result.Text "Hello" "text inserted"
              Expect.equal result.Facets [] "no facets" ]

// ──────────────────────────────────────────────────────────────────
//  RichText.delete
// ──────────────────────────────────────────────────────────────────

[<Tests>]
let deleteTests =
    testList
        "RichText.delete"
        [ testCase "delete before facet shifts it back"
          <| fun _ ->
              // "Hello #tag" -- tag at 6..10
              let rt = RichText.create "Hello #tag" [ tagFacet 6 10 "tag" ]
              // Delete "He" (bytes 0..2)
              let result = RichText.delete 0 2 rt
              Expect.equal result.Text "llo #tag" "text deleted"
              Expect.equal result.Facets.[0].Index.ByteStart 4L "start shifted back by 2"
              Expect.equal result.Facets.[0].Index.ByteEnd 8L "end shifted back by 2"

          testCase "delete after facet does not affect it"
          <| fun _ ->
              // "Hello #tag world" -- tag at 6..10
              let rt =
                  RichText.create "Hello #tag world" [ tagFacet 6 10 "tag" ]

              // Delete " world" (bytes 10..16)
              let result = RichText.delete 10 16 rt
              Expect.equal result.Text "Hello #tag" "trailing text removed"
              Expect.equal result.Facets.[0].Index.ByteStart 6L "start unchanged"
              Expect.equal result.Facets.[0].Index.ByteEnd 10L "end unchanged"

          testCase "delete entire facet removes it"
          <| fun _ ->
              let rt = RichText.create "Hello #tag world" [ tagFacet 6 10 "tag" ]
              // Delete bytes 6..10 which covers the entire tag facet
              let result = RichText.delete 6 10 rt
              Expect.equal result.Text "Hello  world" "facet text removed"
              Expect.equal result.Facets.Length 0 "facet removed"

          testCase "delete overlapping start of facet truncates it"
          <| fun _ ->
              // "Hello #tag" -- tag at 6..10
              let rt = RichText.create "Hello #tag" [ tagFacet 6 10 "tag" ]
              // Delete bytes 4..8 (overlaps start of facet)
              let result = RichText.delete 4 8 rt
              Expect.equal result.Text "Hellag" "text after delete"
              // The facet started at 6, but we deleted 4..8, so:
              // - Original bytes 6..8 are within the deletion
              // - Original bytes 8..10 survive but shift to 4..6
              Expect.equal result.Facets.Length 1 "facet partially survives"
              Expect.equal result.Facets.[0].Index.ByteStart 4L "facet truncated start"
              Expect.equal result.Facets.[0].Index.ByteEnd 6L "facet truncated end"

          testCase "delete overlapping end of facet truncates it"
          <| fun _ ->
              // "Hello #tag world" -- tag at 6..10
              let rt =
                  RichText.create "Hello #tag world" [ tagFacet 6 10 "tag" ]

              // Delete bytes 8..12 (overlaps end of facet)
              let result = RichText.delete 8 12 rt
              Expect.equal result.Text "Hello #torld" "text after delete"
              Expect.equal result.Facets.Length 1 "facet partially survives"
              Expect.equal result.Facets.[0].Index.ByteStart 6L "facet start unchanged"
              Expect.equal result.Facets.[0].Index.ByteEnd 8L "facet truncated at deletion start"

          testCase "delete within facet shrinks it"
          <| fun _ ->
              // "xxxx #abcde yyyy" -- tag at 5..11 (#abcde)
              // x(0)x(1)x(2)x(3) (4)#(5)a(6)b(7)c(8)d(9)e(10) (11)y(12)y(13)y(14)y(15)
              let rt =
                  RichText.create "xxxx #abcde yyyy" [ tagFacet 5 11 "abcde" ]

              // Delete bytes 7..9 (within facet: "bc")
              let result = RichText.delete 7 9 rt
              Expect.equal result.Text "xxxx #ade yyyy" "middle removed"
              Expect.equal result.Facets.[0].Index.ByteStart 5L "start unchanged"
              Expect.equal result.Facets.[0].Index.ByteEnd 9L "end shrunk by 2"

          testCase "delete with multiple facets adjusts correctly"
          <| fun _ ->
              // "A #a B #b C"
              //    ^2-4  ^7-9
              let rt =
                  RichText.create "A #a B #b C" [ tagFacet 2 4 "a"; tagFacet 7 9 "b" ]

              // Delete bytes 4..7 (" B ")
              let result = RichText.delete 4 7 rt
              Expect.equal result.Text "A #a#b C" "gap removed"
              Expect.equal result.Facets.[0].Index.ByteStart 2L "first facet unchanged"
              Expect.equal result.Facets.[0].Index.ByteEnd 4L "first facet end unchanged"
              Expect.equal result.Facets.[1].Index.ByteStart 4L "second facet shifted back by 3"
              Expect.equal result.Facets.[1].Index.ByteEnd 6L "second facet end shifted"

          testCase "delete zero-length range is identity"
          <| fun _ ->
              let rt = RichText.create "Hello #tag" [ tagFacet 6 10 "tag" ]
              let result = RichText.delete 3 3 rt
              Expect.equal result.Text "Hello #tag" "text unchanged"
              Expect.equal result.Facets.[0].Index.ByteStart 6L "facet unchanged"

          testCase "delete with emoji adjusts byte offsets correctly"
          <| fun _ ->
              // "\U0001F44B #tag" -- emoji is 4 bytes, space is 1, tag at 5..9
              let rt =
                  RichText.create "\U0001F44B #tag" [ tagFacet 5 9 "tag" ]

              // Delete emoji (bytes 0..4)
              let result = RichText.delete 0 4 rt
              Expect.equal result.Text " #tag" "emoji removed"
              Expect.equal result.Facets.[0].Index.ByteStart 1L "facet shifted back by 4"
              Expect.equal result.Facets.[0].Index.ByteEnd 5L "facet end shifted" ]

// ──────────────────────────────────────────────────────────────────
//  RichText.segments
// ──────────────────────────────────────────────────────────────────

[<Tests>]
let segmentTests =
    testList
        "RichText.segments"
        [ testCase "plain text returns single unfaceted segment"
          <| fun _ ->
              let rt = RichText.plain "Hello world"
              let segs = RichText.segments rt
              Expect.equal segs.Length 1 "one segment"
              Expect.equal segs.[0].Text "Hello world" "full text"
              Expect.isNone segs.[0].Facet "no facet"

          testCase "empty text returns empty list"
          <| fun _ ->
              let rt = RichText.plain ""
              let segs = RichText.segments rt
              Expect.equal segs.Length 0 "no segments"

          testCase "single facet spanning entire text"
          <| fun _ ->
              let rt = RichText.create "#tag" [ tagFacet 0 4 "tag" ]
              let segs = RichText.segments rt
              Expect.equal segs.Length 1 "one segment"
              Expect.equal segs.[0].Text "#tag" "faceted text"
              Expect.isSome segs.[0].Facet "has facet"

          testCase "facet in middle creates three segments"
          <| fun _ ->
              // "Hello #tag world"
              let rt =
                  RichText.create "Hello #tag world" [ tagFacet 6 10 "tag" ]

              let segs = RichText.segments rt
              Expect.equal segs.Length 3 "three segments"
              Expect.equal segs.[0].Text "Hello " "before facet"
              Expect.isNone segs.[0].Facet "no facet before"
              Expect.equal segs.[1].Text "#tag" "faceted text"
              Expect.isSome segs.[1].Facet "has facet"
              Expect.equal segs.[2].Text " world" "after facet"
              Expect.isNone segs.[2].Facet "no facet after"

          testCase "facet at start creates two segments"
          <| fun _ ->
              // "#tag world"
              let rt = RichText.create "#tag world" [ tagFacet 0 4 "tag" ]
              let segs = RichText.segments rt
              Expect.equal segs.Length 2 "two segments"
              Expect.equal segs.[0].Text "#tag" "faceted text"
              Expect.isSome segs.[0].Facet "has facet"
              Expect.equal segs.[1].Text " world" "after facet"
              Expect.isNone segs.[1].Facet "no facet after"

          testCase "facet at end creates two segments"
          <| fun _ ->
              // "Hello #tag"
              let rt = RichText.create "Hello #tag" [ tagFacet 6 10 "tag" ]
              let segs = RichText.segments rt
              Expect.equal segs.Length 2 "two segments"
              Expect.equal segs.[0].Text "Hello " "before facet"
              Expect.isNone segs.[0].Facet "no facet before"
              Expect.equal segs.[1].Text "#tag" "faceted text"
              Expect.isSome segs.[1].Facet "has facet"

          testCase "multiple facets create alternating segments"
          <| fun _ ->
              // "A #a B #b C"
              //    ^2-4  ^7-9
              let rt =
                  RichText.create "A #a B #b C" [ tagFacet 2 4 "a"; tagFacet 7 9 "b" ]

              let segs = RichText.segments rt
              Expect.equal segs.Length 5 "five segments"
              Expect.equal segs.[0].Text "A " "gap before first"
              Expect.isNone segs.[0].Facet "no facet"
              Expect.equal segs.[1].Text "#a" "first faceted"
              Expect.isSome segs.[1].Facet "has facet"
              Expect.equal segs.[2].Text " B " "gap between"
              Expect.isNone segs.[2].Facet "no facet"
              Expect.equal segs.[3].Text "#b" "second faceted"
              Expect.isSome segs.[3].Facet "has facet"
              Expect.equal segs.[4].Text " C" "gap after"
              Expect.isNone segs.[4].Facet "no facet"

          testCase "adjacent facets with no gap"
          <| fun _ ->
              // "#a#b"
              let rt =
                  RichText.create "#a#b" [ tagFacet 0 2 "a"; tagFacet 2 4 "b" ]

              let segs = RichText.segments rt
              Expect.equal segs.Length 2 "two segments"
              Expect.equal segs.[0].Text "#a" "first"
              Expect.equal segs.[1].Text "#b" "second"

          testCase "segments with emoji text"
          <| fun _ ->
              // "\U0001F44B #tag end" -- emoji 4 bytes, space 1, #tag at 5..9
              let rt =
                  RichText.create "\U0001F44B #tag end" [ tagFacet 5 9 "tag" ]

              let segs = RichText.segments rt
              Expect.equal segs.Length 3 "three segments"
              Expect.equal segs.[0].Text "\U0001F44B " "emoji + space"
              Expect.equal segs.[1].Text "#tag" "tag"
              Expect.equal segs.[2].Text " end" "trailing"

          testCase "segments preserve facet features"
          <| fun _ ->
              let link = linkFacet 0 19 "https://example.com"
              let rt = RichText.create "https://example.com" [ link ]
              let segs = RichText.segments rt

              match segs.[0].Facet with
              | Some f ->
                  match f.Features.[0] with
                  | AppBskyRichtext.Facet.FacetFeaturesItem.Link l ->
                      Expect.equal (FSharp.ATProto.Syntax.Uri.value l.Uri) "https://example.com" "link preserved"
                  | _ -> failtest "expected link feature"
              | None -> failtest "expected facet" ]

// ──────────────────────────────────────────────────────────────────
//  RichText.sanitize
// ──────────────────────────────────────────────────────────────────

[<Tests>]
let sanitizeTests =
    testList
        "RichText.sanitize"
        [ testCase "trims leading and trailing whitespace"
          <| fun _ ->
              let rt = RichText.plain "  Hello world  "
              let result = RichText.sanitize rt
              Expect.equal result.Text "Hello world" "trimmed"

          testCase "collapses multiple spaces to one"
          <| fun _ ->
              let rt = RichText.plain "Hello    world"
              let result = RichText.sanitize rt
              Expect.equal result.Text "Hello world" "spaces collapsed"

          testCase "normalizes CRLF to LF"
          <| fun _ ->
              let rt = RichText.plain "Hello\r\nworld"
              let result = RichText.sanitize rt
              Expect.equal result.Text "Hello\nworld" "CRLF normalized"

          testCase "normalizes bare CR to LF"
          <| fun _ ->
              let rt = RichText.plain "Hello\rworld"
              let result = RichText.sanitize rt
              Expect.equal result.Text "Hello\nworld" "CR normalized"

          testCase "collapses excessive newlines to two"
          <| fun _ ->
              let rt = RichText.plain "Hello\n\n\nworld"
              let result = RichText.sanitize rt
              Expect.equal result.Text "Hello\n\nworld" "newlines collapsed to 2"

          testCase "preserves two consecutive newlines"
          <| fun _ ->
              let rt = RichText.plain "Hello\n\nworld"
              let result = RichText.sanitize rt
              Expect.equal result.Text "Hello\n\nworld" "two newlines preserved"

          testCase "sanitize adjusts facet positions after trim"
          <| fun _ ->
              // "  #tag  " -- tag at bytes 2..6
              let rt = RichText.create "  #tag  " [ tagFacet 2 6 "tag" ]
              let result = RichText.sanitize rt
              Expect.equal result.Text "#tag" "trimmed"
              Expect.equal result.Facets.Length 1 "facet preserved"
              Expect.equal result.Facets.[0].Index.ByteStart 0L "facet start adjusted"
              Expect.equal result.Facets.[0].Index.ByteEnd 4L "facet end adjusted"

          testCase "sanitize adjusts facet after space collapse"
          <| fun _ ->
              // "Hello    #tag" -- tag at bytes 9..13
              let rt =
                  RichText.create "Hello    #tag" [ tagFacet 9 13 "tag" ]

              let result = RichText.sanitize rt
              Expect.equal result.Text "Hello #tag" "spaces collapsed"
              Expect.equal result.Facets.Length 1 "facet preserved"
              Expect.equal result.Facets.[0].Index.ByteStart 6L "facet start adjusted"
              Expect.equal result.Facets.[0].Index.ByteEnd 10L "facet end adjusted"

          testCase "sanitize preserves facet when no changes needed"
          <| fun _ ->
              let rt = RichText.create "Hello #tag" [ tagFacet 6 10 "tag" ]
              let result = RichText.sanitize rt
              Expect.equal result.Text "Hello #tag" "text unchanged"
              Expect.equal result.Facets.[0].Index.ByteStart 6L "start preserved"
              Expect.equal result.Facets.[0].Index.ByteEnd 10L "end preserved"

          testCase "sanitize with no facets works"
          <| fun _ ->
              let rt = RichText.plain "  Hello    world  "
              let result = RichText.sanitize rt
              Expect.equal result.Text "Hello world" "sanitized"
              Expect.equal result.Facets [] "no facets"

          testCase "sanitize empty text"
          <| fun _ ->
              let rt = RichText.plain ""
              let result = RichText.sanitize rt
              Expect.equal result.Text "" "still empty"

          testCase "sanitize whitespace-only text"
          <| fun _ ->
              let rt = RichText.plain "   \n\n   "
              let result = RichText.sanitize rt
              Expect.equal result.Text "" "trimmed to empty"
              Expect.equal result.Facets [] "no facets"

          testCase "sanitize tabs collapsed to space"
          <| fun _ ->
              let rt = RichText.plain "Hello\t\tworld"
              let result = RichText.sanitize rt
              Expect.equal result.Text "Hello world" "tabs collapsed"

          testCase "sanitize does not collapse single newline"
          <| fun _ ->
              let rt = RichText.plain "Hello\nworld"
              let result = RichText.sanitize rt
              Expect.equal result.Text "Hello\nworld" "single newline preserved" ]

// ──────────────────────────────────────────────────────────────────
//  RichText.truncate
// ──────────────────────────────────────────────────────────────────

[<Tests>]
let truncateTests =
    testList
        "RichText.truncate"
        [ testCase "no truncation when text fits"
          <| fun _ ->
              let rt = RichText.create "Hello #tag" [ tagFacet 6 10 "tag" ]
              let result = RichText.truncate 100 rt
              Expect.equal result.Text "Hello #tag" "text unchanged"
              Expect.equal result.Facets.Length 1 "facet preserved"

          testCase "truncate ASCII text at byte limit"
          <| fun _ ->
              let rt = RichText.plain "Hello world"
              let result = RichText.truncate 5 rt
              Expect.equal result.Text "Hello" "truncated at 5 bytes"

          testCase "truncate removes facets beyond limit"
          <| fun _ ->
              // "Hello #tag" -- tag at 6..10
              let rt = RichText.create "Hello #tag" [ tagFacet 6 10 "tag" ]
              let result = RichText.truncate 8 rt
              Expect.equal result.Text "Hello #t" "text truncated"
              Expect.equal result.Facets.Length 0 "facet removed (extends beyond limit)"

          testCase "truncate preserves facets within limit"
          <| fun _ ->
              // "Hello #tag world" -- tag at 6..10
              let rt =
                  RichText.create "Hello #tag world" [ tagFacet 6 10 "tag" ]

              let result = RichText.truncate 10 rt
              Expect.equal result.Text "Hello #tag" "text truncated"
              Expect.equal result.Facets.Length 1 "facet preserved"
              Expect.equal result.Facets.[0].Index.ByteStart 6L "facet start"
              Expect.equal result.Facets.[0].Index.ByteEnd 10L "facet end"

          testCase "truncate at UTF-8 boundary with emoji"
          <| fun _ ->
              // "\U0001F44B hello" -- emoji is 4 bytes
              let rt = RichText.plain "\U0001F44B hello"
              // Truncate at 3 (mid-emoji) should back up to 0
              let result = RichText.truncate 3 rt
              Expect.equal result.Text "" "backed up past emoji"

          testCase "truncate at UTF-8 boundary with emoji keeps complete char"
          <| fun _ ->
              // "\U0001F44B hello" -- emoji is 4 bytes
              let rt = RichText.plain "\U0001F44B hello"
              // Truncate at 4 keeps the emoji
              let result = RichText.truncate 4 rt
              Expect.equal result.Text "\U0001F44B" "emoji preserved"

          testCase "truncate at UTF-8 boundary with 2-byte char"
          <| fun _ ->
              // "caf\u00E9" -- e-acute is 2 bytes, total 5 bytes
              let rt = RichText.plain "caf\u00E9"
              // Truncate at 4 (mid e-acute) should back up to 3
              let result = RichText.truncate 4 rt
              Expect.equal result.Text "caf" "backed up past 2-byte char"

          testCase "truncate with multiple facets keeps only those within limit"
          <| fun _ ->
              // "A #a B #b C"
              //    ^2-4  ^7-9
              let rt =
                  RichText.create "A #a B #b C" [ tagFacet 2 4 "a"; tagFacet 7 9 "b" ]

              let result = RichText.truncate 6 rt
              Expect.equal result.Text "A #a B" "truncated"
              Expect.equal result.Facets.Length 1 "only first facet within limit"
              Expect.equal result.Facets.[0].Index.ByteStart 2L "first facet preserved"

          testCase "truncate at 0 returns empty"
          <| fun _ ->
              let rt = RichText.create "Hello #tag" [ tagFacet 6 10 "tag" ]
              let result = RichText.truncate 0 rt
              Expect.equal result.Text "" "empty text"
              Expect.equal result.Facets.Length 0 "no facets"

          testCase "truncate preserves facets that end exactly at limit"
          <| fun _ ->
              // "#tag" -- tag at 0..4
              let rt = RichText.create "#tag" [ tagFacet 0 4 "tag" ]
              let result = RichText.truncate 4 rt
              Expect.equal result.Text "#tag" "text unchanged"
              Expect.equal result.Facets.Length 1 "facet preserved" ]

// ──────────────────────────────────────────────────────────────────
//  Combined operations
// ──────────────────────────────────────────────────────────────────

[<Tests>]
let combinedTests =
    testList
        "RichText combined operations"
        [ testCase "insert then delete is identity"
          <| fun _ ->
              let rt = RichText.create "Hello #tag" [ tagFacet 6 10 "tag" ]
              let inserted = RichText.insert 3 "XX" rt
              let result = RichText.delete 3 5 inserted
              Expect.equal result.Text "Hello #tag" "text restored"
              Expect.equal result.Facets.[0].Index.ByteStart 6L "facet restored start"
              Expect.equal result.Facets.[0].Index.ByteEnd 10L "facet restored end"

          testCase "truncate then segments covers truncated text"
          <| fun _ ->
              let rt =
                  RichText.create "Hello #tag world" [ tagFacet 6 10 "tag" ]

              let truncated = RichText.truncate 10 rt
              let segs = RichText.segments truncated
              Expect.equal segs.Length 2 "two segments"
              Expect.equal segs.[0].Text "Hello " "before facet"
              Expect.equal segs.[1].Text "#tag" "faceted text"

          testCase "sanitize then segments preserves structure"
          <| fun _ ->
              let rt =
                  RichText.create "  Hello    #tag  " [ tagFacet 11 15 "tag" ]

              let sanitized = RichText.sanitize rt
              Expect.equal sanitized.Text "Hello #tag" "sanitized"
              let segs = RichText.segments sanitized
              Expect.equal segs.Length 2 "two segments"
              Expect.equal segs.[0].Text "Hello " "gap before facet"
              Expect.equal segs.[1].Text "#tag" "faceted text"

          testCase "multiple inserts maintain facet ordering"
          <| fun _ ->
              // "A #tag B" -- tag at 2..6
              let rt =
                  RichText.create "A #tag B" [ tagFacet 2 6 "tag" ]

              // Insert "X" at 0: "XA #tag B" -- facet shifts to 3..7
              let r1 = RichText.insert 0 "X" rt
              Expect.equal r1.Facets.[0].Index.ByteStart 3L "facet shifted by first insert"
              Expect.equal r1.Facets.[0].Index.ByteEnd 7L "facet end shifted by first insert"
              // Insert "Y" at 5 (within facet): "XA #tYag B" -- facet expands to 3..8
              let r2 = RichText.insert 5 "Y" r1
              Expect.equal r2.Facets.[0].Index.ByteStart 3L "facet start unchanged"
              Expect.equal r2.Facets.[0].Index.ByteEnd 8L "facet expanded by second insert (within)" ]

// ──────────────────────────────────────────────────────────────────
//  Property-based tests
// ──────────────────────────────────────────────────────────────────

open FsCheck

[<Tests>]
let propertyTests =
    testList
        "RichText manipulation properties"
        [ testProperty "insert preserves text content"
          <| fun (text : NonNull<string>) (insert : NonNull<string>) ->
              let rt = RichText.plain text.Get
              let textBytes = Encoding.UTF8.GetByteCount text.Get
              // Insert at a valid position
              let pos = if textBytes = 0 then 0 else abs (text.Get.GetHashCode ()) % textBytes
              let result = RichText.insert pos insert.Get rt
              let resultBytes = Encoding.UTF8.GetByteCount result.Text
              let insertBytes = Encoding.UTF8.GetByteCount insert.Get
              Expect.equal resultBytes (textBytes + insertBytes) "byte length preserved"

          testProperty "truncate result is within byte limit"
          <| fun (text : NonNull<string>) (limit : PositiveInt) ->
              let rt = RichText.plain text.Get
              let maxBytes = limit.Get % 1000
              let result = RichText.truncate maxBytes rt
              let resultBytes = Encoding.UTF8.GetByteCount result.Text
              Expect.isLessThanOrEqual resultBytes maxBytes "within limit"

          testProperty "segments cover full text"
          <| fun (text : NonNull<string>) ->
              let rt = RichText.plain text.Get
              let segs = RichText.segments rt
              let reconstructed = segs |> List.map (fun s -> s.Text) |> System.String.Concat
              Expect.equal reconstructed text.Get "segments reconstruct original"

          testProperty "sanitize result has no leading/trailing whitespace"
          <| fun (text : NonNull<string>) ->
              let rt = RichText.plain text.Get
              let result = RichText.sanitize rt
              Expect.equal result.Text (result.Text.Trim ()) "no leading/trailing whitespace"

          testProperty "sanitize result has no excessive whitespace runs"
          <| fun (text : NonNull<string>) ->
              let rt = RichText.plain text.Get
              let result = RichText.sanitize rt
              Expect.isFalse (result.Text.Contains "  ") "no double spaces"

          testProperty "delete then segments still reconstruct"
          <| fun (text : NonNull<string>) ->
              let rt = RichText.plain text.Get
              let textBytes = Encoding.UTF8.GetByteCount text.Get

              if textBytes > 0 then
                  let s = abs (text.Get.GetHashCode ()) % textBytes
                  let e = min textBytes (s + abs (text.Get.Length.GetHashCode ()) % (textBytes - s + 1))
                  let result = RichText.delete s e rt
                  let segs = RichText.segments result
                  let reconstructed = segs |> List.map (fun s -> s.Text) |> System.String.Concat
                  Expect.equal reconstructed result.Text "segments match text after delete" ]
