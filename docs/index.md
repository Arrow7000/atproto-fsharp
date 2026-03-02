---
title: FSharp.ATProto
category: Guides
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

## Guides

- [Quickstart](quickstart.html) -- zero to first post in 5 minutes
- [Build a Bot](guides/build-a-bot.html) -- end-to-end bot tutorial
- [Concepts](concepts.html) -- AT Protocol terms explained
- [Posts](guides/posts.html) -- create, reply, quote, and delete posts
- [Social Actions](guides/social.html) -- like, repost, follow, block, undo
- [Feeds](guides/feeds.html) -- timelines, author feeds, bookmarks
- [Profiles](guides/profiles.html) -- fetch and search user profiles
- [Media](guides/media.html) -- upload and attach images
- [Chat / DMs](guides/chat.html) -- conversations and direct messages
- [Notifications](guides/notifications.html) -- read and manage notifications
- [Moderation](guides/moderation.html) -- mute, block lists, report content
- [Rich Text](guides/rich-text.html) -- mentions, links, hashtags, and byte offsets
- [Identity](guides/identity.html) -- resolve and verify handles and DIDs
- [Error Handling](guides/error-handling.html) -- taskResult CE and XrpcError
- [Pagination](guides/pagination.html) -- cursors and IAsyncEnumerable
- [Raw XRPC](guides/raw-xrpc.html) -- drop to the generated XRPC layer

## Architecture

The library is organized in layers, each building on the one below:

| Package | Purpose |
|---------|---------|
| `FSharp.ATProto.Syntax` | Identifier types (DID, Handle, NSID, AT-URI, etc.) |
| `FSharp.ATProto.DRISL` | CBOR encoding, CID computation, data integrity |
| `FSharp.ATProto.Lexicon` | Lexicon schema parser and record validator |
| `FSharp.ATProto.Core` | XRPC client, session auth, rate limiting, pagination |
| `FSharp.ATProto.Bluesky` | Generated types, rich text, identity, convenience methods |

1,723 tests across the stack.

## License

MIT
