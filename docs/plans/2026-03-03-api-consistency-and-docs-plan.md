# API Consistency & Docs Restructuring Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make the convenience API type-safe and consistent (entity types accepted everywhere, no bare strings for typed identifiers), then restructure docs into three sections with type catalog reference pages.

**Architecture:** Extend SRTP witness pattern with three new witnesses (ActorDidWitness, PostUriWitness, ConvoWitness), modify ActorWitness to remove bare string. Restructure docs via fsdocs frontmatter categories. Rewrite 6 guide pages as type catalog reference pages.

**Tech Stack:** F# SRTP, Expecto tests, fsdocs markdown with YAML frontmatter

---

## Task 1: Add ActorDidWitness — follow/block/mute accept ProfileSummary/Profile/Did

**Files:**
- Modify: `src/FSharp.ATProto.Bluesky/Bluesky.fs`
- Modify: `tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs`

**Context:** Currently `follow`, `block`, `muteUser`, `unmuteUser` take a typed `Did` directly. We add SRTP so they also accept `ProfileSummary` and `Profile` entities, extracting `.Did` automatically. The `*ByHandle` variants stay as-is (they do async handle resolution).

**Step 1: Add test fixtures and failing SRTP tests**

In `tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs`, add near the existing `testTimelinePost` (used by postRefSrtpTests, around line 3640):

```fsharp
let testProfileSummary =
    { ProfileSummary.Did = Did.parse "did:plc:testactor" |> Result.defaultWith failwith
      Handle = Handle.parse "test.bsky.social" |> Result.defaultWith failwith
      DisplayName = "Test"
      Avatar = None }

let testProfile =
    { Profile.Did = Did.parse "did:plc:testactor" |> Result.defaultWith failwith
      Handle = Handle.parse "test.bsky.social" |> Result.defaultWith failwith
      DisplayName = "Test"
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
```

Then add the test list:

```fsharp
[<Tests>]
let actorDidSrtpTests =
    testList
        "ActorDid SRTP (follow/block/mute accept entities)"
        [ testCase "follow accepts ProfileSummary directly"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)
              let result = Bluesky.follow agent testProfileSummary |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "did:plc:testactor" "should use DID from ProfileSummary"

          testCase "follow accepts Profile directly"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)
              let result = Bluesky.follow agent testProfile |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "did:plc:testactor" "should use DID from Profile"

          testCase "follow still accepts Did directly"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)
              let did = Did.parse "did:plc:testactor" |> Result.defaultWith failwith
              let result = Bluesky.follow agent did |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed"

          testCase "block accepts ProfileSummary directly"
          <| fun _ ->
              let mutable captured = None
              let agent = createRecordAgent (fun req -> captured <- Some req)
              let result = Bluesky.block agent testProfileSummary |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "did:plc:testactor" "should use DID from ProfileSummary"

          testCase "muteUser accepts ProfileSummary directly"
          <| fun _ ->
              let agent = voidProcedureAgent ()
              let result = Bluesky.muteUser agent testProfileSummary |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed"

          testCase "unmuteUser accepts Profile directly"
          <| fun _ ->
              let agent = voidProcedureAgent ()
              let result = Bluesky.unmuteUser agent testProfile |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed" ]
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test tests/FSharp.ATProto.Bluesky.Tests
```

Expected: Compilation errors — functions don't accept ProfileSummary/Profile yet.

**Step 3: Add ActorDidWitness type and helper**

In `src/FSharp.ATProto.Bluesky/Bluesky.fs`, after `ActorWitness` (line ~164), add:

```fsharp
/// <summary>
/// Witness type enabling SRTP-based overloading for functions that need a DID.
/// Allows functions like <c>follow</c> and <c>block</c> to accept
/// a <see cref="ProfileSummary"/>, <see cref="Profile"/>, or <see cref="Did"/> directly.
/// This type is an implementation detail and should not be used directly.
/// </summary>
[<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
type ActorDidWitness =
    | ActorDidWitness

    static member ToDid (ActorDidWitness, d : Did) = d
    static member ToDid (ActorDidWitness, p : ProfileSummary) = p.Did
    static member ToDid (ActorDidWitness, p : Profile) = p.Did
```

**Important:** This must go AFTER the `ProfileSummary` and `Profile` type definitions (which are at lines ~178-260). Move it to after those types, right before `module Bluesky`.

In the `Bluesky` module, after `asPostRef` (line ~496), add the inline helper:

```fsharp
    let inline internal toActorDid (x : ^a) : Did =
        ((^a or ActorDidWitness) : (static member ToDid : ActorDidWitness * ^a -> Did) (ActorDidWitness, x))
```

