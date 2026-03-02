---
title: Posts
category: Guides
categoryindex: 1
index: 2
description: Create, read, quote, reply to, search, and delete Bluesky posts with FSharp.ATProto
keywords: posts, create, reply, delete, thread, quote, search, bluesky
---

# Posts

Posts are the primary content type on Bluesky. FSharp.ATProto gives you convenience methods for the most common operations and generated XRPC wrappers when you need full control.

All examples below assume you have an authenticated `AtpAgent` and these namespaces open:

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax
```

## Creating a Post

`Bluesky.post` creates a post with automatic rich text detection. Mentions (`@handle`), links, and hashtags in the text are detected, resolved, and attached as facets -- you do not need to handle any of that yourself:

```fsharp
task {
    let! result = Bluesky.post agent "Hello from F#! #atproto"

    match result with
    | Ok postRef ->
        printfn "Posted: %s" (AtUri.value postRef.Uri)
    | Error err ->
        printfn "Failed: %A" err
}
```

On success you get a `PostRef` containing the AT-URI and CID of the new post:

```fsharp
type PostRef = { Uri: AtUri; Cid: Cid }
```

The `Uri` uniquely identifies the post (e.g. `at://did:plc:xxx/app.bsky.feed.post/3k2la3b`). The `Cid` identifies the exact version. You need both to reply, like, or repost.

## Reading Posts

`Bluesky.getPosts` fetches one or more posts by AT-URI and returns them as `TimelinePost` domain types:

```fsharp
task {
    let uri =
        AtUri.parse "at://did:plc:xxx/app.bsky.feed.post/3k2la3b"
        |> Result.defaultWith failwith

    let! result = Bluesky.getPosts agent [ uri ]

    match result with
    | Ok posts ->
        for post in posts do
            printfn "@%s: %s" post.Author.DisplayName post.Text
            printfn "  Likes: %d  Replies: %d" post.LikeCount post.ReplyCount
    | Error err ->
        printfn "Failed: %A" err
}
```

`TimelinePost` gives you direct access to all fields -- `Uri`, `Cid`, `Author` (a `ProfileSummary`), `Text`, `Facets`, `LikeCount`, `RepostCount`, `ReplyCount`, `QuoteCount`, `IndexedAt`, `IsLiked`, `IsReposted`, and `IsBookmarked`. No need to dig into record internals.

> **Power users**: If you need the raw `AppBskyFeed.Defs.PostView` from the generated types, use `AppBskyFeed.GetPosts.query` directly. `PostView` provides `.Text`, `.Facets`, and `.AsPost` extension properties for convenient access to post content at the raw layer.

## Quote Posts

`Bluesky.quotePost` creates a post that quotes another post. The quoted post appears as an embedded card below your text:

```fsharp
task {
    let! result = Bluesky.quotePost agent "This is a great take" originalPostRef

    match result with
    | Ok quoteRef ->
        printfn "Quote posted: %s" (AtUri.value quoteRef.Uri)
    | Error err ->
        printfn "Failed: %A" err
}
```

Like `Bluesky.post`, mentions, links, and hashtags in the text are auto-detected and resolved. The `originalPostRef` is the `PostRef` of the post you want to quote.

### Reading Quotes

`Bluesky.getQuotes` returns a paginated list of posts that quote a given post:

```fsharp
task {
    let! result = Bluesky.getQuotes agent postRef.Uri None None

    match result with
    | Ok page ->
        for quote in page.Items do
            printfn "@%s quoted: %s" quote.Author.DisplayName quote.Text

        match page.Cursor with
        | Some cursor -> printfn "More quotes available"
        | None -> printfn "No more quotes"
    | Error err ->
        printfn "Failed: %A" err
}
```

The first `int64 option` is the page size limit and the second `string option` is the pagination cursor. Pass `None` for both to use server defaults. See the [Pagination guide](pagination.html) for fetching additional pages.

## Replying to a Post

`Bluesky.replyTo` creates a reply with automatic rich text detection and automatic thread root resolution. You only need the `PostRef` of the post you are replying to -- the library figures out the thread root for you:

```fsharp
task {
    let! result = Bluesky.replyTo agent "Great post!" parentRef

    match result with
    | Ok replyRef ->
        printfn "Replied: %s" (AtUri.value replyRef.Uri)
    | Error err ->
        printfn "Failed: %A" err
}
```

Under the hood, the library fetches the parent post to determine the thread root. If the parent is a top-level post, it becomes both parent and root. If the parent is itself a reply, the original thread root is extracted automatically.

If you have a `PostView` (from a query result), you can build a `PostRef` from it:

```fsharp
let toPostRef (pv: AppBskyFeed.Defs.PostView) : PostRef =
    { Uri = pv.Uri; Cid = pv.Cid }
```

