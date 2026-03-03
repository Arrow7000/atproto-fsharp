---
title: FSharp.ATProto
category: Getting Started
categoryindex: 1
index: 0
description: Idiomatic F# library for the AT Protocol and Bluesky
keywords: fsharp, atproto, bluesky, at-protocol, decentralized, social
---

# FSharp.ATProto

A native F# library for the [AT Protocol](https://atproto.com) and [Bluesky](https://bsky.app). Pure F#, no C# wrappers. Immutable domain types, result-based error handling, and a convenience API that makes the protocol disappear -- you think in posts, profiles, and follows, not records and XRPC calls.

```fsharp
open FSharp.ATProto.Bluesky

taskResult {
    let! agent = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"
    let! post = Bluesky.post agent "Hello from F#! #atproto"
    printfn "Posted: %s" (AtUri.value post.Uri)
    return post
}
```

## Getting Started

Add a project reference to `FSharp.ATProto.Bluesky` -- it pulls in all dependencies transitively. Then head to the [Quickstart](quickstart.html) to go from zero to first post in five minutes.

## Getting Started
- [Quickstart](quickstart.html) — authenticate and make your first post
- [Build a Bot](guides/build-a-bot.html) — full bot example with notifications and replies
- [Concepts](concepts.html) — domain types, SRTP, and design philosophy
- [Error Handling](guides/error-handling.html) — taskResult CE and error patterns

## Type Reference
- [Posts](guides/posts.html) — creating, reading, and engaging with posts
- [Profiles](guides/profiles.html) — reading and searching user profiles
- [Social Actions](guides/social.html) — following, blocking, and undo operations
- [Feeds](guides/feeds.html) — timelines, author feeds, and pagination
- [Chat](guides/chat.html) — direct messaging conversations
- [Notifications](guides/notifications.html) — reading and managing notifications

## Advanced Guides
- [Media](guides/media.html) — uploading images and blobs
- [Rich Text](guides/rich-text.html) — mentions, links, and hashtags
- [Identity](guides/identity.html) — DID resolution and handle verification
- [Moderation](guides/moderation.html) — muting, reporting, and moderation lists
- [Pagination](guides/pagination.html) — IAsyncEnumerable-based paginators
- [Raw XRPC](guides/raw-xrpc.html) — direct XRPC calls for uncovered endpoints

## Architecture

The library is organized in layers, each building on the one below:

| Package | Purpose |
|---------|---------|
| `FSharp.ATProto.Syntax` | Identifier types (DID, Handle, NSID, AT-URI, etc.) |
| `FSharp.ATProto.DRISL` | CBOR encoding, CID computation, data integrity |
| `FSharp.ATProto.Lexicon` | Lexicon schema parser and record validator |
| `FSharp.ATProto.Core` | XRPC client, session auth, rate limiting, pagination |
| `FSharp.ATProto.Bluesky` | Generated types, rich text, identity, convenience methods |

1,761 tests across the stack.

## License

MIT
