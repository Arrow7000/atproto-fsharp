module FSharp.ATProto.Bluesky.Tests.BlueskyTests

open Expecto
open System.Net
open System.Net.Http
open System.Text.Json
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax
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

let private parseAtUri s = AtUri.parse s |> Result.defaultWith failwith
let private parseCid s = Cid.parse s |> Result.defaultWith failwith
let private parseDid s = Did.parse s |> Result.defaultWith failwith

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

        testCase "postWith returns PostRef with typed fields" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.postWith agent "Hello world" [] |> Async.AwaitTask |> Async.RunSynchronously
            let postRef = Expect.wantOk result "should succeed"
            Expect.equal (AtUri.value postRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "uri"
            Expect.equal (Cid.value postRef.Cid) "bafyreiabc123" "cid"

        testCase "postWith includes facets when provided" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let facets : AppBskyRichtext.Facet.Facet list = [
                { Index = { ByteStart = 0L; ByteEnd = 5L }
                  Features = [ AppBskyRichtext.Facet.FacetFeaturesItem.Tag { Tag = "hello" } ] }
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
            let result = Bluesky.like agent (parseAtUri "at://did:plc:other/app.bsky.feed.post/abc") (parseCid "bafyreiabc") |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.feed.like" "like collection"
            Expect.stringContains body "bafyreiabc" "cid in subject"

        testCase "like returns AtUri of created record" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.like agent (parseAtUri "at://did:plc:other/app.bsky.feed.post/abc") (parseCid "bafyreiabc") |> Async.AwaitTask |> Async.RunSynchronously
            let uri = Expect.wantOk result "should succeed"
            Expect.equal (AtUri.value uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "returns uri"

        testCase "repost creates record with correct collection" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.repost agent (parseAtUri "at://did:plc:other/app.bsky.feed.post/abc") (parseCid "bafyreiabc") |> Async.AwaitTask |> Async.RunSynchronously
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
            let result = Bluesky.follow agent (parseDid "did:plc:other") |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.graph.follow" "follow collection"
            Expect.stringContains body "did:plc:other" "subject DID"

        testCase "follow returns AtUri of created record" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.follow agent (parseDid "did:plc:other") |> Async.AwaitTask |> Async.RunSynchronously
            let uri = Expect.wantOk result "should succeed"
            Expect.equal (AtUri.value uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "returns uri"

        testCase "block creates record with DID subject" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.block agent (parseDid "did:plc:other") |> Async.AwaitTask |> Async.RunSynchronously
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
            let result = Bluesky.deleteRecord agent (parseAtUri "at://did:plc:testuser/app.bsky.feed.post/abc123") |> Async.AwaitTask |> Async.RunSynchronously
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
            let parent = { Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"; Cid = parseCid "bafyparent" }
            let root = { Uri = parseAtUri "at://did:plc:r/app.bsky.feed.post/root"; Cid = parseCid "bafyroot" }
            let result =
                Bluesky.reply agent "A reply" parent root
                |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "bafyparent" "parent cid"
            Expect.stringContains body "bafyroot" "root cid"

        testCase "reply returns PostRef" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let parent = { Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"; Cid = parseCid "bafyparent" }
            let root = { Uri = parseAtUri "at://did:plc:r/app.bsky.feed.post/root"; Cid = parseCid "bafyroot" }
            let result =
                Bluesky.reply agent "A reply" parent root
                |> Async.AwaitTask |> Async.RunSynchronously
            let postRef = Expect.wantOk result "should succeed"
            Expect.equal (AtUri.value postRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "uri"
            Expect.equal (Cid.value postRef.Cid) "bafyreiabc123" "cid"
    ]

[<Tests>]
let blobTests =
    testList "Bluesky.uploadBlob" [
        testCase "uploadBlob sends binary content with correct content type" <| fun _ ->
            let mutable captured = None
            let agent = createMockAgent (fun req ->
                captured <- Some req
                jsonResponse HttpStatusCode.OK {| blob = {| ``$type`` = "blob"; ref = {| ``$link`` = "bafyblob" |}; mimeType = "image/png"; size = 100 |} |})
            agent.Session <- Some testSession
            let data = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy |] // PNG header bytes
            let result = Bluesky.uploadBlob agent data "image/png" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let req = captured.Value
            Expect.equal (req.Content.Headers.ContentType.MediaType) "image/png" "content type"
            Expect.equal (req.Method) HttpMethod.Post "POST method"

        testCase "uploadBlob includes Bearer auth header" <| fun _ ->
            let mutable captured = None
            let agent = createMockAgent (fun req ->
                captured <- Some req
                jsonResponse HttpStatusCode.OK {| blob = {| ``$type`` = "blob"; ref = {| ``$link`` = "bafyblob" |}; mimeType = "image/jpeg"; size = 50 |} |})
            agent.Session <- Some testSession
            let result = Bluesky.uploadBlob agent [| 0xFFuy; 0xD8uy |] "image/jpeg" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let req = captured.Value
            Expect.equal (req.Headers.Authorization.Scheme) "Bearer" "auth scheme"
            Expect.equal (req.Headers.Authorization.Parameter) "test-jwt" "auth token"

        testCase "uploadBlob returns error on failure" <| fun _ ->
            let agent = createMockAgent (fun _ ->
                jsonResponse HttpStatusCode.BadRequest {| error = "InvalidBlob"; message = "too large" |})
            agent.Session <- Some testSession
            let result = Bluesky.uploadBlob agent [| 0uy |] "image/png" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isError result "should fail"

        testCase "uploadBlob sends to correct XRPC endpoint" <| fun _ ->
            let mutable captured = None
            let agent = createMockAgent (fun req ->
                captured <- Some req
                jsonResponse HttpStatusCode.OK {| blob = {| ``$type`` = "blob"; ref = {| ``$link`` = "bafyblob" |}; mimeType = "image/png"; size = 10 |} |})
            agent.Session <- Some testSession
            let result = Bluesky.uploadBlob agent [| 0uy |] "image/png" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let req = captured.Value
            Expect.stringContains (req.RequestUri.ToString()) "com.atproto.repo.uploadBlob" "correct endpoint"
    ]

[<Tests>]
let imagePostTests =
    testList "Bluesky.postWithImages" [
        testCase "postWithImages uploads blob and creates post with embed" <| fun _ ->
            let mutable requestCount = 0
            let agent = createMockAgent (fun req ->
                requestCount <- requestCount + 1
                if req.RequestUri.PathAndQuery.Contains("uploadBlob") then
                    jsonResponse HttpStatusCode.OK
                        {| blob = {| ``$type`` = "blob"; ref = {| ``$link`` = "bafyblob" |}; mimeType = "image/png"; size = 100 |} |}
                else
                    jsonResponse HttpStatusCode.OK {| uri = "at://did:plc:testuser/app.bsky.feed.post/abc"; cid = "bafypost" |})
            agent.Session <- Some testSession
            let images = [ { Data = [| 0x89uy; 0x50uy |]; MimeType = "image/png"; AltText = "A test image" } ]
            let result = Bluesky.postWithImages agent "Check this out" images |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            Expect.isGreaterThanOrEqual requestCount 2 "at least 2 requests (upload + create)"

        testCase "postWithImages includes embed in record body" <| fun _ ->
            let mutable lastBody = ""
            let agent = createMockAgent (fun req ->
                let body = req.Content.ReadAsStringAsync().Result
                lastBody <- body
                if req.RequestUri.PathAndQuery.Contains("uploadBlob") then
                    jsonResponse HttpStatusCode.OK
                        {| blob = {| ``$type`` = "blob"; ref = {| ``$link`` = "bafyblob" |}; mimeType = "image/png"; size = 100 |} |}
                else
                    jsonResponse HttpStatusCode.OK {| uri = "at://did:plc:testuser/app.bsky.feed.post/abc"; cid = "bafypost" |})
            agent.Session <- Some testSession
            let images = [ { Data = [| 0x89uy |]; MimeType = "image/png"; AltText = "My image" } ]
            let result = Bluesky.postWithImages agent "Look at this" images |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            Expect.stringContains lastBody "app.bsky.embed.images" "embed type in body"
            Expect.stringContains lastBody "My image" "alt text in body"

        testCase "postWithImages returns PostRef" <| fun _ ->
            let agent = createMockAgent (fun req ->
                if req.RequestUri.PathAndQuery.Contains("uploadBlob") then
                    jsonResponse HttpStatusCode.OK
                        {| blob = {| ``$type`` = "blob"; ref = {| ``$link`` = "bafyblob" |}; mimeType = "image/png"; size = 100 |} |}
                else
                    jsonResponse HttpStatusCode.OK {| uri = "at://did:plc:testuser/app.bsky.feed.post/abc"; cid = "bafypost" |})
            agent.Session <- Some testSession
            let images = [ { Data = [| 0x89uy |]; MimeType = "image/png"; AltText = "Test" } ]
            let result = Bluesky.postWithImages agent "Test" images |> Async.AwaitTask |> Async.RunSynchronously
            let postRef = Expect.wantOk result "should succeed"
            Expect.equal (AtUri.value postRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc" "uri"
            Expect.equal (Cid.value postRef.Cid) "bafypost" "cid"

        testCase "postWithImages fails if blob upload fails" <| fun _ ->
            let agent = createMockAgent (fun req ->
                if req.RequestUri.PathAndQuery.Contains("uploadBlob") then
                    jsonResponse HttpStatusCode.BadRequest {| error = "BlobError"; message = "failed" |}
                else
                    jsonResponse HttpStatusCode.OK {| uri = "at://did:plc:testuser/app.bsky.feed.post/abc"; cid = "bafypost" |})
            agent.Session <- Some testSession
            let images = [ { Data = [| 0x89uy |]; MimeType = "image/png"; AltText = "fail" } ]
            let result = Bluesky.postWithImages agent "Should fail" images |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isError result "should fail when blob upload fails"
    ]
