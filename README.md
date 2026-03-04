<!-- @format -->

<p align="center">
  <img src="https://raw.githubusercontent.com/Arrow7000/atproto-fsharp/main/docs/assets/header.svg" alt="FSharp.ATProto" width="400"/>
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/FSharp.ATProto.Bluesky"><img src="https://img.shields.io/nuget/v/FSharp.ATProto.Bluesky" alt="NuGet"></a>
  <a href="https://github.com/Arrow7000/atproto-fsharp/actions/workflows/ci.yml"><img src="https://github.com/Arrow7000/atproto-fsharp/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet" alt=".NET 10">
  <img src="https://img.shields.io/endpoint?url=https://gist.githubusercontent.com/Arrow7000/b4926c04f5a0cf1326acd6be7fab8ef3/raw/test-count.json" alt="Tests">
  <a href="https://github.com/Arrow7000/atproto-fsharp/blob/main/LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="License: MIT"></a>
</p>

<p align="center">
  A native F# library for <a href="https://bsky.app">Bluesky</a> and the <a href="https://atproto.com">AT Protocol</a>.
  <br/>
  Built from the ground up in F#. No C# wrappers. Functional-first.
</p>

---

## Install

```bash
dotnet add package FSharp.ATProto.Bluesky
```

## Quick Example

```fsharp
open FSharp.ATProto.Bluesky

taskResult {
    let! agent = Bluesky.login "https://bsky.social" "my-handle.bsky.social" "app-password"
    let! post = Bluesky.post agent "Hello from F#! 🦋"
    let! like = Bluesky.like agent post // PostRef -> LikeRef (the compiler prevents mix-ups)
    let! reply = Bluesky.replyTo agent "Nice thread!" post // thread root resolved automatically
    let! _ = Bluesky.undo agent like // generic undo — works on any ref type
    return reply
}
// : Task<Result<PostRef, XrpcError>> — no exceptions, ever
```

## Design

- **If it compiles, it's correct** -- distinct types for every domain concept (`PostRef`, `LikeRef`, `FollowRef`, `BlockRef`...) mean the compiler catches your mistakes.
- **The library handles protocol complexity** -- thread roots, rich text facets, chat proxy headers -- all resolved automatically.
- **Results, not exceptions** -- every public function returns `Result`. No `failwith`, no try/catch.
- **Rich domain types** -- `PostRef`, `Profile`, `FeedItem`, `ConvoSummary`, `Page<'T>`, and more. Plus convenience functions for search, bookmarks, muting, notifications, and moderation.
- **Generated from the spec** -- 324 Lexicon schemas compiled to F# types + 237 typed XRPC endpoint wrappers.

## Getting Started

See the [Quickstart](docs/quickstart.md) to get up and running in 5 minutes.

## Features

- **Posts** -- create, reply, quote, delete, with automatic rich text detection ([guide](docs/guides/posts.md))
- **Rich text** -- mentions, links, and hashtags detected and resolved automatically ([guide](docs/guides/rich-text.md))
- **Images** -- upload and attach with typed `ImageMime` and alt text ([guide](docs/guides/media.md))
- **Social graph** -- follow, block, like, repost, mute, with typed refs and generic undo ([guide](docs/guides/social.md))
- **Feeds** -- timeline, author feed, actor likes, bookmarks ([guide](docs/guides/feeds.md))
- **Profiles** -- get, search, typeahead, batch fetch, upsert ([guide](docs/guides/profiles.md))
- **Chat / DMs** -- conversations, messages, reactions, with automatic proxy headers ([guide](docs/guides/chat.md))
- **Notifications** -- fetch, count unread, mark seen ([guide](docs/guides/notifications.md))
- **Moderation** -- report content, mute threads, mod lists, and a full moderation engine ([guide](docs/guides/moderation.md))
- **Identity** -- DID resolution, handle verification, PDS discovery ([guide](docs/guides/identity.md))
- **Lists** -- create and manage lists and starter packs ([guide](docs/guides/lists.md))
- **Preferences** -- saved feeds, muted words, content filtering ([guide](docs/guides/preferences.md))
- **Streaming** -- real-time events via Jetstream and Firehose ([guide](docs/guides/streaming.md))
- **Video** -- upload and post video content ([guide](docs/guides/media.md))
- **Pagination** -- lazy `IAsyncEnumerable` paginators for timeline, followers, notifications ([guide](docs/guides/pagination.md))
- **OAuth** -- OAuth 2.0 client with DPoP/PKCE, plus authorization server ([guide](docs/guides/oauth.md))
- **Server-side** -- feed generator framework, XRPC server, service auth
- **Full XRPC access** -- all 237 Bluesky endpoints available as typed wrappers ([guide](docs/guides/raw-xrpc.md))

## Documentation

- [Quickstart](docs/quickstart.md) -- zero to first post
- [Build a Bot](docs/guides/build-a-bot.md) -- end-to-end tutorial
- [Concepts](docs/concepts.md) -- AT Protocol terms explained (DID, Handle, AT-URI, PDS, Lexicon)
- [Guides](docs/guides/) -- 25+ guides covering posts, social, feeds, profiles, media, chat, notifications, moderation, streaming, OAuth, and more

## Building & Testing

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build && dotnet test
```

2,608 tests across 14 projects.

## AI Transparency

This project was built with heavy use of AI coding assistants, mostly Claude Opus 4.6.

To ensure correctness the project validates against ground truth at every layer:

- **Syntax parsing** -- [tested](tests/FSharp.ATProto.Syntax.Tests/) against the official [AT Protocol interop test vectors](https://github.com/bluesky-social/atproto-interop-tests) (valid and invalid inputs for DIDs, Handles, NSIDs, TIDs, AT-URIs, and more)
- **CBOR & CID** -- [tested](tests/FSharp.ATProto.DRISL.Tests/InteropTests.fs) against the interop data-model fixtures (known JSON -> CBOR -> CID round-trips), plus [property-based tests](tests/FSharp.ATProto.DRISL.Tests/PropertyTests.fs) for encoding invariants
- **Lexicon schemas** -- all 324 real lexicon files from the [official atproto repo](https://github.com/bluesky-social/atproto/tree/main/lexicons) are [parsed and validated](tests/FSharp.ATProto.Lexicon.Tests/RealLexiconTests.fs); the code generator is tested against them
- **Rich text** -- [property-based tests](tests/FSharp.ATProto.Bluesky.Tests/RichTextTests.fs) verify byte-range correctness and facet ordering
- **XRPC / Bluesky** -- [tested](tests/FSharp.ATProto.Bluesky.Tests/) via mock HTTP handlers that verify request construction, multi-step orchestration (e.g. thread root resolution), error handling, and domain type mapping (note: the mocks don't validate against real Bluesky API responses -- that contract is covered by the generated types matching the lexicon schemas above)

All told, 2,608 tests across 14 projects, with zero reliance on manual testing or live API calls.

## License

MIT
