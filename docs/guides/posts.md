---
title: Posts
category: Type Reference
categoryindex: 2
index: 5
description: Create, read, quote, reply to, search, and delete Bluesky posts with FSharp.ATProto
keywords: posts, create, reply, delete, thread, quote, search, bluesky
---

# Posts

Create, read, quote, reply to, search, and delete Bluesky posts.

## Domain Types

### TimelinePost

A post with engagement counts and viewer state, returned by feed, search, and thread functions.

| Field | Type | Description |
|-------|------|-------------|
| `Uri` | `AtUri` | AT-URI identifying the post record |
| `Cid` | `Cid` | Content hash of this exact version |
| `Author` | `ProfileSummary` | Post author |
| `Text` | `string` | Post text content |
| `Facets` | `Facet list` | Rich text facets (mentions, links, hashtags) |
| `LikeCount` | `int64` | Number of likes |
| `RepostCount` | `int64` | Number of reposts |
| `ReplyCount` | `int64` | Number of replies |
| `QuoteCount` | `int64` | Number of quote posts |
| `IndexedAt` | `DateTimeOffset` | When the post was indexed |
| `IsLiked` | `bool` | Whether you have liked this post |
| `IsReposted` | `bool` | Whether you have reposted this post |
| `IsBookmarked` | `bool` | Whether you have bookmarked this post |

### PostRef

A reference to a specific version of a post record, returned when creating a post.

| Field | Type | Description |
|-------|------|-------------|
| `Uri` | `AtUri` | AT-URI of the post record |
| `Cid` | `Cid` | Content hash of the post version |

### FeedItem

A single item from a feed or timeline, pairing a post with an optional reason.

| Field | Type | Description |
|-------|------|-------------|
| `Post` | `TimelinePost` | The post content |
| `Reason` | `FeedReason option` | Why the post appeared (repost, pin, or `None` for organic) |

### FeedReason

Discriminated union indicating why a post appeared in a feed.

| Case | Fields | Description |
|------|--------|-------------|
| `Repost` | `by: ProfileSummary` | Someone reposted this post |
| `Pin` | -- | Post is pinned |

### ThreadNode

Discriminated union representing a node in a post thread tree.

| Case | Fields | Description |
|------|--------|-------------|
| `Post` | `ThreadPost` | An accessible post with thread context |
| `NotFound` | `AtUri` | The post was deleted or does not exist |
| `Blocked` | `AtUri` | The post is blocked |

### ThreadPost

A post within a thread, with parent and reply context.

| Field | Type | Description |
|-------|------|-------------|
| `Post` | `TimelinePost` | The post itself |
| `Parent` | `ThreadNode option` | Parent post in the thread |
| `Replies` | `ThreadNode list` | Reply posts |

## Functions

### Creating Posts

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.post` | `agent` `text:string` | `Result<PostRef, XrpcError>` | Create a post with auto-detected rich text |
| `Bluesky.postWithFacets` | `agent` `text:string` `facets:Facet list` | `Result<PostRef, XrpcError>` | Create a post with pre-resolved facets |
| `Bluesky.postWithImages` | `agent` `text:string` `images:ImageUpload list` | `Result<PostRef, XrpcError>` | Create a post with attached images |
| `Bluesky.quotePost` | `agent` `text:string` `quoted:PostRef\|TimelinePost` | `Result<PostRef, XrpcError>` | Create a quote post |
| `Bluesky.replyTo` | `agent` `text:string` `parent:PostRef\|TimelinePost` | `Result<PostRef, XrpcError>` | Reply to a post (auto-resolves thread root) |
| `Bluesky.replyWithKnownRoot` | `agent` `text:string` `parent:PostRef` `root:PostRef` | `Result<PostRef, XrpcError>` | Reply with explicit parent and root |

```fsharp
taskResult {
    // Simple post with auto-detected mentions, links, hashtags
    let! postRef = Bluesky.post agent "Hello from F#! #atproto"

    // Quote another post
    let! quoteRef = Bluesky.quotePost agent "Great take" postRef

    // Reply (thread root resolved automatically)
    let! replyRef = Bluesky.replyTo agent "I agree!" postRef

    // Post with images
    let imageBytes = System.IO.File.ReadAllBytes "photo.jpg"
    let! imagePost = Bluesky.postWithImages agent "Check this out!"
                        [ { Data = imageBytes; MimeType = Jpeg; AltText = "A photo" } ]
}
```

### Reading Posts

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.getPosts` | `agent` `uris:AtUri list` | `Result<TimelinePost list, XrpcError>` | Fetch multiple posts by AT-URI |
| `Bluesky.getPostThread` | `agent` `target:TimelinePost\|PostRef\|AtUri` `depth:int64 option` `parentHeight:int64 option` | `Result<ThreadNode, XrpcError>` | Get full thread tree for pattern matching |
| `Bluesky.getPostThreadView` | `agent` `target:TimelinePost\|PostRef\|AtUri` `depth:int64 option` `parentHeight:int64 option` | `Result<ThreadPost option, XrpcError>` | Get thread as `Some ThreadPost` or `None` |
| `Bluesky.searchPosts` | `agent` `query:string` `limit:int64 option` `cursor:string option` | `Result<Page<TimelinePost>, XrpcError>` | Full-text post search |
| `Bluesky.getQuotes` | `agent` `target:TimelinePost\|PostRef\|AtUri` `limit:int64 option` `cursor:string option` | `Result<Page<TimelinePost>, XrpcError>` | Get posts that quote a given post |

