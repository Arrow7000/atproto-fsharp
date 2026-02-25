---
title: Pagination
category: Guides
categoryindex: 2
index: 5
description: Iterate through cursor-based AT Protocol API results with IAsyncEnumerable
keywords: pagination, cursor, async, enumerable, timeline, feed
---

# Pagination

Many AT Protocol endpoints return paginated results with a cursor. FSharp.ATProto provides `Xrpc.paginate` to iterate through all pages as an `IAsyncEnumerable<Result<'O, XrpcError>>`.

## Basic Usage

`Xrpc.paginate` takes five arguments:

1. The XRPC endpoint name (its `TypeId`)
2. The initial parameters (with `Cursor = None` to start from the beginning)
3. A function to extract the cursor from a response
4. A function to set the cursor on the parameters for the next request
5. The agent

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

let pages =
    Xrpc.paginate<AppBskyFeed.GetTimeline.Params, AppBskyFeed.GetTimeline.Output>
        AppBskyFeed.GetTimeline.TypeId
        { Algorithm = None; Cursor = None; Limit = Some 25L }
        (fun output -> output.Cursor)
        (fun cursor params -> { params with Cursor = cursor })
        agent
```

The returned `IAsyncEnumerable` yields one `Result` per page. Pages arrive lazily -- the next page is only fetched when you advance the enumerator. Iteration stops when the server returns no cursor (end of results) or when an error occurs.

## Consuming Pages

### Process All Pages

```fsharp
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
                    item.Post.Author.Handle
                    (item.Post.Record.GetProperty("text").GetString())
        | Error e ->
            printfn "Error: %A" e.Message
            hasMore <- false
```

### Take a Fixed Number of Pages

```fsharp
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

## How Cursors Work

The AT Protocol uses opaque cursor strings for pagination. The server includes a `cursor` field in the response when more results are available. When the cursor is `None`, you've reached the end.

Each call to `MoveNextAsync` on the enumerator:

1. Sends a query with the current parameters (including the cursor from the previous response)
2. Yields the response as `Ok page` or `Error e`
3. Extracts the new cursor from the response for the next iteration
4. If the cursor is `None` or an error occurs, marks the sequence as finished

## Other Paginated Endpoints

The same pattern works with any cursor-based endpoint. Here are some common ones:

### Notifications

```fsharp
let pages =
    Xrpc.paginate<AppBskyNotification.ListNotifications.Params,
                   AppBskyNotification.ListNotifications.Output>
        AppBskyNotification.ListNotifications.TypeId
        { Cursor = None; Limit = Some 50L; Priority = None
          Reasons = None; SeenAt = None }
        (fun o -> o.Cursor)
        (fun c p -> { p with Cursor = c })
        agent
```

### Author Feed

```fsharp
let pages =
    Xrpc.paginate<AppBskyFeed.GetAuthorFeed.Params,
                   AppBskyFeed.GetAuthorFeed.Output>
        AppBskyFeed.GetAuthorFeed.TypeId
        { Actor = "alice.bsky.social"
          Cursor = None; Filter = None
          IncludePins = None; Limit = Some 25L }
        (fun o -> o.Cursor)
        (fun c p -> { p with Cursor = c })
        agent
```

### Chat Conversations

```fsharp
let chatAgent = AtpAgent.withChatProxy agent

let pages =
    Xrpc.paginate<ChatBskyConvo.ListConvos.Params,
                   ChatBskyConvo.ListConvos.Output>
        ChatBskyConvo.ListConvos.TypeId
        { Limit = Some 20L; Cursor = None; ReadState = None; Status = None }
        (fun o -> o.Cursor)
        (fun c p -> { p with Cursor = c })
        chatAgent
```

## Single-Page Queries

If you only need one page, you don't need `paginate` at all. Just call the endpoint directly:

```fsharp
let! result =
    AppBskyFeed.GetTimeline.query agent
        { Algorithm = None; Cursor = None; Limit = Some 50L }
```

`paginate` is most useful when you want to process an unbounded or large result set without loading everything into memory at once.
