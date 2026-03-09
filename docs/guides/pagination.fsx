(**
---
title: Pagination
category: Advanced Guides
categoryindex: 3
index: 15
description: Iterate through cursor-based AT Protocol API results with IAsyncEnumerable
keywords: pagination, cursor, async, enumerable, timeline, feed, taskResult
---

# Pagination

Many AT Protocol endpoints return paginated results. FSharp.ATProto provides single-page convenience functions, pre-built paginators for full iteration, and a general `Xrpc.paginate` function for everything else.

All examples below use the `taskResult` computation expression, which short-circuits on errors automatically. See [Error Handling](error-handling.html) for details.

## Single-Page Queries

Most use cases only need one page. Use the convenience functions in the `Bluesky` module -- they accept an optional page size and an optional cursor, and return a single `Page<'T>`:
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
let myHandle = Unchecked.defaultof<Handle>
(***)

open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

taskResult {
    let! page = Bluesky.getTimeline agent (Some 50L) None
    for item in page.Items do
        printfn "@%s: %s" item.Post.Author.DisplayName item.Post.Text
}

(**
There are matching single-page functions for most read endpoints: `getFollowers`, `getFollows`, `getAuthorFeed`, `getNotifications`, `searchPosts`, `searchActors`, and more.

## Pre-Built Paginators

When you need to process an unbounded result set without loading everything into memory, use a paginator. Each returns `IAsyncEnumerable<Result<Page<'T>, XrpcError>>` -- pages arrive lazily, one at a time.

| Paginator | Signature |
|-----------|-----------|
| `Bluesky.paginateTimeline` | `AtpAgent -> int64 option -> IAsyncEnumerable<Result<Page<FeedItem>, XrpcError>>` |
| `Bluesky.paginateFollowers` | `AtpAgent -> actor -> int64 option -> IAsyncEnumerable<Result<Page<ProfileSummary>, XrpcError>>` |
| `Bluesky.paginateNotifications` | `AtpAgent -> int64 option -> IAsyncEnumerable<Result<Page<Notification>, XrpcError>>` |
| `Bluesky.paginateBlocks` | `AtpAgent -> int64 option -> IAsyncEnumerable<Result<Page<ProfileSummary>, XrpcError>>` |
| `Bluesky.paginateMutes` | `AtpAgent -> int64 option -> IAsyncEnumerable<Result<Page<ProfileSummary>, XrpcError>>` |
| `Bluesky.paginateFeed` | `AtpAgent -> AtUri -> int64 option -> IAsyncEnumerable<Result<Page<FeedItem>, XrpcError>>` |
| `Bluesky.paginateListFeed` | `AtpAgent -> AtUri -> int64 option -> IAsyncEnumerable<Result<Page<FeedItem>, XrpcError>>` |

`paginateFollowers` accepts a `ProfileSummary`, `Profile`, `Handle`, or `Did` as the actor parameter -- pass entities directly instead of extracting identifiers. Pass `None` as the page size to use the server's default.

## Consuming Pages

Here is how to iterate through all pages of your home timeline:
*)

task {
    let pages = Bluesky.paginateTimeline agent (Some 25L)
    let enumerator = pages.GetAsyncEnumerator()

    let mutable hasMore = true
    while hasMore do
        let! moved = enumerator.MoveNextAsync()
        if not moved then
            hasMore <- false
        else
            match enumerator.Current with
            | Ok page ->
                for item in page.Items do
                    printfn "@%s: %s" item.Post.Author.DisplayName item.Post.Text
            | Error err ->
                printfn "Error: %A" err
                hasMore <- false
}

(**
To take a fixed number of pages, add a counter:
*)

task {
    let pages = Bluesky.paginateTimeline agent (Some 25L)
    let enumerator = pages.GetAsyncEnumerator()

    let mutable pageCount = 0
    let mutable hasMore = true
    while hasMore && pageCount < 3 do
        let! moved = enumerator.MoveNextAsync()
        if not moved then
            hasMore <- false
        else
            match enumerator.Current with
            | Ok page ->
                pageCount <- pageCount + 1
                printfn "Page %d: %d items" pageCount page.Items.Length
            | Error _ ->
                hasMore <- false
}

(**
**A note on `IAsyncEnumerable`.** This is a .NET interface, not a native F# type, so consuming it requires manual enumerator management as shown above. If you prefer a more functional style, consider the [FSharp.Control.TaskSeq](https://github.com/fsprojects/FSharp.Control.TaskSeq) NuGet package, which provides `taskSeq {}` computation expressions for async sequences.

## Custom Pagination

For endpoints without a pre-built paginator, use `Xrpc.paginate` directly. It takes five arguments: the endpoint's `TypeId`, initial parameters (with `Cursor = None`), a cursor extractor, a cursor setter, and the agent.
*)

let pages =
    Xrpc.paginate<AppBskyFeed.GetAuthorFeed.Params, AppBskyFeed.GetAuthorFeed.Output>
        AppBskyFeed.GetAuthorFeed.TypeId
        { Actor = Handle.value myHandle
          Cursor = None; Filter = None
          IncludePins = None; Limit = Some 25L }
        (fun o -> o.Cursor)
        (fun c p -> { p with Cursor = c })
        agent

(**
Chat endpoints need the chat proxy agent:
*)

let chatAgent = AtpAgent.withChatProxy agent

let chatPages =
    Xrpc.paginate<ChatBskyConvo.ListConvos.Params, ChatBskyConvo.ListConvos.Output>
        ChatBskyConvo.ListConvos.TypeId
        { Limit = Some 20L; Cursor = None; ReadState = None; Status = None }
        (fun o -> o.Cursor)
        (fun c p -> { p with Cursor = c })
        chatAgent

(**
The pre-built paginators are just this pattern pre-wired for common endpoints.

## How Cursors Work

The AT Protocol uses opaque cursor strings for pagination. The server includes a `cursor` field in the response when more results are available. When the cursor is `None`, you have reached the end.

Each call to `MoveNextAsync` on the enumerator:

1. Sends a query with the current parameters (including the cursor from the previous response)
2. Yields the response as `Ok page` or `Error err`
3. Extracts the new cursor from the response for the next iteration
4. If the cursor is `None` or an error occurs, marks the sequence as finished

You never need to manage cursors yourself -- the paginator handles it all. Pages are fetched lazily, so you only pay for the pages you actually consume.
*)