```fsharp
taskResult {
    let! posts = Bluesky.getPosts agent [ postRef.Uri ]
    let! results = Bluesky.searchPosts agent "F# atproto" (Some 10L) None

    // Thread with pattern matching
    let! thread = Bluesky.getPostThread agent postRef (Some 6L) (Some 3L)
    match thread with
    | ThreadNode.Post tp -> printfn "Post: %s" tp.Post.Text
    | ThreadNode.NotFound uri -> printfn "Not found: %s" (AtUri.value uri)
    | ThreadNode.Blocked uri -> printfn "Blocked: %s" (AtUri.value uri)

    // Or the simplified view
    let! threadOpt = Bluesky.getPostThreadView agent postRef (Some 6L) (Some 3L)
    match threadOpt with
    | Some tp -> printfn "Post: %s with %d replies" tp.Post.Text tp.Replies.Length
    | None -> printfn "Post not accessible"
}
```

### Engagement

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.like` | `agent` `target:PostRef\|TimelinePost` | `Result<LikeRef, XrpcError>` | Like a post |
| `Bluesky.repost` | `agent` `target:PostRef\|TimelinePost` | `Result<RepostRef, XrpcError>` | Repost a post |
| `Bluesky.unlikePost` | `agent` `target:PostRef\|TimelinePost` | `Result<UndoResult, XrpcError>` | Unlike by post (looks up viewer state) |
| `Bluesky.unrepostPost` | `agent` `target:PostRef\|TimelinePost` | `Result<UndoResult, XrpcError>` | Un-repost by post (looks up viewer state) |
| `Bluesky.undoLike` | `agent` `likeRef:LikeRef` | `Result<UndoResult, XrpcError>` | Undo a like by its ref |
| `Bluesky.undoRepost` | `agent` `repostRef:RepostRef` | `Result<UndoResult, XrpcError>` | Undo a repost by its ref |

`unlike` and `unrepost` are also available as simpler alternatives that return `unit` instead of `UndoResult`. The generic `Bluesky.undo` accepts any ref type (`LikeRef`, `RepostRef`, `FollowRef`, `BlockRef`).

```fsharp
taskResult {
    let! likeRef = Bluesky.like agent post
    let! _ = Bluesky.undoLike agent likeRef

    // Or without keeping the ref:
    let! result = Bluesky.unlikePost agent post
    match result with
    | Undone -> printfn "Unliked"
    | WasNotPresent -> printfn "Was not liked"
}
```

### Bookmarks

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.addBookmark` | `agent` `target:PostRef\|TimelinePost` | `Result<unit, XrpcError>` | Bookmark a post |
| `Bluesky.removeBookmark` | `agent` `target:TimelinePost\|PostRef\|AtUri` | `Result<unit, XrpcError>` | Remove a bookmark |
| `Bluesky.getBookmarks` | `agent` `limit:int64 option` `cursor:string option` | `Result<Page<TimelinePost>, XrpcError>` | List bookmarked posts |

```fsharp
taskResult {
    do! Bluesky.addBookmark agent post
    let! page = Bluesky.getBookmarks agent (Some 25L) None
}
```

### Deleting

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.deleteRecord` | `agent` `target:TimelinePost\|PostRef\|AtUri` | `Result<unit, XrpcError>` | Delete any record by AT-URI |

```fsharp
taskResult {
    do! Bluesky.deleteRecord agent postRef
}
```

## SRTP Polymorphism

Many post functions accept multiple types via SRTP (statically resolved type parameters):

- `like`, `repost`, `replyTo`, `quotePost`, `addBookmark` accept `TimelinePost` or `PostRef`
- `deleteRecord`, `removeBookmark`, `getPostThread`, `getPostThreadView`, `getLikes`, `getRepostedBy`, `getQuotes`, `muteThread`, `unmuteThread` accept `TimelinePost`, `PostRef`, or `AtUri`

Pass entities directly -- no need to extract `.Uri` or construct a `PostRef`:

```fsharp
taskResult {
    let! page = Bluesky.getTimeline agent (Some 10L) None
    let post = page.Items.Head.Post

    // Pass the TimelinePost directly
    let! likeRef = Bluesky.like agent post
    let! thread = Bluesky.getPostThreadView agent post (Some 6L) None
}
```
