---
title: FSharp.ATProto
category: Overview
categoryindex: 1
index: 1
description: Idiomatic F# library for the AT Protocol and Bluesky
keywords: fsharp, atproto, bluesky, at-protocol, decentralized, social
---

# FSharp.ATProto

A native, idiomatic F# library for the [AT Protocol](https://atproto.com) -- the decentralized social networking protocol that powers [Bluesky](https://bsky.app). Built from the ground up in F# with no C# wrapper dependencies, designed around functional principles: immutable data, pure functions, and composition.

## Features

- **Spec-compliant identifiers** -- DIDs, Handles, NSIDs, AT-URIs, TIDs, and more, all validated against the AT Protocol specification with 726 tests drawn from official interop test vectors
- **XRPC client** -- Full HTTP transport for AT Protocol queries and procedures, with automatic session management, token refresh, and rate limit handling
- **Code-generated types** -- All 324 Bluesky Lexicon schemas compiled to strongly-typed F# records and discriminated unions, with 228 XRPC endpoint wrappers
- **Rich text** -- Automatic detection and resolution of @mentions, links, and #hashtags with correct UTF-8 byte offsets
- **Chat / DMs** -- Convenience methods for Bluesky direct messaging: conversations, messages, reactions, muting
- **Identity resolution** -- Resolve handles to DIDs and vice versa, with bidirectional verification and DID document parsing
- **DRISL / CBOR** -- Low-level encoding for data integrity: content-addressed records, CID computation, canonical CBOR serialization
- **Pagination** -- Cursor-based pagination via `IAsyncEnumerable` for any list endpoint

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
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

task {
    // Create an agent and log in
    let agent = AtpAgent.create "https://bsky.social"
    let! _ = AtpAgent.login "alice.bsky.social" "app-password-here" agent

    // Post with automatic @mention, link, and #hashtag detection
    let! result = Bluesky.post agent "Hello from F#! Check https://atproto.com #atproto"

    match result with
    | Ok post -> printfn "Posted: %s" post.Uri
    | Error e -> printfn "Failed: %A" e.Message
}
```

## Next Steps

- [Quickstart Guide](quickstart.html) -- get up and running in 5 minutes
- [Rich Text Guide](guides/rich-text.html) -- control how mentions, links, and hashtags are processed
- [Chat / DM Guide](guides/chat.html) -- send and receive direct messages
- [Identity Guide](guides/identity.html) -- resolve handles and DIDs
- [Pagination Guide](guides/pagination.html) -- iterate through paged API results
- [Example Project](https://github.com/aronka/atproto-fsharp/tree/main/examples/BskyBotExample) -- a full bot example covering posts, replies, likes, follows, DMs, and more

## Architecture

The library is organized in layers, each building on the one below:

| Package | Purpose |
|---------|---------|
| `FSharp.ATProto.Syntax` | Identifier types (DID, Handle, NSID, AT-URI, etc.) |
| `FSharp.ATProto.DRISL` | CBOR encoding, CID computation, data integrity |
| `FSharp.ATProto.Lexicon` | Lexicon schema parser and record validator |
| `FSharp.ATProto.Core` | XRPC client, session auth, rate limiting, pagination |
| `FSharp.ATProto.Bluesky` | Generated types, rich text, identity, convenience methods |

Each layer is independently testable with over 1,400 tests across the stack.

## License

MIT
