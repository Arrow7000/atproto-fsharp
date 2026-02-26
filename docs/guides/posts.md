---
title: Posts
category: Guides
categoryindex: 1
index: 2
description: Create, read, reply to, and delete Bluesky posts with FSharp.ATProto
keywords: posts, create, reply, delete, thread, bluesky
---

# Posts

Posts are the primary content type on Bluesky. FSharp.ATProto provides convenience methods for creating and deleting posts, and generated XRPC wrappers for reading them.

All examples below assume you have an authenticated agent:

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax
```

## Creating a Post

`Bluesky.post` creates a post with automatic rich text detection. Mentions, links, and hashtags in the text are detected, resolved, and attached as facets:

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

On success, you get a `PostRef` containing the AT-URI and CID of the new post:

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
            let text = post.Record.GetProperty("text").GetString()
            let author = post.Author.Handle
            printfn "@%s: %s" (Handle.value author) text
            printfn "  Likes: %A  Replies: %A" post.LikeCount post.ReplyCount
    | Error err ->
        printfn "Failed: %A" err
}
```

`PostView.Record` is a `JsonElement` because the post record schema is open-ended. Use `GetProperty` to access fields like `text`, `createdAt`, or `reply`.

## Replying to a Post

`Bluesky.replyTo` creates a reply with automatic rich text detection and automatic thread root resolution. You only need the `PostRef` of the post you are replying to:

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

The library fetches the parent post to determine the thread root automatically. If the parent is a top-level post, it is used as both parent and root. If the parent is itself a reply, the original thread root is extracted.

If you have a `PostView` (from a query), construct a `PostRef` from it:

```fsharp
let toPostRef (pv: AppBskyFeed.Defs.PostView) : PostRef =
    { Uri = pv.Uri; Cid = pv.Cid }
```

### Explicit Parent and Root

If you already know both the parent and root `PostRef`s (for example, when building a thread yourself), use `Bluesky.replyWithKnownRoot` to skip the fetch:

```fsharp
let! result = Bluesky.replyWithKnownRoot agent "I agree!" someoneElsesReply originalPost
```

## Threads

Use `AppBskyFeed.GetPostThread.query` to fetch a full thread (the post, its parent chain, and its replies):

```fsharp
task {
    let uri =
        AtUri.parse "at://did:plc:xxx/app.bsky.feed.post/3k2la3b"
        |> Result.defaultWith failwith

    let! result =
        AppBskyFeed.GetPostThread.query agent
            { Uri = uri; Depth = Some 6L; ParentHeight = Some 3L }

    match result with
    | Ok output ->
        match output.Thread with
        | AppBskyFeed.GetPostThread.OutputThreadUnion.ThreadViewPost thread ->
            let text = thread.Post.Record.GetProperty("text").GetString()
            printfn "Post: %s" text

            // Walk the replies
            match thread.Replies with
            | Some replies ->
                for reply in replies do
                    match reply with
                    | AppBskyFeed.Defs.ThreadViewPostParentUnion.ThreadViewPost r ->
                        let replyText = r.Post.Record.GetProperty("text").GetString()
                        printfn "  Reply: %s" replyText
                    | AppBskyFeed.Defs.ThreadViewPostParentUnion.NotFoundPost _ ->
                        printfn "  [deleted]"
                    | AppBskyFeed.Defs.ThreadViewPostParentUnion.BlockedPost _ ->
                        printfn "  [blocked]"
                    | AppBskyFeed.Defs.ThreadViewPostParentUnion.Unknown _ ->
                        printfn "  [unknown type]"
            | None -> ()

        | AppBskyFeed.GetPostThread.OutputThreadUnion.NotFoundPost _ ->
            printfn "Post not found"
        | AppBskyFeed.GetPostThread.OutputThreadUnion.BlockedPost _ ->
            printfn "Post is blocked"
        | AppBskyFeed.GetPostThread.OutputThreadUnion.Unknown _ ->
            printfn "Unknown thread type"
    | Error err ->
        printfn "Failed: %A" err
}
```

The `Depth` parameter controls how many levels of replies to fetch (default varies by server). `ParentHeight` controls how many parent posts to include above the target post.

The thread union has four cases: `ThreadViewPost` for normal posts, `NotFoundPost` for deleted posts, `BlockedPost` for posts from users you have blocked or who have blocked you, and `Unknown` for future protocol additions.

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
              Sort = Some "latest"
              Tag = None
              Until = None
              Url = None }

    match result with
    | Ok output ->
        for post in output.Posts do
            let text = post.Record.GetProperty("text").GetString()
            printfn "@%s: %s" (Handle.value post.Author.Handle) text

        // Use output.Cursor for pagination
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

The same function works for undoing likes, reposts, follows, and blocks -- any record you created. Pass the AT-URI that was returned when you created the record:

```fsharp
// Unlike a post (likeUri was returned by Bluesky.like)
let! _ = Bluesky.deleteRecord agent likeUri

// Unfollow someone (followUri was returned by Bluesky.follow)
let! _ = Bluesky.deleteRecord agent followUri
```

## Posting with Pre-Resolved Facets

If you have already detected and resolved rich text facets yourself, use `Bluesky.postWith` to skip auto-detection:

```fsharp
task {
    let! facets = RichText.parse agent "Check @alice.bsky.social"
    // Modify facets here if needed...
    let! result = Bluesky.postWith agent "Check @alice.bsky.social" facets

    match result with
    | Ok postRef -> printfn "Posted: %s" (AtUri.value postRef.Uri)
    | Error err -> printfn "Failed: %A" err
}
```

Pass an empty list for plain text with no facets. See the [Rich Text guide](rich-text.html) for details on the detection and resolution pipeline.
