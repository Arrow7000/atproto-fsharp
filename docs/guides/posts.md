---
title: Posts
category: Type Reference
categoryindex: 2
index: 5
description: Create, read, quote, reply to, search, and delete Bluesky posts with FSharp.ATProto
keywords: posts, create, reply, delete, thread, quote, search, bluesky
---

# Posts

Posts are the primary content type on Bluesky. FSharp.ATProto gives you convenience methods for the most common operations and generated XRPC wrappers when you need full control.

All examples use `taskResult {}` for concise error handling. See the [Error Handling guide](error-handling.html) for details.

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax
```

## Creating a Post

`Bluesky.post` creates a post with automatic rich text detection. Mentions, links, and hashtags are detected, resolved, and attached as facets automatically:

```fsharp
taskResult {
    let! postRef = Bluesky.post agent "Hello from F#! #atproto"
    printfn "Posted: %s" (AtUri.value postRef.Uri)
    return postRef
}
```

On success you get a `PostRef` containing the [AT-URI](../concepts.html) and CID of the new post. The `Uri` uniquely identifies the post. The `Cid` identifies the exact version. You need both to reply, like, or repost.

At the program boundary, handle the result:

```fsharp
match! Bluesky.post agent "Hello!" with
| Ok postRef -> printfn "Posted: %s" (AtUri.value postRef.Uri)
| Error err -> printfn "Failed: %A" err
```

Every example below returns `Task<Result<_, XrpcError>>` -- we show only the `taskResult` happy path for brevity.

## Reading Posts

`Bluesky.getPosts` fetches one or more posts by AT-URI and returns `TimelinePost` domain types:

```fsharp
taskResult {
    let! posts = Bluesky.getPosts agent [ postRef.Uri ]

    for post in posts do
        printfn "@%s: %s" post.Author.DisplayName post.Text
        printfn "  Likes: %d  Replies: %d" post.LikeCount post.ReplyCount
}
```

`TimelinePost` gives you direct access to `Uri`, `Cid`, `Author`, `Text`, `Facets`, `LikeCount`, `RepostCount`, `ReplyCount`, `QuoteCount`, `IndexedAt`, `IsLiked`, `IsReposted`, and `IsBookmarked`. No need to dig into record internals.

> **Power users**: If you need the raw `AppBskyFeed.Defs.PostView` from the generated types, use `AppBskyFeed.GetPosts.query` directly. `PostView` provides `.Text`, `.Facets`, and `.AsPost` extension properties for convenient access at the raw layer.

## Quote Posts

`Bluesky.quotePost` creates a post that quotes another. The quoted post appears as an embedded card:

```fsharp
taskResult {
    let! quoteRef = Bluesky.quotePost agent "This is a great take" originalPostRef
    printfn "Quote posted: %s" (AtUri.value quoteRef.Uri)
}
```

Like `Bluesky.post`, mentions, links, and hashtags in the text are auto-detected.

### Reading Quotes

`Bluesky.getQuotes` returns a paginated list of posts that quote a given post:

```fsharp
taskResult {
    let! page = Bluesky.getQuotes agent postRef.Uri None None

    for quote in page.Items do
        printfn "@%s quoted: %s" quote.Author.DisplayName quote.Text

    match page.Cursor with
    | Some _ -> printfn "More quotes available"
    | None -> printfn "No more quotes"
}
```

The first `int64 option` is the page size limit, the second `string option` is the pagination cursor. Pass `None` for both to use server defaults. See the [Pagination guide](pagination.html) for fetching additional pages.

## Replying to a Post

`Bluesky.replyTo` creates a reply with automatic rich text detection and automatic thread root resolution. Pass the post you are replying to -- the library figures out the thread root:

```fsharp
taskResult {
    let! replyRef = Bluesky.replyTo agent "Great post!" parentRef
    printfn "Replied: %s" (AtUri.value replyRef.Uri)
}
```

Under the hood, the library fetches the parent post to determine the thread root. If the parent is a top-level post, it becomes both parent and root. If the parent is itself a reply, the original thread root is extracted automatically.

### Explicit Parent and Root

If you already know both the parent and root `PostRef`s (for example, when building a thread yourself), use `Bluesky.replyWithKnownRoot` to skip the network fetch:

```fsharp
let! result = Bluesky.replyWithKnownRoot agent "I agree!" someoneElsesReply originalPost
```

The parameter order is: agent, text, parent, root.

## Threads

### The Simple Way

`Bluesky.getPostThreadView` returns a `ThreadPost option` -- `Some` for a normal accessible thread, `None` for deleted or blocked posts:

```fsharp
taskResult {
    let! threadOpt = Bluesky.getPostThreadView agent postRef.Uri (Some 6L) (Some 3L)

    match threadOpt with
    | Some thread ->
        printfn "Post: %s" thread.Post.Text

        for reply in thread.Replies do
            match reply with
            | ThreadNode.Post r ->
                printfn "  Reply by @%s: %s" r.Post.Author.DisplayName r.Post.Text
            | ThreadNode.NotFound uri ->
                printfn "  [deleted: %s]" (AtUri.value uri)
            | ThreadNode.Blocked uri ->
                printfn "  [blocked: %s]" (AtUri.value uri)
    | None ->
        printfn "Post is not available (deleted or blocked)"
}
```

The first `int64 option` is the reply depth. The second is the parent height (how many parent posts above the target). Pass `None` for either to use the server default.

### Full Pattern Matching

For cases where you need to distinguish between not-found and blocked at the top level, use `Bluesky.getPostThread` which returns a `ThreadNode`:

```fsharp
taskResult {
    let! node = Bluesky.getPostThread agent postRef.Uri (Some 6L) (Some 3L)

    match node with
    | ThreadNode.Post thread ->
        printfn "Post: %s" thread.Post.Text

        // Walk the parent chain
        let rec walkParents = function
            | Some (ThreadNode.Post p) ->
                printfn "  Parent: %s" p.Post.Text
                walkParents p.Parent
            | _ -> ()

        walkParents thread.Parent

    | ThreadNode.NotFound uri ->
        printfn "Post not found: %s" (AtUri.value uri)
    | ThreadNode.Blocked uri ->
        printfn "Post is blocked: %s" (AtUri.value uri)
}
```

> **Power users**: For access to the raw `AppBskyFeed.GetPostThread.OutputThreadUnion` (aliased as `ThreadResult`), call `AppBskyFeed.GetPostThread.query` directly.

## Searching Posts

`Bluesky.searchPosts` runs a full-text search and returns a paginated `Page<TimelinePost>`:

```fsharp
taskResult {
    let! page = Bluesky.searchPosts agent "F# atproto" (Some 10L) None

    for post in page.Items do
        printfn "@%s: %s" post.Author.DisplayName post.Text
}
```

> **Power users**: For advanced search filters (author, language, domain, date range, sort order), use `AppBskyFeed.SearchPosts.query` directly with the full parameter record.

## Deleting a Post

`Bluesky.deleteRecord` deletes any record by its AT-URI. Pass the URI from the `PostRef` you received when creating the post:

```fsharp
taskResult {
    do! Bluesky.deleteRecord agent postRef.Uri
    printfn "Deleted"
}
```

The same function works for any record you have created -- likes, reposts, follows, and blocks.

## Posting with Pre-Resolved Facets

If you have already resolved rich text facets yourself (or want to construct them manually), use `Bluesky.postWithFacets` to skip auto-detection:

```fsharp
task {
    let! facets = RichText.parse agent "Check @my-handle.bsky.social"
    // Modify facets here if needed...
    let! result = Bluesky.postWithFacets agent "Check @my-handle.bsky.social" facets

    match result with
    | Ok postRef -> printfn "Posted: %s" (AtUri.value postRef.Uri)
    | Error err -> printfn "Failed: %A" err
}
```

Note: `RichText.parse` returns a bare `Task` (it silently drops unresolvable mentions rather than failing), so this example uses `task {}` with a manual match on the `postWithFacets` result.

Pass an empty list for plain text with no facets. See the [Rich Text guide](rich-text.html) for details on the detection and resolution pipeline.
