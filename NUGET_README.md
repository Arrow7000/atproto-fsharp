# FSharp.ATProto

A native F# library for [Bluesky](https://bsky.app) and the [AT Protocol](https://atproto.com).
Built from the ground up in F#. No C# wrappers. Functional-first.

## Install

```bash
dotnet add package FSharp.ATProto.Bluesky
```

## Quick Example

```fsharp
open FSharp.ATProto.Bluesky

taskResult {
    let! agent = Bluesky.login "https://bsky.social" "my-handle.bsky.social" "app-password"
    let! post = Bluesky.post agent "Hello from F#!"
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

## Features

- **Posts** -- create, reply, quote, delete, with automatic rich text detection
- **Rich text** -- mentions, links, and hashtags detected and resolved automatically
- **Images** -- upload and attach with typed `ImageMime` and alt text
- **Social graph** -- follow, block, like, repost, mute, with typed refs and generic undo
- **Feeds** -- timeline, author feed, actor likes, bookmarks
- **Profiles** -- get, search, typeahead, batch fetch, upsert
- **Chat / DMs** -- conversations, messages, reactions, with automatic proxy headers
- **Notifications** -- fetch, count unread, mark seen
- **Moderation** -- report content, mute threads and mod lists
- **Identity** -- DID resolution, handle verification, PDS discovery
- **Pagination** -- lazy `IAsyncEnumerable` paginators for timeline, followers, notifications
- **Full XRPC access** -- all 237 Bluesky endpoints available as typed wrappers

## Documentation

Full docs at [arrow7000.github.io/atproto-fsharp](https://arrow7000.github.io/atproto-fsharp/)

## License

MIT
