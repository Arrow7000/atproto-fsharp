module FSharp.ATProto.Bluesky.Tests.BlueskyTests

open Expecto
open System.Net
open System.Net.Http
open System.Text.Json
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax
open FSharp.ATProto.Streaming
open FSharp.ATProto.DRISL
open TestHelpers

let private parseDid' s = Did.parse s |> Result.defaultWith failwith
let private parseHandle' s = Handle.parse s |> Result.defaultWith failwith

let private testSession =
    { AccessJwt = "test-jwt"
      RefreshJwt = "test-refresh"
      Did = parseDid' "did:plc:testuser"
      Handle = parseHandle' "test.bsky.social" }

let private createRecordAgent (captureRequest : System.Net.Http.HttpRequestMessage -> unit) =
    let agent =
        createMockAgent (fun req ->
            captureRequest req

            jsonResponse
                HttpStatusCode.OK
                {| uri = "at://did:plc:testuser/app.bsky.feed.post/abc123"
                   cid = "bafyreiabc123" |})

    agent.Session <- Some testSession
    agent

let private deleteRecordAgent (captureRequest : System.Net.Http.HttpRequestMessage -> unit) =
    let agent =
        createMockAgent (fun req ->
            captureRequest req
            jsonResponse HttpStatusCode.OK {| |})

    agent.Session <- Some testSession
    agent

let private parseAtUri s = AtUri.parse s |> Result.defaultWith failwith
let private parseCid s = Cid.parse s |> Result.defaultWith failwith
let private parseDid s = Did.parse s |> Result.defaultWith failwith

/// Helper to create a mock blob response with a valid CID
let private blobResponse mimeType size =
    {| blob =
        {| ``$type`` = "blob"
           ref = {| ``$link`` = "bafyreiabc123" |}
           mimeType = mimeType
           size = size |} |}

let private testPostRef =
    { PostRef.Uri = parseAtUri "at://did:plc:other/app.bsky.feed.post/abc"
      Cid = parseCid "bafyreiabc" }

[<Tests>]
let postTests =
    testList
        "Bluesky.post"
        [ testCase "postWithFacets creates post with correct collection"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.postWithFacets agent "Hello world" []
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              let body = req.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.feed.post" "collection in body"
              Expect.stringContains body "did:plc:testuser" "repo = session DID"
              Expect.stringContains body "Hello world" "text in record"

          testCase "postWithFacets returns PostRef with typed fields"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.postWithFacets agent "Hello world" []
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let postRef = Expect.wantOk result "should succeed"
              Expect.equal (AtUri.value postRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "uri"
              Expect.equal (Cid.value postRef.Cid) "bafyreiabc123" "cid"

          testCase "postWithFacets includes facets when provided"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let facets : AppBskyRichtext.Facet.Facet list =
                  [ { Index = { ByteStart = 0L; ByteEnd = 5L }
                      Features = [ AppBskyRichtext.Facet.FacetFeaturesItem.Tag { Tag = "hello" } ] } ]

              let result =
                  Bluesky.postWithFacets agent "#hello world" facets
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"

          testCase "postWithFacets returns NotLoggedIn error without session"
          <| fun _ ->
              let agent = createMockAgent (fun _ -> jsonResponse HttpStatusCode.OK {| |})
              // No session set
              let result =
                  Bluesky.postWithFacets agent "Hello world" []
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail without session"
              Expect.equal err.StatusCode 401 "status code"
              Expect.equal err.Error (Some "NotLoggedIn") "error code"
              Expect.equal err.Message (Some "No active session") "message" ]

[<Tests>]
let quotePostTests =
    testList
        "Bluesky.quotePost"
        [ testCase "quotePost creates post with embed record"
          <| fun () ->
              let mutable capturedRequest : HttpRequestMessage = null
              let agent = createRecordAgent (fun req -> capturedRequest <- req)

              let quotedPost =
                  { PostRef.Uri = parseAtUri "at://did:plc:other/app.bsky.feed.post/quoted123"
                    Cid = parseCid "bafyreiquoted" }

              let result =
                  (Bluesky.quotePost agent "Check this out!" quotedPost).GetAwaiter().GetResult ()

              Expect.isOk result "quotePost should succeed"
              let body = capturedRequest.Content.ReadAsStringAsync().GetAwaiter().GetResult ()
              Expect.stringContains body "app.bsky.embed.record" "should have embed record type"
              Expect.stringContains body "at://did:plc:other/app.bsky.feed.post/quoted123" "should contain quoted URI"
              Expect.stringContains body "bafyreiquoted" "should contain quoted CID"

          testCase "quotePost auto-detects facets"
          <| fun () ->
              let mutable capturedRequest : HttpRequestMessage = null
              let agent = createRecordAgent (fun req -> capturedRequest <- req)
              let quotedPost = testPostRef

              let result =
                  (Bluesky.quotePost agent "Check https://example.com" quotedPost).GetAwaiter().GetResult ()

              Expect.isOk result "quotePost should succeed"
              let body = capturedRequest.Content.ReadAsStringAsync().GetAwaiter().GetResult ()
              Expect.stringContains body "facets" "should have facets from auto-detection" ]

[<Tests>]
let likeTests =
    testList
        "Bluesky.like"
        [ testCase "like creates record with correct collection and subject"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.like agent testPostRef |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.feed.like" "like collection"
              Expect.stringContains body "bafyreiabc" "cid in subject"

          testCase "like returns LikeRef with uri"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.like agent testPostRef |> Async.AwaitTask |> Async.RunSynchronously

              let likeRef = Expect.wantOk result "should succeed"
              Expect.equal (AtUri.value likeRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "returns uri"

          testCase "repost creates record with correct collection"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.repost agent testPostRef |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.feed.repost" "repost collection"

          testCase "repost returns RepostRef with uri"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.repost agent testPostRef |> Async.AwaitTask |> Async.RunSynchronously

              let repostRef = Expect.wantOk result "should succeed"
              Expect.equal (AtUri.value repostRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "returns uri"

          testCase "like returns NotLoggedIn error without session"
          <| fun _ ->
              let agent = createMockAgent (fun _ -> jsonResponse HttpStatusCode.OK {| |})

              let result =
                  Bluesky.like agent testPostRef |> Async.AwaitTask |> Async.RunSynchronously

              let err = Expect.wantError result "should fail without session"
              Expect.equal err.StatusCode 401 "status code"
              Expect.equal err.Error (Some "NotLoggedIn") "error code" ]

[<Tests>]
let followTests =
    testList
        "Bluesky.follow"
        [ testCase "follow creates record with DID subject"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.follow agent (parseDid "did:plc:other")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.graph.follow" "follow collection"
              Expect.stringContains body "did:plc:other" "subject DID"

          testCase "follow returns FollowRef with uri"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.follow agent (parseDid "did:plc:other")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let followRef = Expect.wantOk result "should succeed"
              Expect.equal (AtUri.value followRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "returns uri"

          testCase "block creates record with DID subject"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.block agent (parseDid "did:plc:other")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.graph.block" "block collection"

          testCase "block returns BlockRef with uri"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.block agent (parseDid "did:plc:other")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let blockRef = Expect.wantOk result "should succeed"
              Expect.equal (AtUri.value blockRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "returns uri"

          testCase "follow returns NotLoggedIn error without session"
          <| fun _ ->
              let agent = createMockAgent (fun _ -> jsonResponse HttpStatusCode.OK {| |})

              let result =
                  Bluesky.follow agent (parseDid "did:plc:other")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail without session"
              Expect.equal err.StatusCode 401 "status code" ]

[<Tests>]
let followByHandleTests =
    testList
        "Bluesky.followByHandle"
        [ testCase "followByHandle with DID string passes through without resolution"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.followByHandle agent "did:plc:other"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.graph.follow" "follow collection"
              Expect.stringContains body "did:plc:other" "subject DID"

          testCase "followByHandle with handle resolves to DID first"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createMockAgent (fun req ->
                      if req.RequestUri.PathAndQuery.Contains ("resolveHandle") then
                          jsonResponse HttpStatusCode.OK {| did = "did:plc:resolved" |}
                      else
                          captured <- Some req

                          jsonResponse
                              HttpStatusCode.OK
                              {| uri = "at://did:plc:testuser/app.bsky.graph.follow/abc123"
                                 cid = "bafyreiabc123" |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.followByHandle agent "my-handle.bsky.social"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "did:plc:resolved" "resolved DID used as subject"
              Expect.stringContains body "app.bsky.graph.follow" "follow collection"

          testCase "followByHandle returns error for invalid identifier"
          <| fun _ ->
              let agent = createRecordAgent (fun _ -> ())

              let result =
                  Bluesky.followByHandle agent "not a valid handle or did"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail for invalid identifier"
              Expect.equal err.StatusCode 400 "status code"
              Expect.isSome err.Message "should have error message"

          testCase "followByHandle returns error when handle resolution fails"
          <| fun _ ->
              let agent =
                  createMockAgent (fun req ->
                      if req.RequestUri.PathAndQuery.Contains ("resolveHandle") then
                          jsonResponse
                              HttpStatusCode.BadRequest
                              {| error = "HandleNotFound"
                                 message = "handle not found" |}
                      else
                          jsonResponse
                              HttpStatusCode.OK
                              {| uri = "at://did:plc:testuser/app.bsky.graph.follow/abc123"
                                 cid = "bafyreiabc123" |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.followByHandle agent "nonexistent.bsky.social"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isError result "should fail when handle resolution fails"

          testCase "followByHandle returns FollowRef on success"
          <| fun _ ->
              let agent = createRecordAgent (fun _ -> ())

              let result =
                  Bluesky.followByHandle agent "did:plc:other"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let followRef = Expect.wantOk result "should succeed"
              Expect.equal (AtUri.value followRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "returns uri" ]

[<Tests>]
let blockByHandleTests =
    testList
        "Bluesky.blockByHandle"
        [ testCase "blockByHandle with DID string passes through without resolution"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.blockByHandle agent "did:plc:other"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.graph.block" "block collection"
              Expect.stringContains body "did:plc:other" "subject DID"

          testCase "blockByHandle with handle resolves to DID first"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createMockAgent (fun req ->
                      if req.RequestUri.PathAndQuery.Contains ("resolveHandle") then
                          jsonResponse HttpStatusCode.OK {| did = "did:plc:resolved" |}
                      else
                          captured <- Some req

                          jsonResponse
                              HttpStatusCode.OK
                              {| uri = "at://did:plc:testuser/app.bsky.graph.block/abc123"
                                 cid = "bafyreiabc123" |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.blockByHandle agent "my-handle.bsky.social"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "did:plc:resolved" "resolved DID used as subject"
              Expect.stringContains body "app.bsky.graph.block" "block collection"

          testCase "blockByHandle returns error for invalid identifier"
          <| fun _ ->
              let agent = createRecordAgent (fun _ -> ())

              let result =
                  Bluesky.blockByHandle agent "not a valid handle or did"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail for invalid identifier"
              Expect.equal err.StatusCode 400 "status code"
              Expect.isSome err.Message "should have error message"

          testCase "blockByHandle returns error when handle resolution fails"
          <| fun _ ->
              let agent =
                  createMockAgent (fun req ->
                      if req.RequestUri.PathAndQuery.Contains ("resolveHandle") then
                          jsonResponse
                              HttpStatusCode.BadRequest
                              {| error = "HandleNotFound"
                                 message = "handle not found" |}
                      else
                          jsonResponse
                              HttpStatusCode.OK
                              {| uri = "at://did:plc:testuser/app.bsky.graph.block/abc123"
                                 cid = "bafyreiabc123" |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.blockByHandle agent "nonexistent.bsky.social"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isError result "should fail when handle resolution fails"

          testCase "blockByHandle returns BlockRef on success"
          <| fun _ ->
              let agent = createRecordAgent (fun _ -> ())

              let result =
                  Bluesky.blockByHandle agent "did:plc:other"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let blockRef = Expect.wantOk result "should succeed"
              Expect.equal (AtUri.value blockRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "returns uri" ]

[<Tests>]
let undoTests =
    testList
        "Bluesky.undo"
        [ testCase "unlike delegates to deleteRecord with LikeRef uri"
          <| fun _ ->
              let mutable captured = None
              let agent = deleteRecordAgent (fun req -> captured <- Some req)

              let likeRef =
                  { LikeRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.like/abc123" }

              let result =
                  Bluesky.unlike agent likeRef |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.feed.like" "collection"
              Expect.stringContains body "abc123" "rkey"

          testCase "unrepost delegates to deleteRecord with RepostRef uri"
          <| fun _ ->
              let mutable captured = None
              let agent = deleteRecordAgent (fun req -> captured <- Some req)

              let repostRef =
                  { RepostRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.repost/def456" }

              let result =
                  Bluesky.unrepost agent repostRef |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.feed.repost" "collection"
              Expect.stringContains body "def456" "rkey"

          testCase "unfollow delegates to deleteRecord with FollowRef uri"
          <| fun _ ->
              let mutable captured = None
              let agent = deleteRecordAgent (fun req -> captured <- Some req)

              let followRef =
                  { FollowRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.graph.follow/ghi789" }

              let result =
                  Bluesky.unfollow agent followRef |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.graph.follow" "collection"
              Expect.stringContains body "ghi789" "rkey"

          testCase "unblock delegates to deleteRecord with BlockRef uri"
          <| fun _ ->
              let mutable captured = None
              let agent = deleteRecordAgent (fun req -> captured <- Some req)

              let blockRef =
                  { BlockRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.graph.block/jkl012" }

              let result =
                  Bluesky.unblock agent blockRef |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.graph.block" "collection"
              Expect.stringContains body "jkl012" "rkey" ]

[<Tests>]
let undoResultTests =
    testList
        "Bluesky.undo (UndoResult)"
        [ testCase "undoLike returns Undone on successful delete"
          <| fun _ ->
              let mutable captured = None
              let agent = deleteRecordAgent (fun req -> captured <- Some req)

              let likeRef =
                  { LikeRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.like/abc123" }

              let result =
                  Bluesky.undoLike agent likeRef |> Async.AwaitTask |> Async.RunSynchronously

              let undoResult = Expect.wantOk result "should succeed"
              Expect.equal undoResult Undone "should be Undone"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.feed.like" "collection"
              Expect.stringContains body "abc123" "rkey"

          testCase "undoRepost returns Undone on successful delete"
          <| fun _ ->
              let agent = deleteRecordAgent (fun _ -> ())

              let repostRef =
                  { RepostRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.repost/def456" }

              let result =
                  Bluesky.undoRepost agent repostRef |> Async.AwaitTask |> Async.RunSynchronously

              let undoResult = Expect.wantOk result "should succeed"
              Expect.equal undoResult Undone "should be Undone"

          testCase "undoFollow returns Undone on successful delete"
          <| fun _ ->
              let agent = deleteRecordAgent (fun _ -> ())

              let followRef =
                  { FollowRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.graph.follow/ghi789" }

              let result =
                  Bluesky.undoFollow agent followRef |> Async.AwaitTask |> Async.RunSynchronously

              let undoResult = Expect.wantOk result "should succeed"
              Expect.equal undoResult Undone "should be Undone"

          testCase "undoBlock returns Undone on successful delete"
          <| fun _ ->
              let agent = deleteRecordAgent (fun _ -> ())

              let blockRef =
                  { BlockRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.graph.block/jkl012" }

              let result =
                  Bluesky.undoBlock agent blockRef |> Async.AwaitTask |> Async.RunSynchronously

              let undoResult = Expect.wantOk result "should succeed"
              Expect.equal undoResult Undone "should be Undone"

          testCase "SRTP undo works with LikeRef"
          <| fun _ ->
              let agent = deleteRecordAgent (fun _ -> ())

              let likeRef =
                  { LikeRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.like/srtp1" }

              let result = Bluesky.undo agent likeRef |> Async.AwaitTask |> Async.RunSynchronously
              let undoResult = Expect.wantOk result "should succeed"
              Expect.equal undoResult Undone "should be Undone"

          testCase "SRTP undo works with FollowRef"
          <| fun _ ->
              let agent = deleteRecordAgent (fun _ -> ())

              let followRef =
                  { FollowRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.graph.follow/srtp2" }

              let result =
                  Bluesky.undo agent followRef |> Async.AwaitTask |> Async.RunSynchronously

              let undoResult = Expect.wantOk result "should succeed"
              Expect.equal undoResult Undone "should be Undone"

          testCase "SRTP undo works with RepostRef"
          <| fun _ ->
              let agent = deleteRecordAgent (fun _ -> ())

              let repostRef =
                  { RepostRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.repost/srtp3" }

              let result =
                  Bluesky.undo agent repostRef |> Async.AwaitTask |> Async.RunSynchronously

              let undoResult = Expect.wantOk result "should succeed"
              Expect.equal undoResult Undone "should be Undone"

          testCase "SRTP undo works with BlockRef"
          <| fun _ ->
              let agent = deleteRecordAgent (fun _ -> ())

              let blockRef =
                  { BlockRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.graph.block/srtp4" }

              let result =
                  Bluesky.undo agent blockRef |> Async.AwaitTask |> Async.RunSynchronously

              let undoResult = Expect.wantOk result "should succeed"
              Expect.equal undoResult Undone "should be Undone" ]

/// Creates a mock agent that handles both getPosts (GET) and deleteRecord (POST) requests.
/// The viewerState is used in the PostView response for getPosts.
let private unlikeMockAgent
    (viewerLike : string option)
    (viewerRepost : string option)
    (captureDelete : HttpRequestMessage -> unit)
    =
    let agent =
        createMockAgent (fun req ->
            let path = req.RequestUri.AbsolutePath
            let query = req.RequestUri.Query

            if
                path.Contains ("app.bsky.feed.getPosts")
                || query.Contains ("app.bsky.feed.getPosts")
            then
                jsonResponse
                    HttpStatusCode.OK
                    {| posts =
                        [| {| uri = "at://did:plc:other/app.bsky.feed.post/abc"
                              cid = "bafyreiabc"
                              author =
                               {| did = "did:plc:other"
                                  handle = "other.test"
                                  displayName = "Other" |}
                              record =
                               {| text = "Hello"
                                  createdAt = "2026-01-01T00:00:00Z" |}
                              indexedAt = "2026-01-01T00:00:00Z"
                              viewer =
                               {| like = viewerLike |> Option.defaultValue null
                                  repost = viewerRepost |> Option.defaultValue null |} |} |] |}
            elif path.Contains ("com.atproto.repo.deleteRecord") then
                captureDelete req
                jsonResponse HttpStatusCode.OK {| |}
            else
                jsonResponse HttpStatusCode.NotFound {| error = "NotFound" |})

    agent.Session <- Some testSession
    agent

/// Creates a mock agent that returns an empty posts array for getPosts.
let private emptyPostsMockAgent () =
    let agent =
        createMockAgent (fun req ->
            let path = req.RequestUri.AbsolutePath
            let query = req.RequestUri.Query

            if
                path.Contains ("app.bsky.feed.getPosts")
                || query.Contains ("app.bsky.feed.getPosts")
            then
                jsonResponse HttpStatusCode.OK {| posts = [||] |}
            else
                jsonResponse HttpStatusCode.NotFound {| error = "NotFound" |})

    agent.Session <- Some testSession
    agent

/// Creates a mock agent that returns a post with no viewer state for getPosts.
let private noViewerMockAgent () =
    let agent =
        createMockAgent (fun req ->
            let path = req.RequestUri.AbsolutePath
            let query = req.RequestUri.Query

            if
                path.Contains ("app.bsky.feed.getPosts")
                || query.Contains ("app.bsky.feed.getPosts")
            then
                jsonResponse
                    HttpStatusCode.OK
                    {| posts =
                        [| {| uri = "at://did:plc:other/app.bsky.feed.post/abc"
                              cid = "bafyreiabc"
                              author =
                               {| did = "did:plc:other"
                                  handle = "other.test"
                                  displayName = "Other" |}
                              record =
                               {| text = "Hello"
                                  createdAt = "2026-01-01T00:00:00Z" |}
                              indexedAt = "2026-01-01T00:00:00Z" |} |] |}
            else
                jsonResponse HttpStatusCode.NotFound {| error = "NotFound" |})

    agent.Session <- Some testSession
    agent

[<Tests>]
let unlikePostTests =
    testList
        "Bluesky.unlikePost"
        [ testCase "unlikePost returns Undone when post has viewer.like set"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  unlikeMockAgent (Some "at://did:plc:testuser/app.bsky.feed.like/mylike1") None (fun req ->
                      captured <- Some req)

              let result =
                  Bluesky.unlikePost agent testPostRef
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let undoResult = Expect.wantOk result "should succeed"
              Expect.equal undoResult Undone "should be Undone"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.feed.like" "deletes like collection"
              Expect.stringContains body "mylike1" "deletes correct rkey"

          testCase "unlikePost returns WasNotPresent when post has no viewer.like"
          <| fun _ ->
              let agent = unlikeMockAgent None None (fun _ -> ())

              let result =
                  Bluesky.unlikePost agent testPostRef
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let undoResult = Expect.wantOk result "should succeed"
              Expect.equal undoResult WasNotPresent "should be WasNotPresent"

          testCase "unlikePost returns WasNotPresent when post not found"
          <| fun _ ->
              let agent = emptyPostsMockAgent ()

              let result =
                  Bluesky.unlikePost agent testPostRef
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let undoResult = Expect.wantOk result "should succeed"
              Expect.equal undoResult WasNotPresent "should be WasNotPresent when post not found"

          testCase "unlikePost returns WasNotPresent when viewer state is absent"
          <| fun _ ->
              let agent = noViewerMockAgent ()

              let result =
                  Bluesky.unlikePost agent testPostRef
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let undoResult = Expect.wantOk result "should succeed"
              Expect.equal undoResult WasNotPresent "should be WasNotPresent when no viewer" ]

[<Tests>]
let unrepostPostTests =
    testList
        "Bluesky.unrepostPost"
        [ testCase "unrepostPost returns Undone when post has viewer.repost set"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  unlikeMockAgent None (Some "at://did:plc:testuser/app.bsky.feed.repost/myrepost1") (fun req ->
                      captured <- Some req)

              let result =
                  Bluesky.unrepostPost agent testPostRef
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let undoResult = Expect.wantOk result "should succeed"
              Expect.equal undoResult Undone "should be Undone"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.feed.repost" "deletes repost collection"
              Expect.stringContains body "myrepost1" "deletes correct rkey"

          testCase "unrepostPost returns WasNotPresent when post has no viewer.repost"
          <| fun _ ->
              let agent = unlikeMockAgent None None (fun _ -> ())

              let result =
                  Bluesky.unrepostPost agent testPostRef
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let undoResult = Expect.wantOk result "should succeed"
              Expect.equal undoResult WasNotPresent "should be WasNotPresent"

          testCase "unrepostPost returns WasNotPresent when post not found"
          <| fun _ ->
              let agent = emptyPostsMockAgent ()

              let result =
                  Bluesky.unrepostPost agent testPostRef
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let undoResult = Expect.wantOk result "should succeed"
              Expect.equal undoResult WasNotPresent "should be WasNotPresent when post not found"

          testCase "unrepostPost returns WasNotPresent when viewer state is absent"
          <| fun _ ->
              let agent = noViewerMockAgent ()

              let result =
                  Bluesky.unrepostPost agent testPostRef
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let undoResult = Expect.wantOk result "should succeed"
              Expect.equal undoResult WasNotPresent "should be WasNotPresent when no viewer" ]

[<Tests>]
let deleteTests =
    testList
        "Bluesky.deleteRecord"
        [ testCase "deleteRecord parses AT-URI and sends correct request"
          <| fun _ ->
              let mutable captured = None
              let agent = deleteRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.deleteRecord agent (parseAtUri "at://did:plc:testuser/app.bsky.feed.post/abc123")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.feed.post" "collection"
              Expect.stringContains body "abc123" "rkey"
              Expect.stringContains body "did:plc:testuser" "repo"

          testCase "deleteRecord returns error for AT-URI without collection"
          <| fun _ ->
              let agent = deleteRecordAgent (fun _ -> ())

              let result =
                  Bluesky.deleteRecord agent (parseAtUri "at://did:plc:testuser")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail without collection"
              Expect.equal err.StatusCode 400 "status code"
              Expect.isSome err.Message "should have message"

          testCase "deleteRecord returns error for AT-URI without rkey"
          <| fun _ ->
              let agent = deleteRecordAgent (fun _ -> ())

              let result =
                  Bluesky.deleteRecord agent (parseAtUri "at://did:plc:testuser/app.bsky.feed.post")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail without rkey"
              Expect.equal err.StatusCode 400 "status code"
              Expect.isSome err.Message "should have message" ]

[<Tests>]
let replyTests =
    testList
        "Bluesky.replyWithKnownRoot"
        [ testCase "replyWithKnownRoot includes root and parent refs"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let parent =
                  { PostRef.Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"
                    Cid = parseCid "bafyparent" }

              let root =
                  { PostRef.Uri = parseAtUri "at://did:plc:r/app.bsky.feed.post/root"
                    Cid = parseCid "bafyroot00" }

              let result =
                  Bluesky.replyWithKnownRoot agent "A reply" parent root
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "bafyparent" "parent cid"
              Expect.stringContains body "bafyroot00" "root cid"

          testCase "replyWithKnownRoot returns PostRef"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let parent =
                  { PostRef.Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"
                    Cid = parseCid "bafyparent" }

              let root =
                  { PostRef.Uri = parseAtUri "at://did:plc:r/app.bsky.feed.post/root"
                    Cid = parseCid "bafyroot00" }

              let result =
                  Bluesky.replyWithKnownRoot agent "A reply" parent root
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let postRef = Expect.wantOk result "should succeed"
              Expect.equal (AtUri.value postRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "uri"
              Expect.equal (Cid.value postRef.Cid) "bafyreiabc123" "cid" ]

/// Creates a mock agent that handles both getPosts (GET) and createRecord (POST) requests.
/// The recordJson is the record field returned in the PostView for getPosts.
let private replyToMockAgent (recordJson : obj) (captureCreateRecord : HttpRequestMessage -> unit) =
    let agent =
        createMockAgent (fun req ->
            let path = req.RequestUri.AbsolutePath

            if path.Contains ("app.bsky.feed.getPosts") then
                // Return a PostView with the given record JSON
                jsonResponse
                    HttpStatusCode.OK
                    {| posts =
                        [| {| uri = "at://did:plc:p/app.bsky.feed.post/parent"
                              cid = "bafyparent"
                              author =
                               {| did = "did:plc:p"
                                  handle = "parent.test"
                                  displayName = "Parent" |}
                              record = recordJson
                              indexedAt = "2026-01-01T00:00:00Z" |} |] |}
            elif path.Contains ("com.atproto.repo.createRecord") then
                captureCreateRecord req

                jsonResponse
                    HttpStatusCode.OK
                    {| uri = "at://did:plc:testuser/app.bsky.feed.post/reply1"
                       cid = "bafyreireply1" |}
            else
                jsonResponse HttpStatusCode.NotFound {| error = "NotFound" |})

    agent.Session <- Some testSession
    agent

[<Tests>]
let replyToTests =
    testList
        "Bluesky.replyTo"
        [ testCase "replyTo top-level post uses parent as root"
          <| fun _ ->
              let mutable captured = None
              // A top-level post has no "reply" field in its record
              let agent =
                  replyToMockAgent
                      {| text = "Hello"
                         createdAt = "2026-01-01T00:00:00Z" |}
                      (fun req -> captured <- Some req)

              let parent =
                  { PostRef.Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"
                    Cid = parseCid "bafyparent" }

              let result =
                  Bluesky.replyTo agent "My reply" parent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              // Both parent and root should be the same (the parent post itself)
              Expect.stringContains body "bafyparent" "parent cid in body"
              Expect.stringContains body "at://did:plc:p/app.bsky.feed.post/parent" "parent uri used as root"

          testCase "replyTo reply post extracts root from reply field"
          <| fun _ ->
              let mutable captured = None
              // A reply post has a "reply" field with root info
              let agent =
                  replyToMockAgent
                      {| text = "A reply"
                         createdAt = "2026-01-01T00:00:00Z"
                         reply =
                          {| root =
                              {| uri = "at://did:plc:r/app.bsky.feed.post/root"
                                 cid = "bafyroot00" |}
                             parent =
                              {| uri = "at://did:plc:x/app.bsky.feed.post/other"
                                 cid = "bafyother0" |} |} |}
                      (fun req -> captured <- Some req)

              let parent =
                  { PostRef.Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"
                    Cid = parseCid "bafyparent" }

              let result =
                  Bluesky.replyTo agent "Deeper reply" parent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              // Parent should be the one we passed in
              Expect.stringContains body "bafyparent" "parent cid in body"
              // Root should be extracted from the reply field, NOT the parent
              Expect.stringContains body "bafyroot00" "root cid from reply field"
              Expect.stringContains body "at://did:plc:r/app.bsky.feed.post/root" "root uri from reply field"

          testCase "replyTo returns error for invalid root URI in reply field"
          <| fun _ ->
              let agent =
                  replyToMockAgent
                      {| text = "A reply"
                         reply =
                          {| root =
                              {| uri = "not-a-valid-uri"
                                 cid = "bafyroot00" |}
                             parent =
                              {| uri = "at://did:plc:x/app.bsky.feed.post/x"
                                 cid = "bafyother0" |} |} |}
                      (fun _ -> ())

              let parent =
                  { PostRef.Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"
                    Cid = parseCid "bafyparent" }

              let result =
                  Bluesky.replyTo agent "Reply" parent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail for invalid root URI"
              Expect.equal err.StatusCode 400 "status code"
              Expect.isSome err.Message "should have error message"

          testCase "replyTo returns error for malformed reply field"
          <| fun _ ->
              // reply field exists but has wrong structure (no root property)
              let agent =
                  replyToMockAgent
                      {| text = "A reply"
                         reply = {| wrong = "structure" |} |}
                      (fun _ -> ())

              let parent =
                  { PostRef.Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"
                    Cid = parseCid "bafyparent" }

              let result =
                  Bluesky.replyTo agent "Reply" parent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail for malformed reply"
              Expect.equal err.StatusCode 400 "status code"
              Expect.isSome err.Message "should have error message"

          testCase "replyTo returns PostRef on success"
          <| fun _ ->
              let agent = replyToMockAgent {| text = "Hello" |} (fun _ -> ())

              let parent =
                  { PostRef.Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"
                    Cid = parseCid "bafyparent" }

              let result =
                  Bluesky.replyTo agent "Reply" parent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let postRef = Expect.wantOk result "should succeed"
              Expect.equal (AtUri.value postRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/reply1" "uri"
              Expect.equal (Cid.value postRef.Cid) "bafyreireply1" "cid"

          testCase "replyTo returns error when getPosts fails"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ ->
                      jsonResponse
                          HttpStatusCode.InternalServerError
                          {| error = "InternalError"
                             message = "server error" |})

              agent.Session <- Some testSession

              let parent =
                  { PostRef.Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"
                    Cid = parseCid "bafyparent" }

              let result =
                  Bluesky.replyTo agent "Reply" parent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isError result "should fail when getPosts fails"

          testCase "replyTo returns error when parent post not found"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ -> jsonResponse HttpStatusCode.OK {| posts = [||] |})

              agent.Session <- Some testSession

              let parent =
                  { PostRef.Uri = parseAtUri "at://did:plc:p/app.bsky.feed.post/parent"
                    Cid = parseCid "bafyparent" }

              let result =
                  Bluesky.replyTo agent "Reply" parent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail when post not found"
              Expect.equal err.StatusCode 400 "status code"
              Expect.isSome err.Message "should have error message" ]

[<Tests>]
let blobTests =
    testList
        "Bluesky.uploadBlob"
        [ testCase "uploadBlob sends binary content with correct content type"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createMockAgent (fun req ->
                      captured <- Some req
                      jsonResponse HttpStatusCode.OK (blobResponse "image/png" 100))

              agent.Session <- Some testSession
              let data = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy |] // PNG header bytes

              let result =
                  Bluesky.uploadBlob agent data Png |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal (req.Content.Headers.ContentType.MediaType) "image/png" "content type"
              Expect.equal (req.Method) HttpMethod.Post "POST method"

          testCase "uploadBlob returns BlobRef with typed fields"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ ->
                      jsonResponse
                          HttpStatusCode.OK
                          {| blob =
                              {| ``$type`` = "blob"
                                 ref = {| ``$link`` = "bafyreiabc123" |}
                                 mimeType = "image/jpeg"
                                 size = 54321 |} |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.uploadBlob agent [| 0xFFuy; 0xD8uy |] Jpeg
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let blobRef = Expect.wantOk result "should succeed"
              Expect.equal (Cid.value blobRef.Ref) "bafyreiabc123" "ref CID"
              Expect.equal blobRef.MimeType "image/jpeg" "mime type"
              Expect.equal blobRef.Size 54321L "size"

          testCase "uploadBlob preserves raw JSON in BlobRef"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ -> jsonResponse HttpStatusCode.OK (blobResponse "image/png" 999))

              agent.Session <- Some testSession

              let result =
                  Bluesky.uploadBlob agent [| 0uy |] Png
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let blobRef = Expect.wantOk result "should succeed"
              let jsonStr = blobRef.Json.ToString ()
              Expect.stringContains jsonStr "blob" "$type in raw JSON"
              Expect.stringContains jsonStr "bafyreiabc123" "link in raw JSON"

          testCase "uploadBlob includes Bearer auth header"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createMockAgent (fun req ->
                      captured <- Some req
                      jsonResponse HttpStatusCode.OK (blobResponse "image/jpeg" 50))

              agent.Session <- Some testSession

              let result =
                  Bluesky.uploadBlob agent [| 0xFFuy; 0xD8uy |] Jpeg
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal (req.Headers.Authorization.Scheme) "Bearer" "auth scheme"
              Expect.equal (req.Headers.Authorization.Parameter) "test-jwt" "auth token"

          testCase "uploadBlob returns error on failure"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ ->
                      jsonResponse
                          HttpStatusCode.BadRequest
                          {| error = "InvalidBlob"
                             message = "too large" |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.uploadBlob agent [| 0uy |] Png
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isError result "should fail"

          testCase "uploadBlob sends to correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createMockAgent (fun req ->
                      captured <- Some req
                      jsonResponse HttpStatusCode.OK (blobResponse "image/png" 10))

              agent.Session <- Some testSession

              let result =
                  Bluesky.uploadBlob agent [| 0uy |] Png
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.stringContains (req.RequestUri.ToString ()) "com.atproto.repo.uploadBlob" "correct endpoint"

          testCase "uploadBlob returns error when response missing blob property"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ -> jsonResponse HttpStatusCode.OK {| unexpected = "data" |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.uploadBlob agent [| 0uy |] Png
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail when blob property missing"
              Expect.equal err.StatusCode 400 "status code"
              Expect.isSome err.Message "should have message" ]

[<Tests>]
let imageMimeTests =
    testList
        "ImageMime.toMimeString"
        [ testCase "Png converts to image/png"
          <| fun _ -> Expect.equal (ImageMime.toMimeString Png) "image/png" "Png"

          testCase "Jpeg converts to image/jpeg"
          <| fun _ -> Expect.equal (ImageMime.toMimeString Jpeg) "image/jpeg" "Jpeg"

          testCase "Gif converts to image/gif"
          <| fun _ -> Expect.equal (ImageMime.toMimeString Gif) "image/gif" "Gif"

          testCase "Webp converts to image/webp"
          <| fun _ -> Expect.equal (ImageMime.toMimeString Webp) "image/webp" "Webp"

          testCase "Custom passes through arbitrary string"
          <| fun _ -> Expect.equal (ImageMime.toMimeString (ImageMime.Custom "video/mp4")) "video/mp4" "Custom" ]

[<Tests>]
let imagePostTests =
    testList
        "Bluesky.postWithImages"
        [ testCase "postWithImages uploads blob and creates post with embed"
          <| fun _ ->
              let mutable requestCount = 0

              let agent =
                  createMockAgent (fun req ->
                      requestCount <- requestCount + 1

                      if req.RequestUri.PathAndQuery.Contains ("uploadBlob") then
                          jsonResponse HttpStatusCode.OK (blobResponse "image/png" 100)
                      else
                          jsonResponse
                              HttpStatusCode.OK
                              {| uri = "at://did:plc:testuser/app.bsky.feed.post/abc"
                                 cid = "bafyreiabc123" |})

              agent.Session <- Some testSession

              let images =
                  [ { Data = [| 0x89uy; 0x50uy |]
                      MimeType = Png
                      AltText = "A test image" } ]

              let result =
                  Bluesky.postWithImages agent "Check this out" images
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.isGreaterThanOrEqual requestCount 2 "at least 2 requests (upload + create)"

          testCase "postWithImages includes embed in record body"
          <| fun _ ->
              let mutable lastBody = ""

              let agent =
                  createMockAgent (fun req ->
                      let body = req.Content.ReadAsStringAsync().Result
                      lastBody <- body

                      if req.RequestUri.PathAndQuery.Contains ("uploadBlob") then
                          jsonResponse HttpStatusCode.OK (blobResponse "image/png" 100)
                      else
                          jsonResponse
                              HttpStatusCode.OK
                              {| uri = "at://did:plc:testuser/app.bsky.feed.post/abc"
                                 cid = "bafyreiabc123" |})

              agent.Session <- Some testSession

              let images =
                  [ { Data = [| 0x89uy |]
                      MimeType = Png
                      AltText = "My image" } ]

              let result =
                  Bluesky.postWithImages agent "Look at this" images
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.stringContains lastBody "app.bsky.embed.images" "embed type in body"
              Expect.stringContains lastBody "My image" "alt text in body"

          testCase "postWithImages returns PostRef"
          <| fun _ ->
              let agent =
                  createMockAgent (fun req ->
                      if req.RequestUri.PathAndQuery.Contains ("uploadBlob") then
                          jsonResponse HttpStatusCode.OK (blobResponse "image/png" 100)
                      else
                          jsonResponse
                              HttpStatusCode.OK
                              {| uri = "at://did:plc:testuser/app.bsky.feed.post/abc"
                                 cid = "bafyreiabc123" |})

              agent.Session <- Some testSession

              let images =
                  [ { Data = [| 0x89uy |]
                      MimeType = Png
                      AltText = "Test" } ]

              let result =
                  Bluesky.postWithImages agent "Test" images
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let postRef = Expect.wantOk result "should succeed"
              Expect.equal (AtUri.value postRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc" "uri"
              Expect.equal (Cid.value postRef.Cid) "bafyreiabc123" "cid"

          testCase "postWithImages fails if blob upload fails"
          <| fun _ ->
              let agent =
                  createMockAgent (fun req ->
                      if req.RequestUri.PathAndQuery.Contains ("uploadBlob") then
                          jsonResponse
                              HttpStatusCode.BadRequest
                              {| error = "BlobError"
                                 message = "failed" |}
                      else
                          jsonResponse
                              HttpStatusCode.OK
                              {| uri = "at://did:plc:testuser/app.bsky.feed.post/abc"
                                 cid = "bafyreiabc123" |})

              agent.Session <- Some testSession

              let images =
                  [ { Data = [| 0x89uy |]
                      MimeType = Png
                      AltText = "fail" } ]

              let result =
                  Bluesky.postWithImages agent "Should fail" images
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isError result "should fail when blob upload fails" ]

// ── Read convenience method helpers ─────────────────────────────────

let private queryAgent (captureRequest : HttpRequestMessage -> unit) (responseBody : obj) =
    let agent =
        createMockAgent (fun req ->
            captureRequest req
            jsonResponse HttpStatusCode.OK responseBody)

    agent.Session <- Some testSession
    agent

/// Minimal JSON for a ProfileViewDetailed (only required fields)
let private profileJson =
    {| did = "did:plc:testuser"
       handle = "my-handle.bsky.social"
       displayName = "Alice" |}

/// Minimal JSON for a GetTimeline response
let private timelineJson =
    {| feed = ([||] : obj array)
       cursor = "cursor123" |}

/// Minimal JSON for a ListNotifications response
let private notificationsJson =
    {| notifications = ([||] : obj array)
       cursor = "notif-cursor" |}

[<Tests>]
let getProfileTests =
    testList
        "Bluesky.getProfile"
        [ testCase "getProfile calls correct XRPC endpoint with actor param"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) profileJson
              let handle = parseHandle' "my-handle.bsky.social"

              let result =
                  Bluesky.getProfile agent handle
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.actor.getProfile" "correct endpoint"
              Expect.stringContains (req.RequestUri.ToString ()) "my-handle.bsky.social" "actor in query string"

          testCase "getProfile accepts Did"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) profileJson
              let did = parseDid' "did:plc:testuser"

              let result =
                  Bluesky.getProfile agent did
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"

              Expect.stringContains
                  (captured.Value.RequestUri.ToString ())
                  "did%3Aplc%3Atestuser"
                  "DID in query string (URL-encoded)"

          testCase "getProfile deserializes response"
          <| fun _ ->
              let agent = queryAgent (fun _ -> ()) profileJson
              let handle = parseHandle' "my-handle.bsky.social"

              let result =
                  Bluesky.getProfile agent handle
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let profile = Expect.wantOk result "should succeed"
              Expect.equal (Did.value profile.Did) "did:plc:testuser" "did"
              Expect.equal (Handle.value profile.Handle) "my-handle.bsky.social" "handle"

          testCase "getProfile returns error on failure"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ ->
                      jsonResponse
                          HttpStatusCode.BadRequest
                          {| error = "InvalidRequest"
                             message = "Invalid actor" |})

              agent.Session <- Some testSession
              let handle = parseHandle' "nonexistent.bsky.social"

              let result =
                  Bluesky.getProfile agent handle
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isError result "should fail"

          testCase "getProfile accepts Handle directly"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) profileJson
              let handle = Handle.parse "my-handle.bsky.social" |> Result.defaultWith failwith

              let result =
                  Bluesky.getProfile agent handle |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"

              Expect.stringContains
                  (captured.Value.RequestUri.ToString ())
                  "my-handle.bsky.social"
                  "handle value in query string"

          testCase "getProfile accepts Did directly"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) profileJson
              let did = Did.parse "did:plc:testuser" |> Result.defaultWith failwith

              let result =
                  Bluesky.getProfile agent did |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"

              Expect.stringContains
                  (captured.Value.RequestUri.ToString ())
                  "did%3Aplc%3Atestuser"
                  "DID value in query string (URL-encoded)"

          testCase "getProfile accepts ProfileSummary"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) profileJson

              let summary : ProfileSummary =
                  { Did = parseDid' "did:plc:testuser"
                    Handle = parseHandle' "my-handle.bsky.social"
                    DisplayName = "Alice"
                    Avatar = None }

              let result =
                  Bluesky.getProfile agent summary |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"

              Expect.stringContains
                  (captured.Value.RequestUri.ToString ())
                  "did%3Aplc%3Atestuser"
                  "DID from ProfileSummary in query string"

          testCase "getProfile accepts Profile"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) profileJson

              let profile : Profile =
                  { Did = parseDid' "did:plc:testuser"
                    Handle = parseHandle' "my-handle.bsky.social"
                    DisplayName = "Alice"
                    Description = ""
                    Avatar = None
                    Banner = None
                    PostsCount = 0L
                    FollowersCount = 0L
                    FollowsCount = 0L
                    IsFollowing = false
                    IsFollowedBy = false
                    IsBlocking = false
                    IsBlockedBy = false
                    IsMuted = false }

              let result =
                  Bluesky.getProfile agent profile |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"

              Expect.stringContains
                  (captured.Value.RequestUri.ToString ())
                  "did%3Aplc%3Atestuser"
                  "DID from Profile in query string" ]

[<Tests>]
let getTimelineTests =
    testList
        "Bluesky.getTimeline"
        [ testCase "getTimeline calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) timelineJson

              let result =
                  Bluesky.getTimeline agent None None |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.feed.getTimeline" "correct endpoint"

          testCase "getTimeline passes limit and cursor params"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) timelineJson

              let result =
                  Bluesky.getTimeline agent (Some 25L) (Some "abc")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let url = captured.Value.RequestUri.ToString ()
              Expect.stringContains url "limit=25" "limit in query"
              Expect.stringContains url "cursor=abc" "cursor in query"

          testCase "getTimeline omits None params from query string"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) timelineJson

              let result =
                  Bluesky.getTimeline agent None None |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let url = captured.Value.RequestUri.ToString ()
              Expect.isFalse (url.Contains ("limit")) "no limit in query"
              Expect.isFalse (url.Contains ("cursor")) "no cursor in query"

          testCase "getTimeline deserializes cursor from response"
          <| fun _ ->
              let agent = queryAgent (fun _ -> ()) timelineJson

              let result =
                  Bluesky.getTimeline agent None None |> Async.AwaitTask |> Async.RunSynchronously

              let output = Expect.wantOk result "should succeed"
              Expect.equal output.Cursor (Some "cursor123") "cursor"
              Expect.equal output.Items [] "empty feed" ]

[<Tests>]
let getPostThreadTests =
    testList
        "Bluesky.getPostThread"
        [ testCase "getPostThread calls correct XRPC endpoint with uri param"
          <| fun _ ->
              let mutable captured = None
              // Use error response to verify request properties without needing valid response JSON
              let agent =
                  createMockAgent (fun req ->
                      captured <- Some req
                      jsonResponse HttpStatusCode.BadRequest {| error = "Test"; message = "mock" |})

              agent.Session <- Some testSession
              let uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.post/abc123"

              let _result =
                  Bluesky.getPostThread agent uri None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.feed.getPostThread" "correct endpoint"

          testCase "getPostThread passes depth and parentHeight params"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createMockAgent (fun req ->
                      captured <- Some req
                      jsonResponse HttpStatusCode.BadRequest {| error = "Test"; message = "mock" |})

              agent.Session <- Some testSession
              let uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.post/abc123"

              let _result =
                  Bluesky.getPostThread agent uri (Some 6L) (Some 80L)
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let url = captured.Value.RequestUri.ToString ()
              Expect.stringContains url "depth=6" "depth in query"
              Expect.stringContains url "parentHeight=80" "parentHeight in query"

          testCase "getPostThread omits None optional params"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createMockAgent (fun req ->
                      captured <- Some req
                      jsonResponse HttpStatusCode.BadRequest {| error = "Test"; message = "mock" |})

              agent.Session <- Some testSession
              let uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.post/abc123"

              let _result =
                  Bluesky.getPostThread agent uri None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let url = captured.Value.RequestUri.ToString ()
              Expect.isFalse (url.Contains ("depth")) "no depth in query"
              Expect.isFalse (url.Contains ("parentHeight")) "no parentHeight in query"

          testCase "getPostThread returns error on failure"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ ->
                      jsonResponse
                          HttpStatusCode.NotFound
                          {| error = "NotFound"
                             message = "Post not found" |})

              agent.Session <- Some testSession
              let uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.post/missing"

              let result =
                  Bluesky.getPostThread agent uri None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isError result "should fail" ]

[<Tests>]
let getNotificationsTests =
    testList
        "Bluesky.getNotifications"
        [ testCase "getNotifications calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) notificationsJson

              let result =
                  Bluesky.getNotifications agent None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"

              Expect.stringContains
                  (req.RequestUri.ToString ())
                  "app.bsky.notification.listNotifications"
                  "correct endpoint"

          testCase "getNotifications passes limit and cursor params"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) notificationsJson

              let result =
                  Bluesky.getNotifications agent (Some 50L) (Some "notif-abc")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let url = captured.Value.RequestUri.ToString ()
              Expect.stringContains url "limit=50" "limit in query"
              Expect.stringContains url "cursor=notif-abc" "cursor in query"

          testCase "getNotifications omits None params from query string"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) notificationsJson

              let result =
                  Bluesky.getNotifications agent None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let url = captured.Value.RequestUri.ToString ()
              Expect.isFalse (url.Contains ("limit")) "no limit in query"
              Expect.isFalse (url.Contains ("cursor")) "no cursor in query"

          testCase "getNotifications deserializes cursor from response"
          <| fun _ ->
              let agent = queryAgent (fun _ -> ()) notificationsJson

              let result =
                  Bluesky.getNotifications agent None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let output = Expect.wantOk result "should succeed"
              Expect.equal output.Cursor (Some "notif-cursor") "cursor"
              Expect.equal output.Items [] "empty notifications" ]

[<Tests>]
let loginTests =
    testList
        "Bluesky.login"
        [ testCase "loginWithClient authenticates and returns agent with session"
          <| fun _ ->
              let loginResponse =
                  {| did = "did:plc:testlogin"
                     handle = "testlogin.bsky.social"
                     accessJwt = "access-jwt-123"
                     refreshJwt = "refresh-jwt-456" |}

              let client =
                  new HttpClient (new MockHandler (fun _ -> jsonResponse HttpStatusCode.OK loginResponse))

              let result =
                  Bluesky.loginWithClient client "https://bsky.social" "testlogin.bsky.social" "app-pass"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let agent = Expect.wantOk result "login should succeed"
              Expect.isSome agent.Session "agent should have session"
              let session = agent.Session.Value
              Expect.equal (Did.value session.Did) "did:plc:testlogin" "session DID"
              Expect.equal (Handle.value session.Handle) "testlogin.bsky.social" "session handle"
              Expect.equal session.AccessJwt "access-jwt-123" "access JWT"
              Expect.equal session.RefreshJwt "refresh-jwt-456" "refresh JWT"

          testCase "loginWithClient returns error on auth failure"
          <| fun _ ->
              let client =
                  new HttpClient (
                      new MockHandler (fun _ ->
                          jsonResponse
                              HttpStatusCode.Unauthorized
                              {| error = "AuthenticationRequired"
                                 message = "Invalid password" |})
                  )

              let result =
                  Bluesky.loginWithClient client "https://bsky.social" "test.bsky.social" "wrong-pass"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isError result "login should fail" ]

// ── Paginator helpers ──────────────────────────────────────────────

/// Create a mock agent whose handler dispatches based on whether the request URL contains a cursor param
let private paginatingAgent (page1 : obj) (page2 : obj) =
    let agent =
        createMockAgent (fun req ->
            let url = req.RequestUri.ToString ()

            if url.Contains ("cursor=") then
                jsonResponse HttpStatusCode.OK page2
            else
                jsonResponse HttpStatusCode.OK page1)

    agent.Session <- Some testSession
    agent

/// Collect all pages from an IAsyncEnumerable into a list
let private collectPages (pages : System.Collections.Generic.IAsyncEnumerable<'T>) : 'T list =
    let result = System.Collections.Generic.List<'T> ()
    let enumerator = pages.GetAsyncEnumerator (System.Threading.CancellationToken.None)

    let rec loop () =
        async {
            let! hasNext = enumerator.MoveNextAsync().AsTask () |> Async.AwaitTask

            if hasNext then
                result.Add (enumerator.Current)
                return! loop ()
        }

    loop () |> Async.RunSynchronously
    enumerator.DisposeAsync().AsTask () |> Async.AwaitTask |> Async.RunSynchronously
    result |> Seq.toList

[<Tests>]
let paginateTimelineTests =
    testList
        "Bluesky.paginateTimeline"
        [ testCase "returns pages and stops when no cursor"
          <| fun _ ->
              let page1 =
                  {| feed = ([||] : obj array)
                     cursor = "page2" |}

              let page2 = {| feed = ([||] : obj array) |}
              let agent = paginatingAgent page1 page2
              let pages = Bluesky.paginateTimeline agent (Some 10L) |> collectPages
              Expect.equal pages.Length 2 "exactly 2 pages"
              Expect.isOk pages.[0] "first page is Ok"
              Expect.isOk pages.[1] "second page is Ok"

          testCase "first page has cursor, second page has none"
          <| fun _ ->
              let page1 =
                  {| feed = ([||] : obj array)
                     cursor = "page2" |}

              let page2 = {| feed = ([||] : obj array) |}
              let agent = paginatingAgent page1 page2
              let pages = Bluesky.paginateTimeline agent None |> collectPages
              let p1 = Expect.wantOk pages.[0] "page1 ok"
              let p2 = Expect.wantOk pages.[1] "page2 ok"
              Expect.equal p1.Cursor (Some "page2") "first page cursor"
              Expect.equal p2.Cursor None "second page no cursor"

          testCase "single page when server returns no cursor"
          <| fun _ ->
              let agent = queryAgent (fun _ -> ()) {| feed = ([||] : obj array) |}
              let pages = Bluesky.paginateTimeline agent None |> collectPages
              Expect.equal pages.Length 1 "exactly 1 page" ]

[<Tests>]
let paginateFollowersTests =
    testList
        "Bluesky.paginateFollowers"
        [ testCase "returns pages and stops when no cursor"
          <| fun _ ->
              let subject =
                  {| did = "did:plc:testuser"
                     handle = "test.bsky.social" |}

              let page1 =
                  {| followers = ([||] : obj array)
                     subject = subject
                     cursor = "page2" |}

              let page2 =
                  {| followers = ([||] : obj array)
                     subject = subject |}

              let agent = paginatingAgent page1 page2

              let handle = parseHandle' "my-handle.bsky.social"

              let pages =
                  Bluesky.paginateFollowers agent handle (Some 25L)
                  |> collectPages

              Expect.equal pages.Length 2 "exactly 2 pages"
              Expect.isOk pages.[0] "first page is Ok"
              Expect.isOk pages.[1] "second page is Ok"

          testCase "passes actor parameter in requests"
          <| fun _ ->
              let mutable captured = System.Collections.Generic.List<HttpRequestMessage> ()

              let subject =
                  {| did = "did:plc:testuser"
                     handle = "test.bsky.social" |}

              let agent =
                  createMockAgent (fun req ->
                      captured.Add (req)

                      jsonResponse
                          HttpStatusCode.OK
                          {| followers = ([||] : obj array)
                             subject = subject |})

              agent.Session <- Some testSession

              let handle = parseHandle' "my-handle.bsky.social"

              let pages =
                  Bluesky.paginateFollowers agent handle None |> collectPages

              Expect.equal pages.Length 1 "single page"
              Expect.stringContains (captured.[0].RequestUri.ToString ()) "my-handle.bsky.social" "actor in query" ]

[<Tests>]
let paginateNotificationsTests =
    testList
        "Bluesky.paginateNotifications"
        [ testCase "returns pages and stops when no cursor"
          <| fun _ ->
              let page1 =
                  {| notifications = ([||] : obj array)
                     cursor = "page2" |}

              let page2 = {| notifications = ([||] : obj array) |}
              let agent = paginatingAgent page1 page2
              let pages = Bluesky.paginateNotifications agent (Some 50L) |> collectPages
              Expect.equal pages.Length 2 "exactly 2 pages"
              Expect.isOk pages.[0] "first page is Ok"
              Expect.isOk pages.[1] "second page is Ok"

          testCase "first page has cursor, second page has none"
          <| fun _ ->
              let page1 =
                  {| notifications = ([||] : obj array)
                     cursor = "notif-page2" |}

              let page2 = {| notifications = ([||] : obj array) |}
              let agent = paginatingAgent page1 page2
              let pages = Bluesky.paginateNotifications agent None |> collectPages
              let p1 = Expect.wantOk pages.[0] "page1 ok"
              let p2 = Expect.wantOk pages.[1] "page2 ok"
              Expect.equal p1.Cursor (Some "notif-page2") "first page cursor"
              Expect.equal p2.Cursor None "second page no cursor"

          testCase "single page when server returns no cursor"
          <| fun _ ->
              let agent = queryAgent (fun _ -> ()) {| notifications = ([||] : obj array) |}
              let pages = Bluesky.paginateNotifications agent None |> collectPages
              Expect.equal pages.Length 1 "exactly 1 page" ]

// ── Helpers for new write operation tests ────────────────────────────

/// Creates a mock agent that returns an empty 200 OK (for procedureVoid endpoints).
let private voidProcedureAgent (captureRequest : HttpRequestMessage -> unit) =
    let agent =
        createMockAgent (fun req ->
            captureRequest req
            emptyResponse HttpStatusCode.OK)

    agent.Session <- Some testSession
    agent

// ── Write operation tests ────────────────────────────────────────────

[<Tests>]
let muteUserTests =
    testList
        "Bluesky.muteUser"
        [ testCase "muteUser calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.muteUser agent (parseDid "did:plc:testmute")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.graph.muteActor" "correct endpoint"

          testCase "muteUser sends actor in request body"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)

              let _result =
                  Bluesky.muteUser agent (parseDid "did:plc:spammer")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "did:plc:spammer" "actor in body" ]

[<Tests>]
let unmuteUserTests =
    testList
        "Bluesky.unmuteUser"
        [ testCase "unmuteUser calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.unmuteUser agent (parseDid "did:plc:testunmute")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.graph.unmuteActor" "correct endpoint"

          testCase "unmuteUser sends actor in request body"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)

              let _result =
                  Bluesky.unmuteUser agent (parseDid "did:plc:friend")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "did:plc:friend" "actor in body" ]

[<Tests>]
let muteThreadTests =
    testList
        "Bluesky.muteThread"
        [ testCase "muteThread calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)
              let root = parseAtUri "at://did:plc:testuser/app.bsky.feed.post/abc123"

              let result =
                  Bluesky.muteThread agent root |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.graph.muteThread" "correct endpoint"

          testCase "muteThread sends root URI in request body"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)
              let root = parseAtUri "at://did:plc:testuser/app.bsky.feed.post/thread1"

              let _result =
                  Bluesky.muteThread agent root |> Async.AwaitTask |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "thread1" "root in body" ]

[<Tests>]
let unmuteThreadTests =
    testList
        "Bluesky.unmuteThread"
        [ testCase "unmuteThread calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)
              let root = parseAtUri "at://did:plc:testuser/app.bsky.feed.post/abc123"

              let result =
                  Bluesky.unmuteThread agent root |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.graph.unmuteThread" "correct endpoint"

          testCase "unmuteThread sends root URI in request body"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)
              let root = parseAtUri "at://did:plc:testuser/app.bsky.feed.post/thread2"

              let _result =
                  Bluesky.unmuteThread agent root |> Async.AwaitTask |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "thread2" "root in body" ]

[<Tests>]
let reportContentTests =
    testList
        "Bluesky.reportContent"
        [ testCase "reportContent calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None
              // Use error response to verify request properties without needing valid response JSON.
              // The CreateReport.Output has KnownValue DUs (ReasonType) and inline unions (OutputSubjectUnion)
              // which conflict with the global JsonFSharpConverter during deserialization (known limitation).
              let agent =
                  createMockAgent (fun req ->
                      captured <- Some req
                      jsonResponse HttpStatusCode.BadRequest {| error = "Test"; message = "mock" |})

              agent.Session <- Some testSession
              let subject = ReportSubject.Account (parseDid "did:plc:test")

              let _result =
                  Bluesky.reportContent agent subject ComAtprotoModeration.Defs.ReasonType.ReasonSpam None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"

              Expect.stringContains
                  (req.RequestUri.ToString ())
                  "com.atproto.moderation.createReport"
                  "correct endpoint"

          testCase "reportContent sends correct request body for account report"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createMockAgent (fun req ->
                      captured <- Some req
                      jsonResponse HttpStatusCode.BadRequest {| error = "Test"; message = "mock" |})

              agent.Session <- Some testSession
              let subject = ReportSubject.Account (parseDid "did:plc:test")

              let _result =
                  Bluesky.reportContent
                      agent
                      subject
                      ComAtprotoModeration.Defs.ReasonType.ReasonSpam
                      (Some "spam account")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "did:plc:test" "subject DID in body"
              Expect.stringContains body "com.atproto.moderation.defs#reasonSpam" "reason type in body"
              Expect.stringContains body "spam account" "description in body"

          testCase "reportContent sends correct request body for record report"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createMockAgent (fun req ->
                      captured <- Some req
                      jsonResponse HttpStatusCode.BadRequest {| error = "Test"; message = "mock" |})

              agent.Session <- Some testSession

              let postRef =
                  { PostRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.post/abc123"
                    Cid = parseCid "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454" }

              let subject = ReportSubject.Record postRef

              let _result =
                  Bluesky.reportContent agent subject ComAtprotoModeration.Defs.ReasonType.ReasonSpam None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "abc123" "post rkey in body"
              Expect.stringContains body "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454" "CID in body" ]

[<Tests>]
let addBookmarkTests =
    testList
        "Bluesky.addBookmark"
        [ testCase "addBookmark calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)

              let postRef =
                  { PostRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.post/abc123"
                    Cid = parseCid "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454" }

              let result =
                  Bluesky.addBookmark agent postRef |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"

              Expect.stringContains
                  (req.RequestUri.ToString ())
                  "app.bsky.bookmark.createBookmark"
                  "correct endpoint"

          testCase "addBookmark sends uri and cid in request body"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)

              let postRef =
                  { PostRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.post/bookmark1"
                    Cid = parseCid "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454" }

              let _result =
                  Bluesky.addBookmark agent postRef |> Async.AwaitTask |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "bookmark1" "uri in body"
              Expect.stringContains body "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454" "cid in body" ]

[<Tests>]
let removeBookmarkTests =
    testList
        "Bluesky.removeBookmark"
        [ testCase "removeBookmark calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)
              let uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.post/abc123"

              let result =
                  Bluesky.removeBookmark agent uri |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"

              Expect.stringContains
                  (req.RequestUri.ToString ())
                  "app.bsky.bookmark.deleteBookmark"
                  "correct endpoint"

          testCase "removeBookmark sends uri in request body"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)
              let uri = parseAtUri "at://did:plc:testuser/app.bsky.feed.post/rm1"

              let _result =
                  Bluesky.removeBookmark agent uri |> Async.AwaitTask |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "rm1" "uri in body" ]

[<Tests>]
let updateHandleTests =
    testList
        "Bluesky.updateHandle"
        [ testCase "updateHandle calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)
              let handle = Handle.parse "new-handle.bsky.social" |> Result.defaultWith failwith

              let result =
                  Bluesky.updateHandle agent handle |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"

              Expect.stringContains
                  (req.RequestUri.ToString ())
                  "com.atproto.identity.updateHandle"
                  "correct endpoint"

          testCase "updateHandle sends handle in request body"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)
              let handle = Handle.parse "new-handle.bsky.social" |> Result.defaultWith failwith

              let _result =
                  Bluesky.updateHandle agent handle |> Async.AwaitTask |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "new-handle.bsky.social" "handle in body" ]

// ── Read operation tests ─────────────────────────────────────────────

[<Tests>]
let searchPostsTests =
    testList
        "Bluesky.searchPosts"
        [ testCase "searchPosts calls correct XRPC endpoint with q param"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| posts =
                          [| {| uri = "at://did:plc:test/app.bsky.feed.post/abc"
                                cid = "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454"
                                author = {| did = "did:plc:test"; handle = "test.bsky.social" |}
                                record = {| |}
                                indexedAt = "2024-01-15T12:00:00.000Z" |} |]
                         cursor = "page2" |}

              let result =
                  Bluesky.searchPosts agent "cats" None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.feed.searchPosts" "correct endpoint"
              Expect.stringContains (req.RequestUri.ToString ()) "q=cats" "q param"

          testCase "searchPosts returns Page with cursor and items"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| posts =
                          [| {| uri = "at://did:plc:test/app.bsky.feed.post/abc"
                                cid = "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454"
                                author = {| did = "did:plc:test"; handle = "test.bsky.social" |}
                                record = {| |}
                                indexedAt = "2024-01-15T12:00:00.000Z" |} |]
                         cursor = "page2" |}

              let result =
                  Bluesky.searchPosts agent "cats" None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.equal page.Cursor (Some "page2") "cursor"
              Expect.equal page.Items.Length 1 "one post" ]

[<Tests>]
let searchActorsTests =
    testList
        "Bluesky.searchActors"
        [ testCase "searchActors calls correct XRPC endpoint with q param"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| actors =
                          [| {| did = "did:plc:test"
                                handle = "test.bsky.social"
                                displayName = "Test User" |} |]
                         cursor = "page2" |}

              let result =
                  Bluesky.searchActors agent "alice" None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.actor.searchActors" "correct endpoint"
              Expect.stringContains (req.RequestUri.ToString ()) "q=alice" "q param"

          testCase "searchActors returns Page with cursor and items"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| actors =
                          [| {| did = "did:plc:test"
                                handle = "test.bsky.social"
                                displayName = "Test User" |} |]
                         cursor = "page2" |}

              let result =
                  Bluesky.searchActors agent "alice" None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.equal page.Cursor (Some "page2") "cursor"
              Expect.equal page.Items.Length 1 "one actor" ]

[<Tests>]
let getAuthorFeedTests =
    testList
        "Bluesky.getAuthorFeed"
        [ testCase "getAuthorFeed calls correct XRPC endpoint with actor param"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| feed =
                          [| {| post =
                                  {| uri = "at://did:plc:test/app.bsky.feed.post/abc"
                                     cid = "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454"
                                     author = {| did = "did:plc:test"; handle = "test.bsky.social" |}
                                     record = {| |}
                                     indexedAt = "2024-01-15T12:00:00.000Z" |} |} |]
                         cursor = "page2" |}

              let handle = parseHandle' "test.bsky.social"

              let result =
                  Bluesky.getAuthorFeed agent handle None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.feed.getAuthorFeed" "correct endpoint"
              Expect.stringContains (req.RequestUri.ToString ()) "actor=test.bsky.social" "actor param"

          testCase "getAuthorFeed returns Page with cursor and items"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| feed =
                          [| {| post =
                                  {| uri = "at://did:plc:test/app.bsky.feed.post/abc"
                                     cid = "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454"
                                     author = {| did = "did:plc:test"; handle = "test.bsky.social" |}
                                     record = {| |}
                                     indexedAt = "2024-01-15T12:00:00.000Z" |} |} |]
                         cursor = "page2" |}

              let handle = parseHandle' "test.bsky.social"

              let result =
                  Bluesky.getAuthorFeed agent handle None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.equal page.Cursor (Some "page2") "cursor"
              Expect.equal page.Items.Length 1 "one item" ]

[<Tests>]
let getActorLikesTests =
    testList
        "Bluesky.getActorLikes"
        [ testCase "getActorLikes calls correct XRPC endpoint with actor param"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| feed =
                          [| {| post =
                                  {| uri = "at://did:plc:test/app.bsky.feed.post/abc"
                                     cid = "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454"
                                     author = {| did = "did:plc:test"; handle = "test.bsky.social" |}
                                     record = {| |}
                                     indexedAt = "2024-01-15T12:00:00.000Z" |} |} |]
                         cursor = "page2" |}

              let handle = parseHandle' "test.bsky.social"

              let result =
                  Bluesky.getActorLikes agent handle None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.feed.getActorLikes" "correct endpoint"
              Expect.stringContains (req.RequestUri.ToString ()) "actor=test.bsky.social" "actor param"

          testCase "getActorLikes returns Page with cursor and items"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| feed =
                          [| {| post =
                                  {| uri = "at://did:plc:test/app.bsky.feed.post/abc"
                                     cid = "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454"
                                     author = {| did = "did:plc:test"; handle = "test.bsky.social" |}
                                     record = {| |}
                                     indexedAt = "2024-01-15T12:00:00.000Z" |} |} |]
                         cursor = "page2" |}

              let handle = parseHandle' "test.bsky.social"

              let result =
                  Bluesky.getActorLikes agent handle None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.equal page.Cursor (Some "page2") "cursor"
              Expect.equal page.Items.Length 1 "one item" ]

[<Tests>]
let getLikesTests =
    testList
        "Bluesky.getLikes"
        [ testCase "getLikes calls correct XRPC endpoint with uri param"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| uri = "at://did:plc:test/app.bsky.feed.post/abc"
                         likes =
                          [| {| actor =
                                  {| did = "did:plc:test"
                                     handle = "test.bsky.social"
                                     displayName = "Test User" |}
                                createdAt = "2024-01-15T12:00:00.000Z"
                                indexedAt = "2024-01-15T12:00:00.000Z" |} |]
                         cursor = "page2" |}

              let uri = parseAtUri "at://did:plc:test/app.bsky.feed.post/abc"

              let result =
                  Bluesky.getLikes agent uri None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.feed.getLikes" "correct endpoint"

          testCase "getLikes returns Page with cursor and items"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| uri = "at://did:plc:test/app.bsky.feed.post/abc"
                         likes =
                          [| {| actor =
                                  {| did = "did:plc:test"
                                     handle = "test.bsky.social"
                                     displayName = "Test User" |}
                                createdAt = "2024-01-15T12:00:00.000Z"
                                indexedAt = "2024-01-15T12:00:00.000Z" |} |]
                         cursor = "page2" |}

              let uri = parseAtUri "at://did:plc:test/app.bsky.feed.post/abc"

              let result =
                  Bluesky.getLikes agent uri None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.equal page.Cursor (Some "page2") "cursor"
              Expect.equal page.Items.Length 1 "one liker" ]

[<Tests>]
let getRepostedByTests =
    testList
        "Bluesky.getRepostedBy"
        [ testCase "getRepostedBy calls correct XRPC endpoint with uri param"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| uri = "at://did:plc:test/app.bsky.feed.post/abc"
                         repostedBy =
                          [| {| did = "did:plc:test"
                                handle = "test.bsky.social"
                                displayName = "Test User" |} |]
                         cursor = "page2" |}

              let uri = parseAtUri "at://did:plc:test/app.bsky.feed.post/abc"

              let result =
                  Bluesky.getRepostedBy agent uri None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.feed.getRepostedBy" "correct endpoint"

          testCase "getRepostedBy returns Page with cursor and items"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| uri = "at://did:plc:test/app.bsky.feed.post/abc"
                         repostedBy =
                          [| {| did = "did:plc:test"
                                handle = "test.bsky.social"
                                displayName = "Test User" |} |]
                         cursor = "page2" |}

              let uri = parseAtUri "at://did:plc:test/app.bsky.feed.post/abc"

              let result =
                  Bluesky.getRepostedBy agent uri None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.equal page.Cursor (Some "page2") "cursor"
              Expect.equal page.Items.Length 1 "one reposter" ]

