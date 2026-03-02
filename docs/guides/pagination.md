---
title: Pagination
category: Guides
categoryindex: 1
index: 10
description: Iterate through cursor-based AT Protocol API results with IAsyncEnumerable
keywords: pagination, cursor, async, enumerable, timeline, feed
---

# Pagination

Many AT Protocol endpoints return paginated results. FSharp.ATProto provides pre-built paginators for common use cases and a general `Xrpc.paginate` function for everything else. All paginators return `IAsyncEnumerable<Result<'O, XrpcError>>` -- pages arrive lazily, one at a time, only when you ask for the next one.

## Quick Start

The fastest way to paginate is with a pre-built paginator. Here's how to scroll through your home timeline:

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax

let pages = Bluesky.paginateTimeline agent (Some 25L)
let enumerator = pages.GetAsyncEnumerator()
let mutable hasMore = true

while hasMore do
    let! moved = enumerator.MoveNextAsync()
    hasMore <- moved
    if hasMore then
        match enumerator.Current with
        | Ok page ->
            for item in page.Feed do
                printfn "@%s: %s"
                    (Handle.value item.Post.Author.Handle)
                    item.Post.Text
        | Error e ->
            printfn "Error: %A" e
            hasMore <- false
```

`paginateTimeline` takes just two arguments -- an authenticated agent and an optional page size. The `PostView.Text` extension property gives you the post text directly, no JSON digging required.

## Pre-Built Paginators

The `Bluesky` module provides three pre-built paginators that handle all the cursor plumbing for you:

### Timeline

```fsharp
Bluesky.paginateTimeline : AtpAgent -> int64 option -> IAsyncEnumerable<Result<GetTimeline.Output, XrpcError>>
```

```fsharp
let pages = Bluesky.paginateTimeline agent (Some 25L)
```

### Followers

```fsharp
Bluesky.paginateFollowers : AtpAgent -> string -> int64 option -> IAsyncEnumerable<Result<GetFollowers.Output, XrpcError>>
```

```fsharp
let pages = Bluesky.paginateFollowers agent "my-handle.bsky.social" (Some 50L)
```

The `actor` parameter is a string that can be either a handle or a DID.

### Notifications

```fsharp
Bluesky.paginateNotifications : AtpAgent -> int64 option -> IAsyncEnumerable<Result<ListNotifications.Output, XrpcError>>
```

```fsharp
let pages = Bluesky.paginateNotifications agent (Some 30L)
```

For all three, pass `None` as the page size to use the server's default.

## Consuming Pages

The paginators return `IAsyncEnumerable`, which you consume with `GetAsyncEnumerator`, `MoveNextAsync`, and `Current`.

### Process All Pages

```fsharp
let pages = Bluesky.paginateTimeline agent (Some 25L)
let enumerator = pages.GetAsyncEnumerator()
let mutable hasMore = true

while hasMore do
    let! moved = enumerator.MoveNextAsync()
    hasMore <- moved
    if hasMore then
        match enumerator.Current with
        | Ok page ->
            for item in page.Feed do
                printfn "@%s: %s"
                    (Handle.value item.Post.Author.Handle)
                    item.Post.Text
        | Error e ->
            printfn "Error: %A" e
            hasMore <- false
```

### Take a Fixed Number of Pages

```fsharp
let pages = Bluesky.paginateTimeline agent (Some 25L)
let enumerator = pages.GetAsyncEnumerator()
let mutable pageCount = 0

while pageCount < 3 do
    let! moved = enumerator.MoveNextAsync()
    if moved then
        match enumerator.Current with
        | Ok page ->
            pageCount <- pageCount + 1
            printfn "Page %d: %d items" pageCount page.Feed.Length
        | Error _ ->
            pageCount <- 3  // stop on error
    else
        pageCount <- 3  // no more pages
```

## Custom Pagination

For endpoints that don't have a pre-built paginator, use `Xrpc.paginate` directly. It takes five arguments:

1. The XRPC endpoint name (its `TypeId`)
2. Initial parameters (with `Cursor = None` to start from the beginning)
3. A function to extract the cursor from a response
4. A function to set the cursor on the parameters for the next request
5. The agent

Here's how to paginate an author's feed:

```fsharp
let pages =
    Xrpc.paginate<AppBskyFeed.GetAuthorFeed.Params, AppBskyFeed.GetAuthorFeed.Output>
        AppBskyFeed.GetAuthorFeed.TypeId
        { Actor = "my-handle.bsky.social"
          Cursor = None; Filter = None
          IncludePins = None; Limit = Some 25L }
        (fun o -> o.Cursor)
        (fun c p -> { p with Cursor = c })
        agent
```

And chat conversations (note the chat proxy agent):

```fsharp
let chatAgent = AtpAgent.withChatProxy agent

let pages =
    Xrpc.paginate<ChatBskyConvo.ListConvos.Params, ChatBskyConvo.ListConvos.Output>
        ChatBskyConvo.ListConvos.TypeId
        { Limit = Some 20L; Cursor = None; ReadState = None; Status = None }
        (fun o -> o.Cursor)
        (fun c p -> { p with Cursor = c })
        chatAgent
```

The pattern is always the same: provide the type parameters, the endpoint TypeId, initial params, a cursor getter, a cursor setter, and the agent. The pre-built paginators are just this pattern pre-wired for common endpoints.

## How Cursors Work

The AT Protocol uses opaque cursor strings for pagination. The server includes a `cursor` field in the response when more results are available. When the cursor is `None`, you've reached the end.

Each call to `MoveNextAsync` on the enumerator:

1. Sends a query with the current parameters (including the cursor from the previous response)
2. Yields the response as `Ok page` or `Error e`
3. Extracts the new cursor from the response for the next iteration
4. If the cursor is `None` or an error occurs, marks the sequence as finished

You never need to manage cursors yourself -- the paginator handles it all. Pages are fetched lazily, so you only pay for the pages you actually consume.

## Single-Page Queries

If you only need one page of results, skip pagination entirely and call the endpoint directly:

```fsharp
let! result =
    AppBskyFeed.GetTimeline.query agent
        { Algorithm = None; Cursor = None; Limit = Some 50L }
```

Pagination is most useful when you want to process a large or unbounded result set without loading everything into memory at once.
