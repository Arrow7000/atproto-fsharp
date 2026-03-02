---
title: Feeds
category: Guides
categoryindex: 1
index: 4
description: Read timelines, author feeds, liked posts, bookmarks, and custom feeds with FSharp.ATProto
keywords: feeds, timeline, author feed, liked posts, bookmarks, custom feed, pagination, bluesky
---

# Feeds

Bluesky exposes several kinds of feeds: your **home timeline** (posts from people you follow, plus algorithmic suggestions), **author feeds** (posts by a specific user), **liked posts**, **bookmarks**, and **custom feeds** (algorithmic feeds created by third parties). The convenience methods return domain types (`FeedItem`, `TimelinePost`) and support cursor-based pagination through `Page<'T>`.

All examples assume you have an authenticated `AtpAgent` and these namespaces open:

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax
```

## Reading Your Timeline

### Convenience Method

`Bluesky.getTimeline` is the easiest way to fetch your home timeline. It takes an optional page size and an optional cursor:

```fsharp
task {
    let! result = Bluesky.getTimeline agent (Some 25L) None

    match result with
    | Ok page ->
        for item in page.Items do
            let author = Handle.value item.Post.Author.Handle
            printfn "@%s: %s" author item.Post.Text
    | Error err -> printfn "Failed: %A" err
}
```

Pass `None` for the limit to use the server default. Pass `None` for the cursor to start from the most recent posts.

The return type is `Page<FeedItem>`. Each `FeedItem` contains a `Post : TimelinePost` and an optional `Reason : FeedReason` (see [Understanding Feed Items](#Understanding-Feed-Items) below).

### Power Users: Raw XRPC Wrapper

For full control over parameters (e.g., the `Algorithm` field), use the generated `AppBskyFeed.GetTimeline.query` directly:

```fsharp
task {
    let! result =
        AppBskyFeed.GetTimeline.query
            agent
            { Algorithm = None
              Cursor = None
              Limit = Some 25L }

    match result with
    | Ok output ->
        for item in output.Feed do
            printfn "@%s: %s" (Handle.value item.Post.Author.Handle) item.Post.Text
    | Error err -> printfn "Failed: %A" err
}
```

The raw wrapper returns `AppBskyFeed.GetTimeline.Output` with a `Feed : FeedViewPost list` instead of the domain types.

## Author Feed

### Convenience Method

`Bluesky.getAuthorFeed` returns posts by a specific user. Pass the actor's handle or DID as a string:

```fsharp
task {
    let! result = Bluesky.getAuthorFeed agent "my-handle.bsky.social" (Some 25L) None

    match result with
    | Ok page ->
        printfn "Got %d posts" page.Items.Length

        for item in page.Items do
            printfn "%s (%d likes)" item.Post.Text item.Post.LikeCount
    | Error err -> printfn "Failed: %A" err
}
```

### Power Users: Raw XRPC Wrapper

The raw `AppBskyFeed.GetAuthorFeed.query` gives access to filter and pin options:

```fsharp
task {
    let! result =
        AppBskyFeed.GetAuthorFeed.query
            agent
            { Actor = "my-handle.bsky.social"
              Cursor = None
              Filter = Some AppBskyFeed.GetAuthorFeed.PostsNoReplies
              IncludePins = Some true
              Limit = Some 25L }

    match result with
    | Ok output ->
        for item in output.Feed do
            printfn "%s" item.Post.Text
    | Error err -> printfn "Failed: %A" err
}
```

### Filter Options

The `Filter` parameter on the raw wrapper is a `ParamsFilter` DU that controls which posts are included:

| Value | Description |
|-------|-------------|
| `PostsWithReplies` | All posts including replies (default) |
| `PostsNoReplies` | Original posts only, no replies |
| `PostsWithMedia` | Only posts with images or video |
| `PostsAndAuthorThreads` | Posts and the author's own reply threads |
| `PostsWithVideo` | Only posts with video |

Pass `None` to use the default (`PostsWithReplies`).

## Liked Posts

`Bluesky.getActorLikes` returns posts that a specific user has liked:

```fsharp
task {
    let! result = Bluesky.getActorLikes agent "my-handle.bsky.social" (Some 25L) None

    match result with
    | Ok page ->
        for item in page.Items do
            printfn "Liked: %s by @%s" item.Post.Text (Handle.value item.Post.Author.Handle)
    | Error err -> printfn "Failed: %A" err
}
```

The return type is the same `Page<FeedItem>` as the timeline and author feed.

## Custom Feeds

Custom feeds are algorithmic feeds created by third parties. Each feed is identified by an AT-URI of the form `at://did:plc:xxx/app.bsky.feed.generator/feed-name`.

