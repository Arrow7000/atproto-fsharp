(**
---
title: Feeds
category: Type Reference
categoryindex: 2
index: 8
description: Read timelines, author feeds, liked posts, bookmarks, and custom feeds with FSharp.ATProto
keywords: feeds, timeline, author feed, liked posts, bookmarks, custom feed, pagination, bluesky
---

# Feeds

Read timelines, author feeds, liked posts, and bookmarks through the `Bluesky` module.

All examples use `taskResult {}` -- see the [Error Handling guide](error-handling.html) for details.

## Domain Types

### FeedItem

A single item in a feed or timeline, pairing a post with the reason it appeared.

| Field | Type | Description |
|-------|------|-------------|
| `Post` | `TimelinePost` | The post content and metadata |
| `Reason` | `FeedReason option` | Why this item appeared (repost, pin, or `None` for organic) |

### FeedReason

Discriminated union indicating why a post appeared in a feed.

| Case | Payload | Description |
|------|---------|-------------|
| `Repost` | `by : ProfileSummary` | Someone reposted this post |
| `Pin` | -- | The post is pinned to the author's profile |

### TimelinePost

A flattened, ergonomic view of a post. Engagement counts are plain `int64` (not `Option`), and viewer state is exposed as simple booleans.

| Field | Type | Description |
|-------|------|-------------|
| `Uri` | `AtUri` | The AT-URI of the post record |
| `Cid` | `Cid` | The CID of the post record version |
| `Author` | `ProfileSummary` | The post author |
| `Text` | `string` | The post text content |
| `Facets` | `Facet list` | Rich text facets (mentions, links, hashtags) |
| `LikeCount` | `int64` | Number of likes |
| `RepostCount` | `int64` | Number of reposts |
| `ReplyCount` | `int64` | Number of replies |
| `QuoteCount` | `int64` | Number of quote posts |
| `IndexedAt` | `DateTimeOffset` | When the post was indexed |
| `IsLiked` | `bool` | Whether the authenticated user liked this post |
| `IsReposted` | `bool` | Whether the authenticated user reposted this post |
| `IsBookmarked` | `bool` | Whether the authenticated user bookmarked this post |

### Page&lt;'T&gt;

A paginated result containing a list of items and an optional cursor for the next page.

| Field | Type | Description |
|-------|------|-------------|
| `Items` | `'T list` | The items in this page |
| `Cursor` | `string option` | Cursor for the next page, or `None` if this is the last page |

### FeedGenerator

Metadata about a custom feed generator.

| Field | Type | Description |
|-------|------|-------------|
| `Uri` | `AtUri` | Feed generator AT-URI |
| `Did` | `Did` | Feed generator service DID |
| `Creator` | `ProfileSummary` | Feed creator |
| `DisplayName` | `string` | Display name |
| `Description` | `string option` | Description |
| `Avatar` | `string option` | Avatar URL |
| `LikeCount` | `int64` | Number of likes |
| `IsOnline` | `bool` | Whether the generator is currently online |
| `IsValid` | `bool` | Whether the generator is valid |

## Functions

### Reading Feeds

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.getTimeline` | `agent`, `limit: int64 option`, `cursor: string option` | `Result<Page<FeedItem>, XrpcError>` | Fetch the authenticated user's home timeline |
| `Bluesky.getAuthorFeed` | `agent`, `actor`, `limit: int64 option`, `cursor: string option` | `Result<Page<FeedItem>, XrpcError>` | Fetch posts by a specific user |
| `Bluesky.getActorLikes` | `agent`, `actor`, `limit: int64 option`, `cursor: string option` | `Result<Page<FeedItem>, XrpcError>` | Fetch posts that a specific user has liked |
| `Bluesky.getBookmarks` | `agent`, `limit: int64 option`, `cursor: string option` | `Result<Page<TimelinePost>, XrpcError>` | Fetch the authenticated user's bookmarked posts |
| `Bluesky.getFeed` | `agent`, `feed: AtUri`, `limit: int64 option`, `cursor: string option` | `Result<Page<FeedItem>, XrpcError>` | Fetch posts from a custom feed generator |
| `Bluesky.getActorFeeds` | `agent`, `actor`, `limit: int64 option`, `cursor: string option` | `Result<Page<FeedGenerator>, XrpcError>` | List feed generators created by a user (SRTP) |
| `Bluesky.getListFeed` | `agent`, `list: AtUri`, `limit: int64 option`, `cursor: string option` | `Result<Page<FeedItem>, XrpcError>` | Fetch posts from a list-based feed |
| `Bluesky.getFeedGenerator` | `agent`, `feed: AtUri` | `Result<FeedGenerator, XrpcError>` | Get metadata for a feed generator |

**SRTP:** `getAuthorFeed`, `getActorLikes`, and `getActorFeeds` accept `ProfileSummary`, `Profile`, `Handle`, or `Did` for the `actor` parameter.
*)

(*** hide ***)
#nowarn "20"
#r "../../src/FSharp.ATProto.Syntax/bin/Release/net10.0/FSharp.ATProto.Syntax.dll"
#r "../../src/FSharp.ATProto.Core/bin/Release/net10.0/FSharp.ATProto.Core.dll"
#r "../../src/FSharp.ATProto.Bluesky/bin/Release/net10.0/FSharp.ATProto.Bluesky.dll"
open FSharp.ATProto.Syntax
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

let agent = Unchecked.defaultof<AtpAgent>
let someProfile = Unchecked.defaultof<Profile>
let timelinePost = Unchecked.defaultof<TimelinePost>
let feedUri = Unchecked.defaultof<AtUri>
let page = Unchecked.defaultof<Page<FeedItem>>

(***)

taskResult {
    let! page = Bluesky.getTimeline agent (Some 25L) None

    for item in page.Items do
        let author = Handle.value item.Post.Author.Handle
        printfn "@%s: %s" author item.Post.Text
}

taskResult {
    // Pass a ProfileSummary directly -- no need to extract a handle string
    let! page = Bluesky.getAuthorFeed agent someProfile (Some 25L) None

    for item in page.Items do
        printfn "%s (%d likes)" item.Post.Text item.Post.LikeCount
}

(**
### Bookmarks

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.addBookmark` | `agent`, `target` | `Result<unit, XrpcError>` | Add a post to your bookmarks |
| `Bluesky.removeBookmark` | `agent`, `target` | `Result<unit, XrpcError>` | Remove a post from your bookmarks |

**SRTP:** `addBookmark` accepts `PostRef` or `TimelinePost`. `removeBookmark` accepts `AtUri`, `PostRef`, or `TimelinePost`.
*)

