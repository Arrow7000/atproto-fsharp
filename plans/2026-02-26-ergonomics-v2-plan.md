# API Ergonomics v2 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Improve the consumer-facing API based on fresh-eyes review of the example Program.fs.

**Architecture:** Seven independent changes to the Bluesky convenience layer. Each adds helpers/extensions without changing generated code. A new `PostExtensions.fs` file, plus modifications to `Bluesky.fs` and test files.

**Tech Stack:** F# 10, FSharp.SystemTextJson, Expecto, System.Text.Json

---

### Task 1: Post Content Extension Properties

Add extension properties on `PostView` so consumers can access post text, facets, etc. without raw JSON fishing.

**Files:**
- Create: `src/FSharp.ATProto.Bluesky/PostExtensions.fs`
- Modify: `src/FSharp.ATProto.Bluesky/FSharp.ATProto.Bluesky.fsproj` (add to compile order)
- Test: `tests/FSharp.ATProto.Bluesky.Tests/PostExtensionTests.fs`
- Modify: `tests/FSharp.ATProto.Bluesky.Tests/FSharp.ATProto.Bluesky.Tests.fsproj` (add to compile order)

**Step 1: Write failing tests**

Create `tests/FSharp.ATProto.Bluesky.Tests/PostExtensionTests.fs`:

```fsharp
module FSharp.ATProto.Bluesky.Tests.PostExtensionTests

open System.Text.Json
open Expecto
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax

let private makePostView (recordJson: string) : AppBskyFeed.Defs.PostView =
    let record = JsonSerializer.Deserialize<JsonElement>(recordJson)

    { Author =
        { Did = Did.parse "did:plc:test123456789012345678" |> Result.defaultWith failwith
          Handle = Handle.parse "test.bsky.social" |> Result.defaultWith failwith
          Avatar = None
          CreatedAt = None
          DisplayName = None
          Labels = None
          Viewer = None
          Associated = None }
      BookmarkCount = None
      Cid = Cid.parse "bafyreihffx5a2e4k3lqajmrzwl7g3v2m2xcojnqkmqy7beto5hfkerei2m" |> Result.defaultWith failwith
      Debug = None
      Embed = None
      IndexedAt = AtDateTime.parse "2024-01-01T00:00:00Z" |> Result.defaultWith failwith
      Labels = None
      LikeCount = None
      QuoteCount = None
      Record = record
      ReplyCount = None
      RepostCount = None
      Threadgate = None
      Uri = AtUri.parse "at://did:plc:test123456789012345678/app.bsky.feed.post/abc123" |> Result.defaultWith failwith
      Viewer = None }

[<Tests>]
let postExtensionTests =
    testList
        "PostView extension properties"
        [ testCase "Text returns post text"
          <| fun _ ->
              let pv = makePostView """{"text":"Hello world!","createdAt":"2024-01-01T00:00:00Z"}"""
              Expect.equal pv.Text "Hello world!" "Text should match"

          testCase "Text returns empty string for non-post record"
          <| fun _ ->
              let pv = makePostView """{"notAPost":true}"""
              Expect.equal pv.Text "" "Text should be empty for non-post record"

          testCase "Facets returns facets list"
          <| fun _ ->
              let json =
                  """{"text":"Hello @test.bsky.social","createdAt":"2024-01-01T00:00:00Z","facets":[{"index":{"byteStart":6,"byteEnd":24},"features":[{"$type":"app.bsky.richtext.facet#mention","did":"did:plc:test123456789012345678"}]}]}"""

              let pv = makePostView json
              Expect.equal pv.Facets.Length 1 "Should have one facet"

          testCase "Facets returns empty list when none"
          <| fun _ ->
              let pv = makePostView """{"text":"Plain text","createdAt":"2024-01-01T00:00:00Z"}"""
              Expect.equal pv.Facets [] "Should return empty facets"

          testCase "AsPost returns Some for valid post record"
          <| fun _ ->
              let pv = makePostView """{"text":"Hello","createdAt":"2024-01-01T00:00:00Z"}"""
              Expect.isSome pv.AsPost "AsPost should be Some"
              Expect.equal pv.AsPost.Value.Text "Hello" "AsPost text should match"

          testCase "AsPost returns None for non-post record"
          <| fun _ ->
              let pv = makePostView """{"notAPost":true}"""
              Expect.isNone pv.AsPost "AsPost should be None for non-post record" ]
```

Add to test .fsproj compile order (before `Main.fs`). Add to src .fsproj compile order (after `Generated/Generated.fs`, before `RichText.fs`).

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "PostView extension"`
Expected: Compilation failure — `PostView` has no `Text`/`Facets`/`AsPost` members.

**Step 3: Implement PostExtensions.fs**

Create `src/FSharp.ATProto.Bluesky/PostExtensions.fs`:

```fsharp
namespace FSharp.ATProto.Bluesky