[<Tests>]
let getQuotesTests =
    testList
        "Bluesky.getQuotes"
        [ testCase "getQuotes calls correct XRPC endpoint with uri param"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| uri = "at://did:plc:test/app.bsky.feed.post/abc"
                         posts =
                          [| {| uri = "at://did:plc:test/app.bsky.feed.post/quote1"
                                cid = "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454"
                                author = {| did = "did:plc:test"; handle = "test.bsky.social" |}
                                record = {| |}
                                indexedAt = "2024-01-15T12:00:00.000Z" |} |]
                         cursor = "page2" |}

              let uri = parseAtUri "at://did:plc:test/app.bsky.feed.post/abc"

              let result =
                  Bluesky.getQuotes agent uri None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.feed.getQuotes" "correct endpoint"

          testCase "getQuotes returns Page with cursor and items"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| uri = "at://did:plc:test/app.bsky.feed.post/abc"
                         posts =
                          [| {| uri = "at://did:plc:test/app.bsky.feed.post/quote1"
                                cid = "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454"
                                author = {| did = "did:plc:test"; handle = "test.bsky.social" |}
                                record = {| |}
                                indexedAt = "2024-01-15T12:00:00.000Z" |} |]
                         cursor = "page2" |}

              let uri = parseAtUri "at://did:plc:test/app.bsky.feed.post/abc"

              let result =
                  Bluesky.getQuotes agent uri None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.equal page.Cursor (Some "page2") "cursor"
              Expect.equal page.Items.Length 1 "one quote" ]

