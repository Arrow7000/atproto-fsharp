(**
---
title: Raw XRPC
category: Advanced Guides
categoryindex: 3
index: 16
description: Using generated XRPC wrappers for advanced AT Protocol operations
keywords: fsharp, atproto, bluesky, xrpc, generated, lexicon, advanced
---

# Raw XRPC

The convenience API (`Bluesky.*`, `Chat.*`) covers common operations with domain types like `PostRef`, `Profile`, and `FeedItem`. For anything it doesn't cover, all 237 Bluesky API endpoints are available as typed F# wrappers generated directly from the AT Protocol Lexicon schemas. These wrappers give you full access to every parameter and response field.

## Query Endpoints

Query (GET) endpoints use a `query` function that takes an `AtpAgent` and a `Params` record, and returns a typed `Output` record:
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
(***)

open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

taskResult {
    let! agent = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"

    let! result =
        AppBskyFeed.GetActorFeeds.query agent
            { Actor = "alice.bsky.social"; Cursor = None; Limit = Some 10L }

    for feed in result.Feeds do
        printfn "%s" feed.DisplayName
}

(**
Every field on the `Params` and `Output` records is fully typed. Optional protocol fields are `option` types, so the compiler ensures you handle them.

## Procedure Endpoints

Procedure (POST) endpoints use a `call` function that takes an `AtpAgent` and an `Input` record. Some return a typed `Output` record; others return `unit` for void operations:
*)

(*** hide ***)
let interaction = Unchecked.defaultof<AppBskyFeed.Defs.Interaction>
(***)

taskResult {
    let! _result =
        AppBskyFeed.SendInteractions.call agent
            { Interactions = [ interaction ] }

    return ()
}

(**
The return type tells you which kind you're dealing with -- check the generated `Output` type or look at the AT Protocol reference for the endpoint.

## Finding the Right Endpoint

The generated wrappers follow the Lexicon namespace structure exactly. Convert the dot-separated NSID to PascalCase modules:

| Lexicon NSID | F# module | Function |
|---|---|---|
| `app.bsky.feed.getTimeline` | `AppBskyFeed.GetTimeline` | `.query` |
| `app.bsky.actor.getProfile` | `AppBskyActor.GetProfile` | `.query` |
| `com.atproto.repo.createRecord` | `ComAtprotoRepo.CreateRecord` | `.call` |
| `chat.bsky.convo.sendMessage` | `ChatBskyConvo.SendMessage` | `.call` |

Each module also exposes a `TypeId` string literal (e.g., `AppBskyFeed.GetTimeline.TypeId = "app.bsky.feed.getTimeline"`) which is useful when you need the raw endpoint name for `Xrpc.paginate` or logging.

For the full list of endpoints, see the [AT Protocol HTTP Reference](https://docs.bsky.app/docs/category/http-reference).

## Custom Pagination

For paginated endpoints that don't have a pre-built paginator, use `Xrpc.paginate` directly. It returns an `IAsyncEnumerable` that fetches pages lazily:
*)

let pages =
    Xrpc.paginate<AppBskyFeed.GetActorFeeds.Params, AppBskyFeed.GetActorFeeds.Output>
        AppBskyFeed.GetActorFeeds.TypeId
        { Actor = "alice.bsky.social"; Cursor = None; Limit = Some 50L }
        (fun output -> output.Cursor)
        (fun cursor input -> { input with Cursor = cursor })
        agent

(**
The five arguments are: the endpoint type ID, initial parameters (with `Cursor = None`), a function to extract the cursor from the response, a function to inject a new cursor into the parameters, and the agent. The paginator stops automatically when the server returns no cursor.

See the [Pagination guide](pagination.html) for patterns on consuming the `IAsyncEnumerable` result.

## Chat Endpoints

Chat endpoints (`chat.bsky.*`) require a proxy header. Create a chat-proxied agent with `AtpAgent.withChatProxy` before calling them:
*)

taskResult {
    let chatAgent = AtpAgent.withChatProxy agent

    let! result =
        ChatBskyConvo.ListConvos.query chatAgent
            { Limit = Some 20L; Cursor = None; ReadState = None; Status = None }

    for convo in result.Convos do
        printfn "Conversation %s with %d members" convo.Id convo.Members.Length
}

(**
The `Chat.*` convenience functions handle this automatically, but when using raw XRPC wrappers for chat endpoints, you need to set up the proxy yourself.

## Mixing Convenience and Raw

You can freely mix convenience functions and raw XRPC calls in the same `taskResult` block. Both use the same `AtpAgent` and return `Task<Result<'T, XrpcError>>`, so they compose naturally:
*)

taskResult {
    let! agent = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"

    // Convenience: post with automatic rich text detection
    let! postRef = Bluesky.post agent "Check out my custom feeds!"

    // Raw XRPC: list the actor's custom feeds (no convenience wrapper for this)
    let! feeds =
        AppBskyFeed.GetActorFeeds.query agent
            { Actor = "handle.bsky.social"; Cursor = None; Limit = Some 5L }

    for feed in feeds.Feeds do
        printfn "%s" feed.DisplayName

    // Convenience: like the post we just created
    let! _like = Bluesky.like agent postRef

    return ()
}

(**
The convenience layer and the raw wrappers are complementary. Use `Bluesky.*` and `Chat.*` for the operations they cover, and drop to the generated wrappers when you need parameters or endpoints the convenience layer doesn't expose.
*)