open System.Text.Json

[<AutoOpen>]
module PostExtensions =

    type AppBskyFeed.Defs.PostView with

        /// Deserialize the raw record into a typed Bluesky post.
        /// Returns None if the record is not a valid app.bsky.feed.post.
        member this.AsPost: AppBskyFeed.Post.Post option =
            try
                Some(JsonSerializer.Deserialize<AppBskyFeed.Post.Post>(this.Record, FSharp.ATProto.Core.Json.options))
            with _ ->
                None

        /// Post text. Empty string if the record is not a post.
        member this.Text: string =
            match this.Record.TryGetProperty("text") with
            | true, v when v.ValueKind = JsonValueKind.String -> v.GetString()
            | _ -> ""

        /// Post facets. Empty list if no facets present.
        member this.Facets: AppBskyRichtext.Facet.Facet list =
            this.AsPost
            |> Option.bind (fun p -> p.Facets)
            |> Option.defaultValue []
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "PostView extension"`
Expected: All 6 tests pass.

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.Bluesky/PostExtensions.fs \
        src/FSharp.ATProto.Bluesky/FSharp.ATProto.Bluesky.fsproj \
        tests/FSharp.ATProto.Bluesky.Tests/PostExtensionTests.fs \
        tests/FSharp.ATProto.Bluesky.Tests/FSharp.ATProto.Bluesky.Tests.fsproj
git commit -m "Add PostView extension properties for typed post content access"
```

---

### Task 2: Combined Bluesky.login

Add a one-liner login that combines agent creation and authentication.

**Files:**
- Modify: `src/FSharp.ATProto.Bluesky/Bluesky.fs` (~line 89, in the Bluesky module)
- Test: `tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs`

**Step 1: Write failing test**

Add to `BlueskyTests.fs` after the existing test groups:

```fsharp
[<Tests>]
let loginTests =
    testList
        "Bluesky.login"
        [ testCase "login creates agent and authenticates"
          <| fun _ ->
              let handler =
                  TestHelpers.MockHandler(fun req ->
                      TestHelpers.jsonResponse
                          200
                          {| did = "did:plc:test123456789012345678"
                             handle = "test.bsky.social"
                             accessJwt = "access-token"
                             refreshJwt = "refresh-token" |})

              let client = new System.Net.Http.HttpClient(handler)

              let result =
                  Bluesky.loginWithClient client "https://bsky.social" "test.bsky.social" "test-password"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Ok agent ->
                  Expect.isSome agent.Session "Agent should have session"
                  Expect.equal (Did.value agent.Session.Value.Did) "did:plc:test123456789012345678" "DID should match"
              | Error e -> failtest (sprintf "Login failed: %A" e) ]
```

Note: We test `loginWithClient` (which accepts an HttpClient for testing) rather than `login` (which creates its own).

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "Bluesky.login"`
Expected: Compilation failure — `Bluesky.loginWithClient` does not exist.

**Step 3: Implement**

Add to `Bluesky.fs` near the top of the module (after the private helpers, around line 135):

```fsharp
    /// Create an agent and authenticate in one step. For testing, use loginWithClient.
    let login (baseUrl: string) (identifier: string) (password: string) : Task<Result<AtpAgent, XrpcError>> =
        task {
            let agent = AtpAgent.create baseUrl
            let! result = AtpAgent.login identifier password agent
            return result |> Result.map (fun _ -> agent)
        }

    /// Create an agent with a custom HttpClient and authenticate. For testing/mocking.
    let loginWithClient
        (client: System.Net.Http.HttpClient)
        (baseUrl: string)
        (identifier: string)
        (password: string)
        : Task<Result<AtpAgent, XrpcError>> =
        task {
            let agent = AtpAgent.createWithClient client baseUrl
            let! result = AtpAgent.login identifier password agent
            return result |> Result.map (fun _ -> agent)
        }
```

**Step 4: Run tests**

Run: `dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "Bluesky.login"`
Expected: PASS.

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.Bluesky/Bluesky.fs tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs
git commit -m "Add Bluesky.login for one-step agent creation and authentication"
```

---

### Task 3: Typed Parameter Overloads

Make functions like `getProfile` accept `Handle` and `Did` directly without manual stringifying.

**Files:**
- Modify: `src/FSharp.ATProto.Bluesky/Bluesky.fs`
- Test: `tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs`

**Step 1: Write failing tests**

Add to `BlueskyTests.fs`:

```fsharp
[<Tests>]
let typedParamTests =
    testList
        "Typed parameter overloads"
        [ testCase "getProfile accepts Handle"
          <| fun _ ->
              let mutable capturedUrl = ""

              let agent =
                  queryAgent (fun req ->
                      capturedUrl <- req.RequestUri.ToString()
                      {| did = "did:plc:test123456789012345678"
                         handle = "test.bsky.social" |})

              let handle = Handle.parse "test.bsky.social" |> Result.defaultWith failwith

              let _ =
                  Bluesky.getProfile agent handle |> Async.AwaitTask |> Async.RunSynchronously

              Expect.stringContains capturedUrl "test.bsky.social" "Should use handle value"

          testCase "getProfile accepts Did"
          <| fun _ ->
              let mutable capturedUrl = ""

              let agent =
                  queryAgent (fun req ->
                      capturedUrl <- req.RequestUri.ToString()
                      {| did = "did:plc:test123456789012345678"
                         handle = "test.bsky.social" |})

              let did = Did.parse "did:plc:test123456789012345678" |> Result.defaultWith failwith

              let _ =
                  Bluesky.getProfile agent did |> Async.AwaitTask |> Async.RunSynchronously

              Expect.stringContains capturedUrl "did%3Aplc%3Atest123456789012345678" "Should use DID value"

          testCase "getProfile accepts string"
          <| fun _ ->
              let mutable capturedUrl = ""

              let agent =
                  queryAgent (fun req ->
                      capturedUrl <- req.RequestUri.ToString()
                      {| did = "did:plc:test123456789012345678"
                         handle = "bsky.app" |})

              let _ =
                  Bluesky.getProfile agent "bsky.app"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.stringContains capturedUrl "bsky.app" "Should use string value" ]
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "Typed parameter"`
Expected: Compilation failure — `getProfile` doesn't accept `Handle` or `Did`.

**Step 3: Implement using SRTP**

Add a private SRTP witness type at the top of `Bluesky.fs` (inside the module, before the public functions):

```fsharp
    /// Witness type for SRTP-based actor parameter overloading.
    /// Allows getProfile and similar functions to accept Handle, Did, or string.
    type internal ActorWitness =
        | ActorWitness
        static member ToActorString(ActorWitness, h: Handle) = Handle.value h
        static member ToActorString(ActorWitness, d: Did) = Did.value d
        static member ToActorString(ActorWitness, s: string) = s

    let inline internal toActorString (x: ^a) : string =
        ((^a or ActorWitness) : (static member ToActorString : ActorWitness * ^a -> string) (ActorWitness, x))
```

Then change `getProfile` (line ~556) from:

```fsharp
    let getProfile (agent: AtpAgent) (actor: string) =
        AppBskyActor.GetProfile.query agent { Actor = actor }
```

To:

```fsharp
    let inline getProfile (agent: AtpAgent) (actor: ^a) =
        let actorStr = toActorString actor
        AppBskyActor.GetProfile.query agent { Actor = actorStr }
```

Apply the same pattern to `follow` and `block` (accept Handle in addition to Did):

Add to the witness type:
```fsharp
        static member ToDidString(ActorWitness, d: Did) = Did.value d
        static member ToDidString(ActorWitness, h: Handle) = Handle.value h
        static member ToDidString(ActorWitness, s: string) = s

    let inline internal toDidString (x: ^a) : string =
        ((^a or ActorWitness) : (static member ToDidString : ActorWitness * ^a -> string) (ActorWitness, x))
```

**Important**: If SRTP causes compilation issues (inline functions in modules can be tricky with `task {}` CEs), fall back to adding separate named functions: `getProfileByHandle`, `getProfileByDid`. The SRTP approach is preferred but has known edge cases in F#.

**Step 4: Run tests**