[<Tests>]
let getPostsTests =
    testList
        "Bluesky.getPosts"
        [ testCase "getPosts calls correct XRPC endpoint with uris param"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| posts =
                          [| {| uri = "at://did:plc:test/app.bsky.feed.post/abc"
                                cid = "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454"
                                author = {| did = "did:plc:test"; handle = "test.bsky.social" |}
                                record = {| |}
                                indexedAt = "2024-01-15T12:00:00.000Z" |} |] |}

              let uris =
                  [ parseAtUri "at://did:plc:test/app.bsky.feed.post/abc" ]

              let result =
                  Bluesky.getPosts agent uris |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.feed.getPosts" "correct endpoint"

          testCase "getPosts returns TimelinePost list"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| posts =
                          [| {| uri = "at://did:plc:test/app.bsky.feed.post/abc"
                                cid = "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454"
                                author = {| did = "did:plc:test"; handle = "test.bsky.social" |}
                                record = {| |}
                                indexedAt = "2024-01-15T12:00:00.000Z" |} |] |}

              let uris =
                  [ parseAtUri "at://did:plc:test/app.bsky.feed.post/abc" ]

              let result =
                  Bluesky.getPosts agent uris |> Async.AwaitTask |> Async.RunSynchronously

              let posts = Expect.wantOk result "should succeed"
              Expect.equal posts.Length 1 "one post" ]