### Explicit Parent and Root

If you already know both the parent and root `PostRef`s (for example, when building a thread yourself), use `Bluesky.replyWithKnownRoot` to skip the network fetch:

```fsharp
let! result = Bluesky.replyWithKnownRoot agent "I agree!" someoneElsesReply originalPost
```

The parameter order is: agent, text, parent, root.

## Threads

### The Simple Way

`Bluesky.getPostThreadView` returns a `ThreadPost option` -- `Some` for a normal accessible thread, `None` for deleted or blocked posts. `ThreadPost` is a domain type with `Post` (a `TimelinePost`), `Parent`, and `Replies`:

```fsharp
task {
    let uri =
        AtUri.parse "at://did:plc:xxx/app.bsky.feed.post/3k2la3b"
        |> Result.defaultWith failwith

    let! result = Bluesky.getPostThreadView agent uri (Some 6L) (Some 3L)

    match result with
    | Ok (Some thread) ->
        printfn "Post: %s" thread.Post.Text

        for reply in thread.Replies do
            match reply with
            | ThreadNode.Post r ->
                printfn "  Reply by @%s: %s" r.Post.Author.DisplayName r.Post.Text
            | ThreadNode.NotFound uri ->
                printfn "  [deleted: %s]" (AtUri.value uri)
            | ThreadNode.Blocked uri ->
                printfn "  [blocked: %s]" (AtUri.value uri)
    | Ok None ->
        printfn "Post is not available (deleted or blocked)"
    | Error err ->
        printfn "Failed: %A" err
}
```

The first `int64 option` is the reply depth (how many levels of replies to fetch). The second is the parent height (how many parent posts to include above the target post). Pass `None` for either to use the server default.

### Full Pattern Matching

For cases where you need to distinguish between not-found and blocked posts at the top level, use `Bluesky.getPostThread` which returns a `ThreadNode`:

```fsharp
task {
    let! result = Bluesky.getPostThread agent uri (Some 6L) (Some 3L)

    match result with
    | Ok (ThreadNode.Post thread) ->
        printfn "Post: %s" thread.Post.Text

        // Walk the parent chain
        let mutable current = thread.Parent
        while current.IsSome do
            match current.Value with
            | ThreadNode.Post p ->
                printfn "  Parent: %s" p.Post.Text
                current <- p.Parent
            | _ ->
                current <- None
    | Ok (ThreadNode.NotFound uri) ->
        printfn "Post not found: %s" (AtUri.value uri)
    | Ok (ThreadNode.Blocked uri) ->
        printfn "Post is blocked: %s" (AtUri.value uri)
    | Error err ->
        printfn "Failed: %A" err
}
```

`ThreadNode` is a discriminated union with three cases: `Post` (containing a `ThreadPost` with nested `Parent` and `Replies`), `NotFound`, and `Blocked`. This gives you full control over how to handle each case in the thread tree.

> **Power users**: For access to the raw `AppBskyFeed.GetPostThread.OutputThreadUnion` (aliased as `ThreadResult`), call `AppBskyFeed.GetPostThread.query` directly.

## Searching Posts

`Bluesky.searchPosts` runs a full-text search over indexed posts and returns a paginated `Page<TimelinePost>`:

```fsharp
task {
    let! result = Bluesky.searchPosts agent "F# atproto" (Some 10L) None

    match result with
    | Ok page ->
        for post in page.Items do
            printfn "@%s: %s" post.Author.DisplayName post.Text

        match page.Cursor with
        | Some cursor -> printfn "More results available"
        | None -> printfn "No more results"
    | Error err ->
        printfn "Failed: %A" err
}
```

The first `int64 option` is the page size limit and the second `string option` is the pagination cursor. Pass `None` for both to use server defaults. See the [Pagination guide](pagination.html) for fetching additional pages with the cursor.

> **Power users**: For advanced search filters (author, language, domain, date range, sort order), use `AppBskyFeed.SearchPosts.query` directly with the full parameter record.

## Deleting a Post

`Bluesky.deleteRecord` deletes any record by its AT-URI. Pass the URI from the `PostRef` you received when creating the post:

```fsharp
task {
    let! result = Bluesky.deleteRecord agent postRef.Uri

    match result with
    | Ok () -> printfn "Deleted"
    | Error err -> printfn "Failed: %A" err
}
```

The same function works for any record you have created -- likes, reposts, follows, and blocks. Pass the AT-URI that was returned when you created the record.

## Posting with Pre-Resolved Facets

If you have already detected and resolved rich text facets yourself (or want to construct them manually), use `Bluesky.postWithFacets` to skip auto-detection:

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

Pass an empty list for plain text with no facets. See the [Rich Text guide](rich-text.html) for details on the detection and resolution pipeline.
