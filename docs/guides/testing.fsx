(**
---
title: Testing
category: Infrastructure
categoryindex: 5
index: 29
description: TestFactory for creating domain type instances in unit tests
keywords: fsharp, atproto, testing, factory, mock, unit-test
---

# Testing

The `TestFactory` class provides static factory methods for creating domain type instances with sensible defaults. Every parameter is optional -- specify only what your test cares about, and let the factory fill in the rest.

`TestFactory` lives in the `FSharp.ATProto.Bluesky` namespace. Add a reference to `FSharp.ATProto.Bluesky` in your test project.

## Basic Usage
*)

(*** hide ***)
#nowarn "20"
#r "nuget: Expecto, 10.2.3"
#r "../../src/FSharp.ATProto.Syntax/bin/Release/net10.0/FSharp.ATProto.Syntax.dll"
#r "../../src/FSharp.ATProto.Core/bin/Release/net10.0/FSharp.ATProto.Core.dll"
#r "../../src/FSharp.ATProto.Bluesky/bin/Release/net10.0/FSharp.ATProto.Bluesky.dll"

open FSharp.ATProto.Syntax
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
(***)

open FSharp.ATProto.Bluesky

// Minimal -- all defaults
let post = TestFactory.TimelinePost()

// Override specific fields
let post2 = TestFactory.TimelinePost(text = "Hello world", likeCount = 42L)

// Create a profile
let profile = TestFactory.ProfileSummary(displayName = "Alice")

(**
Default values are deterministic: the default DID is `did:plc:testfactory`, the default handle is `test.bsky.social`, and so on. This keeps test output stable.

## Available Factory Methods

### PostRef

```fsharp
TestFactory.PostRef(?uri: AtUri, ?cid: Cid) : PostRef
```

### ProfileSummary

```fsharp
TestFactory.ProfileSummary(?did: Did, ?handle: Handle, ?displayName: string, ?avatar: string) : ProfileSummary
```

### Profile

```fsharp
TestFactory.Profile(
    ?did, ?handle, ?displayName, ?description, ?avatar, ?banner,
    ?postsCount, ?followersCount, ?followsCount,
    ?isFollowing, ?isFollowedBy, ?isBlocking, ?isBlockedBy, ?isMuted) : Profile
```

### TimelinePost

```fsharp
TestFactory.TimelinePost(
    ?uri, ?cid, ?author, ?text, ?facets,
    ?likeCount, ?repostCount, ?replyCount, ?quoteCount,
    ?indexedAt, ?isLiked, ?isReposted, ?isBookmarked) : TimelinePost
```

### Ref Types

```fsharp
TestFactory.LikeRef(?uri: AtUri) : LikeRef
TestFactory.RepostRef(?uri: AtUri) : RepostRef
TestFactory.FollowRef(?uri: AtUri) : FollowRef
TestFactory.BlockRef(?uri: AtUri) : BlockRef
```

### FeedItem and Notification

```fsharp
TestFactory.FeedItem(?post: TimelinePost, ?reason: FeedReason) : FeedItem

TestFactory.Notification(
    ?kind: NotificationKind, ?author: ProfileSummary,
    ?subjectUri: AtUri, ?isRead: bool, ?indexedAt: DateTimeOffset) : Notification
```

## Example: Testing a Filter Function
*)

(*** hide ***)
open Expecto
(***)

open FSharp.ATProto.Bluesky
open Expecto

let filterTests = testList "post filter" [
    test "filters posts with high like count" {
        let posts = [
            TestFactory.TimelinePost(text = "Popular", likeCount = 100L)
            TestFactory.TimelinePost(text = "Unpopular", likeCount = 2L)
            TestFactory.TimelinePost(text = "Medium", likeCount = 50L)
        ]

        let popular = posts |> List.filter (fun p -> p.LikeCount >= 50L)
        Expect.equal popular.Length 2 "Should find 2 popular posts"
    }

    test "groups notifications by author" {
        let alice = TestFactory.ProfileSummary(displayName = "Alice")
        let bob = TestFactory.ProfileSummary(displayName = "Bob")

        let notifications = [
            TestFactory.Notification(kind = NotificationKind.Like, author = alice)
            TestFactory.Notification(kind = NotificationKind.Follow, author = bob)
            TestFactory.Notification(kind = NotificationKind.Repost, author = alice)
        ]

        let byAuthor = notifications |> List.groupBy (fun n -> n.Author.DisplayName)
        Expect.equal byAuthor.Length 2 "Should have 2 authors"
    }
]

(**
## Example: Testing Undo Logic
*)

let undoTests = test "undo references have valid URIs" {
    let likeRef = TestFactory.LikeRef()
    let followRef = TestFactory.FollowRef()

    // Default URIs contain the correct collection
    Expect.stringContains
        (AtUri.value likeRef.Uri)
        "app.bsky.feed.like"
        "LikeRef URI should reference the like collection"

    Expect.stringContains
        (AtUri.value followRef.Uri)
        "app.bsky.graph.follow"
        "FollowRef URI should reference the follow collection"
}