**Step 4: Update follow/block/muteUser/unmuteUser to use SRTP**

Replace `follow` (lines 900-909) with Impl + inline wrapper:

```fsharp
    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let followImpl (agent : AtpAgent) (did : Did) : Task<Result<FollowRef, XrpcError>> =
        task {
            let record =
                {| ``$type`` = AppBskyGraph.Follow.TypeId
                   createdAt = nowTimestamp ()
                   subject = Did.value did |}

            let! result = createRecord agent "app.bsky.graph.follow" record
            return result |> Result.map (fun o -> { FollowRef.Uri = o.Uri })
        }

    /// <summary>
    /// Follow a user. Accepts a <see cref="ProfileSummary"/>, <see cref="Profile"/>, or <see cref="Did"/>.
    /// </summary>
    let inline follow (agent : AtpAgent) (target : ^a) : Task<Result<FollowRef, XrpcError>> =
        followImpl agent (toActorDid target)
```

Same pattern for `block` (lines 917-926):

```fsharp
    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let blockImpl (agent : AtpAgent) (did : Did) : Task<Result<BlockRef, XrpcError>> =
        task {
            let record =
                {| ``$type`` = AppBskyGraph.Block.TypeId
                   createdAt = nowTimestamp ()
                   subject = Did.value did |}

            let! result = createRecord agent "app.bsky.graph.block" record
            return result |> Result.map (fun o -> { BlockRef.Uri = o.Uri })
        }

    /// <summary>
    /// Block a user. Accepts a <see cref="ProfileSummary"/>, <see cref="Profile"/>, or <see cref="Did"/>.
    /// </summary>
    let inline block (agent : AtpAgent) (target : ^a) : Task<Result<BlockRef, XrpcError>> =
        blockImpl agent (toActorDid target)
```

For `muteUser` (line 1377-1378):

```fsharp
    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let muteUserImpl (agent : AtpAgent) (did : Did) : Task<Result<unit, XrpcError>> =
        AppBskyGraph.MuteActor.call agent { Actor = Did.value did }

    /// <summary>
    /// Mute an account. Accepts a <see cref="ProfileSummary"/>, <see cref="Profile"/>, or <see cref="Did"/>.
    /// </summary>
    let inline muteUser (agent : AtpAgent) (target : ^a) : Task<Result<unit, XrpcError>> =
        muteUserImpl agent (toActorDid target)
```

For `unmuteUser` (lines 1386-1387):

```fsharp
    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let unmuteUserImpl (agent : AtpAgent) (did : Did) : Task<Result<unit, XrpcError>> =
        AppBskyGraph.UnmuteActor.call agent { Actor = Did.value did }

    /// <summary>
    /// Unmute a previously muted account. Accepts a <see cref="ProfileSummary"/>, <see cref="Profile"/>, or <see cref="Did"/>.
    /// </summary>
    let inline unmuteUser (agent : AtpAgent) (target : ^a) : Task<Result<unit, XrpcError>> =
        unmuteUserImpl agent (toActorDid target)
```

