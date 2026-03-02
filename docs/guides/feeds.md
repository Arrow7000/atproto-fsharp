---
title: Feeds
category: Guides
categoryindex: 1
index: 6
description: Read timelines, author feeds, liked posts, bookmarks, and custom feeds with FSharp.ATProto
keywords: feeds, timeline, author feed, liked posts, bookmarks, custom feed, pagination, bluesky
---

# Feeds

All examples use `taskResult {}`. See the [Error Handling guide](error-handling.html) for details.

Bluesky exposes several kinds of feeds: your **home timeline**, **author feeds**, **liked posts**, **bookmarks**, and **custom feeds** (algorithmic feeds created by third parties). The convenience methods return domain types (`FeedItem`, `TimelinePost`) and support cursor-based pagination through `Page<'T>`.

All examples assume you have an authenticated `AtpAgent` and these namespaces open:

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax
```

## Reading Your Timeline

`Bluesky.getTimeline` fetches your home timeline. Pass an optional page size and cursor:

```fsharp
taskResult {
    let! page = Bluesky.getTimeline agent (Some 25L) None

    for item in page.Items do
        let author = Handle.value item.Post.Author.Handle
        printfn "@%s: %s" author item.Post.Text
}
```

Pass `None` for the limit to use the server default. Pass `None` for the cursor to start from the most recent posts.

The return type is `Page<FeedItem>`. Each `FeedItem` contains a `Post : TimelinePost` and an optional `Reason : FeedReason` (see [Understanding Feed Items](#Understanding-Feed-Items) below).

### Power Users: Raw XRPC

For full control over parameters (e.g., the `Algorithm` field), use the generated wrapper directly:

```fsharp
taskResult {
    let! output =
        AppBskyFeed.GetTimeline.query
            agent
            { Algorithm = None; Cursor = None; Limit = Some 25L }

    for item in output.Feed do
        printfn "@%s: %s" (Handle.value item.Post.Author.Handle) item.Post.Text
}
```

## Author Feed

`Bluesky.getAuthorFeed` returns posts by a specific user. Pass the actor's handle or DID as a string:

```fsharp
taskResult {
    let! page = Bluesky.getAuthorFeed agent "someone.bsky.social" (Some 25L) None

    for item in page.Items do
        printfn "%s (%d likes)" item.Post.Text item.Post.LikeCount
}
```

### Power Users: Raw XRPC

The raw `AppBskyFeed.GetAuthorFeed.query` gives access to filter and pin options:

```fsharp
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
```

### Filter Options

The `Filter` parameter on the raw wrapper is a `ParamsFilter` DU:

| Value | Description |
|-------|-------------|
| `PostsWithReplies` | All posts including replies (default) |
| `PostsNoReplies` | Original posts only, no replies |
| `PostsWithMedia` | Only posts with images or video |
| `PostsAndAuthorThreads` | Posts and the author's own reply threads |
| `PostsWithVideo` | Only posts with video |

## Liked Posts

`Bluesky.getActorLikes` returns posts that a specific user has liked:

```fsharp
taskResult {
    let! page = Bluesky.getActorLikes agent "someone.bsky.social" (Some 25L) None

    for item in page.Items do
        printfn "Liked: %s by @%s" item.Post.Text (Handle.value item.Post.Author.Handle)
}
```

The return type is the same `Page<FeedItem>` as the timeline and author feed.

## Custom Feeds

Custom feeds are algorithmic feeds created by third parties. Each feed is identified by an [AT-URI](../concepts.html) of the form `at://did:plc:xxx/app.bsky.feed.generator/feed-name`.

Use `AppBskyFeed.GetFeed.query` to read a custom feed:

```fsharp
// feedUri is an AtUri -- typically from a feed discovery result or known feed
// e.g. AtUri.parse "at://did:plc:.../app.bsky.feed.generator/whats-hot"
taskResult {
    let! output =
        AppBskyFeed.GetFeed.query
            agent
            { Feed = feedUri; Cursor = None; Limit = Some 25L }

    for item in output.Feed do
        printfn "@%s: %s" (Handle.value item.Post.Author.Handle) item.Post.Text
}
```

## Feed Generator Metadata

To get information about a feed generator before fetching its posts, use `AppBskyFeed.GetFeedGenerator.query`:

```fsharp
taskResult {
    let! info = AppBskyFeed.GetFeedGenerator.query agent { Feed = feedUri }

    let view = info.View
    printfn "Feed: %s" view.DisplayName
    printfn "By: %s" (Handle.value view.Creator.Handle)
    printfn "Online: %b, Valid: %b" info.IsOnline info.IsValid

    match view.Description with
    | Some desc -> printfn "Description: %s" desc
    | None -> ()

    match view.LikeCount with
    | Some n -> printfn "Likes: %d" n
    | None -> ()
}
```

The `IsOnline` and `IsValid` fields indicate whether the feed generator is currently reachable and returning well-formed responses.

## Bookmarks

### Adding and Removing Bookmarks

`Bluesky.addBookmark` accepts a `TimelinePost` (or `PostRef`) directly:

```fsharp
taskResult {
    let! _ = Bluesky.addBookmark agent postRef
    printfn "Bookmarked!"
}
```

To remove a bookmark, pass the post's [AT-URI](../concepts.html):

```fsharp
taskResult {
    let! _ = Bluesky.removeBookmark agent postRef.Uri
    printfn "Bookmark removed"
}
```

### Reading Your Bookmarks

`Bluesky.getBookmarks` returns a `Page<TimelinePost>` (bookmarks have no repost/pin reason, so they use `TimelinePost` directly instead of `FeedItem`):

```fsharp
taskResult {
    let! page = Bluesky.getBookmarks agent (Some 25L) None

    for post in page.Items do
        printfn "@%s: %s" (Handle.value post.Author.Handle) post.Text
}
```

## Understanding Feed Items

### FeedItem

A `FeedItem` pairs a post with an optional reason for why it appeared in the feed:

```fsharp
type FeedItem =
    { Post : TimelinePost
      Reason : FeedReason option }
```

### TimelinePost

`TimelinePost` is a flattened, ergonomic view of a post. Engagement counts are plain `int64` values (not `Option`), and viewer state is exposed as simple booleans:

```fsharp
let post = item.Post
printfn "@%s: %s" (Handle.value post.Author.Handle) post.Text
printfn "  %d likes, %d replies, %d reposts, %d quotes"
    post.LikeCount post.ReplyCount post.RepostCount post.QuoteCount

if post.IsLiked then printfn "  (you liked this)"
if post.IsReposted then printfn "  (you reposted this)"
if post.IsBookmarked then printfn "  (bookmarked)"
```

The `Facets` field gives you the rich text facets (mentions, links, hashtags) as a typed list.

### Reposts and Pins

Match on the `FeedReason` DU to distinguish reposts and pins from organic posts:

```fsharp
for item in page.Items do
    match item.Reason with
    | Some (FeedReason.Repost (by = reposter)) ->
        printfn "Reposted by @%s: %s"
            (Handle.value reposter.Handle) item.Post.Text
    | Some FeedReason.Pin ->
        printfn "[Pinned] %s" item.Post.Text
    | None ->
        printfn "@%s: %s" (Handle.value item.Post.Author.Handle) item.Post.Text
```

## Discovering Feeds

`AppBskyFeed.GetSuggestedFeeds.query` returns a list of popular or recommended feed generators:

```fsharp
taskResult {
    let! output =
        AppBskyFeed.GetSuggestedFeeds.query agent { Cursor = None; Limit = Some 10L }

    for feed in output.Feeds do
        let likes = feed.LikeCount |> Option.defaultValue 0L
        printfn "%s by @%s (%d likes)" feed.DisplayName (Handle.value feed.Creator.Handle) likes
}
```

The `Uri` field of each `GeneratorView` is the [AT-URI](../concepts.html) you pass to `AppBskyFeed.GetFeed.query` to read that feed's posts.

## Pagination

For your timeline, `Bluesky.paginateTimeline` returns an `IAsyncEnumerable<Result<Page<FeedItem>, XrpcError>>` that fetches pages lazily:

```fsharp
let pages = Bluesky.paginateTimeline agent (Some 50L)
```

For endpoints without a pre-built paginator (custom feeds, search results, etc.), use `Xrpc.paginate` to build your own:

```fsharp
let pages =
    Xrpc.paginate<AppBskyFeed.GetFeed.Params, AppBskyFeed.GetFeed.Output>
        AppBskyFeed.GetFeed.TypeId
        { Feed = feedUri; Cursor = None; Limit = Some 50L }
        (fun output -> output.Cursor)
        (fun cursor p -> { p with Cursor = cursor })
        agent
```

For full consumption patterns and examples, see the [Pagination guide](pagination.html).