[<Tests>]
let getProfilesTests =
    testList
        "Bluesky.getProfiles"
        [ testCase "getProfiles calls correct XRPC endpoint with actors param"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| profiles =
                          [| {| did = "did:plc:test"
                                handle = "test.bsky.social"
                                displayName = "Test User"
                                description = "A test profile"
                                postsCount = 42L
                                followersCount = 100L
                                followsCount = 50L |} |] |}

              let result =
                  Bluesky.getProfiles agent [ parseDid' "did:plc:test" ]
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.actor.getProfiles" "correct endpoint"

          testCase "getProfiles returns Profile list"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| profiles =
                          [| {| did = "did:plc:test"
                                handle = "test.bsky.social"
                                displayName = "Test User"
                                description = "A test profile"
                                postsCount = 42L
                                followersCount = 100L
                                followsCount = 50L |} |] |}

              let result =
                  Bluesky.getProfiles agent [ parseDid' "did:plc:test" ]
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let profiles = Expect.wantOk result "should succeed"
              Expect.equal profiles.Length 1 "one profile" ]

[<Tests>]
let getSuggestedFollowsTests =
    testList
        "Bluesky.getSuggestedFollows"
        [ testCase "getSuggestedFollows calls correct XRPC endpoint with actor param"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| suggestions =
                          [| {| did = "did:plc:test"
                                handle = "test.bsky.social"
                                displayName = "Test User" |} |] |}

              let handle = parseHandle' "alice.bsky.social"

              let result =
                  Bluesky.getSuggestedFollows agent handle
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"

              Expect.stringContains
                  (req.RequestUri.ToString ())
                  "app.bsky.graph.getSuggestedFollowsByActor"
                  "correct endpoint"

              Expect.stringContains (req.RequestUri.ToString ()) "alice.bsky.social" "actor param"

          testCase "getSuggestedFollows returns ProfileSummary list"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| suggestions =
                          [| {| did = "did:plc:test"
                                handle = "test.bsky.social"
                                displayName = "Test User" |} |] |}

              let handle = parseHandle' "alice.bsky.social"

              let result =
                  Bluesky.getSuggestedFollows agent handle
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let suggestions = Expect.wantOk result "should succeed"
              Expect.equal suggestions.Length 1 "one suggestion" ]

[<Tests>]
let getUnreadNotificationCountTests =
    testList
        "Bluesky.getUnreadNotificationCount"
        [ testCase "getUnreadNotificationCount calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) {| count = 7L |}

              let result =
                  Bluesky.getUnreadNotificationCount agent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"

              Expect.stringContains
                  (req.RequestUri.ToString ())
                  "app.bsky.notification.getUnreadCount"
                  "correct endpoint"

          testCase "getUnreadNotificationCount returns count from response"
          <| fun _ ->
              let agent = queryAgent (fun _ -> ()) {| count = 42L |}

              let result =
                  Bluesky.getUnreadNotificationCount agent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let count = Expect.wantOk result "should succeed"
              Expect.equal count 42L "notification count" ]

[<Tests>]
let markNotificationsSeenTests =
    testList
        "Bluesky.markNotificationsSeen"
        [ testCase "markNotificationsSeen calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.markNotificationsSeen agent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"

              Expect.stringContains
                  (req.RequestUri.ToString ())
                  "app.bsky.notification.updateSeen"
                  "correct endpoint"

          testCase "markNotificationsSeen sends seenAt in request body"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)

              let _result =
                  Bluesky.markNotificationsSeen agent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "seenAt" "seenAt in body" ]

[<Tests>]
let getPreferencesTests =
    testList
        "Bluesky.getPreferences"
        [ testCase "getPreferences calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) {| preferences = ([||] : obj array) |}

              let result =
                  Bluesky.getPreferences agent |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"

              Expect.stringContains
                  (req.RequestUri.ToString ())
                  "app.bsky.actor.getPreferences"
                  "correct endpoint"

          testCase "getPreferences returns Preferences from response"
          <| fun _ ->
              let agent = queryAgent (fun _ -> ()) {| preferences = ([||] : obj array) |}

              let result =
                  Bluesky.getPreferences agent |> Async.AwaitTask |> Async.RunSynchronously

              let prefs = Expect.wantOk result "should succeed"
              Expect.isEmpty prefs "empty preferences" ]

[<Tests>]
let getBookmarksTests =
    testList
        "Bluesky.getBookmarks"
        [ testCase "getBookmarks calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None
              // Use empty bookmarks list to avoid BookmarkViewItemUnion deserialization
              // (inline union $type tag conflicts with global JsonFSharpConverter -- known limitation).
              let agent =
                  queryAgent (fun req -> captured <- Some req) {| bookmarks = ([||] : obj array); cursor = "page2" |}

              let result =
                  Bluesky.getBookmarks agent None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.bookmark.getBookmarks" "correct endpoint"

          testCase "getBookmarks returns Page with cursor"
          <| fun _ ->
              let agent =
                  queryAgent (fun _ -> ()) {| bookmarks = ([||] : obj array); cursor = "page2" |}

              let result =
                  Bluesky.getBookmarks agent None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.equal page.Cursor (Some "page2") "cursor"
              Expect.equal page.Items [] "empty items from empty bookmarks"

          testCase "getBookmarks passes limit and cursor params"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| bookmarks = ([||] : obj array)
                         cursor = null |}

              let result =
                  Bluesky.getBookmarks agent (Some 10L) (Some "prev-cursor")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let url = captured.Value.RequestUri.ToString ()
              Expect.stringContains url "limit=10" "limit in query"
              Expect.stringContains url "cursor=prev-cursor" "cursor in query" ]

// ── Quick-win convenience function tests ──────────────────────────

[<Tests>]
let searchActorsTypeaheadTests =
    testList
        "Bluesky.searchActorsTypeahead"
        [ testCase "searchActorsTypeahead calls correct XRPC endpoint with q param"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| actors =
                          [| {| did = "did:plc:test"
                                handle = "test.bsky.social"
                                displayName = "Test User" |} |] |}

              let result =
                  Bluesky.searchActorsTypeahead agent "ali" None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"

              Expect.stringContains
                  (req.RequestUri.ToString ())
                  "app.bsky.actor.searchActorsTypeahead"
                  "correct endpoint"

              Expect.stringContains (req.RequestUri.ToString ()) "q=ali" "q param"

          testCase "searchActorsTypeahead returns flat list of ProfileSummary"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| actors =
                          [| {| did = "did:plc:alice"
                                handle = "alice.bsky.social"
                                displayName = "Alice" |}
                             {| did = "did:plc:bob"
                                handle = "bob.bsky.social"
                                displayName = "Bob" |} |] |}

              let result =
                  Bluesky.searchActorsTypeahead agent "a" None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let profiles = Expect.wantOk result "should succeed"
              Expect.equal profiles.Length 2 "two profiles"
              Expect.equal (Did.value profiles.[0].Did) "did:plc:alice" "first DID"
              Expect.equal (Did.value profiles.[1].Did) "did:plc:bob" "second DID"

          testCase "searchActorsTypeahead passes limit param"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent (fun req -> captured <- Some req) {| actors = ([||] : obj array) |}

              let result =
                  Bluesky.searchActorsTypeahead agent "test" (Some 5L)
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let url = captured.Value.RequestUri.ToString ()
              Expect.stringContains url "limit=5" "limit in query" ]

[<Tests>]
let getSuggestionsTests =
    testList
        "Bluesky.getSuggestions"
        [ testCase "getSuggestions calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| actors =
                          [| {| did = "did:plc:test"
                                handle = "test.bsky.social"
                                displayName = "Test User" |} |]
                         cursor = "page2" |}

              let result =
                  Bluesky.getSuggestions agent None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"

              Expect.stringContains
                  (req.RequestUri.ToString ())
                  "app.bsky.actor.getSuggestions"
                  "correct endpoint"

          testCase "getSuggestions returns Page with cursor and items"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| actors =
                          [| {| did = "did:plc:test"
                                handle = "test.bsky.social"
                                displayName = "Test User" |} |]
                         cursor = "page2" |}

              let result =
                  Bluesky.getSuggestions agent None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.equal page.Cursor (Some "page2") "cursor"
              Expect.equal page.Items.Length 1 "one suggestion"

          testCase "getSuggestions passes limit and cursor params"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| actors = ([||] : obj array)
                         cursor = null |}

              let result =
                  Bluesky.getSuggestions agent (Some 10L) (Some "prev")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let url = captured.Value.RequestUri.ToString ()
              Expect.stringContains url "limit=10" "limit in query"
              Expect.stringContains url "cursor=prev" "cursor in query" ]

[<Tests>]
let muteModListTests =
    testList
        "Bluesky.muteModList"
        [ testCase "muteModList calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)
              let listUri = parseAtUri "at://did:plc:testuser/app.bsky.graph.list/mod1"

              let result =
                  Bluesky.muteModList agent listUri |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"

              Expect.stringContains
                  (req.RequestUri.ToString ())
                  "app.bsky.graph.muteActorList"
                  "correct endpoint"

          testCase "muteModList sends list URI in request body"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)
              let listUri = parseAtUri "at://did:plc:testuser/app.bsky.graph.list/mod1"

              let _result =
                  Bluesky.muteModList agent listUri |> Async.AwaitTask |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "mod1" "list URI in body" ]

[<Tests>]
let unmuteModListTests =
    testList
        "Bluesky.unmuteModList"
        [ testCase "unmuteModList calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)
              let listUri = parseAtUri "at://did:plc:testuser/app.bsky.graph.list/mod1"

              let result =
                  Bluesky.unmuteModList agent listUri |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"

              Expect.stringContains
                  (req.RequestUri.ToString ())
                  "app.bsky.graph.unmuteActorList"
                  "correct endpoint"

          testCase "unmuteModList sends list URI in request body"
          <| fun _ ->
              let mutable captured = None
              let agent = voidProcedureAgent (fun req -> captured <- Some req)
              let listUri = parseAtUri "at://did:plc:testuser/app.bsky.graph.list/mod2"

              let _result =
                  Bluesky.unmuteModList agent listUri |> Async.AwaitTask |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "mod2" "list URI in body" ]

[<Tests>]
let blockModListTests =
    testList
        "Bluesky.blockModList"
        [ testCase "blockModList creates record with correct collection and subject"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)
              let listUri = parseAtUri "at://did:plc:testuser/app.bsky.graph.list/mod1"

              let result =
                  Bluesky.blockModList agent listUri |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.graph.listblock" "listblock collection"
              Expect.stringContains body "mod1" "list URI in record"

          testCase "blockModList returns ListBlockRef with uri"
          <| fun _ ->
              let agent = createRecordAgent (fun _ -> ())
              let listUri = parseAtUri "at://did:plc:testuser/app.bsky.graph.list/mod1"

              let result =
                  Bluesky.blockModList agent listUri |> Async.AwaitTask |> Async.RunSynchronously

              let listBlockRef = Expect.wantOk result "should succeed"

              Expect.equal
                  (AtUri.value listBlockRef.Uri)
                  "at://did:plc:testuser/app.bsky.feed.post/abc123"
                  "returns uri"

          testCase "blockModList returns NotLoggedIn error without session"
          <| fun _ ->
              let agent = createMockAgent (fun _ -> jsonResponse HttpStatusCode.OK {| |})
              let listUri = parseAtUri "at://did:plc:testuser/app.bsky.graph.list/mod1"

              let result =
                  Bluesky.blockModList agent listUri |> Async.AwaitTask |> Async.RunSynchronously

              let err = Expect.wantError result "should fail without session"
              Expect.equal err.StatusCode 401 "status code"
              Expect.equal err.Error (Some "NotLoggedIn") "error code" ]