Run: `dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "Typed parameter"`
Expected: PASS. Also run all existing tests to ensure no regressions:
Run: `dotnet test tests/FSharp.ATProto.Bluesky.Tests`

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.Bluesky/Bluesky.fs tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs
git commit -m "Add typed parameter overloads for getProfile, follow, block"
```

---

### Task 4: Simplified Reply API

Simplify `replyTo` to only need a PostRef (fetch root internally). Rename `reply` to `replyWithKnownRoot`.

**Files:**
- Modify: `src/FSharp.ATProto.Bluesky/Bluesky.fs` (lines 192-243)
- Test: `tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs`

**Step 1: Write failing tests**

Add to `BlueskyTests.fs`:

```fsharp
[<Tests>]
let replyToSimplifiedTests =
    testList
        "Simplified reply API"
        [ testCase "replyTo fetches parent post to resolve root"
          <| fun _ ->
              let mutable requestCount = 0

              let handler =
                  TestHelpers.MockHandler(fun req ->
                      requestCount <- requestCount + 1

                      if req.RequestUri.PathAndQuery.Contains("getPosts") then
                          // Return a post with a reply field pointing to a root
                          TestHelpers.jsonResponse
                              200
                              {| posts =
                                  [| {| uri = "at://did:plc:test123456789012345678/app.bsky.feed.post/parent1"
                                        cid = "bafyreihffx5a2e4k3lqajmrzwl7g3v2m2xcojnqkmqy7beto5hfkerei2m"
                                        author =
                                         {| did = "did:plc:test123456789012345678"
                                            handle = "test.bsky.social" |}
                                        record =
                                         {| text = "parent post"
                                            createdAt = "2024-01-01T00:00:00Z"
                                            reply =
                                             {| root =
                                                 {| uri = "at://did:plc:test123456789012345678/app.bsky.feed.post/root1"
                                                    cid = "bafyreihffx5a2e4k3lqajmrzwl7g3v2m2xcojnqkmqy7beto5hfkerei2m" |} |} |}
                                        indexedAt = "2024-01-01T00:00:00Z" |} |] |})
                      else
                          // createRecord response
                          TestHelpers.jsonResponse
                              200
                              {| uri = "at://did:plc:test123456789012345678/app.bsky.feed.post/reply1"
                                 cid = "bafyreihffx5a2e4k3lqajmrzwl7g3v2m2xcojnqkmqy7beto5hfkerei2m" |})

              let client = new System.Net.Http.HttpClient(handler)
              let agent = AtpAgent.createWithClient client "https://bsky.social"

              agent.Session <-
                  Some
                      { AccessJwt = "test"
                        RefreshJwt = "test"
                        Did = Did.parse "did:plc:test123456789012345678" |> Result.defaultWith failwith
                        Handle = Handle.parse "test.bsky.social" |> Result.defaultWith failwith }

              let parentRef =
                  { PostRef.Uri =
                      AtUri.parse "at://did:plc:test123456789012345678/app.bsky.feed.post/parent1"
                      |> Result.defaultWith failwith
                    Cid =
                      Cid.parse "bafyreihffx5a2e4k3lqajmrzwl7g3v2m2xcojnqkmqy7beto5hfkerei2m"
                      |> Result.defaultWith failwith }

              let result =
                  Bluesky.replyTo agent "Great post!" parentRef
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "replyTo should succeed"
              Expect.equal requestCount 2 "Should make 2 requests (getPosts + createRecord)"

          testCase "replyWithKnownRoot uses provided root directly"
          <| fun _ ->
              let agent, _ = createRecordAgent ()

              let parent =
                  { PostRef.Uri =
                      AtUri.parse "at://did:plc:test123456789012345678/app.bsky.feed.post/parent1"
                      |> Result.defaultWith failwith
                    Cid =
                      Cid.parse "bafyreihffx5a2e4k3lqajmrzwl7g3v2m2xcojnqkmqy7beto5hfkerei2m"
                      |> Result.defaultWith failwith }

              let root =
                  { PostRef.Uri =
                      AtUri.parse "at://did:plc:test123456789012345678/app.bsky.feed.post/root1"
                      |> Result.defaultWith failwith
                    Cid =
                      Cid.parse "bafyreihffx5a2e4k3lqajmrzwl7g3v2m2xcojnqkmqy7beto5hfkerei2m"
                      |> Result.defaultWith failwith }

              let result =
                  Bluesky.replyWithKnownRoot agent "Reply!" parent root
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "replyWithKnownRoot should succeed" ]
```

**Step 2: Run tests to verify they fail**

Expected: Compilation failure — new `replyTo` signature and `replyWithKnownRoot` don't exist.

**Step 3: Implement**

In `Bluesky.fs`:

1. Rename `reply` (line 192) to `replyWithKnownRoot`.
2. Replace `replyTo` (line 223) with a new version that fetches the parent post:

```fsharp
    /// Reply to a post. Fetches the parent to auto-resolve the thread root.
    let replyTo (agent: AtpAgent) (text: string) (parentRef: PostRef) : Task<Result<PostRef, XrpcError>> =
        task {
            // Fetch the parent post to get its record (needed to find thread root)
            let! postsResult = AppBskyFeed.GetPosts.query agent { Uris = [ parentRef.Uri ] }

            match postsResult with
            | Error e -> return Error e
            | Ok posts ->
                match posts.Posts with
                | [] -> return Error(toXrpcError "Parent post not found")
                | parentPost :: _ ->
                    // Check if parent has a reply field → extract root from it
                    match parentPost.Record.TryGetProperty("reply") with
                    | true, replyProp ->
                        try
                            let rootProp = replyProp.GetProperty("root")
                            let rootUri = rootProp.GetProperty("uri").GetString()
                            let rootCid = rootProp.GetProperty("cid").GetString()

                            match AtUri.parse rootUri, Cid.parse rootCid with
                            | Ok uri, Ok cid ->
                                let root = { PostRef.Uri = uri; Cid = cid }
                                return! replyWithKnownRoot agent text parentRef root
                            | Error msg, _ ->
                                return Error(toXrpcError (sprintf "Invalid root AT-URI: %s" msg))
                            | _, Error msg ->
                                return Error(toXrpcError (sprintf "Invalid root CID: %s" msg))
                        with ex ->
                            return Error(toXrpcError (sprintf "Malformed reply field: %s" ex.Message))
                    | false, _ ->
                        // Parent has no reply field — it's a top-level post, use as both parent and root
                        return! replyWithKnownRoot agent text parentRef parentRef
        }
