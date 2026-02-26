# FSharp.ATProto

A native, idiomatic F# library for the [AT Protocol](https://atproto.com) — the decentralized social networking protocol behind [Bluesky](https://bsky.app).

Built from the ground up in F#. No C# wrappers. Functional-first. If it compiles, it's correct.

## Features

- **Strongly-typed domain model** — Distinct types for every concept (`PostRef`, `LikeRef`, `FollowRef`, `BlockRef`). The compiler catches your mistakes.
- **Result-based error handling** — Every public function returns `Result`. No exceptions, no surprises.
- **Protocol complexity hidden** — Reply to a post and the library resolves the thread root. Post text with mentions and the library detects and resolves them. You think in domain terms, not protocol internals.
- **Code-generated from spec** — All 324 Lexicon schemas compiled to F# types and 237 XRPC endpoint wrappers.
- **1,600+ tests** — Including official AT Protocol interop test vectors and property-based tests.

## Quick Start

```fsharp
open FSharp.ATProto.Bluesky

task {
    // Log in
    let! agent = Bluesky.login "https://bsky.social" "my-handle.bsky.social" "app-password"

    match agent with
    | Ok agent ->
        // Post with auto-detected rich text (mentions, links, hashtags)
        let! result = Bluesky.post agent "Hello from F#! @other-user.bsky.social #atproto"
        match result with
        | Ok postRef -> printfn "Posted: %s" (AtUri.value postRef.Uri)
        | Error e -> printfn "Failed: %A" e
    | Error e -> printfn "Login failed: %A" e
}
```

## Examples

### Social actions

```fsharp
// Like a post
let! likeRef = Bluesky.like agent postRef

// Repost
let! repostRef = Bluesky.repost agent postRef

// Follow someone (accepts Handle, Did, or string)
let! followRef = Bluesky.follow agent did

// Undo any action with a single generic function
let! result = Bluesky.undo agent likeRef   // works on any ref type
// Or use specific functions
let! _ = Bluesky.unlikePost agent postRef  // unlike by post
```

### Replies

```fsharp
// Reply to a post — thread root is resolved automatically
let! reply = Bluesky.replyTo agent "Great post!" parentPostRef
```

### Images

```fsharp
let! result =
    Bluesky.postWithImages agent "Photo dump! #photography"
        [ { Data = imageBytes; MimeType = Jpeg; AltText = "A sunset over the ocean" } ]
```

### Rich text

```fsharp
// Detect mentions, links, hashtags
let detected = RichText.detect "Hey @my-handle.bsky.social, check https://example.com #cool"

// Resolve mentions to DIDs
let! facets = RichText.resolve agent detected

// Check post length (Bluesky uses grapheme clusters)
let len = RichText.graphemeLength text  // 300 grapheme limit
```

### Identity

```fsharp
// Resolve and verify a handle bidirectionally
let! identity = Identity.resolveIdentity agent "my-handle.bsky.social"
match identity with
| Ok id ->
    printfn "DID: %s" (Did.value id.Did)
    printfn "PDS: %A" id.PdsEndpoint
| Error e -> printfn "Resolution failed: %A" e
```

### Direct messages

```fsharp
// Chat proxy headers are handled automatically
let! convos = Chat.listConvos agent (Some 10L) None
let! msg = Chat.sendMessage agent convoId "Hello from F#!"
```

### Pagination

```fsharp
// IAsyncEnumerable-based pagination
let pages = Bluesky.paginateTimeline agent (Some 25L)

await for page in pages do
    match page with
    | Ok timeline ->
        for item in timeline.Feed do
            printfn "@%s: %s" (Handle.value item.Post.Author.Handle) item.Post.Text
    | Error e -> printfn "Error: %A" e
```

### Generated XRPC wrappers

For anything the convenience API doesn't cover, use the 237 generated endpoint wrappers directly:

```fsharp
let! result = AppBskyActor.SearchActors.query agent {| q = Some "fsharp"; limit = Some 10L |}
let! result = AppBskyFeed.GetAuthorFeed.query agent {| actor = handle; limit = Some 20L |}
```

## Architecture

Six layers, each building on the last:

```
┌─────────────────────────────────────────────┐
│  Bluesky    Rich text, identity, social     │
│             actions, chat, 237 XRPC wrappers│
├─────────────────────────────────────────────┤
│  Core       XRPC client, session auth,      │
│             rate limiting, pagination        │
├──────────────────────┬──────────────────────┤
│  CodeGen (CLI tool)  │  Lexicon             │
│  324 schemas → F#    │  Schema parser +     │
│                      │  validator           │
├──────────────────────┴──────────────────────┤
│  DRISL      CBOR encoding, CID computation  │
├─────────────────────────────────────────────┤
│  Syntax     DID, Handle, NSID, AT-URI, TID, │
│             CID, RecordKey, DateTime, etc.   │
└─────────────────────────────────────────────┘
```

## Project Structure

```
src/
  FSharp.ATProto.Syntax/       Identifier types (DID, Handle, NSID, AT-URI, ...)
  FSharp.ATProto.DRISL/        DRISL/CBOR encoding + CID computation
  FSharp.ATProto.Lexicon/      Lexicon schema parser + record validator
  FSharp.ATProto.CodeGen/      CLI: Lexicon schemas → F# source code
  FSharp.ATProto.Core/         XRPC client, session auth, rate limiting, pagination
  FSharp.ATProto.Bluesky/      Generated types + rich text, identity, convenience API

tests/
  FSharp.ATProto.Syntax.Tests/      726 tests (incl. official interop vectors)
  FSharp.ATProto.DRISL.Tests/       112 tests
  FSharp.ATProto.Lexicon.Tests/     387 tests (parses all 324 real lexicon files)
  FSharp.ATProto.CodeGen.Tests/     169 tests
  FSharp.ATProto.Core.Tests/         30 tests
  FSharp.ATProto.Bluesky.Tests/      48 tests

examples/
  BskyBotExample/              Comprehensive example program

extern/
  atproto/                     Git submodule: official lexicon schemas
  atproto-interop-tests/       Git submodule: official test vectors
```

## Building

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
# Build everything
dotnet build

# Run all tests
dotnet test

# Run a specific test project
dotnet test tests/FSharp.ATProto.Syntax.Tests
```

## Dependencies

Only three runtime NuGet packages:

| Package | Used for |
|---------|----------|
| `System.Formats.Cbor` | Canonical CBOR serialization (DRISL layer) |
| `FSharp.SystemTextJson` | JSON serialization with F# union support |
| `Fabulous.AST` | F# source code generation (CodeGen CLI only) |

## License

MIT