Use `AppBskyFeed.GetFeed.query` to read a custom feed. The `Feed` parameter is a typed `AtUri`, so parse the string first:

```fsharp
task {
    let feedUri =
        AtUri.parse "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.generator/whats-hot"
        |> Result.defaultWith failwith

    let! result =
        AppBskyFeed.GetFeed.query
            agent
            { Feed = feedUri
              Cursor = None
              Limit = Some 25L }

    match result with
    | Ok output ->
        for item in output.Feed do
            let author = Handle.value item.Post.Author.Handle
            printfn "@%s: %s" author item.Post.Text
    | Error err -> printfn "Failed: %A" err
}
```

## Feed Generator Metadata

To get information about a feed generator before fetching its posts, use `AppBskyFeed.GetFeedGenerator.query`:

```fsharp
task {
    let! result = AppBskyFeed.GetFeedGenerator.query agent { Feed = feedUri }

    match result with
    | Ok info ->
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
    | Error err -> printfn "Failed: %A" err
}
```

The `IsOnline` and `IsValid` fields indicate whether the feed generator is currently reachable and returning well-formed responses.

## Bookmarks

Bluesky lets you bookmark posts for later. The convenience methods handle the protocol details for you.

### Adding and Removing Bookmarks

`Bluesky.addBookmark` takes a `PostRef` (the URI + CID pair you get back from creating or referencing a post):

```fsharp
task {
    // Bookmark a post
    let! result = Bluesky.addBookmark agent postRef

    match result with
    | Ok () -> printfn "Bookmarked!"
    | Error err -> printfn "Failed: %A" err
}
```

To remove a bookmark, pass the post's AT-URI:

```fsharp
task {
    let! result = Bluesky.removeBookmark agent postRef.Uri

    match result with
    | Ok () -> printfn "Bookmark removed"
    | Error err -> printfn "Failed: %A" err
}
```

### Reading Your Bookmarks

`Bluesky.getBookmarks` returns a `Page<TimelinePost>` (bookmarks have no repost/pin reason, so they use `TimelinePost` directly instead of `FeedItem`):

```fsharp
task {
    let! result = Bluesky.getBookmarks agent (Some 25L) None

    match result with
    | Ok page ->
        for post in page.Items do
            printfn "@%s: %s" (Handle.value post.Author.Handle) post.Text

            if post.IsBookmarked then
                printfn "  (still bookmarked)"
    | Error err -> printfn "Failed: %A" err
}
```

## Understanding Feed Items

The convenience methods return `Page<FeedItem>` for feeds (timeline, author feed, liked posts) and `Page<TimelinePost>` for bookmarks. These are domain types that simplify the raw protocol types.

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
for item in page.Items do
    let post = item.Post
    let author = Handle.value post.Author.Handle
    printfn "@%s: %s" author post.Text
    printfn "  %d likes, %d replies, %d reposts, %d quotes"
        post.LikeCount post.ReplyCount post.RepostCount post.QuoteCount

    if post.IsLiked then printfn "  (you liked this)"
    if post.IsReposted then printfn "  (you reposted this)"
    if post.IsBookmarked then printfn "  (bookmarked)"