```

**Step 4: Run tests**

Run: `dotnet test tests/FSharp.ATProto.Bluesky.Tests`
Expected: All tests pass (new + existing). Some existing `replyTo` tests may need updating if they use the old signature.

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.Bluesky/Bluesky.fs tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs
git commit -m "Simplify reply API: replyTo auto-fetches root, rename reply to replyWithKnownRoot"
```

---

### Task 5: Unified Undo with UndoResult DU

Add `UndoResult` DU, unified `undo` function, and target-based `unlikePost`/`unrepostPost`.

**Files:**
- Modify: `src/FSharp.ATProto.Bluesky/Bluesky.fs`
- Test: `tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs`

**Step 1: Write failing tests**

```fsharp
[<Tests>]
let undoResultTests =
    testList
        "Unified undo"
        [ testCase "undo LikeRef returns Undone on success"
          <| fun _ ->
              let agent = deleteRecordAgent ()

              let likeRef =
                  { LikeRef.Uri =
                      AtUri.parse "at://did:plc:test123456789012345678/app.bsky.feed.like/abc"
                      |> Result.defaultWith failwith }

              let result =
                  Bluesky.undo agent likeRef |> Async.AwaitTask |> Async.RunSynchronously

              Expect.equal result (Ok UndoResult.Undone) "Should return Undone"

          testCase "undo FollowRef returns Undone on success"
          <| fun _ ->
              let agent = deleteRecordAgent ()

              let followRef =
                  { FollowRef.Uri =
                      AtUri.parse "at://did:plc:test123456789012345678/app.bsky.graph.follow/abc"
                      |> Result.defaultWith failwith }

              let result =
                  Bluesky.undo agent followRef |> Async.AwaitTask |> Async.RunSynchronously

              Expect.equal result (Ok UndoResult.Undone) "Should return Undone"

          testCase "unlikePost fetches post and deletes like"
          <| fun _ ->
              let mutable requests = []

              let handler =
                  TestHelpers.MockHandler(fun req ->
                      requests <- req.RequestUri.PathAndQuery :: requests

                      if req.RequestUri.PathAndQuery.Contains("getPosts") then
                          TestHelpers.jsonResponse
                              200
                              {| posts =
                                  [| {| uri = "at://did:plc:test123456789012345678/app.bsky.feed.post/post1"
                                        cid = "bafyreihffx5a2e4k3lqajmrzwl7g3v2m2xcojnqkmqy7beto5hfkerei2m"
                                        author =
                                         {| did = "did:plc:test123456789012345678"
                                            handle = "test.bsky.social" |}
                                        record =
                                         {| text = "post"
                                            createdAt = "2024-01-01T00:00:00Z" |}
                                        indexedAt = "2024-01-01T00:00:00Z"
                                        viewer =
                                         {| like =
                                             "at://did:plc:test123456789012345678/app.bsky.feed.like/likeabc" |} |} |] |})
                      else
                          TestHelpers.jsonResponse 200 {| |})

              let client = new System.Net.Http.HttpClient(handler)
              let agent = AtpAgent.createWithClient client "https://bsky.social"

              agent.Session <-
                  Some
                      { AccessJwt = "test"
                        RefreshJwt = "test"
                        Did = Did.parse "did:plc:test123456789012345678" |> Result.defaultWith failwith
                        Handle = Handle.parse "test.bsky.social" |> Result.defaultWith failwith }

              let postRef =
                  { PostRef.Uri =
                      AtUri.parse "at://did:plc:test123456789012345678/app.bsky.feed.post/post1"
                      |> Result.defaultWith failwith
                    Cid =
                      Cid.parse "bafyreihffx5a2e4k3lqajmrzwl7g3v2m2xcojnqkmqy7beto5hfkerei2m"
                      |> Result.defaultWith failwith }

              let result =
                  Bluesky.unlikePost agent postRef
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.equal result (Ok UndoResult.Undone) "Should return Undone"

          testCase "unlikePost returns WasNotPresent when not liked"
          <| fun _ ->
              let handler =
                  TestHelpers.MockHandler(fun _ ->
                      TestHelpers.jsonResponse
                          200
                          {| posts =
                              [| {| uri = "at://did:plc:test123456789012345678/app.bsky.feed.post/post1"
                                    cid = "bafyreihffx5a2e4k3lqajmrzwl7g3v2m2xcojnqkmqy7beto5hfkerei2m"
                                    author =
                                     {| did = "did:plc:test123456789012345678"
                                        handle = "test.bsky.social" |}
                                    record =
                                     {| text = "post"
                                        createdAt = "2024-01-01T00:00:00Z" |}
                                    indexedAt = "2024-01-01T00:00:00Z"
                                    viewer = {| |} |} |] |})

              let client = new System.Net.Http.HttpClient(handler)
              let agent = AtpAgent.createWithClient client "https://bsky.social"

              agent.Session <-
                  Some
                      { AccessJwt = "test"
                        RefreshJwt = "test"
                        Did = Did.parse "did:plc:test123456789012345678" |> Result.defaultWith failwith
                        Handle = Handle.parse "test.bsky.social" |> Result.defaultWith failwith }

              let postRef =
                  { PostRef.Uri =
                      AtUri.parse "at://did:plc:test123456789012345678/app.bsky.feed.post/post1"
                      |> Result.defaultWith failwith
                    Cid =
                      Cid.parse "bafyreihffx5a2e4k3lqajmrzwl7g3v2m2xcojnqkmqy7beto5hfkerei2m"
                      |> Result.defaultWith failwith }

              let result =
                  Bluesky.unlikePost agent postRef
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.equal result (Ok UndoResult.WasNotPresent) "Should return WasNotPresent" ]
```

