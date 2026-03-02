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
open FSharp.ATProto.Syntax
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

[<EntryPoint>]
let main _ =
    let result =
        taskResult {
            let! agent = Bluesky.login "https://bsky.social" "your-handle.bsky.social" "your-app-password"
            printfn "Logged in as %s" (Handle.value agent.Session.Value.Handle)
            return 0
        }

    result.Result
    |> function
        | Ok code -> code
        | Error e -> printfn "Error: %A" e; 1
```

Run it:

```bash
dotnet run
# Logged in as your-handle.bsky.social
```

`Bluesky.login` creates the agent, authenticates, and returns it ready to use -- all in one call. If anything fails, you get an `Error` with details. No exceptions.

## Make Your First Post

`Bluesky.post` automatically detects @mentions, links, and #hashtags in your text and creates the correct rich text facets:

```fsharp
let! post = Bluesky.post agent "Hello world from F#! #atproto"
printfn "Posted! URI: %s" (AtUri.value post.Uri)
```

Every `@handle.domain` in the text is resolved to a DID via the API. Links and hashtags are detected by pattern. You never need to compute byte offsets or construct facet objects yourself. The result is a `PostRef` containing the AT-URI and CID of the new post.

## Read Your Timeline

`Bluesky.getTimeline` wraps the `app.bsky.feed.getTimeline` endpoint with a simpler signature:

```fsharp
open FSharp.ATProto.Syntax

let! timeline = Bluesky.getTimeline agent (Some 10L) None

for item in timeline.Feed do
    let author = Handle.value item.Post.Author.Handle
    let text = item.Post.Text
    printfn "@%s: %s" author text
```

The `PostView.Text` extension property gives you the post text directly -- no need to dig into raw JSON.

## Like a Post

Construct a `PostRef` from any `PostView`, then pass it to `Bluesky.like`:

```fsharp
let firstPost = timeline.Feed.[0].Post
let postRef = { PostRef.Uri = firstPost.Uri; Cid = firstPost.Cid }

let! like = Bluesky.like agent postRef
printfn "Liked! %s" (AtUri.value like.Uri)
```

The result is a `LikeRef` you can hold on to. To undo the like later, just pass it to `Bluesky.undo`:

```fsharp
let! undoResult = Bluesky.undo agent like
// undoResult is Undone or WasNotPresent
```

## Reply to a Post

`Bluesky.replyTo` fetches the parent post to resolve the thread root automatically. You only need the text and the `PostRef` of the post you are replying to:

```fsharp
let! reply = Bluesky.replyTo agent "Great post!" postRef
printfn "Replied: %s" (AtUri.value reply.Uri)
```

## Post with Images

`Bluesky.postWithImages` handles blob uploading and embed construction. Pass a list of `ImageUpload` records:

```fsharp
let imageBytes = System.IO.File.ReadAllBytes("photo.jpg")

let! post =
    Bluesky.postWithImages agent "Check out this photo!" [
        { Data = imageBytes; MimeType = Jpeg; AltText = "A sunny landscape" }
    ]
```

`MimeType` is an `ImageMime` discriminated union with cases `Jpeg`, `Png`, `Gif`, `Webp`, and `Custom of string`. Up to 4 images per post.

## Complete Example

Here is a full program that ties everything together using the `taskResult` computation expression. Every `let!` binding short-circuits to the `Error` case if something fails -- no nested match trees needed:

```fsharp
open FSharp.ATProto.Syntax
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

[<EntryPoint>]
let main _ =
    let result =
        taskResult {
            // Log in
            let! agent = Bluesky.login "https://bsky.social" "your-handle.bsky.social" "your-app-password"
            printfn "Logged in!"

            // Create a post with auto-detected rich text
            let! post = Bluesky.post agent "Hello from F#! #fsharp #atproto"
            printfn "Posted: %s" (AtUri.value post.Uri)

            // Read timeline
            let! timeline = Bluesky.getTimeline agent (Some 5L) None
            printfn "Timeline (%d posts):" timeline.Feed.Length

            for item in timeline.Feed do
                printfn "  @%s: %s" (Handle.value item.Post.Author.Handle) item.Post.Text

            // Like the first post from the timeline
            if timeline.Feed.Length > 0 then
                let first = timeline.Feed.[0].Post
                let firstRef = { PostRef.Uri = first.Uri; Cid = first.Cid }
                let! like = Bluesky.like agent firstRef
                printfn "Liked: %s" (AtUri.value like.Uri)

                // Reply to it
                let! reply = Bluesky.replyTo agent "Nice post!" firstRef
                printfn "Replied: %s" (AtUri.value reply.Uri)

                // Clean up: undo the like and delete the reply
                let! _ = Bluesky.undo agent like
                let! _ = Bluesky.deleteRecord agent reply.Uri
                printfn "Cleaned up."

            // Delete our original post
            let! _ = Bluesky.deleteRecord agent post.Uri

            printfn "Done!"
            return 0
        }

    result.Result
    |> function
        | Ok code -> code
        | Error e -> printfn "Error: %A" e; 1
```

## What's Next

- [Posts Guide](guides/posts.html) -- reading posts, threads, and search
- [Social Actions Guide](guides/social.html) -- like, repost, follow, block, and undo
- [Rich Text Guide](guides/rich-text.html) -- finer control over mention/link/hashtag detection
- [Feeds Guide](guides/feeds.html) -- timelines and custom feeds
- [Media Guide](guides/media.html) -- image uploads
- [Profiles Guide](guides/profiles.html) -- fetch user profiles
- [Chat / DM Guide](guides/chat.html) -- direct messaging
- [Identity Guide](guides/identity.html) -- resolve handles and DIDs
- [Pagination Guide](guides/pagination.html) -- iterate through large result sets