taskResult {
    let! _ = Bluesky.addBookmark agent timelinePost
    let! _ = Bluesky.removeBookmark agent timelinePost
    return ()
}

(**
### Pagination

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.paginateTimeline` | `agent`, `pageSize: int64 option` | `IAsyncEnumerable<Result<Page<FeedItem>, XrpcError>>` | Lazily paginate the home timeline |
| `Bluesky.paginateFollowers` | `agent`, `actor`, `pageSize: int64 option` | `IAsyncEnumerable<Result<Page<ProfileSummary>, XrpcError>>` | Lazily paginate an actor's followers |
| `Bluesky.paginateNotifications` | `agent`, `pageSize: int64 option` | `IAsyncEnumerable<Result<Page<Notification>, XrpcError>>` | Lazily paginate notifications |
| `Bluesky.paginateBlocks` | `agent`, `pageSize: int64 option` | `IAsyncEnumerable<Result<Page<ProfileSummary>, XrpcError>>` | Paginate blocked users |
| `Bluesky.paginateMutes` | `agent`, `pageSize: int64 option` | `IAsyncEnumerable<Result<Page<ProfileSummary>, XrpcError>>` | Paginate muted users |
| `Bluesky.paginateFeed` | `agent`, `feed: AtUri`, `pageSize: int64 option` | `IAsyncEnumerable<Result<Page<FeedItem>, XrpcError>>` | Paginate a custom feed |
| `Bluesky.paginateListFeed` | `agent`, `list: AtUri`, `pageSize: int64 option` | `IAsyncEnumerable<Result<Page<FeedItem>, XrpcError>>` | Paginate a list-based feed |

**SRTP:** `paginateFollowers` accepts `ProfileSummary`, `Profile`, `Handle`, or `Did` for the `actor` parameter.

Paginators return an `IAsyncEnumerable` that fetches pages lazily and stops when the server returns no cursor. Iterate with `await foreach`:
*)

task {
    let pages = Bluesky.paginateTimeline agent (Some 50L)

    // Consume from F# using TaskSeq or manual IAsyncEnumerator
    let enumerator = pages.GetAsyncEnumerator()

    let rec loop () = task {
        let! hasNext = enumerator.MoveNextAsync()
        if hasNext then
            match enumerator.Current with
            | Ok page ->
                for item in page.Items do
                    printfn "@%s: %s" (Handle.value item.Post.Author.Handle) item.Post.Text
            | Error err ->
                printfn "Error: %A" err
            do! loop ()
    }

    do! loop ()
    do! enumerator.DisposeAsync()
}

(**
For endpoints without a pre-built paginator, use `Xrpc.paginate` directly. See the [Pagination guide](pagination.html) for full details.

## Matching Feed Reasons

Match on `FeedReason` to distinguish reposts and pins from organic posts:
*)

for item in page.Items do
    match item.Reason with
    | Some (FeedReason.Repost (by = reposter)) ->
        printfn "Reposted by @%s: %s"
            (Handle.value reposter.Handle) item.Post.Text
    | Some FeedReason.Pin ->
        printfn "[Pinned] %s" item.Post.Text
    | None ->
        printfn "@%s: %s" (Handle.value item.Post.Author.Handle) item.Post.Text

(**
## Custom Feeds

Custom feeds (algorithmic feeds by third parties) are identified by an AT-URI. Use `Bluesky.getFeed` for convenience, or the raw XRPC wrapper for full control:
*)

taskResult {
    let! output =
        AppBskyFeed.GetFeed.query
            agent
            { Feed = feedUri; Cursor = None; Limit = Some 25L }

    for item in output.Feed do
        printfn "@%s: %s" (Handle.value item.Post.Author.Handle) item.Post.Text
}

(**
## Power Users: Raw XRPC

The raw `AppBskyFeed.GetAuthorFeed.query` gives access to filter and pin options:
*)

taskResult {
    let! output =
        AppBskyFeed.GetAuthorFeed.query
            agent
            { Actor = "someone.bsky.social"
              Cursor = None
              Filter = Some AppBskyFeed.GetAuthorFeed.PostsNoReplies
              IncludePins = Some true
              Limit = Some 25L }

    for item in output.Feed do
        printfn "%s" item.Post.Text
}

(**
| Filter Value | Description |
|--------------|-------------|
| `PostsWithReplies` | All posts including replies (default) |
| `PostsNoReplies` | Original posts only, no replies |
| `PostsWithMedia` | Only posts with images or video |
| `PostsAndAuthorThreads` | Posts and the author's own reply threads |
| `PostsWithVideo` | Only posts with video |
*)
