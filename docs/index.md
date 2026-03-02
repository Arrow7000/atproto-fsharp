---
title: FSharp.ATProto
category: Guides
categoryindex: 1
index: 0
description: Idiomatic F# library for the AT Protocol and Bluesky
keywords: fsharp, atproto, bluesky, at-protocol, decentralized, social
---

# FSharp.ATProto

Welcome to FSharp.ATProto -- a native F# library for the [AT Protocol](https://atproto.com), the decentralized social networking protocol behind [Bluesky](https://bsky.app). Built from the ground up in pure F# with no C# wrapper dependencies, it embraces the functional style you already love: immutable data, composable functions, and types that make invalid states unrepresentable.

## Features

- **Typed domain model** -- Distinct types for every concept: `PostRef`, `LikeRef`, `RepostRef`, `FollowRef`, `BlockRef`, `Profile`, `TimelinePost`, `FeedItem`, `Page<'T>`, and more. If it compiles, it is correct -- the compiler catches mistakes so you don't have to.
- **`taskResult` computation expression** -- Chain async operations with automatic error short-circuiting. No nested `match` trees, no thrown exceptions. Write straight-line code and let the CE handle the plumbing.
- **237 XRPC wrappers from 324 Lexicon schemas** -- Every Bluesky API endpoint generated as a strongly-typed F# function with typed request/response records and discriminated unions.
- **Rich text** -- Automatic detection and resolution of @mentions, links, and #hashtags with correct UTF-8 byte offsets. Just pass a string; the library does the rest.
- **PostView extensions** -- `.Text` and `.Facets` extension properties for easy access to post content without touching raw JSON.
- **SRTP-powered convenience** -- `Bluesky.getProfile` accepts a `Handle`, `Did`, or plain `string`. `Bluesky.undo` accepts any ref type (`LikeRef`, `RepostRef`, `FollowRef`, `BlockRef`) and returns a typed `UndoResult`.
- **Search, bookmarks, and moderation** -- `searchPosts`, `searchActors`, `addBookmark`/`removeBookmark`, `muteUser`/`unmuteUser`, `muteThread`/`unmuteThread`, and `reportContent`.
- **Pre-built paginators** -- Cursor-based pagination via `IAsyncEnumerable`: `paginateTimeline`, `paginateFollowers`, `paginateNotifications`.
- **Chat / DMs** -- Convenience methods for Bluesky direct messaging: conversations, messages, muting, reactions, accept/leave.
- **Identity resolution** -- Resolve handles to DIDs and vice versa, with bidirectional verification and DID document parsing.
- **Spec-compliant identifiers** -- DIDs, Handles, NSIDs, AT-URIs, TIDs, and more, all validated against the AT Protocol specification.
- **DRISL / CBOR** -- Content-addressed records, CID computation, and canonical CBOR serialization for data integrity.

## Installation

```xml
<PackageReference Include="FSharp.ATProto.Bluesky" Version="0.1.0" />
```

Or via the .NET CLI:

```bash
dotnet add package FSharp.ATProto.Bluesky
```

The `FSharp.ATProto.Bluesky` package pulls in all dependencies transitively (`Core`, `Syntax`, `DRISL`, `Lexicon`).

## Quick Example

Log in and make a post with auto-detected rich text:

```fsharp
open FSharp.ATProto.Syntax
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

let result =
    taskResult {
        let! agent = Bluesky.login "https://bsky.social" "my-handle.bsky.social" "app-password-here"
        let! post = Bluesky.post agent "Hello from F#! Check https://atproto.com #atproto"
        printfn "Posted: %s" (AtUri.value post.Uri)
        return post
    }
```

## Next Steps

- [Quickstart Guide](quickstart.html) -- get up and running in 5 minutes
- [Posts Guide](guides/posts.html) -- create, reply to, and delete posts
- [Profiles Guide](guides/profiles.html) -- fetch user profiles
- [Feeds Guide](guides/feeds.html) -- timelines, author feeds, custom feeds
- [Social Actions Guide](guides/social.html) -- like, repost, follow, block
- [Rich Text Guide](guides/rich-text.html) -- control how mentions, links, and hashtags are processed
- [Media Guide](guides/media.html) -- upload images
- [Chat / DM Guide](guides/chat.html) -- send and receive direct messages
- [Identity Guide](guides/identity.html) -- resolve handles and DIDs
- [Pagination Guide](guides/pagination.html) -- iterate through paged API results

## Architecture

The library is organized in layers, each building on the one below:

| Package | Purpose |
|---------|---------|
| `FSharp.ATProto.Syntax` | Identifier types (DID, Handle, NSID, AT-URI, etc.) |
| `FSharp.ATProto.DRISL` | CBOR encoding, CID computation, data integrity |
| `FSharp.ATProto.Lexicon` | Lexicon schema parser and record validator |
| `FSharp.ATProto.Core` | XRPC client, session auth, rate limiting, pagination |
| `FSharp.ATProto.Bluesky` | Generated types, rich text, identity, convenience methods |

Each layer is independently testable with 1,696 tests across the stack.

## License

MIT
