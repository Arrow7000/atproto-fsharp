module FSharp.ATProto.Bluesky.Tests.PostExtensionTests

open Expecto
open System.Text.Json
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax

let private parseDid s = Did.parse s |> Result.defaultWith failwith
let private parseHandle s = Handle.parse s |> Result.defaultWith failwith
let private parseCid s = Cid.parse s |> Result.defaultWith failwith
let private parseAtUri s = AtUri.parse s |> Result.defaultWith failwith
let private parseAtDateTime s = AtDateTime.parse s |> Result.defaultWith failwith

let private dummyAuthor: AppBskyActor.Defs.ProfileViewBasic =
    { Associated = None
      Avatar = None
      CreatedAt = None
      Debug = None
      Did = parseDid "did:plc:testuser123"
      DisplayName = None
      Handle = parseHandle "test.bsky.social"
      Labels = None
      Pronouns = None
      Status = None
      Verification = None
      Viewer = None }

let private makePostView (recordJson: string) : AppBskyFeed.Defs.PostView =
    let record = JsonSerializer.Deserialize<JsonElement>(recordJson)

    { Author = dummyAuthor
      BookmarkCount = None
      Cid = parseCid "bafyreiabc123"
      Debug = None
      Embed = None
      IndexedAt = parseAtDateTime "2026-01-01T00:00:00.000Z"
      Labels = None
      LikeCount = None
      QuoteCount = None
      Record = record
      ReplyCount = None
      RepostCount = None
      Threadgate = None
      Uri = parseAtUri "at://did:plc:testuser123/app.bsky.feed.post/abc123"
      Viewer = None }

let private validPostJson =
    """{"$type":"app.bsky.feed.post","text":"Hello, world!","createdAt":"2026-01-01T00:00:00.000Z"}"""

let private postWithFacetsJson =
    """{"$type":"app.bsky.feed.post","text":"Hello @my-handle.bsky.social","createdAt":"2026-01-01T00:00:00.000Z","facets":[{"index":{"byteStart":6,"byteEnd":28},"features":[{"$type":"app.bsky.richtext.facet#mention","did":"did:plc:alice123"}]}]}"""

let private nonPostJson =
    """{"$type":"app.bsky.feed.like","subject":{"uri":"at://did:plc:x/app.bsky.feed.post/y","cid":"bafyreiabc123"},"createdAt":"2026-01-01T00:00:00.000Z"}"""

[<Tests>]
let postExtensionTests =
    testList "PostView extensions" [
        testCase "Text returns post text from a valid post record" <| fun _ ->
            let pv = makePostView validPostJson
            Expect.equal pv.Text "Hello, world!" "should extract text from post"

        testCase "Text returns empty string for a non-post record" <| fun _ ->
            let pv = makePostView nonPostJson
            Expect.equal pv.Text "" "should return empty string for non-post"

        testCase "Text returns empty string when record has no text property" <| fun _ ->
            let pv = makePostView """{"foo":"bar"}"""
            Expect.equal pv.Text "" "should return empty string when no text property"

        testCase "Facets returns facets list when present" <| fun _ ->
            let pv = makePostView postWithFacetsJson
            Expect.equal pv.Facets.Length 1 "should have one facet"
            let facet = pv.Facets.[0]
            Expect.equal facet.Index.ByteStart 6L "byteStart"
            Expect.equal facet.Index.ByteEnd 28L "byteEnd"

        testCase "Facets returns empty list when no facets" <| fun _ ->
            let pv = makePostView validPostJson
            Expect.equal pv.Facets [] "should return empty list when no facets"

        testCase "Facets returns empty list for non-post record" <| fun _ ->
            let pv = makePostView nonPostJson
            Expect.equal pv.Facets [] "should return empty list for non-post"

        testCase "AsPost returns Some for valid post record" <| fun _ ->
            let pv = makePostView validPostJson
            Expect.isSome pv.AsPost "should deserialize to Some"
            let post = pv.AsPost.Value
            Expect.equal post.Text "Hello, world!" "text should match"

        testCase "AsPost returns None for non-post record" <| fun _ ->
            let pv = makePostView nonPostJson
            Expect.isNone pv.AsPost "should return None for non-post"
    ]