**Important:** The `*ByHandle` variants (`followByHandle`, `blockByHandle`, `muteUserByHandle`, `unmuteUserByHandle`) call the original function names. Since `follow` is now inline, they need to call `followImpl` instead (inline functions can't be called from non-inline functions via task CE). Update each:
- `followByHandle`: change `return! follow agent did` → `return! followImpl agent did`
- `blockByHandle`: change `return! block agent did` → `return! blockImpl agent did`
- `muteUserByHandle`: change `return! muteUser agent did` → `return! muteUserImpl agent did`
- `unmuteUserByHandle`: change `return! unmuteUser agent did` → `return! unmuteUserImpl agent did`

Also check the existing `muteUserTypedDidTests` test list — update if it references the old non-SRTP signature.

**Step 5: Run tests**

```bash
dotnet test tests/FSharp.ATProto.Bluesky.Tests
```

Expected: All tests pass including the new SRTP tests.

**Step 6: Commit**

```bash
git add src/FSharp.ATProto.Bluesky/Bluesky.fs tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs
git commit -m "Add ActorDidWitness: follow/block/mute accept ProfileSummary/Profile/Did"
```

---

## Task 2: Extend ActorWitness — remove string, add ProfileSummary/Profile, update read functions

**Files:**
- Modify: `src/FSharp.ATProto.Bluesky/Bluesky.fs`
- Modify: `tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs`

**Context:** The existing `ActorWitness` accepts `Handle | Did | string`. We remove the `string` overload and add `ProfileSummary` and `Profile`. This is a **breaking change** — consumers must use typed identifiers. Then update all read functions that currently take `string actor` to use SRTP: `getFollowers`, `getFollows`, `getAuthorFeed`, `getActorLikes`, `getSuggestedFollows`, `paginateFollowers`. Also change `getProfiles` from `string list` to `Did list`.

**Step 1: Update ActorWitness type**

In `src/FSharp.ATProto.Bluesky/Bluesky.fs`, replace the ActorWitness definition (lines 153-164) with:

```fsharp
/// <summary>
/// Witness type enabling SRTP-based overloading for actor parameters.
/// Allows functions like <c>getProfile</c> and <c>getFollowers</c> to accept
/// <see cref="ProfileSummary"/>, <see cref="Profile"/>, <see cref="Handle"/>, or <see cref="Did"/> directly.
/// This type is an implementation detail and should not be used directly.
/// </summary>
[<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
type ActorWitness =
    | ActorWitness

    static member ToActorString (ActorWitness, h : Handle) = Handle.value h
    static member ToActorString (ActorWitness, d : Did) = Did.value d
    static member ToActorString (ActorWitness, p : ProfileSummary) = Did.value p.Did
    static member ToActorString (ActorWitness, p : Profile) = Did.value p.Did
```

Note: `string` overload removed. `ProfileSummary` and `Profile` overloads added.

**Important:** Same as Task 1, the ActorWitness type needs to be positioned AFTER ProfileSummary and Profile type definitions since it references them. Currently at line 159 it's BEFORE them (line ~178). Move it to after the type definitions, before `module Bluesky`. Group all witness types together: UndoWitness, ActorWitness, ActorDidWitness, PostRefWitness (currently at line 475), and the new PostUriWitness.

**Step 2: Update getFollowers to use SRTP (pattern for all actor-read functions)**

Replace `getFollowers` (lines 1658-1677):

```fsharp
    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let getFollowersImpl
        (agent : AtpAgent)
        (actorStr : string)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ProfileSummary>, XrpcError>> =
        task {
            let! result =
                AppBskyGraph.GetFollowers.query
                    agent
                    { Actor = actorStr
                      Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Followers |> List.map ProfileSummary.ofView
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get the followers of an actor. Accepts a <see cref="ProfileSummary"/>, <see cref="Profile"/>,
    /// <see cref="Handle"/>, or <see cref="Did"/>.
    /// </summary>
    let inline getFollowers
        (agent : AtpAgent)
        (actor : ^a)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ProfileSummary>, XrpcError>> =
        getFollowersImpl agent (toActorString actor) limit cursor
```

**Step 3: Apply the same Impl + inline pattern to these functions:**

| Function | Lines | Impl takes | Notes |
|---|---|---|---|
| `getFollows` | 1687-1706 | `actorStr: string` | Same pattern as getFollowers |
| `getAuthorFeed` | 1807-1828 | `actorStr: string` | Same pattern |
| `getActorLikes` | 1838-1857 | `actorStr: string` | Same pattern |
| `getSuggestedFollows` | 1979-1983 | `actorStr: string` | Same pattern, no limit/cursor params |
| `paginateFollowers` | 2218-2232 | `actorStr: string` | Same pattern, returns IAsyncEnumerable |

For each: rename original to `*Impl` with `[<EditorBrowsable(Never)>]`, add inline wrapper that calls `toActorString`.

**Step 4: Update `getProfiles` to take `Did list`**

Replace `getProfiles` (lines 1967-1971):

```fsharp
    /// <summary>
    /// Get multiple profiles by their DIDs in a single request.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actors">A list of <see cref="Did"/> identifiers.</param>
    /// <returns>A list of <see cref="Profile"/> on success, or an <see cref="XrpcError"/>.</returns>
    let getProfiles (agent : AtpAgent) (actors : Did list) : Task<Result<Profile list, XrpcError>> =
        task {
            let! result = AppBskyActor.GetProfiles.query agent { Actors = actors |> List.map Did.value }
            return result |> Result.map (fun output -> output.Profiles |> List.map Profile.ofDetailed)
        }
```

**Step 5: Update getProfile XML doc** (remove reference to plain `string`):

Update the inline `getProfile` wrapper's XML doc to say: "Accepts a <see cref="ProfileSummary"/>, <see cref="Profile"/>, <see cref="Handle"/>, or <see cref="Did"/>."

**Step 6: Fix existing tests**

Tests that currently pass bare strings to these functions will fail to compile. Fix them:

- `getProfileTests`: Remove the "getProfile still accepts plain string" test case. Keep Handle and Did test cases.
- `getFollowersTests` / `paginateFollowersTests`: Change `getFollowers agent "actor.bsky.social"` to `getFollowers agent testDid` or `getFollowers agent testHandle` (create `testDid` and `testHandle` fixtures if not already available).
- `getProfilesTests`: Change `getProfiles agent ["did:plc:testactor"]` to `getProfiles agent [testDid]`.
- Similar changes for `getAuthorFeedTests`, `getActorLikesTests`, `getSuggestedFollowsTests`.

Add new SRTP-specific tests:

```fsharp
[<Tests>]
let actorWitnessSrtpTests =
    testList
        "ActorWitness SRTP (read functions accept entities)"
        [ testCase "getFollowers accepts ProfileSummary"
          <| fun _ ->
              let agent = queryAgent (fun _ -> followersJsonResponse)
              let result =
                  Bluesky.getFollowers agent testProfileSummary None None
                  |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed"

          testCase "getFollowers accepts Profile"
          <| fun _ ->
              let agent = queryAgent (fun _ -> followersJsonResponse)
              let result =
                  Bluesky.getFollowers agent testProfile None None
                  |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed"

          testCase "getFollowers accepts Handle"
          <| fun _ ->
              let agent = queryAgent (fun _ -> followersJsonResponse)
              let handle = Handle.parse "test.bsky.social" |> Result.defaultWith failwith
              let result =
                  Bluesky.getFollowers agent handle None None
                  |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed"

          testCase "getProfiles accepts Did list"
          <| fun _ ->
              let agent = queryAgent (fun _ -> profilesJsonResponse)
              let did = Did.parse "did:plc:testactor" |> Result.defaultWith failwith
              let result =
                  Bluesky.getProfiles agent [did]
                  |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed" ]
```

Note: Use existing mock JSON response patterns from the test file for `followersJsonResponse` etc.

**Step 7: Run tests**

```bash
dotnet test tests/FSharp.ATProto.Bluesky.Tests
```

Expected: All tests pass.

**Step 8: Commit**

```bash
git add src/FSharp.ATProto.Bluesky/Bluesky.fs tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs
git commit -m "Extend ActorWitness: read functions accept ProfileSummary/Profile/Handle/Did, remove bare string"
```

---

## Task 3: Add PostUriWitness — post-read functions accept TimelinePost/PostRef/AtUri

**Files:**
- Modify: `src/FSharp.ATProto.Bluesky/Bluesky.fs`
- Modify: `tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs`

**Context:** Functions that take an `AtUri` for post identification (`removeBookmark`, `getPostThread`, `getPostThreadView`, `getLikes`, `getRepostedBy`, `getQuotes`, `muteThread`, `unmuteThread`, `deleteRecord`) should also accept `TimelinePost` or `PostRef` via SRTP, extracting `.Uri`.

**Step 1: Add PostUriWitness type and helper**

In `src/FSharp.ATProto.Bluesky/Bluesky.fs`, near the other witness types:

```fsharp
/// <summary>
/// Witness type enabling SRTP-based overloading for functions that need an AT-URI.
/// Allows functions like <c>getPostThread</c> and <c>removeBookmark</c> to accept
/// a <see cref="TimelinePost"/>, <see cref="PostRef"/>, or <see cref="AtUri"/> directly.
/// This type is an implementation detail and should not be used directly.
/// </summary>
[<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
type PostUriWitness =
    | PostUriWitness

    static member ToAtUri (PostUriWitness, u : AtUri) = u
    static member ToAtUri (PostUriWitness, pr : PostRef) = pr.Uri
    static member ToAtUri (PostUriWitness, tp : TimelinePost) = tp.Uri
```

In the `Bluesky` module, add the inline helper:

```fsharp
    let inline internal toPostUri (x : ^a) : AtUri =
        ((^a or PostUriWitness) : (static member ToAtUri : PostUriWitness * ^a -> AtUri) (PostUriWitness, x))
```

**Step 2: Update functions to use SRTP**

Apply Impl + inline pattern to each. Example for `removeBookmark` (lines 1513-1514):

```fsharp
    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let removeBookmarkImpl (agent : AtpAgent) (uri : AtUri) : Task<Result<unit, XrpcError>> =
        AppBskyBookmark.DeleteBookmark.call agent { Uri = uri }

    /// <summary>
    /// Remove a post from your bookmarks. Accepts a <see cref="TimelinePost"/>, <see cref="PostRef"/>, or <see cref="AtUri"/>.
    /// </summary>
    let inline removeBookmark (agent : AtpAgent) (target : ^a) : Task<Result<unit, XrpcError>> =
        removeBookmarkImpl agent (toPostUri target)
```

Apply the same pattern to all 9 functions:

| Function | Lines | Notes |
|---|---|---|
| `removeBookmark` | 1513-1514 | Simple one-liner → Impl + inline |
| `getPostThread` | 1578-1593 | Has `depth` and `parentHeight` params |
| `getPostThreadView` | 1604-1619 | Calls `getPostThread` internally — change to call `getPostThreadImpl` |
| `getLikes` | 1867-1887 | Has `limit` and `cursor` params |
| `getRepostedBy` | 1897-1917 | Has `limit` and `cursor` params |
| `getQuotes` | 1927-1947 | Has `limit` and `cursor` params |
| `muteThread` | 1447-1448 | Simple one-liner |
| `unmuteThread` | 1456-1457 | Simple one-liner |
| `deleteRecord` | 992-1014 | Complex (parses AtUri) — Impl + inline |

**Important:** `getPostThreadView` currently calls `getPostThread`. Since `getPostThread` becomes inline, `getPostThreadViewImpl` needs to call `getPostThreadImpl` instead.

**Step 3: Write tests**

```fsharp
[<Tests>]
let postUriSrtpTests =
    testList
        "PostUri SRTP (read functions accept TimelinePost/PostRef/AtUri)"
        [ testCase "removeBookmark accepts TimelinePost directly"
          <| fun _ ->
              let agent = voidProcedureAgent ()
              let result =
                  Bluesky.removeBookmark agent testTimelinePost
                  |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed"

          testCase "removeBookmark accepts PostRef directly"
          <| fun _ ->
              let agent = voidProcedureAgent ()
              let postRef = { PostRef.Uri = testTimelinePost.Uri; Cid = testTimelinePost.Cid }
              let result =
                  Bluesky.removeBookmark agent postRef
                  |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed"

          testCase "getLikes accepts TimelinePost directly"
          <| fun _ ->
              let agent = queryAgent (fun _ -> likesJsonResponse)
              let result =
                  Bluesky.getLikes agent testTimelinePost None None
                  |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed"

          testCase "muteThread accepts TimelinePost directly"
          <| fun _ ->
              let agent = voidProcedureAgent ()
              let result =
                  Bluesky.muteThread agent testTimelinePost
                  |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed"

          testCase "deleteRecord accepts TimelinePost directly"
          <| fun _ ->
              let agent = deleteRecordAgent (fun _ -> ())
              let result =
                  Bluesky.deleteRecord agent testTimelinePost
                  |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed" ]
```

Also fix existing tests that pass `AtUri` to these functions — they should still compile since `AtUri` is in the witness, but verify.

**Step 4: Run tests**

```bash
dotnet test tests/FSharp.ATProto.Bluesky.Tests
```

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.Bluesky/Bluesky.fs tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs
git commit -m "Add PostUriWitness: post-read functions accept TimelinePost/PostRef/AtUri"
```

---

## Task 4: Add ConvoWitness — Chat functions accept ConvoSummary or string

**Files:**
- Modify: `src/FSharp.ATProto.Bluesky/Chat.fs`
- Modify: `tests/FSharp.ATProto.Bluesky.Tests/ChatTests.fs`

**Context:** Chat functions take `convoId: string`. Since convo IDs are opaque server strings with no typed identifier, `string` is the native type (not a stand-in). We add SRTP so functions also accept `ConvoSummary`, extracting `.Id` automatically.

**Step 1: Add ConvoWitness and helper**

In `src/FSharp.ATProto.Bluesky/Chat.fs`, before `module Chat`:

```fsharp
/// <summary>
/// Witness type enabling SRTP-based overloading for conversation parameters.
/// Allows chat functions to accept a <see cref="ConvoSummary"/> or <c>string</c> convo ID.
/// This type is an implementation detail and should not be used directly.
/// </summary>
[<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
type ConvoWitness =
    | ConvoWitness

    static member ToConvoId (ConvoWitness, c : ConvoSummary) = c.Id
    static member ToConvoId (ConvoWitness, s : string) = s
```

In the `Chat` module, after `ensureChatProxy`:

```fsharp
    let inline internal toConvoId (x : ^a) : string =
        ((^a or ConvoWitness) : (static member ToConvoId : ConvoWitness * ^a -> string) (ConvoWitness, x))
```

**Step 2: Update all Chat functions that take convoId**

Apply Impl + inline pattern. 11 functions to update:

| Function | Lines | Extra params beyond convoId |
|---|---|---|
| `sendMessage` | 73-99 | `text: string` |
| `getMessages` | 109-128 | `limit: int64 option`, `cursor: string option` |
| `deleteMessage` | 138-151 | `messageId: string` |
| `markRead` | 159-165 | None |
| `muteConvo` | 184-188 | None |
| `unmuteConvo` | 196-200 | None |
| `acceptConvo` | 208-212 | None |
| `leaveConvo` | 220-224 | None |
| `addReaction` | 234-249 | `messageId: string`, `emoji: string` |
| `removeReaction` | 259-274 | `messageId: string`, `emoji: string` |
| `getConvo` | 282-286 | None |

Example for `sendMessage`:

```fsharp
    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let sendMessageImpl
        (agent : AtpAgent)
        (convoId : string)
        (text : string)
        : Task<Result<ChatMessage, XrpcError>> =
        task {
            // ... existing body unchanged ...
        }

    /// <summary>
    /// Send a message to a conversation. Accepts a <see cref="ConvoSummary"/> or convo ID string.
    /// Rich text is auto-detected.
    /// </summary>
    let inline sendMessage (agent : AtpAgent) (convo : ^a) (text : string) : Task<Result<ChatMessage, XrpcError>> =
        sendMessageImpl agent (toConvoId convo) text
```

Example for `markRead` (simple one):

```fsharp
    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let markReadImpl (agent : AtpAgent) (convoId : string) : Task<Result<unit, XrpcError>> =
        task {
            let! result =
                ChatBskyConvo.UpdateRead.call (ensureChatProxy agent) { ConvoId = convoId; MessageId = None }
            return result |> Result.map ignore
        }

    /// <summary>
    /// Mark a conversation as read. Accepts a <see cref="ConvoSummary"/> or convo ID string.
    /// </summary>
    let inline markRead (agent : AtpAgent) (convo : ^a) : Task<Result<unit, XrpcError>> =
        markReadImpl agent (toConvoId convo)
```

Apply the same pattern to all 11 functions.

**Step 3: Write tests**

In `tests/FSharp.ATProto.Bluesky.Tests/ChatTests.fs`, add:

```fsharp
let testConvoSummary =
    { ConvoSummary.Id = "convo-123"
      Members = []
      LastMessageText = Some "hello"
      UnreadCount = 0L
      IsMuted = false }

[<Tests>]
let convoWitnessSrtpTests =
    testList
        "ConvoWitness SRTP (Chat functions accept ConvoSummary)"
        [ testCase "sendMessage accepts ConvoSummary directly"
          <| fun _ ->
              let agent = chatMockAgent (fun _ -> sendMessageJsonResponse)
              let result =
                  Chat.sendMessage agent testConvoSummary "hello"
                  |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed"

          testCase "getMessages accepts ConvoSummary directly"
          <| fun _ ->
              let agent = chatMockAgent (fun _ -> messagesJsonResponse)
              let result =
                  Chat.getMessages agent testConvoSummary None None
                  |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed"

          testCase "markRead accepts ConvoSummary directly"
          <| fun _ ->
              let agent = chatVoidAgent ()
              let result =
                  Chat.markRead agent testConvoSummary
                  |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed"

          testCase "getConvo accepts ConvoSummary directly"
          <| fun _ ->
              let agent = chatMockAgent (fun _ -> convoJsonResponse)
              let result =
                  Chat.getConvo agent testConvoSummary
                  |> Async.AwaitTask |> Async.RunSynchronously
              Expect.isOk result "should succeed" ]
```

Use existing mock agent patterns from ChatTests.fs.

**Step 4: Run tests**

```bash
dotnet test tests/FSharp.ATProto.Bluesky.Tests
```

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.Bluesky/Chat.fs tests/FSharp.ATProto.Bluesky.Tests/ChatTests.fs
git commit -m "Add ConvoWitness: Chat functions accept ConvoSummary or string"
```

---

## Task 5: Reorganize witness types in Bluesky.fs

**Files:**
- Modify: `src/FSharp.ATProto.Bluesky/Bluesky.fs`

**Context:** Currently witness types are scattered: UndoWitness at line ~144, ActorWitness at line ~159, PostRefWitness at line ~475. After Tasks 1-3, we have 5 witnesses that reference domain types. Group them all together after the domain type definitions, before `module Bluesky`.

**Step 1: Move all witness types to one location**

After all domain type definitions (ProfileSummary, Profile, TimelinePost, ThreadPost, etc.) and their companion modules, create a clear section:

```fsharp
// ── SRTP witness types (implementation detail) ───────────────────

// UndoWitness ... (existing, moved here)
// ActorWitness ... (modified in Task 2, moved here)
// ActorDidWitness ... (new from Task 1, moved here)
// PostRefWitness ... (existing, moved here from line ~475)
// PostUriWitness ... (new from Task 3, moved here)
```

**Step 2: Run tests**

```bash
dotnet test tests/FSharp.ATProto.Bluesky.Tests
```

**Step 3: Commit**

```bash
git add src/FSharp.ATProto.Bluesky/Bluesky.fs
git commit -m "Reorganize witness types into single section after domain types"
```

---

## Task 6: Docs frontmatter restructuring + taskResult intro notes

**Files:**
- Modify: All 17 doc files (frontmatter only for most, add intro note for Getting Started section)

**Context:** Change fsdocs frontmatter to create three sidebar sections. Add a small `taskResult` intro note at the top of each Getting Started page.

**Step 1: Update frontmatter categories**

**Getting Started** (`category: Getting Started`, `categoryindex: 1`):
- `docs/index.md`: index 0
- `docs/quickstart.md`: index 1
- `docs/guides/build-a-bot.md`: index 2
- `docs/concepts.md`: index 3
- `docs/guides/error-handling.md`: index 4

**Type Reference** (`category: Type Reference`, `categoryindex: 2`):
- `docs/guides/posts.md`: index 5
- `docs/guides/profiles.md`: index 6
- `docs/guides/social.md`: index 7
- `docs/guides/feeds.md`: index 8
- `docs/guides/chat.md`: index 9
- `docs/guides/notifications.md`: index 10

**Advanced Guides** (`category: Advanced Guides`, `categoryindex: 3`):
- `docs/guides/media.md`: index 11
- `docs/guides/rich-text.md`: index 12
- `docs/guides/identity.md`: index 13
- `docs/guides/moderation.md`: index 14
- `docs/guides/pagination.md`: index 15
- `docs/guides/raw-xrpc.md`: index 16

**Step 2: Add taskResult intro note to Getting Started pages**

After the title and first paragraph of each Getting Started page (except index.md which is the landing page, and concepts.md which has no code), add:

```markdown
> Code samples use `taskResult {}`, a computation expression that chains async operations returning `Result`. See [Error Handling](error-handling.html) for details.
```

Add this to: `quickstart.md`, `build-a-bot.md`, `error-handling.md` (at the top, as a brief note about what it covers).

**Step 3: Update index.md guide listing**

The landing page currently lists all 16 guides in a flat list. Restructure to show three sections matching the sidebar:

```markdown
## Getting Started
- [Quickstart](quickstart.html) — ...
- [Build a Bot](guides/build-a-bot.html) — ...
- [Concepts](concepts.html) — ...
- [Error Handling](guides/error-handling.html) — ...

## Type Reference
- [Posts](guides/posts.html) — ...
- [Profiles](guides/profiles.html) — ...
...

## Advanced Guides
- [Media](guides/media.html) — ...
...
```

**Step 4: Commit**

```bash
git add docs/
git commit -m "Restructure docs into three sections: Getting Started, Type Reference, Advanced Guides"
```

---

## Task 7: Rewrite Type Reference pages as type catalogs

**Files:**
- Rewrite: `docs/guides/posts.md`
- Rewrite: `docs/guides/profiles.md`
- Rewrite: `docs/guides/social.md`
- Rewrite: `docs/guides/feeds.md`
- Rewrite: `docs/guides/chat.md`
- Rewrite: `docs/guides/notifications.md`

**Context:** Transform narrative guides into structured type catalog pages. Each follows the same template. All code uses `taskResult {}`.

**Template for each page:**

```markdown
---
title: [Topic]
category: Type Reference
categoryindex: 2
index: [N]
---

# [Topic]

[One sentence intro.]

## Domain Types

### [TypeName]

[One sentence about when you encounter this type.]

| Field | Type | Description |
|-------|------|-------------|
| ... | ... | ... |

### [TypeName2]
...

## Functions

### [Category: e.g., Creating, Reading, Engagement]

| Function | Signature | Description |
|----------|-----------|-------------|
| `Bluesky.xyz` | `AtpAgent -> ... -> Task<Result<..., XrpcError>>` | One-liner |

```fsharp
taskResult {
    let! result = Bluesky.xyz agent ...
    ...
}
```

### [Next Category]
...
```

**Page-specific content:**

### posts.md
- Types: `TimelinePost` (all fields), `PostRef`, `FeedItem`, `FeedReason`, `ThreadNode`, `ThreadPost`
- Function groups: Creating (post, postWithFacets, postWithImages, quotePost, replyTo, replyWithKnownRoot), Reading (getPosts, getPostThread, getPostThreadView, searchPosts, getQuotes), Engagement (like, repost, unlikePost, unrepostPost, unlike, unrepost, undoLike, undoRepost), Bookmarks (addBookmark, removeBookmark, getBookmarks), Deleting (deleteRecord)

### profiles.md
- Types: `Profile` (all fields), `ProfileSummary` (all fields)
- Function groups: Reading (getProfile, getProfiles, getFollowers, getFollows, getSuggestedFollows, getSuggestions, searchActors, searchActorsTypeahead), Engagement info (getLikes, getRepostedBy — who liked/reposted a post), Writing (upsertProfile, updateHandle)

### social.md
- Types: `FollowRef`, `BlockRef`, `LikeRef`, `RepostRef`, `UndoResult`
- Function groups: Following (follow, followByHandle, unfollow, undoFollow), Blocking (block, blockByHandle, unblock, undoBlock), Generic undo (undo)

### feeds.md
- Types: `FeedItem`, `FeedReason`, `Page<'T>`
- Function groups: Reading (getTimeline, getAuthorFeed, getActorLikes, getBookmarks), Pagination (paginateTimeline, paginateFollowers, paginateNotifications)

### chat.md
- Types: `ConvoSummary` (all fields), `ChatMessage` (DU cases)
- Function groups: Conversations (listConvos, getConvoForMembers, getConvo, acceptConvo, leaveConvo), Messages (sendMessage, getMessages, deleteMessage), Read state (markRead, markAllRead), Muting (muteConvo, unmuteConvo), Reactions (addReaction, removeReaction)

### notifications.md
- Types: `Notification` (all fields), `NotificationKind` (DU cases)
- Function groups: Reading (getNotifications, getUnreadNotificationCount), Actions (markNotificationsSeen), Pagination (paginateNotifications)

**Important notes for all pages:**
- Use `taskResult {}` for all code snippets
- Where a function returns bare `Task` (e.g., `RichText.parse`), wrap: `let! x = RichText.parse agent text |> Task.map Ok`
- Show the new SRTP-accepting signatures (e.g., `getFollowers` accepting entities)
- Function signature column should show the consumer-facing types, not implementation details

**Step: Commit after each page or all together**

```bash
git add docs/guides/posts.md docs/guides/profiles.md docs/guides/social.md docs/guides/feeds.md docs/guides/chat.md docs/guides/notifications.md
git commit -m "Rewrite 6 guides as Type Reference catalog pages"
```

---

## Task 8: Update remaining docs for API changes and taskResult consistency

**Files:**
- Modify: `docs/quickstart.md` — update code for new SRTP API
- Modify: `docs/guides/build-a-bot.md` — update code, keep `task {}` (deliberate)
- Modify: `docs/guides/media.md` — minor updates for API changes
- Modify: `docs/guides/rich-text.md` — ensure taskResult consistency where possible
- Modify: `docs/guides/moderation.md` — update mute examples for SRTP
- Modify: `docs/guides/pagination.md` — update paginateFollowers for SRTP
- Verify: `docs/guides/identity.md`, `docs/guides/raw-xrpc.md`, `docs/concepts.md` — should need no changes

**Key changes:**
- Any code that passes bare strings to actor functions → use typed identifiers
- Any code that passes `post.Uri` to read functions → pass `post` directly
- Any code that passes `person.Did` to follow/mute → pass `person` directly
- Ensure `taskResult {}` is used consistently (except build-a-bot which deliberately uses `task {}`)
- Where `task {}` is used for legitimate reasons (bare Task returns, IAsyncEnumerable loops), add a brief note explaining why

**Step: Run fsdocs build to verify**

```bash
dotnet fsdocs build
```

Check for broken links or rendering issues.

**Step: Commit**

```bash
git add docs/
git commit -m "Update docs for SRTP API changes and taskResult consistency"
```

---

## Task 9: Update README and verify build

**Files:**
- Modify: `README.md` — update hero code example if it uses old API patterns
- Verify: all 1727+ tests still pass

**Step 1: Full test run**

```bash
dotnet test
```

Expected: All tests pass.

**Step 2: fsdocs build**

```bash
dotnet fsdocs build
```

Expected: Clean build, no warnings.

**Step 3: Commit**

```bash
git add README.md
git commit -m "Update README for API consistency changes"
```

---

## Parallelization Notes

- **Tasks 1-4** are independent and can be done in parallel (each adds a different witness)
- **Task 5** depends on Tasks 1-3 (reorganizes witness types added by those tasks)
- **Task 6** is independent of API tasks (frontmatter only)
- **Task 7** depends on Tasks 1-4 (docs must reflect new API signatures)
- **Task 8** depends on Tasks 1-4 and 7
- **Task 9** depends on all previous tasks

Recommended execution order: Tasks 1-4 in parallel → Task 5 → Task 6 + 7 in parallel → Task 8 → Task 9