[<Tests>]
let unblockModListTests =
    testList
        "Bluesky.unblockModList"
        [ testCase "unblockModList delegates to deleteRecord with ListBlockRef uri"
          <| fun _ ->
              let mutable captured = None
              let agent = deleteRecordAgent (fun req -> captured <- Some req)

              let listBlockRef =
                  { ListBlockRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.graph.listblock/lb1" }

              let result =
                  Bluesky.unblockModList agent listBlockRef
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.graph.listblock" "collection"
              Expect.stringContains body "lb1" "rkey"

          testCase "SRTP undo works with ListBlockRef"
          <| fun _ ->
              let agent = deleteRecordAgent (fun _ -> ())

              let listBlockRef =
                  { ListBlockRef.Uri = parseAtUri "at://did:plc:testuser/app.bsky.graph.listblock/srtp5" }

              let result =
                  Bluesky.undo agent listBlockRef |> Async.AwaitTask |> Async.RunSynchronously

              let undoResult = Expect.wantOk result "should succeed"
              Expect.equal undoResult Undone "should be Undone" ]

[<Tests>]
let resumeSessionTests =
    testList
        "Bluesky.resumeSession"
        [ testCase "resumeSession creates agent with session set"
          <| fun _ ->
              let agent = Bluesky.resumeSession "https://bsky.social" testSession
              Expect.isSome agent.Session "should have session"
              let session = agent.Session.Value
              Expect.equal session.AccessJwt "test-jwt" "access JWT"
              Expect.equal session.RefreshJwt "test-refresh" "refresh JWT"
              Expect.equal (Did.value session.Did) "did:plc:testuser" "session DID"

          testCase "resumeSession sets correct base URL"
          <| fun _ ->
              let agent = Bluesky.resumeSession "https://my-pds.example.com" testSession
              Expect.stringContains (agent.BaseUrl.ToString ()) "my-pds.example.com" "base URL"

          testCase "resumeSessionWithClient uses provided HttpClient"
          <| fun _ ->
              let mutable captured = None

              let client =
                  new HttpClient (
                      new MockHandler (fun req ->
                          captured <- Some req
                          jsonResponse HttpStatusCode.OK {| |})
                  )

              let agent = Bluesky.resumeSessionWithClient client "https://bsky.social" testSession
              Expect.isSome agent.Session "should have session"

              // Verify the agent uses the provided client by making a request
              let _result =
                  Bluesky.muteUser agent (parseDid "did:plc:testmute")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isSome captured "should have captured a request via provided client" ]

[<Tests>]
let logoutTests =
    testList
        "Bluesky.logout"
        [ testCase "logout sends POST to deleteSession with refresh JWT"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createMockAgent (fun req ->
                      captured <- Some req
                      emptyResponse HttpStatusCode.OK)

              agent.Session <- Some testSession

              let result =
                  Bluesky.logout agent |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"

              Expect.stringContains
                  (req.RequestUri.ToString ())
                  "com.atproto.server.deleteSession"
                  "correct endpoint"

              // Verify it uses the refresh JWT, NOT the access JWT
              Expect.equal req.Headers.Authorization.Scheme "Bearer" "auth scheme"
              Expect.equal req.Headers.Authorization.Parameter "test-refresh" "uses refresh JWT"

          testCase "logout clears session on success"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ -> emptyResponse HttpStatusCode.OK)

              agent.Session <- Some testSession

              let result =
                  Bluesky.logout agent |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.isNone agent.Session "session should be cleared"

          testCase "logout returns error without session"
          <| fun _ ->
              let agent = createMockAgent (fun _ -> emptyResponse HttpStatusCode.OK)
              // No session set

              let result =
                  Bluesky.logout agent |> Async.AwaitTask |> Async.RunSynchronously

              let err = Expect.wantError result "should fail without session"
              Expect.equal err.StatusCode 401 "status code"
              Expect.equal err.Error (Some "NoSession") "error code"

          testCase "logout returns error on server failure"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ ->
                      jsonResponse
                          HttpStatusCode.Unauthorized
                          {| error = "ExpiredToken"
                             message = "Token expired" |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.logout agent |> Async.AwaitTask |> Async.RunSynchronously

              let err = Expect.wantError result "should fail"
              Expect.equal err.StatusCode 401 "status code"
              Expect.equal err.Error (Some "ExpiredToken") "error code"

          testCase "logout does not clear session on failure"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ ->
                      jsonResponse
                          HttpStatusCode.InternalServerError
                          {| error = "ServerError"
                             message = "internal error" |})

              agent.Session <- Some testSession

              let _result =
                  Bluesky.logout agent |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isSome agent.Session "session should NOT be cleared on failure" ]

[<Tests>]
let upsertProfileTests =
    testList
        "Bluesky.upsertProfile"
        [ testCase "upsertProfile reads current profile and writes updated profile"
          <| fun _ ->
              let mutable capturedPut = None
              let mutable requestCount = 0

              let agent =
                  createMockAgent (fun req ->
                      requestCount <- requestCount + 1
                      let path = req.RequestUri.PathAndQuery

                      if path.Contains ("com.atproto.repo.getRecord") then
                          jsonResponse
                              HttpStatusCode.OK
                              {| uri = "at://did:plc:testuser/app.bsky.actor.profile/self"
                                 cid = "bafyreicurrent"
                                 value =
                                  {| displayName = "OldName"
                                     description = "Old bio" |} |}
                      elif path.Contains ("com.atproto.repo.putRecord") then
                          capturedPut <- Some req

                          jsonResponse
                              HttpStatusCode.OK
                              {| uri = "at://did:plc:testuser/app.bsky.actor.profile/self"
                                 cid = "bafyreinewcid" |}
                      else
                          jsonResponse HttpStatusCode.NotFound {| error = "NotFound" |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.upsertProfile
                      agent
                      (fun current ->
                          let c =
                              current
                              |> Option.defaultValue
                                  { DisplayName = None
                                    Description = None
                                    Avatar = None
                                    Banner = None
                                    CreatedAt = None
                                    JoinedViaStarterPack = None
                                    Labels = None
                                    PinnedPost = None
                                    Pronouns = None
                                    Website = None }

                          { c with DisplayName = Some "NewName" })
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.isSome capturedPut "should have made a putRecord call"
              let body = capturedPut.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "NewName" "updated displayName in body"
              Expect.stringContains body "app.bsky.actor.profile" "$type injected"
              Expect.stringContains body "bafyreicurrent" "swapRecord CID for CAS"

          testCase "upsertProfile handles record not found (new profile)"
          <| fun _ ->
              let mutable capturedPut = None

              let agent =
                  createMockAgent (fun req ->
                      let path = req.RequestUri.PathAndQuery

                      if path.Contains ("com.atproto.repo.getRecord") then
                          jsonResponse
                              HttpStatusCode.BadRequest
                              {| error = "RecordNotFound"
                                 message = "record not found" |}
                      elif path.Contains ("com.atproto.repo.putRecord") then
                          capturedPut <- Some req

                          jsonResponse
                              HttpStatusCode.OK
                              {| uri = "at://did:plc:testuser/app.bsky.actor.profile/self"
                                 cid = "bafyreinewcid" |}
                      else
                          jsonResponse HttpStatusCode.NotFound {| error = "NotFound" |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.upsertProfile
                      agent
                      (fun current ->
                          Expect.isNone current "should receive None for new profile"

                          { DisplayName = Some "Brand New"
                            Description = Some "First bio"
                            Avatar = None
                            Banner = None
                            CreatedAt = None
                            JoinedViaStarterPack = None
                            Labels = None
                            PinnedPost = None
                            Pronouns = None
                            Website = None })
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.isSome capturedPut "should have made a putRecord call"
              let body = capturedPut.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "Brand New" "displayName in body"

          testCase "upsertProfile retries on InvalidSwap error"
          <| fun _ ->
              let mutable putCount = 0

              let agent =
                  createMockAgent (fun req ->
                      let path = req.RequestUri.PathAndQuery

                      if path.Contains ("com.atproto.repo.getRecord") then
                          jsonResponse
                              HttpStatusCode.OK
                              {| uri = "at://did:plc:testuser/app.bsky.actor.profile/self"
                                 cid = $"bafyreicid{putCount}"
                                 value = {| displayName = "Name" |} |}
                      elif path.Contains ("com.atproto.repo.putRecord") then
                          putCount <- putCount + 1

                          if putCount < 2 then
                              jsonResponse
                                  HttpStatusCode.BadRequest
                                  {| error = "InvalidSwap"
                                     message = "Record has been modified" |}
                          else
                              jsonResponse
                                  HttpStatusCode.OK
                                  {| uri = "at://did:plc:testuser/app.bsky.actor.profile/self"
                                     cid = "bafyreinewcid" |}
                      else
                          jsonResponse HttpStatusCode.NotFound {| error = "NotFound" |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.upsertProfile
                      agent
                      (fun current ->
                          let c =
                              current
                              |> Option.defaultValue
                                  { DisplayName = None
                                    Description = None
                                    Avatar = None
                                    Banner = None
                                    CreatedAt = None
                                    JoinedViaStarterPack = None
                                    Labels = None
                                    PinnedPost = None
                                    Pronouns = None
                                    Website = None }

                          { c with DisplayName = Some "Retried" })
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed after retry"
              Expect.equal putCount 2 "should have tried putRecord twice"

          testCase "upsertProfile returns NotLoggedIn error without session"
          <| fun _ ->
              let agent = createMockAgent (fun _ -> jsonResponse HttpStatusCode.OK {| |})

              let result =
                  Bluesky.upsertProfile
                      agent
                      (fun _ ->
                          { DisplayName = None
                            Description = None
                            Avatar = None
                            Banner = None
                            CreatedAt = None
                            JoinedViaStarterPack = None
                            Labels = None
                            PinnedPost = None
                            Pronouns = None
                            Website = None })
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail without session"
              Expect.equal err.StatusCode 401 "status code"
              Expect.equal err.Error (Some "NotLoggedIn") "error code" ]

// ── PostRef SRTP tests ──────────────────────────────────────────────

/// Helper to create a mock TimelinePost with the minimal fields needed
let private testTimelinePost : TimelinePost =
    { Uri = parseAtUri "at://did:plc:other/app.bsky.feed.post/tp123"
      Cid = parseCid "bafyreitpost"
      Author =
        { Did = parseDid "did:plc:other"
          Handle = Handle.parse "other.bsky.social" |> Result.defaultWith failwith
          DisplayName = "Other"
          Avatar = None }
      Text = "Hello from timeline"
      Facets = []
      LikeCount = 0L
      RepostCount = 0L
      ReplyCount = 0L
      QuoteCount = 0L
      IndexedAt = System.DateTimeOffset.UtcNow
      IsLiked = false
      IsReposted = false
      IsBookmarked = false
      Embed = None }

[<Tests>]
let postRefSrtpTests =
    testList
        "PostRef SRTP (like/repost accept TimelinePost)"
        [ testCase "like accepts TimelinePost directly"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.like agent testTimelinePost |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.feed.like" "like collection"
              Expect.stringContains body "at://did:plc:other/app.bsky.feed.post/tp123" "uri from TimelinePost"
              Expect.stringContains body "bafyreitpost" "cid from TimelinePost"

          testCase "repost accepts TimelinePost directly"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.repost agent testTimelinePost |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.feed.repost" "repost collection"
              Expect.stringContains body "at://did:plc:other/app.bsky.feed.post/tp123" "uri from TimelinePost"
              Expect.stringContains body "bafyreitpost" "cid from TimelinePost" ]

// ── Mute typed Did tests ────────────────────────────────────────────

/// Creates a mock agent for void XRPC calls (muteActor, unmuteActor)
let private voidCallAgent (captureRequest : HttpRequestMessage -> unit) =
    let agent =
        createMockAgent (fun req ->
            captureRequest req
            jsonResponse HttpStatusCode.OK {| |})

    agent.Session <- Some testSession
    agent

[<Tests>]
let muteUserTypedDidTests =
    testList
        "Bluesky.muteUser (typed Did)"
        [ testCase "muteUser sends correct actor DID"
          <| fun _ ->
              let mutable captured = None
              let agent = voidCallAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.muteUser agent (parseDid "did:plc:muted")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "did:plc:muted" "actor DID in body"

          testCase "muteUserByHandle resolves handle then mutes"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createMockAgent (fun req ->
                      if req.RequestUri.PathAndQuery.Contains ("resolveHandle") then
                          jsonResponse HttpStatusCode.OK {| did = "did:plc:resolved-mute" |}
                      else
                          captured <- Some req
                          jsonResponse HttpStatusCode.OK {| |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.muteUserByHandle agent "muted-user.bsky.social"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "did:plc:resolved-mute" "resolved DID used as actor" ]

// ── SRTP test fixtures ───────────────────────────────────────────────

let private testProfileSummary : ProfileSummary =
    { Did = parseDid' "did:plc:testuser"
      Handle = parseHandle' "test.bsky.social"
      DisplayName = "Test User"
      Avatar = None }

let private testProfile : Profile =
    { Did = parseDid' "did:plc:testuser"
      Handle = parseHandle' "test.bsky.social"
      DisplayName = "Test User"
      Description = ""
      Avatar = None
      Banner = None
      PostsCount = 0L
      FollowersCount = 0L
      FollowsCount = 0L
      IsFollowing = false
      IsFollowedBy = false
      IsBlocking = false
      IsBlockedBy = false
      IsMuted = false }

// ── ActorDid SRTP tests ─────────────────────────────────────────────

[<Tests>]
let actorDidSrtpTests =
    testList
        "ActorDid SRTP (follow/block/mute accept entities)"
        [ testCase "follow accepts ProfileSummary directly"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.follow agent testProfileSummary
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "did:plc:testuser" "should use DID from ProfileSummary"

          testCase "follow accepts Profile directly"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.follow agent testProfile
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "did:plc:testuser" "should use DID from Profile"

          testCase "follow still accepts Did directly"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.follow agent (parseDid' "did:plc:testuser")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"

          testCase "block accepts ProfileSummary directly"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.block agent testProfileSummary
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "did:plc:testuser" "should use DID from ProfileSummary"

          testCase "muteUser accepts ProfileSummary directly"
          <| fun _ ->
              let mutable captured = None
              let agent = voidCallAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.muteUser agent testProfileSummary
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "did:plc:testuser" "should use DID from ProfileSummary"

          testCase "unmuteUser accepts Profile directly"
          <| fun _ ->
              let mutable captured = None
              let agent = voidCallAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.unmuteUser agent testProfile
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "did:plc:testuser" "should use DID from Profile" ]

// ── ActorWitness SRTP tests ──────────────────────────────────────────

let private followersJson =
    {| followers = ([||] : obj array)
       subject =
        {| did = "did:plc:testuser"
           handle = "test.bsky.social" |}
       cursor = "page2" |}

let private authorFeedJson =
    {| feed = ([||] : obj array)
       cursor = "page2" |}

let private suggestionsJson =
    {| suggestions =
        [| {| did = "did:plc:test"
              handle = "test.bsky.social"
              displayName = "Test User" |} |] |}

[<Tests>]
let actorWitnessSrtpTests =
    testList
        "ActorWitness SRTP (read functions accept entities)"
        [ testCase "getFollowers accepts Handle"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) followersJson
              let handle = parseHandle' "alice.bsky.social"

              let result =
                  Bluesky.getFollowers agent handle None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.stringContains (captured.Value.RequestUri.ToString ()) "alice.bsky.social" "handle in query"

          testCase "getFollowers accepts Did"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) followersJson
              let did = parseDid' "did:plc:alice"

              let result =
                  Bluesky.getFollowers agent did None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.stringContains (captured.Value.RequestUri.ToString ()) "did%3Aplc%3Aalice" "DID in query"

          testCase "getFollowers accepts ProfileSummary"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) followersJson

              let result =
                  Bluesky.getFollowers agent testProfileSummary None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.stringContains (captured.Value.RequestUri.ToString ()) "did%3Aplc%3Atestuser" "DID from ProfileSummary"

          testCase "getFollowers accepts Profile"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) followersJson

              let result =
                  Bluesky.getFollowers agent testProfile None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.stringContains (captured.Value.RequestUri.ToString ()) "did%3Aplc%3Atestuser" "DID from Profile"

          testCase "getFollows accepts ProfileSummary"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) {| follows = ([||] : obj array); subject = {| did = "did:plc:testuser"; handle = "test.bsky.social" |} |}

              let result =
                  Bluesky.getFollows agent testProfileSummary None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.stringContains (captured.Value.RequestUri.ToString ()) "did%3Aplc%3Atestuser" "DID from ProfileSummary"

          testCase "getAuthorFeed accepts ProfileSummary"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) authorFeedJson

              let result =
                  Bluesky.getAuthorFeed agent testProfileSummary None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.stringContains (captured.Value.RequestUri.ToString ()) "did%3Aplc%3Atestuser" "DID from ProfileSummary"

          testCase "getActorLikes accepts Profile"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) authorFeedJson

              let result =
                  Bluesky.getActorLikes agent testProfile None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.stringContains (captured.Value.RequestUri.ToString ()) "did%3Aplc%3Atestuser" "DID from Profile"

          testCase "getSuggestedFollows accepts ProfileSummary"
          <| fun _ ->
              let mutable captured = None
              let agent = queryAgent (fun req -> captured <- Some req) suggestionsJson

              let result =
                  Bluesky.getSuggestedFollows agent testProfileSummary
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.stringContains (captured.Value.RequestUri.ToString ()) "did%3Aplc%3Atestuser" "DID from ProfileSummary"

          testCase "paginateFollowers accepts Did"
          <| fun _ ->
              let mutable captured = System.Collections.Generic.List<HttpRequestMessage> ()

              let agent =
                  createMockAgent (fun req ->
                      captured.Add (req)

                      jsonResponse
                          HttpStatusCode.OK
                          {| followers = ([||] : obj array)
                             subject =
                              {| did = "did:plc:testuser"
                                 handle = "test.bsky.social" |} |})

              agent.Session <- Some testSession
              let did = parseDid' "did:plc:testuser"

              let pages =
                  Bluesky.paginateFollowers agent did None |> collectPages

              Expect.equal pages.Length 1 "single page"
              Expect.stringContains (captured.[0].RequestUri.ToString ()) "did%3Aplc%3Atestuser" "DID in query"

          testCase "getProfiles takes Did list"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| profiles =
                          [| {| did = "did:plc:test"
                                handle = "test.bsky.social"
                                displayName = "Test User" |} |] |}

              let result =
                  Bluesky.getProfiles agent [ parseDid' "did:plc:test"; parseDid' "did:plc:other" ]
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let url = captured.Value.RequestUri.ToString ()
              Expect.stringContains url "did%3Aplc%3Atest" "first DID in query"
              Expect.stringContains url "did%3Aplc%3Aother" "second DID in query" ]

// ── PostUri SRTP tests ──────────────────────────────────────────────

[<Tests>]
let postUriSrtpTests =
    testList
        "PostUri SRTP (read functions accept TimelinePost/PostRef/AtUri)"
        [ testCase "removeBookmark accepts TimelinePost"
          <| fun _ ->
              let agent = voidProcedureAgent (fun _ -> ())

              let result =
                  Bluesky.removeBookmark agent testTimelinePost
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"

          testCase "removeBookmark accepts PostRef"
          <| fun _ ->
              let agent = voidProcedureAgent (fun _ -> ())
              let postRef = { PostRef.Uri = testTimelinePost.Uri; Cid = testTimelinePost.Cid }

              let result =
                  Bluesky.removeBookmark agent postRef
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"

          testCase "muteThread accepts TimelinePost"
          <| fun _ ->
              let agent = voidProcedureAgent (fun _ -> ())

              let result =
                  Bluesky.muteThread agent testTimelinePost
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"

          testCase "unmuteThread accepts TimelinePost"
          <| fun _ ->
              let agent = voidProcedureAgent (fun _ -> ())

              let result =
                  Bluesky.unmuteThread agent testTimelinePost
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"

          testCase "deleteRecord accepts TimelinePost"
          <| fun _ ->
              let agent = deleteRecordAgent (fun _ -> ())

              let result =
                  Bluesky.deleteRecord agent testTimelinePost
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"

          testCase "deleteRecord accepts PostRef"
          <| fun _ ->
              let agent = deleteRecordAgent (fun _ -> ())
              let postRef = { PostRef.Uri = testTimelinePost.Uri; Cid = testTimelinePost.Cid }

              let result =
                  Bluesky.deleteRecord agent postRef
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed" ]

// ── Phase 1: New convenience method tests ────────────────────────────

[<Tests>]
let getBlocksTests =
    testList
        "Bluesky.getBlocks"
        [ testCase "getBlocks calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| blocks = ([||] : obj array)
                         cursor = "c1" |}

              let result =
                  Bluesky.getBlocks agent None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.graph.getBlocks" "correct endpoint"

          testCase "getBlocks returns Page with cursor"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| blocks =
                          [| {| did = "did:plc:blocked1"
                                handle = "blocked.bsky.social" |} |]
                         cursor = "next" |}

              let result =
                  Bluesky.getBlocks agent (Some 10L) None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.equal page.Cursor (Some "next") "cursor"
              Expect.equal page.Items.Length 1 "one blocked account" ]

