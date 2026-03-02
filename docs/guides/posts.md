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

Use the generated `AppBskyFeed.GetPosts.query` to fetch one or more posts by AT-URI:

```fsharp
task {
    let uri =
        AtUri.parse "at://did:plc:xxx/app.bsky.feed.post/3k2la3b"
        |> Result.defaultWith failwith

    let! result = AppBskyFeed.GetPosts.query agent { Uris = [ uri ] }

    match result with
    | Ok output ->
        for post in output.Posts do
            let author = Handle.value post.Author.Handle
            printfn "@%s: %s" author post.Text
            printfn "  Likes: %A  Replies: %A" post.LikeCount post.ReplyCount
    | Error err ->
        printfn "Failed: %A" err
}
```

`PostView` provides three extension properties for working with post content:

| Extension | Type | Description |
|-----------|------|-------------|
| `.Text` | `string` | The post text (empty string if not a post record) |
| `.Facets` | `Facet list` | Rich text facets -- mentions, links, hashtags (empty list if none) |
| `.AsPost` | `Post option` | Full deserialized `AppBskyFeed.Post.Post` record, if the record type matches |

These extensions are the recommended way to access post content. No need to dig into `Record` manually.

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

`Bluesky.getPostThreadView` returns just the `ThreadViewPost option` -- `Some` for a normal accessible thread, `None` for deleted or blocked posts:

```fsharp
task {
    let uri =
        AtUri.parse "at://did:plc:xxx/app.bsky.feed.post/3k2la3b"
        |> Result.defaultWith failwith

    let! result = Bluesky.getPostThreadView agent uri (Some 6L) (Some 3L)

    match result with
    | Ok (Some thread) ->
        printfn "Post: %s" thread.Post.Text

        match thread.Replies with
        | Some replies ->
            for reply in replies do
                match reply with
                | AppBskyFeed.Defs.ThreadViewPostParentUnion.ThreadViewPost r ->
                    printfn "  Reply: %s" r.Post.Text
                | _ -> ()
        | None -> ()
    | Ok None ->
        printfn "Post is not available (deleted or blocked)"
    | Error err ->
        printfn "Failed: %A" err
}
```

The first `int64 option` is the reply depth (how many levels of replies to fetch). The second is the parent height (how many parent posts to include above the target post). Pass `None` for either to use the server default.

### Full Pattern Matching

For cases where you need to distinguish between not-found and blocked posts, use `Bluesky.getPostThread` with the `ThreadResult` type alias:

```fsharp
task {
    let! result = Bluesky.getPostThread agent uri (Some 6L) (Some 3L)

    match result with
    | Ok output ->
        match output.Thread with
        | ThreadResult.ThreadViewPost thread ->
            printfn "Post: %s" thread.Post.Text
        | ThreadResult.NotFoundPost _ ->
            printfn "Post not found"
        | ThreadResult.BlockedPost _ ->
            printfn "Post is blocked"
        | ThreadResult.Unknown _ ->
            printfn "Unknown thread type (future protocol addition)"
    | Error err ->
        printfn "Failed: %A" err
}
```

`ThreadResult` is a type alias for `AppBskyFeed.GetPostThread.OutputThreadUnion`, giving you shorter pattern match arms.

## Searching Posts

`AppBskyFeed.SearchPosts.query` runs a full-text search over indexed posts:

```fsharp
task {
    let! result =
        AppBskyFeed.SearchPosts.query agent
            { Q = "F# atproto"
              Author = None
              Cursor = None
              Domain = None
              Lang = None
              Limit = Some 10L
              Mentions = None
              Since = None
              Sort = Some AppBskyFeed.SearchPosts.Latest
              Tag = None
              Until = None
              Url = None }

    match result with
    | Ok output ->
        for post in output.Posts do
            printfn "@%s: %s" (Handle.value post.Author.Handle) post.Text

        match output.Cursor with
        | Some cursor -> printfn "More results available (cursor: %s)" cursor
        | None -> printfn "No more results"
    | Error err ->
        printfn "Failed: %A" err
}
```

The `Sort` parameter accepts `"top"` (relevance) or `"latest"` (chronological). See the [Pagination guide](pagination.html) for fetching additional pages with the cursor.

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
