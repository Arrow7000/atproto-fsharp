module FSharp.ATProto.Bluesky.Tests.BlueskyTests

open Expecto
open System.Net
open System.Net.Http
open System.Text.Json
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax
open TestHelpers

let private parseDid' s = Did.parse s |> Result.defaultWith failwith
let private parseHandle' s = Handle.parse s |> Result.defaultWith failwith

let private testSession =
    { AccessJwt = "test-jwt"; RefreshJwt = "test-refresh"; Did = parseDid' "did:plc:testuser"; Handle = parseHandle' "test.bsky.social" }

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

/// Helper to create a mock blob response with a valid CID
let private blobResponse mimeType size =
    {| blob = {| ``$type`` = "blob"; ref = {| ``$link`` = "bafyreiabc123" |}; mimeType = mimeType; size = size |} |}

let private testPostRef =
    { PostRef.Uri = parseAtUri "at://did:plc:other/app.bsky.feed.post/abc"; Cid = parseCid "bafyreiabc" }

[<Tests>]
let postTests =
    testList "Bluesky.post" [
        testCase "postWithFacets creates post with correct collection" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.postWithFacets agent "Hello world" [] |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let req = captured.Value
            let body = req.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.feed.post" "collection in body"
            Expect.stringContains body "did:plc:testuser" "repo = session DID"
            Expect.stringContains body "Hello world" "text in record"

        testCase "postWithFacets returns PostRef with typed fields" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.postWithFacets agent "Hello world" [] |> Async.AwaitTask |> Async.RunSynchronously
            let postRef = Expect.wantOk result "should succeed"
            Expect.equal (AtUri.value postRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "uri"
            Expect.equal (Cid.value postRef.Cid) "bafyreiabc123" "cid"

        testCase "postWithFacets includes facets when provided" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let facets : AppBskyRichtext.Facet.Facet list = [
                { Index = { ByteStart = 0L; ByteEnd = 5L }
                  Features = [ AppBskyRichtext.Facet.FacetFeaturesItem.Tag { Tag = "hello" } ] }
            ]
            let result = Bluesky.postWithFacets agent "#hello world" facets |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"

        testCase "postWithFacets returns NotLoggedIn error without session" <| fun _ ->
            let agent = createMockAgent (fun _ -> jsonResponse HttpStatusCode.OK {| |})
            // No session set
            let result = Bluesky.postWithFacets agent "Hello world" [] |> Async.AwaitTask |> Async.RunSynchronously
            let err = Expect.wantError result "should fail without session"
            Expect.equal err.StatusCode 401 "status code"
            Expect.equal err.Error (Some "NotLoggedIn") "error code"
            Expect.equal err.Message (Some "No active session") "message"
    ]

[<Tests>]
let likeTests =
    testList "Bluesky.like" [
        testCase "like creates record with correct collection and subject" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.like agent testPostRef |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.feed.like" "like collection"
            Expect.stringContains body "bafyreiabc" "cid in subject"

        testCase "like returns LikeRef with uri" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.like agent testPostRef |> Async.AwaitTask |> Async.RunSynchronously
            let likeRef = Expect.wantOk result "should succeed"
            Expect.equal (AtUri.value likeRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "returns uri"

        testCase "repost creates record with correct collection" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.repost agent testPostRef |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.feed.repost" "repost collection"

        testCase "repost returns RepostRef with uri" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.repost agent testPostRef |> Async.AwaitTask |> Async.RunSynchronously
            let repostRef = Expect.wantOk result "should succeed"
            Expect.equal (AtUri.value repostRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "returns uri"

        testCase "like returns NotLoggedIn error without session" <| fun _ ->
            let agent = createMockAgent (fun _ -> jsonResponse HttpStatusCode.OK {| |})
            let result = Bluesky.like agent testPostRef |> Async.AwaitTask |> Async.RunSynchronously
            let err = Expect.wantError result "should fail without session"
            Expect.equal err.StatusCode 401 "status code"
            Expect.equal err.Error (Some "NotLoggedIn") "error code"
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

        testCase "follow returns FollowRef with uri" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.follow agent (parseDid "did:plc:other") |> Async.AwaitTask |> Async.RunSynchronously
            let followRef = Expect.wantOk result "should succeed"
            Expect.equal (AtUri.value followRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "returns uri"

        testCase "block creates record with DID subject" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.block agent (parseDid "did:plc:other") |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.graph.block" "block collection"

        testCase "block returns BlockRef with uri" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.block agent (parseDid "did:plc:other") |> Async.AwaitTask |> Async.RunSynchronously
            let blockRef = Expect.wantOk result "should succeed"
            Expect.equal (AtUri.value blockRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "returns uri"

        testCase "follow returns NotLoggedIn error without session" <| fun _ ->
            let agent = createMockAgent (fun _ -> jsonResponse HttpStatusCode.OK {| |})
            let result = Bluesky.follow agent (parseDid "did:plc:other") |> Async.AwaitTask |> Async.RunSynchronously
            let err = Expect.wantError result "should fail without session"
            Expect.equal err.StatusCode 401 "status code"
    ]

[<Tests>]
let followUserTests =
    testList "Bluesky.followUser" [
        testCase "followUser with DID string passes through without resolution" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.followUser agent "did:plc:other" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.graph.follow" "follow collection"
            Expect.stringContains body "did:plc:other" "subject DID"

        testCase "followUser with handle resolves to DID first" <| fun _ ->
            let mutable captured = None
            let agent = createMockAgent (fun req ->
                if req.RequestUri.PathAndQuery.Contains("resolveHandle") then
                    jsonResponse HttpStatusCode.OK {| did = "did:plc:resolved" |}
                else
                    captured <- Some req
                    jsonResponse HttpStatusCode.OK {| uri = "at://did:plc:testuser/app.bsky.graph.follow/abc123"; cid = "bafyreiabc123" |})
            agent.Session <- Some testSession
            let result = Bluesky.followUser agent "alice.bsky.social" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "did:plc:resolved" "resolved DID used as subject"
            Expect.stringContains body "app.bsky.graph.follow" "follow collection"

        testCase "followUser returns error for invalid identifier" <| fun _ ->
            let agent = createRecordAgent (fun _ -> ())
            let result = Bluesky.followUser agent "not a valid handle or did" |> Async.AwaitTask |> Async.RunSynchronously
            let err = Expect.wantError result "should fail for invalid identifier"
            Expect.equal err.StatusCode 400 "status code"
            Expect.isSome err.Message "should have error message"

        testCase "followUser returns error when handle resolution fails" <| fun _ ->
            let agent = createMockAgent (fun req ->
                if req.RequestUri.PathAndQuery.Contains("resolveHandle") then
                    jsonResponse HttpStatusCode.BadRequest {| error = "HandleNotFound"; message = "handle not found" |}
                else
                    jsonResponse HttpStatusCode.OK {| uri = "at://did:plc:testuser/app.bsky.graph.follow/abc123"; cid = "bafyreiabc123" |})
            agent.Session <- Some testSession
            let result = Bluesky.followUser agent "nonexistent.bsky.social" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isError result "should fail when handle resolution fails"

        testCase "followUser returns FollowRef on success" <| fun _ ->
            let agent = createRecordAgent (fun _ -> ())
            let result = Bluesky.followUser agent "did:plc:other" |> Async.AwaitTask |> Async.RunSynchronously
            let followRef = Expect.wantOk result "should succeed"
            Expect.equal (AtUri.value followRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "returns uri"
    ]

[<Tests>]
let blockUserTests =
    testList "Bluesky.blockUser" [
        testCase "blockUser with DID string passes through without resolution" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.blockUser agent "did:plc:other" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.graph.block" "block collection"
            Expect.stringContains body "did:plc:other" "subject DID"

        testCase "blockUser with handle resolves to DID first" <| fun _ ->
            let mutable captured = None
            let agent = createMockAgent (fun req ->
                if req.RequestUri.PathAndQuery.Contains("resolveHandle") then
                    jsonResponse HttpStatusCode.OK {| did = "did:plc:resolved" |}
                else
                    captured <- Some req
                    jsonResponse HttpStatusCode.OK {| uri = "at://did:plc:testuser/app.bsky.graph.block/abc123"; cid = "bafyreiabc123" |})
            agent.Session <- Some testSession
            let result = Bluesky.blockUser agent "alice.bsky.social" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "did:plc:resolved" "resolved DID used as subject"
            Expect.stringContains body "app.bsky.graph.block" "block collection"

        testCase "blockUser returns error for invalid identifier" <| fun _ ->
            let agent = createRecordAgent (fun _ -> ())
            let result = Bluesky.blockUser agent "not a valid handle or did" |> Async.AwaitTask |> Async.RunSynchronously
            let err = Expect.wantError result "should fail for invalid identifier"
            Expect.equal err.StatusCode 400 "status code"
            Expect.isSome err.Message "should have error message"

        testCase "blockUser returns error when handle resolution fails" <| fun _ ->
            let agent = createMockAgent (fun req ->
                if req.RequestUri.PathAndQuery.Contains("resolveHandle") then
                    jsonResponse HttpStatusCode.BadRequest {| error = "HandleNotFound"; message = "handle not found" |}
                else
                    jsonResponse HttpStatusCode.OK {| uri = "at://did:plc:testuser/app.bsky.graph.block/abc123"; cid = "bafyreiabc123" |})
            agent.Session <- Some testSession
            let result = Bluesky.blockUser agent "nonexistent.bsky.social" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isError result "should fail when handle resolution fails"

        testCase "blockUser returns BlockRef on success" <| fun _ ->
            let agent = createRecordAgent (fun _ -> ())
            let result = Bluesky.blockUser agent "did:plc:other" |> Async.AwaitTask |> Async.RunSynchronously
            let blockRef = Expect.wantOk result "should succeed"
            Expect.equal (AtUri.value blockRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "returns uri"
    ]

[<Tests>]
let undoTests =
    testList "Bluesky.undo" [
        testCase "unlike delegates to deleteRecord with LikeRef uri" <| fun _ ->
            let mutable captured = None
            let agent = deleteRecordAgent (fun req -> captured <- Some req)
            let likeRef = { LikeRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.like/abc123" }
            let result = Bluesky.unlike agent likeRef |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.feed.like" "collection"
            Expect.stringContains body "abc123" "rkey"

        testCase "unrepost delegates to deleteRecord with RepostRef uri" <| fun _ ->
            let mutable captured = None
            let agent = deleteRecordAgent (fun req -> captured <- Some req)
            let repostRef = { RepostRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.repost/def456" }
            let result = Bluesky.unrepost agent repostRef |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.feed.repost" "collection"
            Expect.stringContains body "def456" "rkey"

        testCase "unfollow delegates to deleteRecord with FollowRef uri" <| fun _ ->
            let mutable captured = None
            let agent = deleteRecordAgent (fun req -> captured <- Some req)
            let followRef = { FollowRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.graph.follow/ghi789" }
            let result = Bluesky.unfollow agent followRef |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.graph.follow" "collection"
            Expect.stringContains body "ghi789" "rkey"

        testCase "unblock delegates to deleteRecord with BlockRef uri" <| fun _ ->
            let mutable captured = None
            let agent = deleteRecordAgent (fun req -> captured <- Some req)
            let blockRef = { BlockRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.graph.block/jkl012" }
            let result = Bluesky.unblock agent blockRef |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.graph.block" "collection"
            Expect.stringContains body "jkl012" "rkey"
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

        testCase "deleteRecord returns error for AT-URI without collection" <| fun _ ->
            let agent = deleteRecordAgent (fun _ -> ())
            let result = Bluesky.deleteRecord agent (parseAtUri "at://did:plc:testuser") |> Async.AwaitTask |> Async.RunSynchronously
            let err = Expect.wantError result "should fail without collection"
            Expect.equal err.StatusCode 400 "status code"
            Expect.isSome err.Message "should have message"

        testCase "deleteRecord returns error for AT-URI without rkey" <| fun _ ->
            let agent = deleteRecordAgent (fun _ -> ())
            let result = Bluesky.deleteRecord agent (parseAtUri "at://did:plc:testuser/app.bsky.feed.post") |> Async.AwaitTask |> Async.RunSynchronously
            let err = Expect.wantError result "should fail without rkey"
            Expect.equal err.StatusCode 400 "status code"
            Expect.isSome err.Message "should have message"
    ]

[<Tests>]
let replyTests =
    testList "Bluesky.replyWithKnownRoot" [
        testCase "replyWithKnownRoot includes root and parent refs" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let parent = { PostRef.Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"; Cid = parseCid "bafyparent" }
            let root = { PostRef.Uri = parseAtUri "at://did:plc:r/app.bsky.feed.post/root"; Cid = parseCid "bafyroot00" }
            let result =
                Bluesky.replyWithKnownRoot agent "A reply" parent root
                |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "bafyparent" "parent cid"
            Expect.stringContains body "bafyroot00" "root cid"

        testCase "replyWithKnownRoot returns PostRef" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let parent = { PostRef.Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"; Cid = parseCid "bafyparent" }
            let root = { PostRef.Uri = parseAtUri "at://did:plc:r/app.bsky.feed.post/root"; Cid = parseCid "bafyroot00" }
            let result =
                Bluesky.replyWithKnownRoot agent "A reply" parent root
                |> Async.AwaitTask |> Async.RunSynchronously
            let postRef = Expect.wantOk result "should succeed"
            Expect.equal (AtUri.value postRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "uri"
            Expect.equal (Cid.value postRef.Cid) "bafyreiabc123" "cid"
    ]

/// Creates a mock agent that handles both getPosts (GET) and createRecord (POST) requests.
/// The recordJson is the record field returned in the PostView for getPosts.
let private replyToMockAgent (recordJson: obj) (captureCreateRecord: HttpRequestMessage -> unit) =
    let agent = createMockAgent (fun req ->
        let path = req.RequestUri.AbsolutePath
        if path.Contains("app.bsky.feed.getPosts") then
            // Return a PostView with the given record JSON
            jsonResponse HttpStatusCode.OK
                {| posts = [|
                    {| uri = "at://did:plc:p/app.bsky.feed.post/parent"
                       cid = "bafyparent"
                       author = {| did = "did:plc:p"; handle = "parent.test"; displayName = "Parent" |}
                       record = recordJson
                       indexedAt = "2026-01-01T00:00:00Z" |} |] |}
        elif path.Contains("com.atproto.repo.createRecord") then
            captureCreateRecord req
            jsonResponse HttpStatusCode.OK {| uri = "at://did:plc:testuser/app.bsky.feed.post/reply1"; cid = "bafyreireply1" |}
        else
            jsonResponse HttpStatusCode.NotFound {| error = "NotFound" |})
    agent.Session <- Some testSession
    agent

[<Tests>]
let replyToTests =
    testList "Bluesky.replyTo" [
        testCase "replyTo top-level post uses parent as root" <| fun _ ->
            let mutable captured = None
            // A top-level post has no "reply" field in its record
            let agent = replyToMockAgent
                            {| text = "Hello"; createdAt = "2026-01-01T00:00:00Z" |}
                            (fun req -> captured <- Some req)
            let parent = { PostRef.Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"; Cid = parseCid "bafyparent" }
            let result =
                Bluesky.replyTo agent "My reply" parent
                |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            // Both parent and root should be the same (the parent post itself)
            Expect.stringContains body "bafyparent" "parent cid in body"
            Expect.stringContains body "at://did:plc:p/app.bsky.feed.post/parent" "parent uri used as root"

        testCase "replyTo reply post extracts root from reply field" <| fun _ ->
            let mutable captured = None
            // A reply post has a "reply" field with root info
            let agent = replyToMockAgent
                            {| text = "A reply"
                               createdAt = "2026-01-01T00:00:00Z"
                               reply = {| root = {| uri = "at://did:plc:r/app.bsky.feed.post/root"; cid = "bafyroot00" |}
                                          parent = {| uri = "at://did:plc:x/app.bsky.feed.post/other"; cid = "bafyother0" |} |} |}
                            (fun req -> captured <- Some req)
            let parent = { PostRef.Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"; Cid = parseCid "bafyparent" }
            let result =
                Bluesky.replyTo agent "Deeper reply" parent
                |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            // Parent should be the one we passed in
            Expect.stringContains body "bafyparent" "parent cid in body"
            // Root should be extracted from the reply field, NOT the parent
            Expect.stringContains body "bafyroot00" "root cid from reply field"
            Expect.stringContains body "at://did:plc:r/app.bsky.feed.post/root" "root uri from reply field"

        testCase "replyTo returns error for invalid root URI in reply field" <| fun _ ->
            let agent = replyToMockAgent
                            {| text = "A reply"
                               reply = {| root = {| uri = "not-a-valid-uri"; cid = "bafyroot00" |}
                                          parent = {| uri = "at://did:plc:x/app.bsky.feed.post/x"; cid = "bafyother0" |} |} |}
                            (fun _ -> ())
            let parent = { PostRef.Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"; Cid = parseCid "bafyparent" }
            let result =
                Bluesky.replyTo agent "Reply" parent
                |> Async.AwaitTask |> Async.RunSynchronously
            let err = Expect.wantError result "should fail for invalid root URI"
            Expect.equal err.StatusCode 400 "status code"
            Expect.isSome err.Message "should have error message"

        testCase "replyTo returns error for malformed reply field" <| fun _ ->
            // reply field exists but has wrong structure (no root property)
            let agent = replyToMockAgent
                            {| text = "A reply"
                               reply = {| wrong = "structure" |} |}
                            (fun _ -> ())
            let parent = { PostRef.Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"; Cid = parseCid "bafyparent" }
            let result =
                Bluesky.replyTo agent "Reply" parent
                |> Async.AwaitTask |> Async.RunSynchronously
            let err = Expect.wantError result "should fail for malformed reply"
            Expect.equal err.StatusCode 400 "status code"
            Expect.isSome err.Message "should have error message"

        testCase "replyTo returns PostRef on success" <| fun _ ->
            let agent = replyToMockAgent
                            {| text = "Hello" |}
                            (fun _ -> ())
            let parent = { PostRef.Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"; Cid = parseCid "bafyparent" }
            let result =
                Bluesky.replyTo agent "Reply" parent
                |> Async.AwaitTask |> Async.RunSynchronously
            let postRef = Expect.wantOk result "should succeed"
            Expect.equal (AtUri.value postRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/reply1" "uri"
            Expect.equal (Cid.value postRef.Cid) "bafyreireply1" "cid"

        testCase "replyTo returns error when getPosts fails" <| fun _ ->
            let agent = createMockAgent (fun _ ->
                jsonResponse HttpStatusCode.InternalServerError {| error = "InternalError"; message = "server error" |})
            agent.Session <- Some testSession
            let parent = { PostRef.Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"; Cid = parseCid "bafyparent" }
            let result =
                Bluesky.replyTo agent "Reply" parent
                |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isError result "should fail when getPosts fails"

        testCase "replyTo returns error when parent post not found" <| fun _ ->
            let agent = createMockAgent (fun _ ->
                jsonResponse HttpStatusCode.OK {| posts = [||] |})
            agent.Session <- Some testSession
            let parent = { PostRef.Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"; Cid = parseCid "bafyparent" }
            let result =
                Bluesky.replyTo agent "Reply" parent
                |> Async.AwaitTask |> Async.RunSynchronously
            let err = Expect.wantError result "should fail when post not found"
            Expect.equal err.StatusCode 400 "status code"
            Expect.isSome err.Message "should have error message"
    ]

[<Tests>]
let blobTests =
    testList "Bluesky.uploadBlob" [
        testCase "uploadBlob sends binary content with correct content type" <| fun _ ->
            let mutable captured = None
            let agent = createMockAgent (fun req ->
                captured <- Some req
                jsonResponse HttpStatusCode.OK (blobResponse "image/png" 100))
            agent.Session <- Some testSession
            let data = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy |] // PNG header bytes
            let result = Bluesky.uploadBlob agent data "image/png" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let req = captured.Value
            Expect.equal (req.Content.Headers.ContentType.MediaType) "image/png" "content type"
            Expect.equal (req.Method) HttpMethod.Post "POST method"

        testCase "uploadBlob returns BlobRef with typed fields" <| fun _ ->
            let agent = createMockAgent (fun _ ->
                jsonResponse HttpStatusCode.OK {| blob = {| ``$type`` = "blob"; ref = {| ``$link`` = "bafyreiabc123" |}; mimeType = "image/jpeg"; size = 54321 |} |})
            agent.Session <- Some testSession
            let result = Bluesky.uploadBlob agent [| 0xFFuy; 0xD8uy |] "image/jpeg" |> Async.AwaitTask |> Async.RunSynchronously
            let blobRef = Expect.wantOk result "should succeed"
            Expect.equal (Cid.value blobRef.Ref) "bafyreiabc123" "ref CID"
            Expect.equal blobRef.MimeType "image/jpeg" "mime type"
            Expect.equal blobRef.Size 54321L "size"

        testCase "uploadBlob preserves raw JSON in BlobRef" <| fun _ ->
            let agent = createMockAgent (fun _ ->
                jsonResponse HttpStatusCode.OK (blobResponse "image/png" 999))
            agent.Session <- Some testSession
            let result = Bluesky.uploadBlob agent [| 0uy |] "image/png" |> Async.AwaitTask |> Async.RunSynchronously
            let blobRef = Expect.wantOk result "should succeed"
            let jsonStr = blobRef.Json.ToString()
            Expect.stringContains jsonStr "blob" "$type in raw JSON"
            Expect.stringContains jsonStr "bafyreiabc123" "link in raw JSON"

        testCase "uploadBlob includes Bearer auth header" <| fun _ ->
            let mutable captured = None
            let agent = createMockAgent (fun req ->
                captured <- Some req
                jsonResponse HttpStatusCode.OK (blobResponse "image/jpeg" 50))
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
                jsonResponse HttpStatusCode.OK (blobResponse "image/png" 10))
            agent.Session <- Some testSession
            let result = Bluesky.uploadBlob agent [| 0uy |] "image/png" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let req = captured.Value
            Expect.stringContains (req.RequestUri.ToString()) "com.atproto.repo.uploadBlob" "correct endpoint"

        testCase "uploadBlob returns error when response missing blob property" <| fun _ ->
            let agent = createMockAgent (fun _ ->
                jsonResponse HttpStatusCode.OK {| unexpected = "data" |})
            agent.Session <- Some testSession
            let result = Bluesky.uploadBlob agent [| 0uy |] "image/png" |> Async.AwaitTask |> Async.RunSynchronously
            let err = Expect.wantError result "should fail when blob property missing"
            Expect.equal err.StatusCode 400 "status code"
            Expect.isSome err.Message "should have message"
    ]

[<Tests>]
let imagePostTests =
    testList "Bluesky.postWithImages" [
        testCase "postWithImages uploads blob and creates post with embed" <| fun _ ->
            let mutable requestCount = 0
            let agent = createMockAgent (fun req ->
                requestCount <- requestCount + 1
                if req.RequestUri.PathAndQuery.Contains("uploadBlob") then
                    jsonResponse HttpStatusCode.OK (blobResponse "image/png" 100)
                else
                    jsonResponse HttpStatusCode.OK {| uri = "at://did:plc:testuser/app.bsky.feed.post/abc"; cid = "bafyreiabc123" |})
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
                    jsonResponse HttpStatusCode.OK (blobResponse "image/png" 100)
                else
                    jsonResponse HttpStatusCode.OK {| uri = "at://did:plc:testuser/app.bsky.feed.post/abc"; cid = "bafyreiabc123" |})
            agent.Session <- Some testSession
            let images = [ { Data = [| 0x89uy |]; MimeType = "image/png"; AltText = "My image" } ]
            let result = Bluesky.postWithImages agent "Look at this" images |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            Expect.stringContains lastBody "app.bsky.embed.images" "embed type in body"
            Expect.stringContains lastBody "My image" "alt text in body"

        testCase "postWithImages returns PostRef" <| fun _ ->
            let agent = createMockAgent (fun req ->
                if req.RequestUri.PathAndQuery.Contains("uploadBlob") then
                    jsonResponse HttpStatusCode.OK (blobResponse "image/png" 100)
                else
                    jsonResponse HttpStatusCode.OK {| uri = "at://did:plc:testuser/app.bsky.feed.post/abc"; cid = "bafyreiabc123" |})
            agent.Session <- Some testSession
            let images = [ { Data = [| 0x89uy |]; MimeType = "image/png"; AltText = "Test" } ]
            let result = Bluesky.postWithImages agent "Test" images |> Async.AwaitTask |> Async.RunSynchronously
            let postRef = Expect.wantOk result "should succeed"
            Expect.equal (AtUri.value postRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc" "uri"
            Expect.equal (Cid.value postRef.Cid) "bafyreiabc123" "cid"

        testCase "postWithImages fails if blob upload fails" <| fun _ ->
            let agent = createMockAgent (fun req ->
                if req.RequestUri.PathAndQuery.Contains("uploadBlob") then
                    jsonResponse HttpStatusCode.BadRequest {| error = "BlobError"; message = "failed" |}
                else
                    jsonResponse HttpStatusCode.OK {| uri = "at://did:plc:testuser/app.bsky.feed.post/abc"; cid = "bafyreiabc123" |})
            agent.Session <- Some testSession
            let images = [ { Data = [| 0x89uy |]; MimeType = "image/png"; AltText = "fail" } ]
            let result = Bluesky.postWithImages agent "Should fail" images |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isError result "should fail when blob upload fails"
    ]

// ── Read convenience method helpers ─────────────────────────────────

let private queryAgent (captureRequest: HttpRequestMessage -> unit) (responseBody: obj) =
    let agent = createMockAgent (fun req ->
        captureRequest req
        jsonResponse HttpStatusCode.OK responseBody)
    agent.Session <- Some testSession
    agent

/// Minimal JSON for a ProfileViewDetailed (only required fields)
let private profileJson =
    {| did = "did:plc:testuser"; handle = "alice.bsky.social"; displayName = "Alice" |}

/// Minimal JSON for a GetTimeline response
let private timelineJson =
    {| feed = ([||] : obj array); cursor = "cursor123" |}

/// Minimal JSON for a ListNotifications response
let private notificationsJson =
    {| notifications = ([||] : obj array); cursor = "notif-cursor" |}

[<Tests>]
let getProfileTests =
    testList "Bluesky.getProfile" [
        testCase "getProfile calls correct XRPC endpoint with actor param" <| fun _ ->
            let mutable captured = None
            let agent = queryAgent (fun req -> captured <- Some req) profileJson
            let result = Bluesky.getProfile agent "alice.bsky.social" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let req = captured.Value
            Expect.equal req.Method HttpMethod.Get "GET method"
            Expect.stringContains (req.RequestUri.ToString()) "app.bsky.actor.getProfile" "correct endpoint"
            Expect.stringContains (req.RequestUri.ToString()) "alice.bsky.social" "actor in query string"

        testCase "getProfile accepts DID string" <| fun _ ->
            let mutable captured = None
            let agent = queryAgent (fun req -> captured <- Some req) profileJson
            let result = Bluesky.getProfile agent "did:plc:testuser" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            Expect.stringContains (captured.Value.RequestUri.ToString()) "did%3Aplc%3Atestuser" "DID in query string (URL-encoded)"

        testCase "getProfile deserializes response" <| fun _ ->
            let agent = queryAgent (fun _ -> ()) profileJson
            let result = Bluesky.getProfile agent "alice.bsky.social" |> Async.AwaitTask |> Async.RunSynchronously
            let profile = Expect.wantOk result "should succeed"
            Expect.equal (Did.value profile.Did) "did:plc:testuser" "did"
            Expect.equal (Handle.value profile.Handle) "alice.bsky.social" "handle"

        testCase "getProfile returns error on failure" <| fun _ ->
            let agent = createMockAgent (fun _ ->
                jsonResponse HttpStatusCode.BadRequest {| error = "InvalidRequest"; message = "Invalid actor" |})
            agent.Session <- Some testSession
            let result = Bluesky.getProfile agent "nonexistent" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isError result "should fail"

        testCase "getProfile accepts Handle directly" <| fun _ ->
            let mutable captured = None
            let agent = queryAgent (fun req -> captured <- Some req) profileJson
            let handle = Handle.parse "alice.bsky.social" |> Result.defaultWith failwith
            let result = Bluesky.getProfile agent handle |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            Expect.stringContains (captured.Value.RequestUri.ToString()) "alice.bsky.social" "handle value in query string"

        testCase "getProfile accepts Did directly" <| fun _ ->
            let mutable captured = None
            let agent = queryAgent (fun req -> captured <- Some req) profileJson
            let did = Did.parse "did:plc:testuser" |> Result.defaultWith failwith
            let result = Bluesky.getProfile agent did |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            Expect.stringContains (captured.Value.RequestUri.ToString()) "did%3Aplc%3Atestuser" "DID value in query string (URL-encoded)"

        testCase "getProfile still accepts plain string" <| fun _ ->
            let mutable captured = None
            let agent = queryAgent (fun req -> captured <- Some req) profileJson
            let result = Bluesky.getProfile agent "bob.bsky.social" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            Expect.stringContains (captured.Value.RequestUri.ToString()) "bob.bsky.social" "string value in query string"
    ]

[<Tests>]
let getTimelineTests =
    testList "Bluesky.getTimeline" [
        testCase "getTimeline calls correct XRPC endpoint" <| fun _ ->
            let mutable captured = None
            let agent = queryAgent (fun req -> captured <- Some req) timelineJson
            let result = Bluesky.getTimeline agent None None |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let req = captured.Value
            Expect.equal req.Method HttpMethod.Get "GET method"
            Expect.stringContains (req.RequestUri.ToString()) "app.bsky.feed.getTimeline" "correct endpoint"

        testCase "getTimeline passes limit and cursor params" <| fun _ ->
            let mutable captured = None
            let agent = queryAgent (fun req -> captured <- Some req) timelineJson
            let result = Bluesky.getTimeline agent (Some 25L) (Some "abc") |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let url = captured.Value.RequestUri.ToString()
            Expect.stringContains url "limit=25" "limit in query"
            Expect.stringContains url "cursor=abc" "cursor in query"

        testCase "getTimeline omits None params from query string" <| fun _ ->
            let mutable captured = None
            let agent = queryAgent (fun req -> captured <- Some req) timelineJson
            let result = Bluesky.getTimeline agent None None |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let url = captured.Value.RequestUri.ToString()
            Expect.isFalse (url.Contains("limit")) "no limit in query"
            Expect.isFalse (url.Contains("cursor")) "no cursor in query"

        testCase "getTimeline deserializes cursor from response" <| fun _ ->
            let agent = queryAgent (fun _ -> ()) timelineJson
            let result = Bluesky.getTimeline agent None None |> Async.AwaitTask |> Async.RunSynchronously
            let output = Expect.wantOk result "should succeed"
            Expect.equal output.Cursor (Some "cursor123") "cursor"
            Expect.equal output.Feed [] "empty feed"
    ]

[<Tests>]
let getPostThreadTests =
    testList "Bluesky.getPostThread" [
        testCase "getPostThread calls correct XRPC endpoint with uri param" <| fun _ ->
            let mutable captured = None
            // Use error response to verify request properties without needing valid response JSON
            let agent = createMockAgent (fun req ->
                captured <- Some req
                jsonResponse HttpStatusCode.BadRequest {| error = "Test"; message = "mock" |})
            agent.Session <- Some testSession
            let uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.post/abc123"
            let _result = Bluesky.getPostThread agent uri None None |> Async.AwaitTask |> Async.RunSynchronously
            let req = captured.Value
            Expect.equal req.Method HttpMethod.Get "GET method"
            Expect.stringContains (req.RequestUri.ToString()) "app.bsky.feed.getPostThread" "correct endpoint"

        testCase "getPostThread passes depth and parentHeight params" <| fun _ ->
            let mutable captured = None
            let agent = createMockAgent (fun req ->
                captured <- Some req
                jsonResponse HttpStatusCode.BadRequest {| error = "Test"; message = "mock" |})
            agent.Session <- Some testSession
            let uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.post/abc123"
            let _result = Bluesky.getPostThread agent uri (Some 6L) (Some 80L) |> Async.AwaitTask |> Async.RunSynchronously
            let url = captured.Value.RequestUri.ToString()
            Expect.stringContains url "depth=6" "depth in query"
            Expect.stringContains url "parentHeight=80" "parentHeight in query"

        testCase "getPostThread omits None optional params" <| fun _ ->
            let mutable captured = None
            let agent = createMockAgent (fun req ->
                captured <- Some req
                jsonResponse HttpStatusCode.BadRequest {| error = "Test"; message = "mock" |})
            agent.Session <- Some testSession
            let uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.post/abc123"
            let _result = Bluesky.getPostThread agent uri None None |> Async.AwaitTask |> Async.RunSynchronously
            let url = captured.Value.RequestUri.ToString()
            Expect.isFalse (url.Contains("depth")) "no depth in query"
            Expect.isFalse (url.Contains("parentHeight")) "no parentHeight in query"

        testCase "getPostThread returns error on failure" <| fun _ ->
            let agent = createMockAgent (fun _ ->
                jsonResponse HttpStatusCode.NotFound {| error = "NotFound"; message = "Post not found" |})
            agent.Session <- Some testSession
            let uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.post/missing"
            let result = Bluesky.getPostThread agent uri None None |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isError result "should fail"
    ]

[<Tests>]
let getNotificationsTests =
    testList "Bluesky.getNotifications" [
        testCase "getNotifications calls correct XRPC endpoint" <| fun _ ->
            let mutable captured = None
            let agent = queryAgent (fun req -> captured <- Some req) notificationsJson
            let result = Bluesky.getNotifications agent None None |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let req = captured.Value
            Expect.equal req.Method HttpMethod.Get "GET method"
            Expect.stringContains (req.RequestUri.ToString()) "app.bsky.notification.listNotifications" "correct endpoint"

        testCase "getNotifications passes limit and cursor params" <| fun _ ->
            let mutable captured = None
            let agent = queryAgent (fun req -> captured <- Some req) notificationsJson
            let result = Bluesky.getNotifications agent (Some 50L) (Some "notif-abc") |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let url = captured.Value.RequestUri.ToString()
            Expect.stringContains url "limit=50" "limit in query"
            Expect.stringContains url "cursor=notif-abc" "cursor in query"

        testCase "getNotifications omits None params from query string" <| fun _ ->
            let mutable captured = None
            let agent = queryAgent (fun req -> captured <- Some req) notificationsJson
            let result = Bluesky.getNotifications agent None None |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let url = captured.Value.RequestUri.ToString()
            Expect.isFalse (url.Contains("limit")) "no limit in query"
            Expect.isFalse (url.Contains("cursor")) "no cursor in query"

        testCase "getNotifications deserializes cursor from response" <| fun _ ->
            let agent = queryAgent (fun _ -> ()) notificationsJson
            let result = Bluesky.getNotifications agent None None |> Async.AwaitTask |> Async.RunSynchronously
            let output = Expect.wantOk result "should succeed"
            Expect.equal output.Cursor (Some "notif-cursor") "cursor"
            Expect.equal output.Notifications [] "empty notifications"
    ]

[<Tests>]
let loginTests =
    testList "Bluesky.login" [
        testCase "loginWithClient authenticates and returns agent with session" <| fun _ ->
            let loginResponse =
                {| did = "did:plc:testlogin"
                   handle = "testlogin.bsky.social"
                   accessJwt = "access-jwt-123"
                   refreshJwt = "refresh-jwt-456" |}
            let client = new HttpClient(new MockHandler(fun _ ->
                jsonResponse HttpStatusCode.OK loginResponse))
            let result =
                Bluesky.loginWithClient client "https://bsky.social" "testlogin.bsky.social" "app-pass"
                |> Async.AwaitTask |> Async.RunSynchronously
            let agent = Expect.wantOk result "login should succeed"
            Expect.isSome agent.Session "agent should have session"
            let session = agent.Session.Value
            Expect.equal (Did.value session.Did) "did:plc:testlogin" "session DID"
            Expect.equal (Handle.value session.Handle) "testlogin.bsky.social" "session handle"
            Expect.equal session.AccessJwt "access-jwt-123" "access JWT"
            Expect.equal session.RefreshJwt "refresh-jwt-456" "refresh JWT"

        testCase "loginWithClient returns error on auth failure" <| fun _ ->
            let client = new HttpClient(new MockHandler(fun _ ->
                jsonResponse HttpStatusCode.Unauthorized {| error = "AuthenticationRequired"; message = "Invalid password" |}))
            let result =
                Bluesky.loginWithClient client "https://bsky.social" "test.bsky.social" "wrong-pass"
                |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isError result "login should fail"
    ]