[<Tests>]
let getMutesTests =
    testList
        "Bluesky.getMutes"
        [ testCase "getMutes calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| mutes = ([||] : obj array)
                         cursor = "c1" |}

              let result =
                  Bluesky.getMutes agent None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.graph.getMutes" "correct endpoint"

          testCase "getMutes returns Page with cursor"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| mutes =
                          [| {| did = "did:plc:muted1"
                                handle = "muted.bsky.social" |} |]
                         cursor = "next" |}

              let result =
                  Bluesky.getMutes agent (Some 10L) None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.equal page.Cursor (Some "next") "cursor"
              Expect.equal page.Items.Length 1 "one muted account" ]

[<Tests>]
let getListTests =
    testList
        "Bluesky.getList"
        [ testCase "getList calls correct XRPC endpoint with list param"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| list =
                          {| uri = "at://did:plc:owner/app.bsky.graph.list/abc"
                             cid = "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454"
                             name = "My List"
                             purpose = "app.bsky.graph.defs#curatelist"
                             indexedAt = "2024-01-15T12:00:00.000Z"
                             creator =
                              {| did = "did:plc:owner"
                                 handle = "owner.bsky.social" |} |}
                         items = ([||] : obj array)
                         cursor = "c1" |}

              let listUri =
                  AtUri.parse "at://did:plc:owner/app.bsky.graph.list/abc"
                  |> Result.defaultWith failwith

              let result =
                  Bluesky.getList agent listUri None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.graph.getList" "correct endpoint"

          testCase "getList returns ListDetail with list metadata and items"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| list =
                          {| uri = "at://did:plc:owner/app.bsky.graph.list/abc"
                             cid = "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454"
                             name = "Cool List"
                             purpose = "app.bsky.graph.defs#curatelist"
                             indexedAt = "2024-01-15T12:00:00.000Z"
                             creator =
                              {| did = "did:plc:owner"
                                 handle = "owner.bsky.social" |} |}
                         items =
                          [| {| subject =
                                  {| did = "did:plc:member1"
                                     handle = "member1.bsky.social" |}
                                uri = "at://did:plc:owner/app.bsky.graph.listitem/1" |} |]
                         cursor = "page2" |}

              let listUri =
                  AtUri.parse "at://did:plc:owner/app.bsky.graph.list/abc"
                  |> Result.defaultWith failwith

              let result =
                  Bluesky.getList agent listUri None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let detail = Expect.wantOk result "should succeed"
              Expect.equal detail.List.Name "Cool List" "list name"
              Expect.equal detail.Items.Length 1 "one member"
              Expect.equal detail.Cursor (Some "page2") "cursor" ]

[<Tests>]
let getListsTests =
    testList
        "Bluesky.getLists"
        [ testCase "getLists calls correct XRPC endpoint with actor param"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| lists = ([||] : obj array) |}

              let handle = parseHandle' "my-handle.bsky.social"

              let result =
                  Bluesky.getLists agent handle None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.graph.getLists" "correct endpoint"
              Expect.stringContains (req.RequestUri.ToString ()) "my-handle.bsky.social" "actor in query"

          testCase "getLists returns Page of ListView"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| lists =
                          [| {| uri = "at://did:plc:owner/app.bsky.graph.list/abc"
                                cid = "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454"
                                name = "My List"
                                purpose = "app.bsky.graph.defs#curatelist"
                                indexedAt = "2024-01-15T12:00:00.000Z"
                                creator =
                                 {| did = "did:plc:owner"
                                    handle = "owner.bsky.social" |} |} |]
                         cursor = "next" |}

              let handle = parseHandle' "owner.bsky.social"

              let result =
                  Bluesky.getLists agent handle None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.equal page.Items.Length 1 "one list"
              Expect.equal page.Items.[0].Name "My List" "list name"
              Expect.equal page.Cursor (Some "next") "cursor" ]

[<Tests>]
let getRelationshipsTests =
    testList
        "Bluesky.getRelationships"
        [ testCase "getRelationships calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| actor = "did:plc:testuser"
                         relationships =
                          [| {| ``$type`` = "app.bsky.graph.defs#relationship"
                                did = "did:plc:other"
                                following = "at://did:plc:testuser/app.bsky.graph.follow/1" |} |] |}

              let did = parseDid' "did:plc:testuser"

              let result =
                  Bluesky.getRelationships agent did None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.graph.getRelationships" "correct endpoint"

          testCase "getRelationships returns Relationship list"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| actor = "did:plc:testuser"
                         relationships =
                          [| {| ``$type`` = "app.bsky.graph.defs#relationship"
                                did = "did:plc:other"
                                following = "at://did:plc:testuser/app.bsky.graph.follow/1" |} |] |}

              let did = parseDid' "did:plc:testuser"

              let result =
                  Bluesky.getRelationships agent did None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let rels = Expect.wantOk result "should succeed"
              Expect.equal rels.Length 1 "one relationship" ]

[<Tests>]
let getKnownFollowersTests =
    testList
        "Bluesky.getKnownFollowers"
        [ testCase "getKnownFollowers calls correct XRPC endpoint with actor param"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| followers = ([||] : obj array)
                         subject =
                          {| did = "did:plc:testuser"
                             handle = "test.bsky.social" |} |}

              let handle = parseHandle' "test.bsky.social"

              let result =
                  Bluesky.getKnownFollowers agent handle None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.graph.getKnownFollowers" "correct endpoint"
              Expect.stringContains (req.RequestUri.ToString ()) "test.bsky.social" "actor in query"

          testCase "getKnownFollowers returns Page of ProfileSummary"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| followers =
                          [| {| did = "did:plc:mutual"
                                handle = "mutual.bsky.social" |} |]
                         subject =
                          {| did = "did:plc:testuser"
                             handle = "test.bsky.social" |}
                         cursor = "c2" |}

              let handle = parseHandle' "test.bsky.social"

              let result =
                  Bluesky.getKnownFollowers agent handle None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.equal page.Items.Length 1 "one known follower"
              Expect.equal page.Cursor (Some "c2") "cursor" ]

[<Tests>]
let getFeedTests =
    testList
        "Bluesky.getFeed"
        [ testCase "getFeed calls correct XRPC endpoint with feed param"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| feed = ([||] : obj array)
                         cursor = "c1" |}

              let feedUri =
                  AtUri.parse "at://did:plc:gen/app.bsky.feed.generator/whats-hot"
                  |> Result.defaultWith failwith

              let result =
                  Bluesky.getFeed agent feedUri None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.feed.getFeed" "correct endpoint"

          testCase "getFeed returns Page of FeedItem"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| feed = ([||] : obj array)
                         cursor = "next" |}

              let feedUri =
                  AtUri.parse "at://did:plc:gen/app.bsky.feed.generator/whats-hot"
                  |> Result.defaultWith failwith

              let result =
                  Bluesky.getFeed agent feedUri None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.equal page.Cursor (Some "next") "cursor" ]

[<Tests>]
let getActorFeedsTests =
    testList
        "Bluesky.getActorFeeds"
        [ testCase "getActorFeeds calls correct XRPC endpoint with actor param"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| feeds = ([||] : obj array) |}

              let handle = parseHandle' "creator.bsky.social"

              let result =
                  Bluesky.getActorFeeds agent handle None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.feed.getActorFeeds" "correct endpoint"
              Expect.stringContains (req.RequestUri.ToString ()) "creator.bsky.social" "actor in query"

          testCase "getActorFeeds returns Page of FeedGenerator"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| feeds =
                          [| {| uri = "at://did:plc:gen/app.bsky.feed.generator/hot"
                                cid = "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454"
                                did = "did:plc:gen"
                                displayName = "What's Hot"
                                indexedAt = "2024-01-15T12:00:00.000Z"
                                creator =
                                 {| did = "did:plc:gen"
                                    handle = "creator.bsky.social" |} |} |]
                         cursor = "next" |}

              let handle = parseHandle' "creator.bsky.social"

              let result =
                  Bluesky.getActorFeeds agent handle None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.equal page.Items.Length 1 "one feed generator"
              Expect.equal page.Items.[0].DisplayName "What's Hot" "feed name"
              Expect.equal page.Cursor (Some "next") "cursor" ]

[<Tests>]
let getListFeedTests =
    testList
        "Bluesky.getListFeed"
        [ testCase "getListFeed calls correct XRPC endpoint with list param"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| feed = ([||] : obj array) |}

              let listUri =
                  AtUri.parse "at://did:plc:owner/app.bsky.graph.list/abc"
                  |> Result.defaultWith failwith

              let result =
                  Bluesky.getListFeed agent listUri None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.feed.getListFeed" "correct endpoint"

          testCase "getListFeed returns Page of FeedItem"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| feed = ([||] : obj array)
                         cursor = "next" |}

              let listUri =
                  AtUri.parse "at://did:plc:owner/app.bsky.graph.list/abc"
                  |> Result.defaultWith failwith

              let result =
                  Bluesky.getListFeed agent listUri None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.equal page.Cursor (Some "next") "cursor" ]

[<Tests>]
let getFeedGeneratorTests =
    testList
        "Bluesky.getFeedGenerator"
        [ testCase "getFeedGenerator calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| view =
                          {| uri = "at://did:plc:gen/app.bsky.feed.generator/hot"
                             cid = "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454"
                             did = "did:plc:gen"
                             displayName = "What's Hot"
                             indexedAt = "2024-01-15T12:00:00.000Z"
                             creator =
                              {| did = "did:plc:gen"
                                 handle = "creator.bsky.social" |} |}
                         isOnline = true
                         isValid = true |}

              let feedUri =
                  AtUri.parse "at://did:plc:gen/app.bsky.feed.generator/hot"
                  |> Result.defaultWith failwith

              let result =
                  Bluesky.getFeedGenerator agent feedUri
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.feed.getFeedGenerator" "correct endpoint"

          testCase "getFeedGenerator returns FeedGenerator with online/valid status"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| view =
                          {| uri = "at://did:plc:gen/app.bsky.feed.generator/hot"
                             cid = "bafyreie5cvv4h45feadgeuwhbcutmh6t7ceseocckahdoe6uat64zmz454"
                             did = "did:plc:gen"
                             displayName = "What's Hot"
                             indexedAt = "2024-01-15T12:00:00.000Z"
                             creator =
                              {| did = "did:plc:gen"
                                 handle = "creator.bsky.social" |} |}
                         isOnline = true
                         isValid = false |}

              let feedUri =
                  AtUri.parse "at://did:plc:gen/app.bsky.feed.generator/hot"
                  |> Result.defaultWith failwith

              let result =
                  Bluesky.getFeedGenerator agent feedUri
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let fg = Expect.wantOk result "should succeed"
              Expect.equal fg.DisplayName "What's Hot" "display name"
              Expect.isTrue fg.IsOnline "is online"
              Expect.isFalse fg.IsValid "is not valid" ]

// ── Paginator tests for new methods ──────────────────────────────────

[<Tests>]
let paginateBlocksTests =
    testList
        "Bluesky.paginateBlocks"
        [ testCase "returns pages and stops when no cursor"
          <| fun _ ->
              let page1 =
                  {| blocks = ([||] : obj array)
                     cursor = "page2" |}

              let page2 = {| blocks = ([||] : obj array) |}
              let agent = paginatingAgent page1 page2
              let pages = Bluesky.paginateBlocks agent (Some 10L) |> collectPages
              Expect.equal pages.Length 2 "exactly 2 pages"
              Expect.isOk pages.[0] "first page is Ok"
              Expect.isOk pages.[1] "second page is Ok"

          testCase "single page when server returns no cursor"
          <| fun _ ->
              let agent = queryAgent (fun _ -> ()) {| blocks = ([||] : obj array) |}
              let pages = Bluesky.paginateBlocks agent None |> collectPages
              Expect.equal pages.Length 1 "exactly 1 page" ]

[<Tests>]
let paginateMutesTests =
    testList
        "Bluesky.paginateMutes"
        [ testCase "returns pages and stops when no cursor"
          <| fun _ ->
              let page1 =
                  {| mutes = ([||] : obj array)
                     cursor = "page2" |}

              let page2 = {| mutes = ([||] : obj array) |}
              let agent = paginatingAgent page1 page2
              let pages = Bluesky.paginateMutes agent (Some 10L) |> collectPages
              Expect.equal pages.Length 2 "exactly 2 pages"
              Expect.isOk pages.[0] "first page is Ok"
              Expect.isOk pages.[1] "second page is Ok"

          testCase "single page when server returns no cursor"
          <| fun _ ->
              let agent = queryAgent (fun _ -> ()) {| mutes = ([||] : obj array) |}
              let pages = Bluesky.paginateMutes agent None |> collectPages
              Expect.equal pages.Length 1 "exactly 1 page" ]

[<Tests>]
let paginateFeedTests =
    testList
        "Bluesky.paginateFeed"
        [ testCase "returns pages and stops when no cursor"
          <| fun _ ->
              let page1 =
                  {| feed = ([||] : obj array)
                     cursor = "page2" |}

              let page2 = {| feed = ([||] : obj array) |}
              let agent = paginatingAgent page1 page2

              let feedUri =
                  AtUri.parse "at://did:plc:gen/app.bsky.feed.generator/hot"
                  |> Result.defaultWith failwith

              let pages = Bluesky.paginateFeed agent feedUri (Some 10L) |> collectPages
              Expect.equal pages.Length 2 "exactly 2 pages"
              Expect.isOk pages.[0] "first page is Ok"
              Expect.isOk pages.[1] "second page is Ok"

          testCase "single page when server returns no cursor"
          <| fun _ ->
              let agent = queryAgent (fun _ -> ()) {| feed = ([||] : obj array) |}

              let feedUri =
                  AtUri.parse "at://did:plc:gen/app.bsky.feed.generator/hot"
                  |> Result.defaultWith failwith

              let pages = Bluesky.paginateFeed agent feedUri None |> collectPages
              Expect.equal pages.Length 1 "exactly 1 page" ]

[<Tests>]
let paginateListFeedTests =
    testList
        "Bluesky.paginateListFeed"
        [ testCase "returns pages and stops when no cursor"
          <| fun _ ->
              let page1 =
                  {| feed = ([||] : obj array)
                     cursor = "page2" |}

              let page2 = {| feed = ([||] : obj array) |}
              let agent = paginatingAgent page1 page2

              let listUri =
                  AtUri.parse "at://did:plc:owner/app.bsky.graph.list/abc"
                  |> Result.defaultWith failwith

              let pages = Bluesky.paginateListFeed agent listUri (Some 10L) |> collectPages
              Expect.equal pages.Length 2 "exactly 2 pages"
              Expect.isOk pages.[0] "first page is Ok"
              Expect.isOk pages.[1] "second page is Ok"

          testCase "single page when server returns no cursor"
          <| fun _ ->
              let agent = queryAgent (fun _ -> ()) {| feed = ([||] : obj array) |}

              let listUri =
                  AtUri.parse "at://did:plc:owner/app.bsky.graph.list/abc"
                  |> Result.defaultWith failwith

              let pages = Bluesky.paginateListFeed agent listUri None |> collectPages
              Expect.equal pages.Length 1 "exactly 1 page" ]

// ── Preference mutation tests ────────────────────────────────────────

/// Create a mock agent that returns getPreferences on GET and captures putPreferences on POST
let private preferencesAgent
    (prefs : obj list)
    (captureBody : string -> unit)
    =
    let agent =
        createMockAgent (fun req ->
            if req.Method = HttpMethod.Get then
                jsonResponse HttpStatusCode.OK {| preferences = prefs |}
            else
                let body = req.Content.ReadAsStringAsync().Result
                captureBody body
                emptyResponse HttpStatusCode.OK)

    agent.Session <- Some testSession
    agent

[<Tests>]
let upsertPreferencesTests =
    testList
        "Bluesky.upsertPreferences"
        [ testCase "upsertPreferences reads then writes preferences"
          <| fun _ ->
              let mutable capturedBody = ""

              let agent =
                  preferencesAgent
                      [ {| ``$type`` = "app.bsky.actor.defs#adultContentPref"
                           enabled = false |} ]
                      (fun body -> capturedBody <- body)

              let result =
                  Bluesky.upsertPreferences agent (fun prefs -> prefs)
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.isNotEmpty capturedBody "putPreferences was called" ]

[<Tests>]
let setAdultContentEnabledTests =
    testList
        "Bluesky.setAdultContentEnabled"
        [ testCase "setAdultContentEnabled updates existing pref"
          <| fun _ ->
              let mutable capturedBody = ""

              let agent =
                  preferencesAgent
                      [ {| ``$type`` = "app.bsky.actor.defs#adultContentPref"
                           enabled = false |} ]
                      (fun body -> capturedBody <- body)

              let result =
                  Bluesky.setAdultContentEnabled agent true
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.stringContains capturedBody "true" "body contains enabled=true"
              Expect.stringContains capturedBody "adultContentPref" "body contains the pref type"

          testCase "setAdultContentEnabled inserts when not present"
          <| fun _ ->
              let mutable capturedBody = ""
              let agent = preferencesAgent [] (fun body -> capturedBody <- body)

              let result =
                  Bluesky.setAdultContentEnabled agent true
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.stringContains capturedBody "adultContentPref" "pref was inserted" ]

[<Tests>]
let addMutedWordTests =
    testList
        "Bluesky.addMutedWord"
        [ testCase "addMutedWord adds word to existing muted words pref"
          <| fun _ ->
              let mutable capturedBody = ""

              let agent =
                  preferencesAgent
                      [ {| ``$type`` = "app.bsky.actor.defs#mutedWordsPref"
                           items =
                            [| {| value = "existing"
                                  targets = [| "content" |] |} |] |} ]
                      (fun body -> capturedBody <- body)

              let word : AppBskyActor.Defs.MutedWord =
                  { Value = "newword"
                    Targets = [ AppBskyActor.Defs.MutedWordTarget.Content ]
                    ActorTarget = None
                    ExpiresAt = None
                    Id = None }

              let result =
                  Bluesky.addMutedWord agent word
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.stringContains capturedBody "newword" "new word in body"
              Expect.stringContains capturedBody "existing" "existing word preserved" ]

[<Tests>]
let removeMutedWordTests =
    testList
        "Bluesky.removeMutedWord"
        [ testCase "removeMutedWord removes word by value"
          <| fun _ ->
              let mutable capturedBody = ""

              let agent =
                  preferencesAgent
                      [ {| ``$type`` = "app.bsky.actor.defs#mutedWordsPref"
                           items =
                            [| {| value = "remove-me"
                                  targets = [| "content" |] |}
                               {| value = "keep-me"
                                  targets = [| "content" |] |} |] |} ]
                      (fun body -> capturedBody <- body)

              let result =
                  Bluesky.removeMutedWord agent "remove-me"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.isFalse (capturedBody.Contains "remove-me") "removed word not in body"
              Expect.stringContains capturedBody "keep-me" "kept word in body" ]

[<Tests>]
let setContentLabelPrefTests =
    testList
        "Bluesky.setContentLabelPref"
        [ testCase "setContentLabelPref inserts new label pref"
          <| fun _ ->
              let mutable capturedBody = ""
              let agent = preferencesAgent [] (fun body -> capturedBody <- body)

              let result =
                  Bluesky.setContentLabelPref agent "nsfw" AppBskyActor.Defs.ContentLabelPrefVisibility.Hide None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.stringContains capturedBody "nsfw" "label in body"
              Expect.stringContains capturedBody "hide" "visibility in body" ]

[<Tests>]
let setThreadViewPrefTests =
    testList
        "Bluesky.setThreadViewPref"
        [ testCase "setThreadViewPref sets sort order"
          <| fun _ ->
              let mutable capturedBody = ""
              let agent = preferencesAgent [] (fun body -> capturedBody <- body)

              let result =
                  Bluesky.setThreadViewPref agent AppBskyActor.Defs.ThreadViewPrefSort.Oldest
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.stringContains capturedBody "threadViewPref" "pref type in body"
              Expect.stringContains capturedBody "oldest" "sort order in body" ]

