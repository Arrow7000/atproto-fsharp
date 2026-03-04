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
- [Lists](guides/lists.html) — list and starter pack management
- [Preferences](guides/preferences.html) — saved feeds, muted words, content filtering

## Advanced Guides
- [Media](guides/media.html) — uploading images, video, and blobs
- [Rich Text](guides/rich-text.html) — mentions, links, hashtags, and text manipulation
- [Identity](guides/identity.html) — DID resolution and handle verification
- [Moderation](guides/moderation.html) — muting, reporting, and the moderation engine
- [Pagination](guides/pagination.html) — IAsyncEnumerable-based paginators
- [Raw XRPC](guides/raw-xrpc.html) — direct XRPC calls for uncovered endpoints
- [Streaming](guides/streaming.html) — real-time event streams via Jetstream and Firehose
- [Ozone](guides/ozone.html) — moderation tooling for labeler operators
- [Account](guides/account.html) — account creation, deletion, and session management

## Server-Side
- [Feed Generator](guides/feed-generator.html) — build custom feed algorithms
- [XRPC Server](guides/xrpc-server.html) — host AT Protocol endpoints
- [OAuth](guides/oauth.html) — OAuth client and authorization server

## Infrastructure
- [Cryptography](guides/crypto.html) — P-256/K-256 keys, signing, did:key encoding
- [Repository](guides/repository.html) — MST, signed commits, CAR export
- [Service Auth](guides/service-auth.html) — inter-service JWT authentication
- [PLC Directory](guides/plc.html) — DID PLC resolution and operations
- [Testing](guides/testing.html) — TestFactory for unit testing

## Architecture

The library is organized in layers, each building on the one below:

| Package | Purpose |
|---------|---------|
| `FSharp.ATProto.Syntax` | Identifier types (DID, Handle, NSID, AT-URI, etc.) |
| `FSharp.ATProto.DRISL` | CBOR encoding, CID computation, data integrity |
| `FSharp.ATProto.Lexicon` | Lexicon schema parser and record validator |
| `FSharp.ATProto.Core` | XRPC client, session auth, rate limiting, pagination |
| `FSharp.ATProto.Bluesky` | Generated types, rich text, identity, convenience methods |
| `FSharp.ATProto.Streaming` | Jetstream and Firehose event streams |
| `FSharp.ATProto.Moderation` | Label-aware moderation engine |
| `FSharp.ATProto.FeedGenerator` | Custom feed generator framework |
| `FSharp.ATProto.OAuth` | OAuth 2.0 client with DPoP and PKCE |
| `FSharp.ATProto.OAuthServer` | OAuth 2.0 authorization server |
| `FSharp.ATProto.Crypto` | Cryptographic keys, signing, did:key |
| `FSharp.ATProto.Repo` | Repository MST, commits, CAR export |
| `FSharp.ATProto.XrpcServer` | XRPC server framework with auth and rate limiting |

![Tests](https://img.shields.io/endpoint?url=https://gist.githubusercontent.com/Arrow7000/b4926c04f5a0cf1326acd6be7fab8ef3/raw/test-count.json) across 14 projects.

## License

MIT