```

The `Facets` field gives you the rich text facets (mentions, links, hashtags) as a typed list.

### Reposts and Pins

The `Reason` field tells you why a post appeared in the feed. Match on the `FeedReason` DU to distinguish reposts and pins from organic posts:

```fsharp
for item in page.Items do
    match item.Reason with
    | Some (FeedReason.Repost (by = reposter)) ->
        printfn "Reposted by @%s (originally by @%s): %s"
            (Handle.value reposter.Handle)
            (Handle.value item.Post.Author.Handle)
            item.Post.Text
    | Some FeedReason.Pin ->
        printfn "[Pinned] %s" item.Post.Text
    | None ->
        printfn "@%s: %s" (Handle.value item.Post.Author.Handle) item.Post.Text
```

## Discovering Feeds

`AppBskyFeed.GetSuggestedFeeds.query` returns a list of popular or recommended feed generators:

```fsharp
task {
    let! result = AppBskyFeed.GetSuggestedFeeds.query agent { Cursor = None; Limit = Some 10L }

    match result with
    | Ok output ->
        for feed in output.Feeds do
            let likes = feed.LikeCount |> Option.defaultValue 0L
            printfn "%s by @%s (%d likes)" feed.DisplayName (Handle.value feed.Creator.Handle) likes
            printfn "  URI: %s" (AtUri.value feed.Uri)

            match feed.Description with
            | Some desc -> printfn "  %s" desc
            | None -> ()
    | Error err -> printfn "Failed: %A" err
}
```

The `Uri` field of each `GeneratorView` is the AT-URI you pass to `AppBskyFeed.GetFeed.query` to read that feed's posts.

## Pagination

### Pre-Built Paginator

The easiest way to page through your timeline is `Bluesky.paginateTimeline`. It returns an `IAsyncEnumerable<Result<Page<FeedItem>, XrpcError>>` that fetches pages lazily as you iterate:

```fsharp
task {
    let pages = Bluesky.paginateTimeline agent (Some 50L)
    let enumerator = pages.GetAsyncEnumerator ()

    let mutable keepGoing = true

    while keepGoing do
        let! moved = enumerator.MoveNextAsync ()
        keepGoing <- moved

        if keepGoing then
            match enumerator.Current with
            | Ok page ->
                for item in page.Items do
                    printfn "@%s: %s" (Handle.value item.Post.Author.Handle) item.Post.Text
            | Error err ->
                printfn "Error: %A" err
                keepGoing <- false
}
```

Pagination stops automatically when the server returns no cursor (i.e., you have reached the end of available content).

### Power Users: Custom Pagination with Xrpc.paginate

For endpoints without a pre-built paginator (custom feeds, search results, etc.), use `Xrpc.paginate` directly. It takes five arguments: the endpoint type ID, initial params, a function to extract the cursor from a response, a function to inject a cursor into params, and the agent:

```fsharp
task {
    let feedUri =
        AtUri.parse "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.generator/whats-hot"
        |> Result.defaultWith failwith

    let pages =
        Xrpc.paginate<AppBskyFeed.GetFeed.Params, AppBskyFeed.GetFeed.Output>
            AppBskyFeed.GetFeed.TypeId
            { Feed = feedUri
              Cursor = None
              Limit = Some 50L }
            (fun output -> output.Cursor)
            (fun cursor p -> { p with Cursor = cursor })
            agent

    let enumerator = pages.GetAsyncEnumerator ()
    let mutable keepGoing = true

    while keepGoing do
        let! moved = enumerator.MoveNextAsync ()
        keepGoing <- moved

        if keepGoing then
            match enumerator.Current with
            | Ok page ->
                for item in page.Feed do
                    printfn "%s" item.Post.Text
            | Error err ->
                printfn "Error: %A" err
                keepGoing <- false
}
```

Pages are fetched lazily -- the next page is only requested when you advance the enumerator. For more pagination patterns, see the [Pagination Guide](pagination.html).