[<Tests>]
let addHiddenPostTests =
    testList
        "Bluesky.addHiddenPost"
        [ testCase "addHiddenPost adds URI to hidden posts"
          <| fun _ ->
              let mutable capturedBody = ""
              let agent = preferencesAgent [] (fun body -> capturedBody <- body)

              let uri =
                  AtUri.parse "at://did:plc:test/app.bsky.feed.post/abc"
                  |> Result.defaultWith failwith

              let result =
                  Bluesky.addHiddenPost agent uri
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.stringContains capturedBody "hiddenPostsPref" "pref type in body"
              Expect.stringContains capturedBody "app.bsky.feed.post/abc" "post URI in body" ]

[<Tests>]
let removeHiddenPostTests =
    testList
        "Bluesky.removeHiddenPost"
        [ testCase "removeHiddenPost removes URI from hidden posts"
          <| fun _ ->
              let mutable capturedBody = ""

              let agent =
                  preferencesAgent
                      [ {| ``$type`` = "app.bsky.actor.defs#hiddenPostsPref"
                           items =
                            [| "at://did:plc:test/app.bsky.feed.post/remove"
                               "at://did:plc:test/app.bsky.feed.post/keep" |] |} ]
                      (fun body -> capturedBody <- body)

              let uri =
                  AtUri.parse "at://did:plc:test/app.bsky.feed.post/remove"
                  |> Result.defaultWith failwith

              let result =
                  Bluesky.removeHiddenPost agent uri
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.isFalse (capturedBody.Contains "post/remove") "removed URI not in body"
              Expect.stringContains capturedBody "post/keep" "kept URI in body" ]

// ── List & starter pack management tests ─────────────────────────────

[<Tests>]
let createListTests =
    testList
        "Bluesky.createList"
        [ testCase "createList calls createRecord with correct collection"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let result =
                  Bluesky.createList agent "My List" AppBskyGraph.Defs.ListPurpose.Curatelist (Some "A cool list")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"
              let body = req.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.graph.list" "correct collection type"
              Expect.stringContains body "My List" "list name in body"
              Expect.stringContains body "curatelist" "purpose in body"

          testCase "createList returns ListRef"
          <| fun _ ->
              let agent = createRecordAgent (fun _ -> ())

              let result =
                  Bluesky.createList agent "Test" AppBskyGraph.Defs.ListPurpose.Modlist None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let listRef = Expect.wantOk result "should succeed"
              Expect.isTrue (AtUri.value listRef.Uri |> fun s -> s.Contains "app.bsky") "has valid URI" ]