**Step 2: Run tests to verify they fail**

Expected: Compilation failure — `UndoResult`, `Bluesky.undo`, `Bluesky.unlikePost` don't exist.

**Step 3: Implement**

Add the DU and functions to `Bluesky.fs`:

```fsharp
    /// Result of an undo operation.
    type UndoResult =
        | Undone
        | WasNotPresent
```

Add `undo` functions (these replace the old unlike/unrepost/unfollow/unblock):

```fsharp
    /// Undo a like.
    let undoLike (agent: AtpAgent) (likeRef: LikeRef) : Task<Result<UndoResult, XrpcError>> =
        task {
            let! result = deleteRecord agent likeRef.Uri
            return result |> Result.map (fun () -> Undone)
        }

    /// Undo a repost.
    let undoRepost (agent: AtpAgent) (repostRef: RepostRef) : Task<Result<UndoResult, XrpcError>> =
        task {
            let! result = deleteRecord agent repostRef.Uri
            return result |> Result.map (fun () -> Undone)
        }

    /// Undo a follow.
    let undoFollow (agent: AtpAgent) (followRef: FollowRef) : Task<Result<UndoResult, XrpcError>> =
        task {
            let! result = deleteRecord agent followRef.Uri
            return result |> Result.map (fun () -> Undone)
        }

    /// Undo a block.
    let undoBlock (agent: AtpAgent) (blockRef: BlockRef) : Task<Result<UndoResult, XrpcError>> =
        task {
            let! result = deleteRecord agent blockRef.Uri
            return result |> Result.map (fun () -> Undone)
        }
```

For the unified `undo`, use SRTP (same pattern as Task 3) or separate overloads. Each ref type is distinct, so the simplest approach is to leave the four typed functions above and have the `undo` be a simple dispatch. If SRTP works from Task 3:

```fsharp
    type internal UndoWitness =
        | UndoWitness
        static member Undo(UndoWitness, agent, r: LikeRef) = undoLike agent r
        static member Undo(UndoWitness, agent, r: RepostRef) = undoRepost agent r
        static member Undo(UndoWitness, agent, r: FollowRef) = undoFollow agent r
        static member Undo(UndoWitness, agent, r: BlockRef) = undoBlock agent r

    let inline internal undoDispatch agent (r: ^a) =
        ((^a or UndoWitness) : (static member Undo : UndoWitness * AtpAgent * ^a -> Task<Result<UndoResult, XrpcError>>) (UndoWitness, agent, r))

    let inline undo (agent: AtpAgent) (ref: ^a) = undoDispatch agent ref
```

If SRTP doesn't work cleanly here, just keep the four named functions (`undoLike`, `undoRepost`, `undoFollow`, `undoBlock`) — still a clear improvement over the old names.

Add target-based undo functions:

```fsharp
    /// Unlike a post by PostRef. Fetches the post to find the like record.
    let unlikePost (agent: AtpAgent) (postRef: PostRef) : Task<Result<UndoResult, XrpcError>> =
        task {
            let! postsResult = AppBskyFeed.GetPosts.query agent { Uris = [ postRef.Uri ] }

            match postsResult with
            | Error e -> return Error e
            | Ok posts ->
                match posts.Posts with
                | [] -> return Ok WasNotPresent
                | post :: _ ->
                    match post.Viewer |> Option.bind (fun v -> v.Like) with
                    | Some likeUri ->
                        let! result = deleteRecord agent likeUri
                        return result |> Result.map (fun () -> Undone)
                    | None -> return Ok WasNotPresent
        }

    /// Unrepost a post by PostRef. Fetches the post to find the repost record.
    let unrepostPost (agent: AtpAgent) (postRef: PostRef) : Task<Result<UndoResult, XrpcError>> =
        task {
            let! postsResult = AppBskyFeed.GetPosts.query agent { Uris = [ postRef.Uri ] }

            match postsResult with
            | Error e -> return Error e
            | Ok posts ->
                match posts.Posts with
                | [] -> return Ok WasNotPresent
                | post :: _ ->
                    match post.Viewer |> Option.bind (fun v -> v.Repost) with
                    | Some repostUri ->
                        let! result = deleteRecord agent repostUri
                        return result |> Result.map (fun () -> Undone)
                    | None -> return Ok WasNotPresent
        }
```

Keep the existing `unlike`, `unrepost`, `unfollow`, `unblock` as-is for backward compatibility (they still return `Result<unit, XrpcError>`).

**Step 4: Run tests**

Run: `dotnet test tests/FSharp.ATProto.Bluesky.Tests`
Expected: All pass.

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.Bluesky/Bluesky.fs tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs
git commit -m "Add UndoResult DU, unified undo, and target-based unlikePost/unrepostPost"
```

---

### Task 6: ImageMime DU

Replace bare `string` MIME type with a type-safe DU.

**Files:**
- Modify: `src/FSharp.ATProto.Bluesky/Bluesky.fs`
- Test: `tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs`

**Step 1: Write failing tests**

```fsharp
[<Tests>]
let imageMimeTests =
    testList
        "ImageMime"
        [ testCase "Png converts to image/png"
          <| fun _ -> Expect.equal (ImageMime.toMimeString Png) "image/png" "Png"

          testCase "Jpeg converts to image/jpeg"
          <| fun _ -> Expect.equal (ImageMime.toMimeString Jpeg) "image/jpeg" "Jpeg"

          testCase "Gif converts to image/gif"
          <| fun _ -> Expect.equal (ImageMime.toMimeString Gif) "image/gif" "Gif"

          testCase "Webp converts to image/webp"
          <| fun _ -> Expect.equal (ImageMime.toMimeString Webp) "image/webp" "Webp"

          testCase "Custom preserves raw string"
          <| fun _ ->
              Expect.equal (ImageMime.toMimeString (Custom "video/mp4")) "video/mp4" "Custom" ]
```

**Step 2: Run tests to verify they fail**

Expected: Compilation failure — `ImageMime` doesn't exist.

**Step 3: Implement**

Add to `Bluesky.fs` in the type definitions section (around line 50):

```fsharp
    type ImageMime =
        | Png
        | Jpeg
        | Gif
        | Webp
        | Custom of string

    module ImageMime =
        let toMimeString =
            function
            | Png -> "image/png"
            | Jpeg -> "image/jpeg"
            | Gif -> "image/gif"
            | Webp -> "image/webp"
            | Custom s -> s
```

Change `ImageUpload`:

```fsharp
    type ImageUpload =
        { Data: byte[]
          MimeType: ImageMime  // was: string
          AltText: string }
```

Update `uploadBlob` to accept `ImageMime`:

```fsharp
    let uploadBlob (agent: AtpAgent) (data: byte[]) (mimeType: ImageMime) = ...
        // internally: use ImageMime.toMimeString mimeType where the string is needed
```

Update `postWithImages` / `uploadAllBlobs` internal calls to convert via `ImageMime.toMimeString`.

**Step 4: Run tests**

Run: `dotnet test tests/FSharp.ATProto.Bluesky.Tests`
Expected: All pass. Existing blob/image tests need updating to use `ImageMime.Png` etc.

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.Bluesky/Bluesky.fs tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs
git commit -m "Add ImageMime DU replacing bare string MIME types"
```

---

### Task 7: Pre-built Paginators

Add convenience wrappers for common paginated queries.

**Files:**
- Modify: `src/FSharp.ATProto.Bluesky/Bluesky.fs`
- Test: `tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs`

**Step 1: Write failing tests**

