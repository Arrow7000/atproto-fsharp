module FSharp.ATProto.Bluesky.Tests.BlueskyTests

open Expecto
open System.Net
open System.Text.Json
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open TestHelpers

let private testSession =
    { AccessJwt = "test-jwt"; RefreshJwt = "test-refresh"; Did = "did:plc:testuser"; Handle = "test.bsky.social" }

let private createRecordAgent (captureRequest: System.Net.Http.HttpRequestMessage -> unit) =
    let agent = createMockAgent (fun req ->
        captureRequest req
        jsonResponse HttpStatusCode.OK {| uri = "at://did:plc:testuser/app.bsky.feed.post/abc123"; cid = "bafyreiabc123" |})
    agent.Session <- Some testSession
    agent

let private deleteRecordAgent (captureRequest: System.Net.Http.HttpRequestMessage -> unit) =
    let agent = createMockAgent (fun req ->
        captureRequest req
        jsonResponse HttpStatusCode.OK {| |})
    agent.Session <- Some testSession
    agent

[<Tests>]
let postTests =
    testList "Bluesky.post" [
        testCase "postWith creates post with correct collection" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.postWith agent "Hello world" [] |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let req = captured.Value
            let body = req.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.feed.post" "collection in body"
            Expect.stringContains body "did:plc:testuser" "repo = session DID"
            Expect.stringContains body "Hello world" "text in record"

        testCase "postWith includes facets when provided" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let facets : AppBskyRichtext.Facet.Facet list = [
                { Index = { ByteStart = 0L; ByteEnd = 5L }
                  Features = [ JsonSerializer.SerializeToElement({| ``$type`` = "app.bsky.richtext.facet#tag"; tag = "hello" |}) ] }
            ]
            let result = Bluesky.postWith agent "#hello world" facets |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
    ]

[<Tests>]
let likeTests =
    testList "Bluesky.like" [
        testCase "like creates record with correct collection and subject" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.like agent "at://did:plc:other/app.bsky.feed.post/abc" "bafyreiabc" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.feed.like" "like collection"
            Expect.stringContains body "bafyreiabc" "cid in subject"

        testCase "repost creates record with correct collection" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.repost agent "at://did:plc:other/app.bsky.feed.post/abc" "bafyreiabc" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.feed.repost" "repost collection"
    ]

[<Tests>]
let followTests =
    testList "Bluesky.follow" [
        testCase "follow creates record with DID subject" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.follow agent "did:plc:other" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.graph.follow" "follow collection"
            Expect.stringContains body "did:plc:other" "subject DID"

        testCase "block creates record with DID subject" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.block agent "did:plc:other" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.graph.block" "block collection"
    ]

[<Tests>]
let deleteTests =
    testList "Bluesky.deleteRecord" [
        testCase "deleteRecord parses AT-URI and sends correct request" <| fun _ ->
            let mutable captured = None
            let agent = deleteRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.deleteRecord agent "at://did:plc:testuser/app.bsky.feed.post/abc123" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.feed.post" "collection"
            Expect.stringContains body "abc123" "rkey"
            Expect.stringContains body "did:plc:testuser" "repo"
    ]

[<Tests>]
let replyTests =
    testList "Bluesky.reply" [
        testCase "reply includes root and parent refs" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result =
                Bluesky.reply agent "A reply"
                    "at://did:plc:p/app.bsky.feed.post/parent" "bafyparent"
                    "at://did:plc:r/app.bsky.feed.post/root" "bafyroot"
                |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "bafyparent" "parent cid"
            Expect.stringContains body "bafyroot" "root cid"
    ]