[<Tests>]
let deleteListTests =
    testList
        "Bluesky.deleteList"
        [ testCase "deleteList calls delete endpoint"
          <| fun _ ->
              let mutable captured = None
              let agent = deleteRecordAgent (fun req -> captured <- Some req)

              let uri =
                  AtUri.parse "at://did:plc:test/app.bsky.graph.list/abc"
                  |> Result.defaultWith failwith

              let result =
                  Bluesky.deleteList agent uri
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method (deleteRecord is a procedure)" ]

[<Tests>]
let addListItemTests =
    testList
        "Bluesky.addListItem"
        [ testCase "addListItem creates listitem record"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let listUri =
                  AtUri.parse "at://did:plc:owner/app.bsky.graph.list/abc"
                  |> Result.defaultWith failwith

              let subject = parseDid' "did:plc:member1"

              let result =
                  Bluesky.addListItem agent listUri subject
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.graph.listitem" "correct collection type"
              Expect.stringContains body "did:plc:member1" "subject DID in body"

          testCase "addListItem returns ListItemRef"
          <| fun _ ->
              let agent = createRecordAgent (fun _ -> ())

              let listUri =
                  AtUri.parse "at://did:plc:owner/app.bsky.graph.list/abc"
                  |> Result.defaultWith failwith

              let subject = parseDid' "did:plc:member1"

              let result =
                  Bluesky.addListItem agent listUri subject
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let itemRef = Expect.wantOk result "should succeed"
              Expect.isTrue (AtUri.value itemRef.Uri |> fun s -> s.Length > 0) "has URI" ]

[<Tests>]
let removeListItemTests =
    testList
        "Bluesky.removeListItem"
        [ testCase "removeListItem deletes the list item record"
          <| fun _ ->
              let mutable captured = None
              let agent = deleteRecordAgent (fun req -> captured <- Some req)

              let itemUri =
                  AtUri.parse "at://did:plc:owner/app.bsky.graph.listitem/xyz"
                  |> Result.defaultWith failwith

              let result =
                  Bluesky.removeListItem agent itemUri
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed" ]

[<Tests>]
let createStarterPackTests =
    testList
        "Bluesky.createStarterPack"
        [ testCase "createStarterPack creates record with name and list"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let listUri =
                  AtUri.parse "at://did:plc:owner/app.bsky.graph.list/members"
                  |> Result.defaultWith failwith

              let result =
                  Bluesky.createStarterPack agent "My Starter Pack" listUri (Some "Welcome!") None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.graph.starterpack" "correct collection type"
              Expect.stringContains body "My Starter Pack" "name in body"

          testCase "createStarterPack returns StarterPackRef"
          <| fun _ ->
              let agent = createRecordAgent (fun _ -> ())

              let listUri =
                  AtUri.parse "at://did:plc:owner/app.bsky.graph.list/members"
                  |> Result.defaultWith failwith

              let result =
                  Bluesky.createStarterPack agent "Test" listUri None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let spRef = Expect.wantOk result "should succeed"
              Expect.isTrue (AtUri.value spRef.Uri |> fun s -> s.Length > 0) "has URI" ]

[<Tests>]
let deleteStarterPackTests =
    testList
        "Bluesky.deleteStarterPack"
        [ testCase "deleteStarterPack deletes the record"
          <| fun _ ->
              let agent = deleteRecordAgent (fun _ -> ())

              let uri =
                  AtUri.parse "at://did:plc:owner/app.bsky.graph.starterpack/abc"
                  |> Result.defaultWith failwith

              let result =
                  Bluesky.deleteStarterPack agent uri
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed" ]

// ── Video upload tests ───────────────────────────────────────────────

[<Tests>]
let uploadVideoTests =
    testList
        "Bluesky.uploadVideo"
        [ testCase "uploadVideo sends binary data with correct content type"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createMockAgent (fun req ->
                      captured <- Some req

                      jsonResponse
                          HttpStatusCode.OK
                          {| jobStatus =
                              {| jobId = "job123"
                                 did = "did:plc:testuser"
                                 state = "JOB_STATE_COMPLETED" |} |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.uploadVideo agent [| 0x00uy; 0x01uy |] VideoMime.Mp4
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.video.uploadVideo" "correct endpoint"
              Expect.equal (req.Content.Headers.ContentType.MediaType) "video/mp4" "correct content type"

          testCase "uploadVideo returns job status"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ ->
                      jsonResponse
                          HttpStatusCode.OK
                          {| jobStatus =
                              {| jobId = "job456"
                                 did = "did:plc:testuser"
                                 state = "JOB_STATE_COMPLETED" |} |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.uploadVideo agent [| 0x00uy |] VideoMime.Webm
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let status = Expect.wantOk result "should succeed"
              Expect.equal status.JobId "job456" "correct job ID" ]

[<Tests>]
let getVideoJobStatusTests =
    testList
        "Bluesky.getVideoJobStatus"
        [ testCase "getVideoJobStatus queries correct endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  queryAgent
                      (fun req -> captured <- Some req)
                      {| jobStatus =
                          {| jobId = "job123"
                             did = "did:plc:testuser"
                             state = "JOB_STATE_COMPLETED" |} |}

              let result =
                  Bluesky.getVideoJobStatus agent "job123"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.stringContains (req.RequestUri.ToString ()) "app.bsky.video.getJobStatus" "correct endpoint"
              Expect.stringContains (req.RequestUri.ToString ()) "job123" "job ID in query"

          testCase "getVideoJobStatus returns failed status"
          <| fun _ ->
              let agent =
                  queryAgent
                      (fun _ -> ())
                      {| jobStatus =
                          {| jobId = "job789"
                             did = "did:plc:testuser"
                             state = "JOB_STATE_FAILED"
                             error = "VideoTooLong"
                             message = "Video exceeds maximum duration" |} |}

              let result =
                  Bluesky.getVideoJobStatus agent "job789"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let status = Expect.wantOk result "should succeed"
              Expect.equal status.State AppBskyVideo.Defs.JobStatusState.JOBSTATEFAILED "failed state"
              Expect.equal status.Error (Some "VideoTooLong") "error code" ]

// ── Sync / binary endpoint helpers ────────────────────────────────────

/// Build a CIDv1 dag-cbor SHA-256 from raw data (same helper as CarParserTests).
let private computeCidBytes (data : byte[]) : byte[] =
    use sha = System.Security.Cryptography.SHA256.Create ()
    let hash = sha.ComputeHash data

    Array.concat
        [| Varint.encode 1UL
           Varint.encode 0x71UL
           Varint.encode 0x12UL
           Varint.encode 0x20UL
           hash |]

/// Encode a CID as a DAG-CBOR tag-42 link (0x00 prefix + CID bytes).
let private writeCborCidLink (writer : System.Formats.Cbor.CborWriter) (cidBytes : byte[]) =
    writer.WriteTag (LanguagePrimitives.EnumOfValue 42UL)
    writer.WriteByteString (Array.concat [| [| 0uy |]; cidBytes |])

/// Build a minimal valid CAR v1 file with given blocks.
let private buildCar (roots : byte[] list) (blocks : (byte[] * byte[]) list) : byte[] =
    let headerWriter = System.Formats.Cbor.CborWriter (System.Formats.Cbor.CborConformanceMode.Lax)
    headerWriter.WriteStartMap (System.Nullable 2)
    headerWriter.WriteTextString "version"
    headerWriter.WriteInt32 1
    headerWriter.WriteTextString "roots"
    headerWriter.WriteStartArray (System.Nullable roots.Length)

    for root in roots do
        writeCborCidLink headerWriter root

    headerWriter.WriteEndArray ()
    headerWriter.WriteEndMap ()
    let headerCbor = headerWriter.Encode ()
    let headerLenVarint = Varint.encode (uint64 headerCbor.Length)

    let blockParts =
        blocks
        |> List.map (fun (cidBytes, data) ->
            let blockContent = Array.concat [| cidBytes; data |]
            let lenVarint = Varint.encode (uint64 blockContent.Length)
            Array.concat [| lenVarint; blockContent |])

    Array.concat (headerLenVarint :: headerCbor :: blockParts)

let private binaryAgent (captureRequest : HttpRequestMessage -> unit) (bytes : byte[]) =
    let agent =
        createMockAgent (fun req ->
            captureRequest req
            binaryResponse HttpStatusCode.OK bytes)

    agent.Session <- Some testSession
    agent

[<Tests>]
let getBlobTests =
    testList
        "Bluesky.getBlob"
        [ testCase "getBlob returns raw bytes from sync endpoint"
          <| fun _ ->
              let mutable captured = None
              let blobBytes = [| 0xFFuy; 0xD8uy; 0xFFuy; 0xE0uy |]
              let agent = binaryAgent (fun req -> captured <- Some req) blobBytes

              let result =
                  Bluesky.getBlob agent (parseDid "did:plc:testuser") (parseCid "bafyreiabc123")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let bytes = Expect.wantOk result "should succeed"
              Expect.equal bytes blobBytes "returns raw blob bytes"

          testCase "getBlob sends correct query parameters"
          <| fun _ ->
              let mutable captured = None
              let agent = binaryAgent (fun req -> captured <- Some req) [| 0x00uy |]

              let _ =
                  Bluesky.getBlob agent (parseDid "did:plc:testuser") (parseCid "bafyreiabc123")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = captured.Value
              let uri = req.RequestUri.ToString ()
              Expect.stringContains uri "com.atproto.sync.getBlob" "correct endpoint"
              Expect.stringContains uri "did%3Aplc%3Atestuser" "DID in query"
              Expect.stringContains uri "bafyreiabc123" "CID in query"

          testCase "getBlob returns error on failure"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ ->
                      jsonResponse HttpStatusCode.NotFound {| error = "BlobNotFound"; message = "not found" |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.getBlob agent (parseDid "did:plc:testuser") (parseCid "bafyreiabc123")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail"
              Expect.equal err.StatusCode 404 "404 status"
              Expect.equal err.Error (Some "BlobNotFound") "error code" ]

[<Tests>]
let getRepoTests =
    testList
        "Bluesky.getRepo"
        [ testCase "getRepo parses CAR response"
          <| fun _ ->
              let mutable captured = None
              let blockData = [| 0x01uy; 0x02uy; 0x03uy |]
              let cidBytes = computeCidBytes blockData
              let carBytes = buildCar [ cidBytes ] [ (cidBytes, blockData) ]
              let agent = binaryAgent (fun req -> captured <- Some req) carBytes

              let result =
                  Bluesky.getRepo agent (parseDid "did:plc:testuser")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let carFile = Expect.wantOk result "should succeed"
              Expect.equal carFile.Roots.Length 1 "one root"
              Expect.equal carFile.Blocks.Count 1 "one block"

          testCase "getRepo sends correct query parameters"
          <| fun _ ->
              let mutable captured = None
              let blockData = [| 0x42uy |]
              let cidBytes = computeCidBytes blockData
              let carBytes = buildCar [ cidBytes ] []
              let agent = binaryAgent (fun req -> captured <- Some req) carBytes

              let _ =
                  Bluesky.getRepo agent (parseDid "did:plc:testuser")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = captured.Value
              let uri = req.RequestUri.ToString ()
              Expect.stringContains uri "com.atproto.sync.getRepo" "correct endpoint"
              Expect.stringContains uri "did%3Aplc%3Atestuser" "DID in query"

          testCase "getRepo returns error on server failure"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ ->
                      jsonResponse HttpStatusCode.NotFound {| error = "RepoNotFound"; message = "not found" |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.getRepo agent (parseDid "did:plc:testuser")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail"
              Expect.equal err.StatusCode 404 "404 status"
              Expect.equal err.Error (Some "RepoNotFound") "error code"

          testCase "getRepo returns error on invalid CAR data"
          <| fun _ ->
              let agent = binaryAgent (fun _ -> ()) [| 0x01uy |]

              let result =
                  Bluesky.getRepo agent (parseDid "did:plc:testuser")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail on invalid CAR"
              Expect.equal err.Error (Some "CarParseError") "CAR parse error" ]

[<Tests>]
let getRecordSyncTests =
    testList
        "Bluesky.getRecord (sync)"
        [ testCase "getRecord parses CAR response"
          <| fun _ ->
              let mutable captured = None
              let blockData = [| 0xAAuy; 0xBBuy |]
              let cidBytes = computeCidBytes blockData
              let carBytes = buildCar [ cidBytes ] [ (cidBytes, blockData) ]
              let agent = binaryAgent (fun req -> captured <- Some req) carBytes

              let nsid = Nsid.parse "app.bsky.feed.post" |> Result.defaultWith failwith
              let rkey = RecordKey.parse "abc123" |> Result.defaultWith failwith

              let result =
                  Bluesky.getRecord agent (parseDid "did:plc:testuser") nsid rkey
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let carFile = Expect.wantOk result "should succeed"
              Expect.equal carFile.Roots.Length 1 "one root"
              Expect.equal carFile.Blocks.Count 1 "one block"

          testCase "getRecord sends correct query parameters"
          <| fun _ ->
              let mutable captured = None
              let blockData = [| 0x42uy |]
              let cidBytes = computeCidBytes blockData
              let carBytes = buildCar [ cidBytes ] []
              let agent = binaryAgent (fun req -> captured <- Some req) carBytes

              let nsid = Nsid.parse "app.bsky.feed.post" |> Result.defaultWith failwith
              let rkey = RecordKey.parse "abc123" |> Result.defaultWith failwith

              let _ =
                  Bluesky.getRecord agent (parseDid "did:plc:testuser") nsid rkey
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = captured.Value
              let uri = req.RequestUri.ToString ()
              Expect.stringContains uri "com.atproto.sync.getRecord" "correct endpoint"
              Expect.stringContains uri "did%3Aplc%3Atestuser" "DID in query"
              Expect.stringContains uri "app.bsky.feed.post" "collection in query"
              Expect.stringContains uri "abc123" "rkey in query"

          testCase "getRecord returns error on server failure"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ ->
                      jsonResponse HttpStatusCode.NotFound {| error = "RecordNotFound"; message = "not found" |})

              agent.Session <- Some testSession

              let nsid = Nsid.parse "app.bsky.feed.post" |> Result.defaultWith failwith
              let rkey = RecordKey.parse "abc123" |> Result.defaultWith failwith

              let result =
                  Bluesky.getRecord agent (parseDid "did:plc:testuser") nsid rkey
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail"
              Expect.equal err.StatusCode 404 "404 status"
              Expect.equal err.Error (Some "RecordNotFound") "error code"

          testCase "getRecord returns error on invalid CAR data"
          <| fun _ ->
              let agent = binaryAgent (fun _ -> ()) [| 0x01uy |]

              let nsid = Nsid.parse "app.bsky.feed.post" |> Result.defaultWith failwith
              let rkey = RecordKey.parse "abc123" |> Result.defaultWith failwith

              let result =
                  Bluesky.getRecord agent (parseDid "did:plc:testuser") nsid rkey
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail on invalid CAR"
              Expect.equal err.Error (Some "CarParseError") "CAR parse error" ]

// ── Account management tests ──────────────────────────────────────

[<Tests>]
let createAccountTests =
    testList
        "Bluesky.createAccount"
        [ testCase "createAccount sends handle, email, password, inviteCode to endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createMockAgent (fun req ->
                      captured <- Some req

                      jsonResponse
                          HttpStatusCode.OK
                          {| accessJwt = "jwt"
                             refreshJwt = "refresh"
                             did = "did:plc:new"
                             handle = "new.bsky.social" |})

              let handle = Handle.parse "new.bsky.social" |> Result.defaultWith failwith

              let result =
                  ComAtprotoServer.CreateAccount.call
                      agent
                      { Handle = handle
                        Email = Some "user@test.com"
                        Password = Some "pass123"
                        InviteCode = Some "invite-abc"
                        Did = None
                        PlcOp = None
                        RecoveryKey = None
                        VerificationCode = None
                        VerificationPhone = None }
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "new.bsky.social" "handle in body"
              Expect.stringContains body "user@test.com" "email in body"
              Expect.stringContains body "pass123" "password in body"
              Expect.stringContains body "invite-abc" "invite code in body"

          testCase "createAccount returns error when server rejects"
          <| fun _ ->
              let errorAgent =
                  createMockAgent (fun _ ->
                      jsonResponse
                          HttpStatusCode.BadRequest
                          {| error = "HandleNotAvailable"
                             message = "Handle already taken" |})

              let handle = Handle.parse "taken.bsky.social" |> Result.defaultWith failwith

              let result =
                  ComAtprotoServer.CreateAccount.call
                      errorAgent
                      { Handle = handle
                        Email = Some "test@example.com"
                        Password = Some "password123"
                        InviteCode = None
                        Did = None
                        PlcOp = None
                        RecoveryKey = None
                        VerificationCode = None
                        VerificationPhone = None }
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail with HandleNotAvailable"
              Expect.equal err.Error (Some "HandleNotAvailable") "error code" ]

[<Tests>]
let requestAccountDeleteTests =
    testList
        "Bluesky.requestAccountDelete"
        [ testCase "requestAccountDelete sends POST to correct endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createMockAgent (fun req ->
                      captured <- Some req
                      emptyResponse HttpStatusCode.OK)

              agent.Session <- Some testSession

              let result =
                  Bluesky.requestAccountDelete agent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.stringContains (req.RequestUri.ToString ()) "com.atproto.server.requestAccountDelete" "correct endpoint"
              Expect.equal req.Method HttpMethod.Post "should be POST"

          testCase "requestAccountDelete includes auth header"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createMockAgent (fun req ->
                      captured <- Some req
                      emptyResponse HttpStatusCode.OK)

              agent.Session <- Some testSession

              let _ =
                  Bluesky.requestAccountDelete agent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = captured.Value
              Expect.isNotNull req.Headers.Authorization "should have auth header"
              Expect.stringContains (req.Headers.Authorization.ToString ()) "test-jwt" "should contain access JWT"

          testCase "requestAccountDelete returns error without session"
          <| fun _ ->
              let agent = createMockAgent (fun _ -> emptyResponse HttpStatusCode.OK)

              let result =
                  Bluesky.requestAccountDelete agent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail without session"
              Expect.equal err.StatusCode 401 "status code"
              Expect.equal err.Error (Some "NotLoggedIn") "error code"

          testCase "requestAccountDelete returns server error"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ ->
                      jsonResponse
                          HttpStatusCode.InternalServerError
                          {| error = "InternalServerError"
                             message = "Something went wrong" |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.requestAccountDelete agent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail"
              Expect.equal err.StatusCode 500 "status code"
              Expect.equal err.Error (Some "InternalServerError") "error code" ]

[<Tests>]
let deleteAccountTests =
    testList
        "Bluesky.deleteAccount"
        [ testCase "deleteAccount sends DID, password, and token"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createMockAgent (fun req ->
                      captured <- Some req
                      jsonResponse HttpStatusCode.OK {| |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.deleteAccount agent "mypassword" "delete-token-123"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "did:plc:testuser" "DID in body"
              Expect.stringContains body "mypassword" "password in body"
              Expect.stringContains body "delete-token-123" "token in body"

          testCase "deleteAccount clears session on success"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ -> jsonResponse HttpStatusCode.OK {| |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.deleteAccount agent "pass" "token"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              Expect.isNone agent.Session "session should be cleared after deletion"

          testCase "deleteAccount returns error without session"
          <| fun _ ->
              let agent = createMockAgent (fun _ -> jsonResponse HttpStatusCode.OK {| |})

              let result =
                  Bluesky.deleteAccount agent "pass" "token"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail without session"
              Expect.equal err.StatusCode 401 "status code"

          testCase "deleteAccount returns server error on invalid token"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ ->
                      jsonResponse
                          HttpStatusCode.BadRequest
                          {| error = "InvalidToken"
                             message = "Token is invalid" |})

              agent.Session <- Some testSession

              let result =
                  Bluesky.deleteAccount agent "pass" "bad-token"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail"
              Expect.equal err.Error (Some "InvalidToken") "error code" ]

// ── Via attribution tests ──────────────────────────────────────────

[<Tests>]
let viaAttributionTests =
    testList
        "Bluesky via attribution"
        [ testCase "likeVia includes via field in record"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let viaPost =
                  { PostRef.Uri = parseAtUri "at://did:plc:feed/app.bsky.feed.post/feed123"
                    Cid = parseCid "bafyreifeed" }

              let result =
                  Bluesky.likeVia agent testPostRef viaPost
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.feed.like" "like collection"
              Expect.stringContains body "\"via\"" "should contain via field"
              Expect.stringContains body "at://did:plc:feed/app.bsky.feed.post/feed123" "via URI"
              Expect.stringContains body "bafyreifeed" "via CID"

          testCase "likeVia returns LikeRef"
          <| fun _ ->
              let agent = createRecordAgent (fun _ -> ())

              let viaPost =
                  { PostRef.Uri = parseAtUri "at://did:plc:feed/app.bsky.feed.post/feed123"
                    Cid = parseCid "bafyreifeed" }

              let result =
                  Bluesky.likeVia agent testPostRef viaPost
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let likeRef = Expect.wantOk result "should succeed"
              Expect.equal (AtUri.value likeRef.Uri) "at://did:plc:testuser/app.bsky.feed.post/abc123" "returns uri"

          testCase "repostVia includes via field in record"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let viaPost =
                  { PostRef.Uri = parseAtUri "at://did:plc:feed/app.bsky.feed.post/feed123"
                    Cid = parseCid "bafyreifeed" }

              let result =
                  Bluesky.repostVia agent testPostRef viaPost
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.feed.repost" "repost collection"
              Expect.stringContains body "\"via\"" "should contain via field"
              Expect.stringContains body "at://did:plc:feed/app.bsky.feed.post/feed123" "via URI"

          testCase "followVia includes via field in record"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let viaPost =
                  { PostRef.Uri = parseAtUri "at://did:plc:feed/app.bsky.feed.post/feed123"
                    Cid = parseCid "bafyreifeed" }

              let result =
                  Bluesky.followVia agent (parseDid "did:plc:other") viaPost
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "app.bsky.graph.follow" "follow collection"
              Expect.stringContains body "did:plc:other" "subject DID"
              Expect.stringContains body "\"via\"" "should contain via field"
              Expect.stringContains body "at://did:plc:feed/app.bsky.feed.post/feed123" "via URI"

          testCase "likeVia accepts TimelinePost as via"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)

              let viaTimelinePost = TestFactory.TimelinePost (
                  uri = parseAtUri "at://did:plc:feed/app.bsky.feed.post/tp123",
                  cid = parseCid "bafyreifeedtp"
              )

              let result =
                  Bluesky.likeVia agent testPostRef viaTimelinePost
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "bafyreifeedtp" "via CID from TimelinePost"

          testCase "likeVia returns NotLoggedIn without session"
          <| fun _ ->
              let agent = createMockAgent (fun _ -> jsonResponse HttpStatusCode.OK {| |})

              let viaPost =
                  { PostRef.Uri = parseAtUri "at://did:plc:feed/app.bsky.feed.post/feed123"
                    Cid = parseCid "bafyreifeed" }

              let result =
                  Bluesky.likeVia agent testPostRef viaPost
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail without session"
              Expect.equal err.StatusCode 401 "status code" ]

// ── Notification content tests ──────────────────────────────────────

[<Tests>]
let notificationContentTests =
    testList
        "Notification content"
        [ testCase "Notification.ofRaw maps Like with PostRef"
          <| fun _ ->
              let json =
                  """{"uri":"at://did:plc:liker/app.bsky.feed.like/abc","cid":"bafyreiabc","author":{"did":"did:plc:liker","handle":"liker.bsky.social","displayName":"Liker"},"reason":"like","reasonSubject":"at://did:plc:me/app.bsky.feed.post/xyz","record":{"$type":"app.bsky.feed.like","subject":{"uri":"at://did:plc:me/app.bsky.feed.post/xyz","cid":"bafyreicde"},"createdAt":"2024-01-01T00:00:00Z"},"isRead":false,"indexedAt":"2024-01-01T00:00:00Z"}"""

              let raw =
                  JsonSerializer.Deserialize<AppBskyNotification.ListNotifications.Notification> (json, Json.options)

              let n = Notification.ofRaw raw
              Expect.equal (AtUri.value n.RecordUri) "at://did:plc:liker/app.bsky.feed.like/abc" "recordUri"

              match n.Content with
              | NotificationContent.Like post ->
                  Expect.equal (AtUri.value post.Uri) "at://did:plc:me/app.bsky.feed.post/xyz" "liked post uri"
              | other -> failtest $"Expected Like, got {other}"

          testCase "Notification.ofRaw maps Reply with text"
          <| fun _ ->
              let json =
                  """{"uri":"at://did:plc:replier/app.bsky.feed.post/reply1","cid":"bafyreiabc","author":{"did":"did:plc:replier","handle":"replier.bsky.social"},"reason":"reply","reasonSubject":"at://did:plc:me/app.bsky.feed.post/orig","record":{"$type":"app.bsky.feed.post","text":"nice post!","createdAt":"2024-01-01T00:00:00Z"},"isRead":false,"indexedAt":"2024-01-01T00:00:00Z"}"""

              let raw =
                  JsonSerializer.Deserialize<AppBskyNotification.ListNotifications.Notification> (json, Json.options)

              let n = Notification.ofRaw raw

              match n.Content with
              | NotificationContent.Reply (text, inReplyTo) ->
                  Expect.equal text "nice post!" "reply text"
                  Expect.equal (AtUri.value inReplyTo.Uri) "at://did:plc:me/app.bsky.feed.post/orig" "in reply to"
              | other -> failtest $"Expected Reply, got {other}"

          testCase "Notification.ofRaw maps Follow"
          <| fun _ ->
              let json =
                  """{"uri":"at://did:plc:follower/app.bsky.graph.follow/abc","cid":"bafyreiabc","author":{"did":"did:plc:follower","handle":"follower.bsky.social"},"reason":"follow","record":{"$type":"app.bsky.graph.follow","subject":"did:plc:me","createdAt":"2024-01-01T00:00:00Z"},"isRead":true,"indexedAt":"2024-01-01T00:00:00Z"}"""

              let raw =
                  JsonSerializer.Deserialize<AppBskyNotification.ListNotifications.Notification> (json, Json.options)

              let n = Notification.ofRaw raw

              match n.Content with
              | NotificationContent.Follow -> ()
              | other -> failtest $"Expected Follow, got {other}"

              Expect.isTrue n.IsRead "is read" ]

// ── FeedItem reply parent tests ─────────────────────────────────────

[<Tests>]
let feedItemReplyParentTests =
    testList
        "FeedItem reply parent"
        [ testCase "FeedItem.ofFeedViewPost maps reply parent"
          <| fun _ ->
              let json =
                  """{"post":{"uri":"at://did:plc:a/app.bsky.feed.post/1","cid":"bafyreiabc","author":{"did":"did:plc:a","handle":"a.bsky.social"},"record":{"$type":"app.bsky.feed.post","text":"reply","createdAt":"2024-01-01T00:00:00Z"},"indexedAt":"2024-01-01T00:00:00Z"},"reply":{"parent":{"$type":"app.bsky.feed.defs#postView","uri":"at://did:plc:b/app.bsky.feed.post/2","cid":"bafyreicde","author":{"did":"did:plc:b","handle":"b.bsky.social"},"record":{"$type":"app.bsky.feed.post","text":"original","createdAt":"2024-01-01T00:00:00Z"},"indexedAt":"2024-01-01T00:00:00Z"},"root":{"$type":"app.bsky.feed.defs#postView","uri":"at://did:plc:b/app.bsky.feed.post/2","cid":"bafyreicde","author":{"did":"did:plc:b","handle":"b.bsky.social"},"record":{"$type":"app.bsky.feed.post","text":"original","createdAt":"2024-01-01T00:00:00Z"},"indexedAt":"2024-01-01T00:00:00Z"}}}"""

              let fvp = JsonSerializer.Deserialize<AppBskyFeed.Defs.FeedViewPost> (json, Json.options)
              let fi = FeedItem.ofFeedViewPost fvp
              Expect.isSome fi.ReplyParent "should have reply parent"
              Expect.equal fi.ReplyParent.Value.Text "original" "parent text"

          testCase "FeedItem.ofFeedViewPost with no reply has None ReplyParent"
          <| fun _ ->
              let json =
                  """{"post":{"uri":"at://did:plc:a/app.bsky.feed.post/1","cid":"bafyreiabc","author":{"did":"did:plc:a","handle":"a.bsky.social"},"record":{"$type":"app.bsky.feed.post","text":"hello","createdAt":"2024-01-01T00:00:00Z"},"indexedAt":"2024-01-01T00:00:00Z"}}"""

              let fvp = JsonSerializer.Deserialize<AppBskyFeed.Defs.FeedViewPost> (json, Json.options)
              let fi = FeedItem.ofFeedViewPost fvp
              Expect.isNone fi.ReplyParent "no reply parent" ]

// ── PostEmbed mapping tests ─────────────────────────────────────────

[<Tests>]
let postEmbedTests =
    testList
        "PostEmbed mapping"
        [ testCase "TimelinePost.ofPostView maps image embed"
          <| fun _ ->
              let json =
                  """{"uri":"at://did:plc:a/app.bsky.feed.post/1","cid":"bafyreiabc","author":{"did":"did:plc:a","handle":"a.bsky.social"},"record":{"$type":"app.bsky.feed.post","text":"pic","createdAt":"2024-01-01T00:00:00Z"},"embed":{"$type":"app.bsky.embed.images#view","images":[{"thumb":"https://cdn.example/thumb.jpg","fullsize":"https://cdn.example/full.jpg","alt":"a photo"}]},"indexedAt":"2024-01-01T00:00:00Z"}"""

              let pv = JsonSerializer.Deserialize<AppBskyFeed.Defs.PostView> (json, Json.options)
              let tp = TimelinePost.ofPostView pv

              match tp.Embed with
              | Some (PostEmbed.Images images) ->
                  Expect.equal images.Length 1 "one image"
                  Expect.equal images.[0].Alt "a photo" "alt text"
                  Expect.stringContains images.[0].Thumb "thumb.jpg" "thumb url"
              | other -> failtest $"Expected Images embed, got {other}"

          testCase "TimelinePost.ofPostView maps external link embed"
          <| fun _ ->
              let json =
                  """{"uri":"at://did:plc:a/app.bsky.feed.post/1","cid":"bafyreiabc","author":{"did":"did:plc:a","handle":"a.bsky.social"},"record":{"$type":"app.bsky.feed.post","text":"link","createdAt":"2024-01-01T00:00:00Z"},"embed":{"$type":"app.bsky.embed.external#view","external":{"uri":"https://example.com","title":"Example","description":"A site"}},"indexedAt":"2024-01-01T00:00:00Z"}"""

              let pv = JsonSerializer.Deserialize<AppBskyFeed.Defs.PostView> (json, Json.options)
              let tp = TimelinePost.ofPostView pv

              match tp.Embed with
              | Some (PostEmbed.ExternalLink link) ->
                  Expect.equal link.Title "Example" "title"
                  Expect.equal link.Description "A site" "description"
              | other -> failtest $"Expected ExternalLink embed, got {other}"

          testCase "TimelinePost.ofPostView with no embed returns None"
          <| fun _ ->
              let json =
                  """{"uri":"at://did:plc:a/app.bsky.feed.post/1","cid":"bafyreiabc","author":{"did":"did:plc:a","handle":"a.bsky.social"},"record":{"$type":"app.bsky.feed.post","text":"hello","createdAt":"2024-01-01T00:00:00Z"},"indexedAt":"2024-01-01T00:00:00Z"}"""

              let pv = JsonSerializer.Deserialize<AppBskyFeed.Defs.PostView> (json, Json.options)
              let tp = TimelinePost.ofPostView pv
              Expect.isNone tp.Embed "no embed" ]

// ── PDS resolution after login ──────────────────────────────────────

[<Tests>]
let pdsResolutionTests =
    testList
        "PDS resolution after login"
        [ testCase "login resolves PDS endpoint from DID document"
          <| fun _ ->
              let agent =
                  createMockAgent (fun req ->
                      let url = req.RequestUri.ToString ()

                      if url.Contains ("createSession") then
                          jsonResponse
                              HttpStatusCode.OK
                              {| accessJwt = "jwt"
                                 refreshJwt = "refresh"
                                 did = "did:plc:testuser"
                                 handle = "test.bsky.social" |}
                      elif url.Contains ("plc.directory") then
                          jsonResponse
                              HttpStatusCode.OK
                              {| id = "did:plc:testuser"
                                 alsoKnownAs = [| "at://test.bsky.social" |]
                                 service =
                                  [| {| id = "#atproto_pds"
                                        ``type`` = "AtprotoPersonalDataServer"
                                        serviceEndpoint = "https://pds.example.com" |} |]
                                 verificationMethod = [||] |}
                      else
                          jsonResponse HttpStatusCode.OK {| |})

              let result =
                  Bluesky.loginWithClient agent.HttpClient "https://bsky.social" "test" "pass"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Ok agent ->
                  Expect.equal (agent.BaseUrl.ToString ()) "https://pds.example.com/" "BaseUrl should be updated to PDS"
              | Error e -> failtest $"login failed: {e}"

          testCase "login keeps original URL if PDS resolution fails"
          <| fun _ ->
              let agent =
                  createMockAgent (fun req ->
                      let url = req.RequestUri.ToString ()

                      if url.Contains ("createSession") then
                          jsonResponse
                              HttpStatusCode.OK
                              {| accessJwt = "jwt"
                                 refreshJwt = "refresh"
                                 did = "did:plc:testuser"
                                 handle = "test.bsky.social" |}
                      elif url.Contains ("plc.directory") then
                          jsonResponse HttpStatusCode.NotFound {| |}
                      else
                          jsonResponse HttpStatusCode.OK {| |})

              let result =
                  Bluesky.loginWithClient agent.HttpClient "https://bsky.social" "test" "pass"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Ok agent ->
                  Expect.equal (agent.BaseUrl.ToString ()) "https://bsky.social/" "BaseUrl should remain original"
              | Error e -> failtest $"login failed: {e}" ]

// ── Union deserialization tests ──────────────────────────────────────

[<Tests>]
let unionDeserializationTests =
    testList
        "Union deserialization"
        [ testCase "ReplyRefParentUnion deserializes PostView case"
          <| fun _ ->
              let json =
                  """{"$type":"app.bsky.feed.defs#postView","uri":"at://did:plc:test/app.bsky.feed.post/abc","cid":"bafyreiabc","author":{"did":"did:plc:test","handle":"test.bsky.social"},"record":{"$type":"app.bsky.feed.post","text":"hello","createdAt":"2024-01-01T00:00:00Z"},"indexedAt":"2024-01-01T00:00:00Z"}"""

              let result =
                  JsonSerializer.Deserialize<AppBskyFeed.Defs.ReplyRefParentUnion> (json, Json.options)

              match result with
              | AppBskyFeed.Defs.ReplyRefParentUnion.PostView pv ->
                  Expect.equal (AtUri.value pv.Uri) "at://did:plc:test/app.bsky.feed.post/abc" "uri"
              | other -> failtest $"Expected PostView, got {other}"

          testCase "PostViewEmbedUnion deserializes images view case"
          <| fun _ ->
              let json =
                  """{"$type":"app.bsky.embed.images#view","images":[{"thumb":"https://cdn.example/thumb.jpg","fullsize":"https://cdn.example/full.jpg","alt":"photo"}]}"""

              let result =
                  JsonSerializer.Deserialize<AppBskyFeed.Defs.PostViewEmbedUnion> (json, Json.options)

              match result with
              | AppBskyFeed.Defs.PostViewEmbedUnion.View iv ->
                  Expect.equal iv.Images.Length 1 "one image"
              | other -> failtest $"Expected View (images), got {other}" ]

// ── TestFactory tests ──────────────────────────────────────────────

[<Tests>]
let testFactoryTests =
    testList
        "TestFactory"
        [ testCase "PostRef creates with defaults"
          <| fun _ ->
              let pr = TestFactory.PostRef ()
              Expect.isTrue (AtUri.value pr.Uri |> fun s -> s.Contains "testfactory") "default URI contains testfactory"
              Expect.isTrue (Cid.value pr.Cid |> fun s -> s.Length > 0) "default CID is non-empty"

          testCase "PostRef allows overriding uri"
          <| fun _ ->
              let customUri = parseAtUri "at://did:plc:custom/app.bsky.feed.post/xyz"
              let pr = TestFactory.PostRef (uri = customUri)
              Expect.equal pr.Uri customUri "uri should be overridden"

          testCase "ProfileSummary creates with defaults"
          <| fun _ ->
              let ps = TestFactory.ProfileSummary ()
              Expect.equal ps.DisplayName "Test User" "default display name"
              Expect.isTrue (Did.value ps.Did |> fun s -> s.Contains "testfactory") "default DID"
              Expect.isNone ps.Avatar "default avatar is None"

          testCase "Profile creates with defaults"
          <| fun _ ->
              let p = TestFactory.Profile ()
              Expect.equal p.DisplayName "Test User" "default display name"
              Expect.equal p.PostsCount 0L "default posts count"
              Expect.isFalse p.IsFollowing "default not following"

          testCase "Profile allows overriding fields"
          <| fun _ ->
              let p = TestFactory.Profile (displayName = "Custom", postsCount = 42L, isFollowing = true)
              Expect.equal p.DisplayName "Custom" "display name overridden"
              Expect.equal p.PostsCount 42L "posts count overridden"
              Expect.isTrue p.IsFollowing "isFollowing overridden"

          testCase "TimelinePost creates with defaults"
          <| fun _ ->
              let tp = TestFactory.TimelinePost ()
              Expect.equal tp.Text "Test post" "default text"
              Expect.equal tp.LikeCount 0L "default like count"
              Expect.isFalse tp.IsLiked "default not liked"

          testCase "TimelinePost allows overriding text and counts"
          <| fun _ ->
              let tp = TestFactory.TimelinePost (text = "Hello world", likeCount = 5L, isLiked = true)
              Expect.equal tp.Text "Hello world" "text overridden"
              Expect.equal tp.LikeCount 5L "like count overridden"
              Expect.isTrue tp.IsLiked "isLiked overridden"

          testCase "LikeRef creates with default"
          <| fun _ ->
              let lr = TestFactory.LikeRef ()
              Expect.isTrue (AtUri.value lr.Uri |> fun s -> s.Contains "app.bsky.feed.like") "default like URI"

          testCase "RepostRef creates with default"
          <| fun _ ->
              let rr = TestFactory.RepostRef ()
              Expect.isTrue (AtUri.value rr.Uri |> fun s -> s.Contains "app.bsky.feed.repost") "default repost URI"

          testCase "FollowRef creates with default"
          <| fun _ ->
              let fr = TestFactory.FollowRef ()
              Expect.isTrue (AtUri.value fr.Uri |> fun s -> s.Contains "app.bsky.graph.follow") "default follow URI"

          testCase "BlockRef creates with default"
          <| fun _ ->
              let br = TestFactory.BlockRef ()
              Expect.isTrue (AtUri.value br.Uri |> fun s -> s.Contains "app.bsky.graph.block") "default block URI"

          testCase "FeedItem creates with defaults"
          <| fun _ ->
              let fi = TestFactory.FeedItem ()
              Expect.equal fi.Post.Text "Test post" "default post text"
              Expect.isNone fi.Reason "default no reason"

          testCase "Notification creates with defaults"
          <| fun _ ->
              let n = TestFactory.Notification ()

              match n.Content with
              | NotificationContent.Like _ -> ()
              | other -> failtest $"Expected Like, got {other}"

              Expect.isFalse n.IsRead "default not read" ]