```fsharp
[<Tests>]
let paginatorTests =
    testList
        "Pre-built paginators"
        [ testCase "paginateTimeline returns IAsyncEnumerable"
          <| fun _ ->
              let mutable pageCount = 0

              let handler =
                  TestHelpers.MockHandler(fun _ ->
                      pageCount <- pageCount + 1

                      if pageCount = 1 then
                          TestHelpers.jsonResponse
                              200
                              {| feed = [||]
                                 cursor = "page2" |}
                      else
                          TestHelpers.jsonResponse 200 {| feed = [||] |})

              let client = new System.Net.Http.HttpClient(handler)
              let agent = AtpAgent.createWithClient client "https://bsky.social"

              agent.Session <-
                  Some
                      { AccessJwt = "test"
                        RefreshJwt = "test"
                        Did = Did.parse "did:plc:test123456789012345678" |> Result.defaultWith failwith
                        Handle = Handle.parse "test.bsky.social" |> Result.defaultWith failwith }

              let pages = Bluesky.paginateTimeline agent (Some 5L)
              let enumerator = pages.GetAsyncEnumerator()

              let moved =
                  enumerator.MoveNextAsync().AsTask()
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isTrue moved "Should have first page"
              Expect.isOk enumerator.Current "First page should be Ok" ]
```

**Step 2: Run tests to verify they fail**

Expected: Compilation failure — `Bluesky.paginateTimeline` doesn't exist.

**Step 3: Implement**

Add to `Bluesky.fs`:

```fsharp
    /// Paginate the home timeline.
    let paginateTimeline (agent: AtpAgent) (pageSize: int64 option) =
        Xrpc.paginate<AppBskyFeed.GetTimeline.Params, AppBskyFeed.GetTimeline.Output>
            AppBskyFeed.GetTimeline.TypeId
            { Algorithm = None
              Cursor = None
              Limit = pageSize }
            (fun o -> o.Cursor)
            (fun c p -> { p with Cursor = c })
            agent

    /// Paginate followers for an actor.
    let paginateFollowers (agent: AtpAgent) (actor: string) (pageSize: int64 option) =
        Xrpc.paginate<AppBskyGraph.GetFollowers.Params, AppBskyGraph.GetFollowers.Output>
            AppBskyGraph.GetFollowers.TypeId
            { Actor = actor
              Cursor = None
              Limit = pageSize }
            (fun o -> o.Cursor)
            (fun c p -> { p with Cursor = c })
            agent

    /// Paginate notifications.
    let paginateNotifications (agent: AtpAgent) (pageSize: int64 option) =
        Xrpc.paginate<AppBskyNotification.ListNotifications.Params, AppBskyNotification.ListNotifications.Output>
            AppBskyNotification.ListNotifications.TypeId
            { Cursor = None
              Limit = pageSize
              Priority = None
              Reasons = None
              SeenAt = None }
            (fun o -> o.Cursor)
            (fun c p -> { p with Cursor = c })
            agent
```

**Step 4: Run tests**

Run: `dotnet test tests/FSharp.ATProto.Bluesky.Tests`
Expected: All pass.

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.Bluesky/Bluesky.fs tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs
git commit -m "Add pre-built paginators for timeline, followers, notifications"
```

---

### Task 8: Update Example Program.fs

Rewrite the example to use all new APIs, serving as the consumer-facing showcase.

**Files:**
- Modify: `examples/BskyBotExample/Program.fs`

**Step 1: Update to use new APIs**

Key changes throughout the file:

```fsharp
// Section 1: Combined login
let! agent =
    Bluesky.login "https://bsky.social" (env "BSKY_HANDLE") (env "BSKY_PASSWORD")
    |> Task.map (Result.defaultWith (fun e -> failwithf "Login failed: %A" e))

// Section 2: Typed params
let! ownProfile = Bluesky.getProfile agent session.Handle

// Section 3: Post text via extension property
let text = item.Post.Text

// Section 4: ImageMime DU
{ Data = dummyImage; MimeType = Png; AltText = "A test image" }

// Section 5: Simplified reply
let! replyResult = Bluesky.replyTo agent "Great post!" parentRef

// Section 6: Unified undo
let! undoResult = Bluesky.undo agent likeRef

// Section 7: Target-based undo
let! result = Bluesky.unlikePost agent postRef

// Section 16: Pre-built paginator
let timelinePages = Bluesky.paginateTimeline agent (Some 5L)
```

Remove all inline comments/questions from the user's review.

**Step 2: Verify it compiles**

Run: `dotnet build examples/BskyBotExample`
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add examples/BskyBotExample/Program.fs
git commit -m "Update example Program.fs to use ergonomics v2 APIs"
```
