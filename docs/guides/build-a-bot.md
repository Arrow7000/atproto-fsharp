---
title: Build a Bot
category: Getting Started
categoryindex: 1
index: 2
description: "End-to-end tutorial: build a Bluesky bot that monitors a hashtag"
keywords: fsharp, atproto, bluesky, bot, tutorial, hashtag
---

# Build a Bot

This tutorial walks through building a Bluesky bot from scratch. By the end, you will have a running F# program that monitors the `#fsharp` hashtag on Bluesky and automatically likes new posts it finds.

> Code samples use `taskResult {}`, a computation expression that chains async operations returning `Result`. See [Error Handling](error-handling.html) for details. The bot example below uses `task {}` deliberately -- see [Why task instead of taskResult](#Why-task-instead-of-taskResult) for the reasoning.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- A Bluesky account with an [App Password](https://bsky.app/settings/app-passwords) (do not use your main password)

## Create the Project

```bash
mkdir fsharp-bot && cd fsharp-bot
dotnet new console -lang F#
```

Add a project reference to `FSharp.ATProto.Bluesky` (the top-level package that pulls in everything you need).

Then replace the contents of `Program.fs` with the code below.

## The Complete Bot

```fsharp
open System
open System.Threading.Tasks
open FSharp.ATProto.Syntax
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

[<EntryPoint>]
let main _ =
    let run =
        task {
            // Step 1: Log in
            match! Bluesky.login "https://bsky.social" "your-handle.bsky.social" "your-app-password" with
            | Error err ->
                printfn "Login failed: %s" (err.Message |> Option.defaultValue "unknown error")
            | Ok agent ->
                printfn "Logged in!"

                // Step 2: Poll loop
                while true do
                    match! Bluesky.searchPosts agent "#fsharp" (Some 25L) None with
                    | Ok page ->
                        for post in page.Items do
                            if not post.IsLiked then
                                match! Bluesky.like agent post with
                                | Ok _ ->
                                    printfn "Liked post by @%s: %s"
                                        (Handle.value post.Author.Handle)
                                        (if post.Text.Length > 80 then post.Text.[..79] + "..." else post.Text)
                                | Error err ->
                                    printfn "Failed to like post: %s" (err.Message |> Option.defaultValue "unknown error")

                        printfn "[%s] Checked %d posts" (DateTime.Now.ToString("HH:mm:ss")) page.Items.Length
                    | Error err ->
                        printfn "Search failed: %s" (err.Message |> Option.defaultValue "unknown error")

                    do! Task.Delay(TimeSpan.FromSeconds(60.0))
        }

    run.GetAwaiter().GetResult()
    0
```

Replace `"your-handle.bsky.social"` and `"your-app-password"` with your actual credentials. Run it:

```bash
dotnet run
```

The bot will log in, search for `#fsharp` posts, like any it has not already liked, then sleep for 60 seconds and repeat.

## How It Works

### Authentication

`Bluesky.login` takes a PDS URL, your handle, and an app password. It returns `Task<Result<AtpAgent, XrpcError>>`. On success, the `AtpAgent` holds your authenticated session and handles token refresh automatically -- if your access token expires mid-run, the library refreshes it behind the scenes.

### Why `task {}` instead of `taskResult {}`

You might expect to see `taskResult {}` here, since most examples in this library use it. The difference: `taskResult` short-circuits on the first error. That is perfect for linear workflows (log in, post, done), but a bot needs to keep running even when individual operations fail. A search might time out, or a like might hit a rate limit. By using plain `task {}` with manual `match!` on each call, we handle errors inline and let the loop continue. See the [Error Handling guide](error-handling.html) for more on choosing between these two styles.

### Searching

`Bluesky.searchPosts` runs a full-text search and returns `Page<TimelinePost>`. The first argument after the agent is the query string -- hashtags work as you would expect. `Some 25L` requests up to 25 results per page, and `None` for the cursor means "start from the beginning." Each `TimelinePost` has an `IsLiked` field that reflects whether the authenticated user has already liked that post, so we skip posts we have already liked.

### Liking

`Bluesky.like` accepts a `TimelinePost` directly and returns a `LikeRef` on success. The `LikeRef` contains the AT-URI of the like record itself -- useful if you later want to undo it with `Bluesky.unlike`.

### The Loop

The bot re-searches every 60 seconds. Since we pass `None` as the cursor each time, we always get the most recent results. Posts we have already liked are skipped thanks to the `IsLiked` check, so running the same search repeatedly is safe. In a production bot, you would track the cursor or a timestamp to avoid re-fetching the same page, and you might persist state across restarts.

## Next Steps

Here are some ideas for extending the bot:

- **Reply to mentions**: Use `Bluesky.getNotifications` to check for mentions, then `Bluesky.replyTo` to respond. See the [Notifications guide](notifications.html).
- **Follow back**: When someone follows you, use `Bluesky.follow` to follow them back. See the [Social Actions guide](social.html).
- **Post on a schedule**: Use `Bluesky.post` on a timer to publish content at regular intervals. See the [Posts guide](posts.html).
- **Search for users**: Use `Bluesky.searchActors` to find accounts by keyword. See the [Profiles guide](profiles.html).
- **Send DMs**: Use `Chat.getConvoForMembers` and `Chat.sendMessage` to message users directly. See the [Chat guide](chat.html).
- **Advanced search filters**: Drop down to `AppBskyFeed.SearchPosts.query` for author, language, domain, and date range filters. See the [Posts guide](posts.html).
