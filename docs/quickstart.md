---
title: Quickstart
category: Guides
categoryindex: 1
index: 1
description: Get up and running with FSharp.ATProto in 5 minutes
keywords: quickstart, tutorial, getting started, fsharp, atproto
---

# Quickstart

Get from zero to posting on Bluesky in under 5 minutes.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- A Bluesky account with an [App Password](https://bsky.app/settings/app-passwords) (do not use your main password)

## Create a Project

```bash
dotnet new console -lang F# -n MyBskyBot
cd MyBskyBot
dotnet add package FSharp.ATProto.Bluesky
```

## Log In

Replace the contents of `Program.fs`:

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

[<EntryPoint>]
let main _ =
    task {
        let agent = AtpAgent.create "https://bsky.social"

        let! loginResult =
            AtpAgent.login "your-handle.bsky.social" "your-app-password" agent

        match loginResult with
        | Ok session ->
            printfn "Logged in as %s (%s)" session.Handle session.Did
        | Error e ->
            printfn "Login failed: %A" e
            return 1

        return 0
    }
    |> fun t -> t.GetAwaiter().GetResult()
```

Run it:

```bash
dotnet run
# Logged in as your-handle.bsky.social (did:plc:...)
```

## Make Your First Post

`Bluesky.post` automatically detects @mentions, links, and #hashtags in your text and creates the correct rich text facets:

```fsharp
open FSharp.ATProto.Syntax

let! postResult = Bluesky.post agent "Hello world from F#! #atproto"

match postResult with
| Ok post -> printfn "Posted! URI: %s" (AtUri.value post.Uri)
| Error e -> printfn "Post failed: %A" e
```

Every `@handle.domain` in the text is resolved to a DID via the API. Links and hashtags are detected by pattern. You don't need to compute byte offsets or construct facet objects yourself.

## Read Your Timeline

Use the generated XRPC wrapper for `app.bsky.feed.getTimeline`:

```fsharp
let! timelineResult =
    AppBskyFeed.GetTimeline.query agent
        { Algorithm = None; Cursor = None; Limit = Some 10L }

match timelineResult with
| Ok timeline ->
    for item in timeline.Feed do
        let author = Handle.value item.Post.Author.Handle
        let text =
            item.Post.Record.GetProperty("text").GetString()
        printfn "@%s: %s" author text
| Error e ->
    printfn "Timeline failed: %A" e
```

All 237 XRPC endpoints on Bluesky are available as typed wrappers under their Lexicon namespace (`AppBskyFeed`, `AppBskyActor`, `ComAtprotoRepo`, etc.). Query endpoints use `.query`, procedure endpoints use `.call`.

## Like a Post

```fsharp
// Like the first post from the timeline
match timelineResult with
| Ok timeline when timeline.Feed.Length > 0 ->
    let first = timeline.Feed.[0].Post
    let! likeResult = Bluesky.like agent first.Uri first.Cid

    match likeResult with
    | Ok likeUri -> printfn "Liked! %s" (AtUri.value likeUri)
    | Error e -> printfn "Like failed: %A" e
| _ -> ()
```

## Reply to a Post

`Bluesky.replyTo` fetches the parent post to resolve the thread root automatically. You only need the `PostRef` of the post you are replying to:

```fsharp
let parentRef : PostRef = { Uri = first.Uri; Cid = first.Cid }
let! replyResult =
    Bluesky.replyTo agent "Great post!" parentRef

match replyResult with
| Ok r -> printfn "Replied: %s" (AtUri.value r.Uri)
| Error e -> printfn "Reply failed: %A" e
```

## Post with Images

`Bluesky.postWithImages` handles blob uploading and embed construction. Pass a list of `ImageUpload` records:

```fsharp
let imageBytes = System.IO.File.ReadAllBytes("photo.jpg")

let! result =
    Bluesky.postWithImages agent "Check out this photo!" [
        { Data = imageBytes; MimeType = "image/jpeg"; AltText = "A sunny landscape" }
    ]
```

Up to 4 images per post.

## Complete Example

Putting it all together:

```fsharp
open FSharp.ATProto.Syntax
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

[<EntryPoint>]
let main _ =
    task {
        let agent = AtpAgent.create "https://bsky.social"
        let! _ = AtpAgent.login "your-handle.bsky.social" "your-app-password" agent

        // Post
        let! postResult = Bluesky.post agent "Hello from F#! #fsharp #atproto"
        let post =
            match postResult with
            | Ok p -> printfn "Posted: %s" (AtUri.value p.Uri); p
            | Error e -> failwithf "Post failed: %A" e

        // Read timeline
        let! tl =
            AppBskyFeed.GetTimeline.query agent
                { Algorithm = None; Cursor = None; Limit = Some 5L }

        match tl with
        | Ok t ->
            printfn "Timeline (%d posts):" t.Feed.Length
            for item in t.Feed do
                printfn "  @%s" (Handle.value item.Post.Author.Handle)
        | Error e ->
            printfn "Timeline: %A" e

        // Like our own post
        let! _ = Bluesky.like agent post.Uri post.Cid

        // Clean up
        let! _ = Bluesky.deleteRecord agent post.Uri

        printfn "Done!"
        return 0
    }
    |> fun t -> t.GetAwaiter().GetResult()
```

## What's Next

- [Rich Text Guide](guides/rich-text.html) -- finer control over mention/link/hashtag detection
- [Posts Guide](guides/posts.html) -- reading posts, threads, and search
- [Social Actions Guide](guides/social.html) -- like, repost, follow, block
- [Feeds Guide](guides/feeds.html) -- timelines and custom feeds
- [Media Guide](guides/media.html) -- image uploads
- [Chat / DM Guide](guides/chat.html) -- direct messaging
- [Identity Guide](guides/identity.html) -- resolve handles and DIDs
- [Profiles Guide](guides/profiles.html) -- fetch user profiles
- [Pagination Guide](guides/pagination.html) -- iterate through large result sets
- [API Reference](reference/index.html) -- full generated API documentation
