# API Ergonomics v2 Design

Consumer-driven UX improvements based on reviewing the example Program.fs
through fresh eyes. Every change targets a specific pain point encountered
when actually using the library.

## 1. Post Content Extension Properties

**Problem**: Post text is accessed via `item.Post.Record.TryGetProperty("text")`
— raw JsonElement fishing. The generated `PostView` has `Record: JsonElement`
because the lexicon schema types it as `unknown`, but in practice it's always
an `app.bsky.feed.post` record.

**Solution**: Extension properties on `PostView` that extract typed data from
the raw record. The generated `AppBskyFeed.Post.Post` type already has all the
fields (`Text`, `Facets`, `Embed`, `Reply`, `CreatedAt`, etc.) — we just need
to surface them.

```fsharp
type AppBskyFeed.Defs.PostView with
    /// Deserialize the raw record into a typed post. None if not a post record.
    member this.AsPost: AppBskyFeed.Post.Post option
    /// Post text (empty string if not a post record)
    member this.Text: string
    /// Post facets (empty list if none)
    member this.Facets: AppBskyRichtext.Facet.Facet list
```

Call-site improvement:
```fsharp
// Before
match item.Post.Record.TryGetProperty("text") with
| true, v -> v.GetString()
| false, _ -> "(no text)"

// After
item.Post.Text
```

Extension properties go in a new file `src/FSharp.ATProto.Bluesky/PostExtensions.fs`.

## 2. Combined Login

**Problem**: Two-step `AtpAgent.create` then `AtpAgent.login` is confusing.
Consumers wonder why creation and auth are separate, and must carry both agent
and session values.

**Solution**: Add `Bluesky.login` that combines both steps.

```fsharp
Bluesky.login: string -> string -> string -> Task<Result<AtpAgent, XrpcError>>
//             url       handle    password
```

Returns the ready-to-use authenticated agent. The existing `AtpAgent.create`,
`AtpAgent.createWithClient`, and `AtpAgent.login` remain for advanced use
(test mocks, custom PDS, unauthenticated browsing).

## 3. Typed Parameter Overloads

**Problem**: Functions like `getProfile` accept `string`, forcing
`Handle.value session.Handle` at every call site.

**Solution**: Add overloads accepting `Handle` and `Did` directly.

```fsharp
// Goal call-site:
let! p = Bluesky.getProfile agent session.Handle
let! p = Bluesky.getProfile agent someDid
let! p = Bluesky.getProfile agent "bsky.app"  // string still works
```

F# modules cannot overload functions. Implementation options:
- Inline SRTP (statically resolved type parameters)
- Static type with overloaded members alongside the module
- Simple wrapper type with implicit-like conversion

Determine best F# mechanism during implementation. Applies to: `getProfile`,
`follow`/`block` (accept Handle in addition to Did), `getPostThread`, and
any other function that currently requires stringifying typed values.

## 4. Simplified Reply API

**Problem**: Two confusing functions: `reply` (takes parent + root PostRefs)
and `replyTo` (takes PostRef + raw JsonElement). Neither name makes the
difference clear.

**Solution**: Rename and simplify.

```fsharp
/// Primary: just pass the parent, root is resolved via fetch (1 extra call)
Bluesky.replyTo: AtpAgent -> string -> PostRef -> Task<Result<PostRef, XrpcError>>

/// Escape hatch: caller already has the root ref (no extra fetch)
Bluesky.replyWithKnownRoot: AtpAgent -> string -> PostRef -> PostRef -> Task<Result<PostRef, XrpcError>>
//                                       text     parent     root
```

`replyTo` fetches the parent post to find the thread root. The old `reply`
becomes `replyWithKnownRoot` — the name signals "I already have this data."
The old `replyTo` that takes `JsonElement` is removed.

Docs should be very clear: use `replyTo` in 99.9% of cases.
`replyWithKnownRoot` exists only to save one network call when you happen
to already have the root ref.

## 5. Unified Undo with DU Result

**Problem**: `unlike` takes a `LikeRef`, not a `PostRef` — consumers think
"unlike this post" not "delete this like event." Also, `Result<unit, XrpcError>`
doesn't distinguish "deleted" from "was already gone."

