(**
---
title: Quickstart
category: Getting Started
categoryindex: 1
index: 1
description: Get up and running with FSharp.ATProto in 5 minutes
keywords: quickstart, tutorial, getting started, fsharp, atproto, bluesky
---
*)

(*** hide ***)
#nowarn "20"
#r "../src/FSharp.ATProto.Syntax/bin/Release/net10.0/FSharp.ATProto.Syntax.dll"
#r "../src/FSharp.ATProto.Core/bin/Release/net10.0/FSharp.ATProto.Core.dll"
#r "../src/FSharp.ATProto.Bluesky/bin/Release/net10.0/FSharp.ATProto.Bluesky.dll"

open FSharp.ATProto.Syntax
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

let agent = Unchecked.defaultof<AtpAgent>
let post = Unchecked.defaultof<PostRef>
let timeline = Unchecked.defaultof<Page<FeedItem>>
let firstPost = Unchecked.defaultof<TimelinePost>
let likeRef = Unchecked.defaultof<LikeRef>
(***)

(**
# Quickstart

Get from zero to posting on Bluesky in under 5 minutes.

> Code samples use `taskResult {}`, a computation expression that chains async operations returning `Result`. See [Error Handling](guides/error-handling.html) for details.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- A Bluesky account with an [App Password](https://bsky.app/settings/app-passwords) (do not use your main password)

## Create a Project

```bash
dotnet new console -lang F# -n MyBskyBot
cd MyBskyBot
```

Add the NuGet package:

```bash
dotnet add package FSharp.ATProto.Bluesky
```

## Log In

Replace the contents of `Program.fs`:
*)

open FSharp.ATProto.Syntax
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

let loginExample () =
    let result =
        taskResult {
            let! agent = Bluesky.login "https://bsky.social" "your-handle.bsky.social" "your-app-password"
            printfn "Logged in!"
            return 0
        }

    result.Result
    |> function
        | Ok code -> code
        | Error e -> printfn "Error: %A" e; 1

(**
Run it:

```bash
dotnet run
# Logged in!
```

`Bluesky.login` creates the agent, authenticates, and returns it ready to use -- all in one call. If anything fails, you get an `Error` with details. No exceptions.

## Make Your First Post

`Bluesky.post` automatically detects @mentions, links, and #hashtags in your text and creates the correct rich text facets:
*)

taskResult {
    let! post = Bluesky.post agent "Hello world from F#! #atproto"
    printfn "Posted! URI: %s" (AtUri.value post.Uri)
    ()
}

(**
Every `@handle.domain` in the text is resolved to a [DID](concepts.html) via the API. Links and hashtags are detected by pattern. You never need to compute byte offsets or construct facet objects yourself. The result is a `PostRef` containing the [AT-URI](concepts.html) and [CID](concepts.html) of the new post.

## Read Your Timeline

`Bluesky.getTimeline` wraps the `app.bsky.feed.getTimeline` endpoint with a simpler signature:
*)

taskResult {
    let! timeline = Bluesky.getTimeline agent (Some 10L) None

    for item in timeline.Items do
        let author = Handle.value item.Post.Author.Handle
        let text = item.Post.Text
        printfn "@%s: %s" author text
}

(**
Each `FeedItem` has a `.Post` field (a `TimelinePost`) with `.Text`, `.Author`, `.Uri`, `.Cid`, and engagement counts directly available. If you drop down to the raw XRPC layer, extension properties like `.Text` and `.Facets` are available on `PostView`.

## Like a Post

`Bluesky.like` accepts a `TimelinePost` (or a `PostRef`) directly:
*)

taskResult {
    let firstPost = timeline.Items.[0].Post
    let! likeRef = Bluesky.like agent firstPost
    printfn "Liked! %s" (AtUri.value likeRef.Uri)
    ()
}

(**
The result is a `LikeRef` you can hold on to. To undo the like later, pass it to `Bluesky.undoLike`:
*)

taskResult {
    let! _ = Bluesky.undoLike agent likeRef
    ()
}

(**
## Reply to a Post

`Bluesky.replyTo` fetches the parent post to resolve the thread root automatically. Pass the post you are replying to directly:
*)

taskResult {
    let! reply = Bluesky.replyTo agent "Great post!" firstPost
    printfn "Replied: %s" (AtUri.value reply.Uri)
    ()
}

(**
## Post with Images

`Bluesky.postWithImages` handles blob uploading and embed construction. Pass a list of `ImageUpload` records:
*)

taskResult {
    let imageBytes = System.IO.File.ReadAllBytes("photo.jpg")

    let! post =
        Bluesky.postWithImages agent "Check out this photo!" [
            { Data = imageBytes; MimeType = Jpeg; AltText = "A sunny landscape" }
        ]
    ()
}

(**
`MimeType` is an `ImageMime` discriminated union with cases `Jpeg`, `Png`, `Gif`, `Webp`, and `Custom of string`. Up to 4 images per post.

## Complete Example

Here is a full program that ties everything together using the `taskResult` computation expression. Every `let!` binding short-circuits to the `Error` case if something fails -- no nested match trees needed:

*)

open FSharp.ATProto.Syntax
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

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
            printfn "Timeline (%d posts):" timeline.Items.Length

            for item in timeline.Items do
                printfn "  @%s: %s" (Handle.value item.Post.Author.Handle) item.Post.Text

            // Like the first post from the timeline
            match timeline.Items with
            | first :: _ ->
                let! likeRef = Bluesky.like agent first.Post
                printfn "Liked: %s" (AtUri.value likeRef.Uri)

                // Reply to it
                let! reply = Bluesky.replyTo agent "Nice post!" first.Post
                printfn "Replied: %s" (AtUri.value reply.Uri)

                // Clean up: undo the like and delete the reply
                let! _ = Bluesky.undoLike agent likeRef
                let! _ = Bluesky.deleteRecord agent reply
                printfn "Cleaned up."
            | [] -> ()

            // Delete our original post
            let! _ = Bluesky.deleteRecord agent post

            printfn "Done!"
            return 0
        }

    result.Result
    |> function
        | Ok code -> code
        | Error e -> printfn "Error: %A" e; 1

(**
## What's Next

- [Build a Bot](guides/build-a-bot.html) -- end-to-end bot tutorial
- [Concepts](concepts.html) -- AT Protocol terms explained (DID, Handle, AT-URI, CID, PDS)
- [Posts Guide](guides/posts.html) -- reading posts, threads, and search
- [Social Actions Guide](guides/social.html) -- like, repost, follow, block, and undo
- [Feeds Guide](guides/feeds.html) -- timelines and custom feeds
- [Profiles Guide](guides/profiles.html) -- fetch user profiles
- [Media Guide](guides/media.html) -- image uploads
- [Chat / DM Guide](guides/chat.html) -- direct messaging
- [Notifications](guides/notifications.html) -- unread counts, mark-as-read
- [Moderation](guides/moderation.html) -- mute, block, report
- [Rich Text Guide](guides/rich-text.html) -- finer control over mention/link/hashtag detection
- [Identity Guide](guides/identity.html) -- resolve handles and DIDs
- [Error Handling](guides/error-handling.html) -- XrpcError, taskResult, retry behaviour
- [Pagination Guide](guides/pagination.html) -- iterate through large result sets
- [Raw XRPC](guides/raw-xrpc.html) -- drop to generated wrappers for advanced usage
*)
