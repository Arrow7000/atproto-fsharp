---
title: Feeds
category: Guides
categoryindex: 1
index: 4
description: Read timelines, author feeds, and custom feeds with FSharp.ATProto
keywords: feeds, timeline, author feed, custom feed, pagination, bluesky
---

# Feeds

Bluesky exposes three kinds of feeds: your **home timeline** (posts from people you follow, plus algorithmic suggestions), **author feeds** (posts by a specific user), and **custom feeds** (algorithmic feeds created by third parties). All three return the same `FeedViewPost` type and support cursor-based pagination.

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
    | Ok output ->
        for item in output.Feed do
            let author = Handle.value item.Post.Author.Handle
            printfn "@%s: %s" author item.Post.Text
    | Error err -> printfn "Failed: %A" err
}
```

Pass `None` for the limit to use the server default. Pass `None` for the cursor to start from the most recent posts.

### Low-Level XRPC Wrapper

For full control over parameters, use the generated `AppBskyFeed.GetTimeline.query` directly:

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

The `Algorithm` parameter is optional and currently unused by most servers. Pass `None` to get the default timeline.

## Author Feed

`AppBskyFeed.GetAuthorFeed.query` returns posts by a specific user. The `Actor` field accepts a handle or DID string:

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
        printfn "Got %d posts" output.Feed.Length

        for item in output.Feed do
            let likes = item.Post.LikeCount |> Option.defaultValue 0L
            printfn "%s (%d likes)" item.Post.Text likes
    | Error err -> printfn "Failed: %A" err
}
```

### Filter Options

The `Filter` parameter is a `ParamsFilter` DU that controls which posts are included:

| Value | Description |
|-------|-------------|
| `PostsWithReplies` | All posts including replies (default) |
| `PostsNoReplies` | Original posts only, no replies |
| `PostsWithMedia` | Only posts with images or video |
| `PostsAndAuthorThreads` | Posts and the author's own reply threads |
| `PostsWithVideo` | Only posts with video |

Pass `None` to use the default (`PostsWithReplies`).

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

## Understanding Feed Items

Each item in a feed is a `FeedViewPost`. It wraps a `PostView` (the actual post) with additional context about why it appeared in the feed.

### Accessing Post Content

Use the `PostView.Text` extension property to get the post text directly:

```fsharp
for item in output.Feed do
    let post = item.Post
    let author = Handle.value post.Author.Handle
    printfn "@%s: %s" author post.Text
```

The `.Facets` extension gives you the rich text facets (mentions, links, hashtags) as a typed list, and `.AsPost` gives you the fully deserialized `Post` record if you need all fields.

### Reposts and Pins

The `Reason` field tells you why a post appeared in the feed. Match on the `FeedViewPostReasonUnion` to distinguish reposts and pins from organic posts:

```fsharp
for item in output.Feed do
    match item.Reason with
    | Some (AppBskyFeed.Defs.FeedViewPostReasonUnion.ReasonRepost repost) ->
        let reposter = Handle.value repost.By.Handle
        let author = Handle.value item.Post.Author.Handle
        printfn "Reposted by @%s (originally by @%s): %s" reposter author item.Post.Text
    | Some (AppBskyFeed.Defs.FeedViewPostReasonUnion.ReasonPin _) -> printfn "[Pinned] %s" item.Post.Text
    | Some (AppBskyFeed.Defs.FeedViewPostReasonUnion.Unknown _) -> printfn "%s" item.Post.Text // future reason types
    | None ->
        let author = Handle.value item.Post.Author.Handle
        printfn "@%s: %s" author item.Post.Text
```

### Replies

The `Reply` field is present when the post is a reply. It contains references to the parent and root posts:

```fsharp
match item.Reply with
| Some reply ->
    match reply.Parent with
    | AppBskyFeed.Defs.ReplyRefParentUnion.PostView parent ->
        printfn "  (replying to @%s)" (Handle.value parent.Author.Handle)
    | AppBskyFeed.Defs.ReplyRefParentUnion.NotFoundPost _ -> printfn "  (replying to a deleted post)"
    | AppBskyFeed.Defs.ReplyRefParentUnion.BlockedPost _ -> printfn "  (replying to a blocked post)"
    | AppBskyFeed.Defs.ReplyRefParentUnion.Unknown _ -> ()
| None -> ()
```

### Engagement Counts

`PostView` includes optional engagement counts:

```fsharp
let post = item.Post
let likes = post.LikeCount |> Option.defaultValue 0L
let replies = post.ReplyCount |> Option.defaultValue 0L
let reposts = post.RepostCount |> Option.defaultValue 0L
let quotes = post.QuoteCount |> Option.defaultValue 0L
printfn "  %d likes, %d replies, %d reposts, %d quotes" likes replies reposts quotes
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

The easiest way to page through your timeline is `Bluesky.paginateTimeline`. It returns an `IAsyncEnumerable` that fetches pages lazily as you iterate:

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
                for item in page.Feed do
                    printfn "@%s: %s" (Handle.value item.Post.Author.Handle) item.Post.Text
            | Error err ->
                printfn "Error: %A" err
                keepGoing <- false
}
```

Pagination stops automatically when the server returns no cursor (i.e., you have reached the end of available content).

### Custom Pagination with Xrpc.paginate

For endpoints without a pre-built paginator (author feeds, custom feeds, search results, etc.), use `Xrpc.paginate` directly. It takes five arguments: the endpoint type ID, initial params, a function to extract the cursor from a response, a function to inject a cursor into params, and the agent:

```fsharp
task {
    let pages =
        Xrpc.paginate<AppBskyFeed.GetAuthorFeed.Params, AppBskyFeed.GetAuthorFeed.Output>
            AppBskyFeed.GetAuthorFeed.TypeId
            { Actor = "my-handle.bsky.social"
              Cursor = None
              Filter = Some AppBskyFeed.GetAuthorFeed.PostsNoReplies
              IncludePins = Some true
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