**Solution**: Unified `undo` function + target-based undo + typed result.

```fsharp
type UndoResult = Undone | WasNotPresent

/// Undo any action by its ref
Bluesky.undo: AtpAgent -> LikeRef    -> Task<Result<UndoResult, XrpcError>>
Bluesky.undo: AtpAgent -> RepostRef  -> Task<Result<UndoResult, XrpcError>>
Bluesky.undo: AtpAgent -> FollowRef  -> Task<Result<UndoResult, XrpcError>>
Bluesky.undo: AtpAgent -> BlockRef   -> Task<Result<UndoResult, XrpcError>>

/// Undo by target (requires lookup to find the record)
Bluesky.unlikePost:   AtpAgent -> PostRef -> Task<Result<UndoResult, XrpcError>>
Bluesky.unrepostPost: AtpAgent -> PostRef -> Task<Result<UndoResult, XrpcError>>
```

Each ref type carries collection metadata so `undo` knows what to delete.
`WasNotPresent` is returned when the record was already gone (404) — not an
error, just "nothing to do." True errors (network, auth) remain in the
`Error` case.

The existing `unlike`, `unrepost`, `unfollow`, `unblock` become deprecated
aliases or are removed.

## 6. ImageMime DU

**Problem**: MIME type is a bare `string` — easy to typo, no discoverability.

**Solution**: A discriminated union with escape hatch.

```fsharp
type ImageMime =
    | Png
    | Jpeg
    | Gif
    | Webp
    | Custom of string

type ImageUpload =
    { Data: byte[]
      MimeType: ImageMime  // was: string
      AltText: string }
```

Internally converts to the protocol string (e.g. `Png` -> `"image/png"`).

## 7. Pre-built Paginators

**Problem**: `Xrpc.paginate` requires providing TypeId, cursor extractor,
and cursor setter — extremely verbose for common queries.

**Solution**: Pre-built wrappers for common paginated endpoints.

```fsharp
Bluesky.paginateTimeline:
    AtpAgent -> int64 option -> IAsyncEnumerable<Result<GetTimeline.Output, XrpcError>>

Bluesky.paginateFollowers:
    AtpAgent -> string -> int64 option -> IAsyncEnumerable<Result<GetFollowers.Output, XrpcError>>

Bluesky.paginateNotifications:
    AtpAgent -> int64 option -> IAsyncEnumerable<Result<ListNotifications.Output, XrpcError>>
```

Just wrappers around `Xrpc.paginate` with boilerplate pre-filled. The raw
`Xrpc.paginate` stays available for custom/uncommon queries.

## Non-Goals

These came up during review but are out of scope:

- **Typed codegen for `unknown` record fields**: Would require teaching the
  code generator Bluesky-specific knowledge (cross-layer leaking). Extension
  properties solve the UX problem without this.
- **Engagement contents (likers list, etc.)**: These require separate API
  calls (`getLikes`, `getRepostedBy`). Not a library limitation — the Bluesky
  API returns counts on posts, full lists are separate endpoints.
- **Typed cursor/convoId/messageId**: These are server-assigned opaque strings.
  Wrapping them adds ceremony without preventing misuse.
- **Rich text DMs in Chat convenience API**: Low demand. Consumers can use
  the raw XRPC wrapper with `AtpAgent.withChatProxy` for this.

## Impact on Example Program.fs

After all changes, the example simplifies significantly:

```fsharp
// Auth: 1 line instead of 3
let! agent = Bluesky.login "https://bsky.social" handle password

// Post text: direct property instead of JSON fishing
let text = item.Post.Text

// Profiles: no stringifying
let! profile = Bluesky.getProfile agent session.Handle

// Replies: no raw JSON, no confusion
let! reply = Bluesky.replyTo agent "Great post!" parentRef

// Undo: by ref or by target, clear result
let! result = Bluesky.undo agent likeRef
let! result = Bluesky.unlikePost agent postRef

// Images: type-safe MIME
{ Data = bytes; MimeType = Jpeg; AltText = "Photo" }

// Pagination: one-liner
let pages = Bluesky.paginateTimeline agent (Some 10L)
```
