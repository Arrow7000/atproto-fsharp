# Dogfooding Feedback Design

Design for improvements surfaced by porting bsky-agent (Alice) to use atproto-fsharp.

Source: `../bsky-agent/fsharp/ATPROTO-LIBRARY-NOTES.md`

## Bug Fixes

### B1: ReplyRefParentUnion deserialization failure

`Bluesky.getTimeline` throws `Failed to find union case field for AppBskyFeed+Defs+ReplyRefParentUnion: expected Case`. The generated union type's JSON converter can't handle the `$type` discriminator for reply references in timeline posts. Completely breaks timeline polling.

Root cause likely in codegen: the `ReplyRefParentUnion` may be missing the `JsonFSharpConverter` attribute with `InternalTag` + `$type` discriminator, or the `$type` values don't match the `JsonName` attributes on the cases.

### B4: PostViewEmbedUnion deserialization failure

`Bluesky.getPostThread` throws `Failed to find union case field for AppBskyFeed+Defs+PostViewEmbedUnion: expected Case` when posts contain embedded content. Same root cause as B1 — the codegen-produced union type doesn't deserialize correctly. Fix alongside B1.

### B2: Chat proxy doesn't work via bsky.social entryway

Chat endpoints return `XRPC 404 XRPCNotSupported` when using `bsky.social` as base URL. The entryway doesn't proxy chat requests — the library needs to resolve the user's actual PDS from their DID document after login and use that as the base URL for subsequent requests.

Fix: After successful login, resolve the DID document, extract the PDS service endpoint, and update `AtpAgent.BaseUrl`.

### B3: withChatProxy must be called after login

`AtpAgent.withChatProxy` creates a record copy, but `Session` is mutable. If called before `login`, the copy captures `Session = None` and all chat requests fail with 401.

Fix: Make `withChatProxy` share the session reference with the original agent, or document/enforce the ordering, or resolve lazily.

## Feature: Profile Mutation

### API

```fsharp
// Full control -- transform function applied to current profile record.
// Uses swapRecord for optimistic concurrency. No retry on conflict.
Bluesky.updateProfile :
    AtpAgent -> (AppBskyActor.Profile.Profile -> AppBskyActor.Profile.Profile)
    -> Task<Result<unit, XrpcError>>

// Field-specific setters. Auto-retry once on conflict (safe: single-field merge).
// None = clear the field, Some = set it.
Bluesky.setDisplayName : AtpAgent -> string option -> Task<Result<unit, XrpcError>>
Bluesky.setDescription : AtpAgent -> string option -> Task<Result<unit, XrpcError>>

// Image setters. Upload blob first, then read-merge-write the profile record.
// None = clear the image, Some = upload and set.
Bluesky.setAvatar : AtpAgent -> (byte[] * ImageMime) option -> Task<Result<unit, XrpcError>>
Bluesky.setBanner : AtpAgent -> (byte[] * ImageMime) option -> Task<Result<unit, XrpcError>>
```

### Internal mechanism

1. `com.atproto.repo.getRecord` to read current `app.bsky.actor.profile` (collection `app.bsky.actor.profile`, rkey `self`)
2. Deserialize JSON record to `AppBskyActor.Profile.Profile`
3. Apply modification (transform function or single-field update)
4. Serialize back to JSON, `com.atproto.repo.putRecord` with `swapRecord = Some currentCid`
5. Field setters: on 409 conflict, re-read and retry once. `updateProfile`: return error on conflict.

## Feature: Notification Refactor

Replace flat `Notification` type with DU-based content.

### Types

```fsharp
type NotificationContent =
    | Like of post: PostRef
    | Repost of post: PostRef
    | Follow
    | Reply of text: string * inReplyTo: PostRef
    | Mention of text: string
    | Quote of text: string * quotedPost: PostRef
    | StarterpackJoined of starterPackUri: AtUri  // TODO: replace with StarterPackRef once it exists
    | Unknown of reason: string

type Notification =
    { RecordUri : AtUri
      Author : ProfileSummary
      Content : NotificationContent
      IsRead : bool
      IndexedAt : DateTimeOffset }
```

### Mapping

- `RecordUri` from raw `Notification.Uri`
- `Author` from raw `Notification.Author` via `ProfileSummary.ofView`
- `Content` mapped from `Reason` + `ReasonSubject` + `Record`:
  - Like/Repost: `PostRef` from `ReasonSubject` (the liked/reposted post URI) + `Cid` from the record's subject
  - Reply/Mention/Quote: parse `Record` JsonElement to extract `text` field; `PostRef` from `ReasonSubject`
  - Follow/StarterpackJoined: straightforward from `ReasonSubject`
- `IsRead`, `IndexedAt` mapped directly

## Feature: ConvoSummary Last Message

### Types

```fsharp
type LastMessage =
    { Text : string
      Sender : Did
      SentAt : DateTimeOffset }

type ConvoSummary =
    { Id : string
      Members : ProfileSummary list
      LastMessage : LastMessage option
      UnreadCount : int64
      IsMuted : bool }
```

### Mapping

Extract from `ConvoView.LastMessage` when it's a `MessageView` case: text, sender DID, sentAt. `None` when no last message or deleted message.

## Feature: FeedItem Reply Parent

### Types

```fsharp
type FeedItem =
    { Post : TimelinePost
      Reason : FeedReason option
      ReplyParent : TimelinePost option }
```

### Mapping

From `FeedViewPost.Reply` (a `ReplyRef` with `Parent: ReplyRefParentUnion`). When parent is `PostView`, map via `TimelinePost.ofPostView`. When `NotFoundPost`, `BlockedPost`, or `Unknown`, set to `None`.

## Feature: TimelinePost Embeds

### Types

```fsharp
type PostImage =
    { Thumb : Uri
      Fullsize : Uri
      Alt : string }

type PostVideo =
    { Thumbnail : Uri option
      Playlist : Uri option
      Alt : string option }

type PostExternalLink =
    { Uri : Uri
      Title : string
      Description : string
      Thumb : Uri option }

type PostMediaEmbed =
    | Images of PostImage list
    | Video of PostVideo

type PostEmbed =
    | Images of PostImage list
    | Video of PostVideo
    | ExternalLink of PostExternalLink
    | QuotedPost of AppBskyEmbed.Record.ViewRecordUnion
    | RecordWithMedia of AppBskyEmbed.Record.ViewRecordUnion * PostMediaEmbed
    | Unknown

// Added to TimelinePost:
type TimelinePost =
    { ... existing fields ...
      Embed : PostEmbed option }
```

`QuotedPost` uses the generated `ViewRecordUnion` rather than recursive `TimelinePost` mapping to avoid circular complexity.

## Feature: ChatMessage Embeds

### Types

```fsharp
type ChatMessage =
    | Message of
        {| Id : string
           Text : string
           Sender : Did
           SentAt : DateTimeOffset
           Embed : PostEmbed option |}
    | Deleted of {| Id : string; Sender : Did |}
```

Reuses `PostEmbed` from above. In practice, chat embeds are only record embeds (quoted posts), so this will always be `QuotedPost` or `None`.

## Out of Scope

- `StarterPackRef` typed URI wrapper (noted as TODO)
- Typed URI wrappers beyond `PostRef` (future work)
- `setProfile` convenience function (covered by `updateProfile` transform + individual setters)
- `replyTo` changes (already resolves root automatically)
- `Profile.IsFollowing` naming (our naming is intentionally clearer)
