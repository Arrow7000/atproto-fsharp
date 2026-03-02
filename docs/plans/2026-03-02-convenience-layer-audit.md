# Convenience Layer Audit

The convenience layer (`Bluesky.fs`, `Chat.fs`, `Identity.fs`, `RichText.fs`) sits on top of the auto-generated XRPC bindings. It should be the primary API surface -- consumers should rarely need to touch the generated layer directly.

**Writes are solid.** `Bluesky.post`, `replyTo`, `like`, `follow`, `block`, `undo`, etc. return domain types (`PostRef`, `LikeRef`, `FollowRef`), auto-parse rich text, and resolve thread roots. The consumer never thinks about records or NSIDs.

**Reads are raw.** Every read function returns generated types directly. Consumers must pattern-match on protocol unions, navigate nested generated type hierarchies, and sprinkle `Option.map`/`Option.defaultValue` everywhere for values that are conceptually non-optional (like a profile's post count).

**Chat is half-done.** `Chat.sendMessage` only supports plain text. Sending a message with a link requires dropping to the raw `ChatBskyConvo.SendMessage.call` API, manually parsing facets, and manually setting the chat proxy header -- all things the library handles automatically for posts.

## Layer architecture

```
Consumer code
    │
    ▼
Convenience layer (Bluesky.fs, Chat.fs, Identity.fs, RichText.fs)
    │  hand-written, domain types, should handle 99% of use cases
    │
    ▼
Generated layer (Generated.fs)
    │  auto-generated from lexicon schemas
    │  raw XRPC bindings, generic Input/Output/Params types
    │  escape hatch for power users
    │
    ▼
Core (AtpAgent, Xrpc, session refresh, rate limiting)
    │
    ▼
Syntax + DRISL (DID, Handle, NSID, CBOR, CID)
```

## Problem 1: Read functions return raw generated types

Every read function currently returns the generated type directly:

| Function | Returns |
|----------|---------|
| `getProfile` | `AppBskyActor.GetProfile.Output` |
| `getTimeline` | `AppBskyFeed.GetTimeline.Output` |
| `getPostThread` | `AppBskyFeed.GetPostThread.Output` |
| `getNotifications` | `AppBskyNotification.ListNotifications.Output` |
| `getFollowers` | `AppBskyGraph.GetFollowers.Output` |
| `getFollows` | `AppBskyGraph.GetFollows.Output` |
| `Chat.listConvos` | `ChatBskyConvo.ListConvos.Output` |
| `Chat.getMessages` | `ChatBskyConvo.GetMessages.Output` |
| `Chat.sendMessage` | `ChatBskyConvo.SendMessage.Output` |
| `Chat.muteConvo` | `ChatBskyConvo.MuteConvo.Output` |

This means consumers must:

- **Match on generated unions**: `AppBskyFeed.Defs.FeedViewPostReasonUnion.ReasonRepost`, `ChatBskyConvo.GetMessages.OutputMessagesItem.MessageView`, `AppBskyFeed.Defs.ThreadViewPostParentUnion.ThreadViewPost`, etc.
- **Handle deeply nested optional fields**: `profile.PostsCount |> Option.map string |> Option.defaultValue "?"` for what should just be an `int64`.
- **Import generated namespaces**: `AppBskyNotification.ListNotifications.NotificationReason`, `AppBskyFeed.Defs.ThreadViewPostParentUnion`, etc.
- **Understand `Output` naming**: `ChatBskyConvo.MuteConvo.Output` tells you nothing about what it contains.

### What to do

Introduce domain types for reads, matching the same pattern as writes. Examples:

**Profile:**
```fsharp
type Profile = {
    Did: Did
    Handle: Handle
    DisplayName: string       // default "" not option
    Description: string       // default "" not option
    Avatar: string option
    PostsCount: int64         // default 0 not option
    FollowersCount: int64
    FollowsCount: int64
    IsFollowing: bool         // extracted from ViewerState
    IsFollowedBy: bool
}
```

**Timeline:**
```fsharp
type TimelinePost = {
    Uri: AtUri
    Cid: Cid
    Author: ProfileSummary
    Text: string
    Facets: AppBskyRichtext.Facet.Facet list
    LikeCount: int64
    RepostCount: int64
    ReplyCount: int64
    IndexedAt: DateTimeOffset
    IsLiked: bool
    IsReposted: bool
}

type FeedReason =
    | Repost of by: ProfileSummary
    | Pin

type FeedItem = {
    Post: TimelinePost
    Reason: FeedReason option
}

type TimelinePage = {
    Items: FeedItem list
    Cursor: string option
}
```

**Notifications:**
```fsharp
type NotificationKind = Like | Repost | Follow | Mention | Reply | Quote | StarterpackJoined

type Notification = {
    Kind: NotificationKind
    Author: ProfileSummary
    SubjectUri: AtUri option
    IsRead: bool
    IndexedAt: DateTimeOffset
}
```

**Chat messages:**
```fsharp
type ChatMessage =
    | Message of {| Id: string; Text: string; Sender: Did; SentAt: DateTimeOffset |}
    | Deleted of {| Id: string; Sender: Did |}
```

**Thread:**
```fsharp
type ThreadNode =
    | Post of ThreadPost
    | NotFound of AtUri
    | Blocked of AtUri

type ThreadPost = {
    Post: TimelinePost
    Parent: ThreadNode option
    Replies: ThreadNode list
}
```

The generated types should still be accessible for power users who need the full protocol detail. The convenience functions should map from generated types to domain types internally.

## Problem 2: `Chat.sendMessage` doesn't support rich text

`Bluesky.post` automatically parses rich text (links, mentions, tags) via `RichText.parse`. `Chat.sendMessage` always sends `Facets = None`.

Sending a DM with a link currently requires:
1. Manually calling `RichText.parse`
2. Manually creating a chat proxy agent with `AtpAgent.withChatProxy`
3. Calling the raw `ChatBskyConvo.SendMessage.call`
4. Wrapping facets in `Some`/`None`

### What to do

`Chat.sendMessage` should auto-parse rich text, just like `Bluesky.post` does. This is a one-line change in `Chat.fs` -- call `RichText.parse` on the text before building the `MessageInput`.

## Problem 3: Many common operations have no convenience wrapper

### Missing reads (commonly needed)

| Operation | Generated endpoint | Notes |
|-----------|-------------------|-------|
| Search posts | `AppBskyFeed.SearchPosts` | |
| Search actors | `AppBskyActor.SearchActors` | |
| Get a user's posts | `AppBskyFeed.GetAuthorFeed` | Very common |
| Get a user's likes | `AppBskyFeed.GetActorLikes` | |
| Get likes on a post | `AppBskyFeed.GetLikes` | |
| Get reposts of a post | `AppBskyFeed.GetRepostedBy` | |
| Get quotes of a post | `AppBskyFeed.GetQuotes` | |
| Get multiple posts | `AppBskyFeed.GetPosts` | Used internally but not exposed |
| Get multiple profiles | `AppBskyActor.GetProfiles` | |
| Get suggested follows | `AppBskyGraph.GetSuggestedFollowsByActor` | |
| Get unread notification count | `AppBskyNotification.ListUnreadCount` | |
| Mark notifications as seen | `AppBskyNotification.UpdateSeen` | |
| Get preferences | `AppBskyActor.GetPreferences` | |

### Missing writes (commonly needed)

| Operation | Generated endpoint | Notes |
|-----------|-------------------|-------|
| Mute/unmute user | `AppBskyGraph.MuteActor` / `UnmuteActor` | |
| Mute/unmute thread | `AppBskyGraph.MuteThread` / `UnmuteThread` | |
| Report content | `ComAtprotoModeration.CreateReport` | |
| Bookmarks (add/remove/get) | `AppBskyBookmark.*` | |
| Update handle | `ComAtprotoIdentity.UpdateHandle` | |
| Chat: accept convo | `ChatBskyConvo.AcceptConvo` | |
| Chat: leave convo | `ChatBskyConvo.LeaveConvo` | |
| Chat: reactions | `ChatBskyConvo.AddReaction` / `RemoveReaction` | |
| Chat: get single convo | `ChatBskyConvo.GetConvo` | |

### Missing reads (less common, lower priority)

| Operation | Generated endpoint |
|-----------|-------------------|
| Lists (get, create, manage) | `AppBskyGraph.GetList`, `GetLists`, etc. |
| Starter packs | `AppBskyGraph.GetStarterPack`, etc. |
| Feed generators | `AppBskyFeed.GetFeedGenerator`, etc. |
| Custom feeds | `AppBskyFeed.GetFeed` |
| Labeler services | `AppBskyLabeler.GetServices` |
| Video upload | `AppBskyVideo.*` |

## Problem 4: Generated type naming

Every XRPC endpoint has types called `Input`, `Output`, and `Params`. These names tell the consumer nothing:

- `ChatBskyConvo.MuteConvo.Output` -- what does this contain?
- `AppBskyFeed.GetTimeline.Output` -- is this a page? A list? A single post?
- `AppBskyActor.GetProfile.Output` -- this is actually `ProfileViewDetailed` but you'd never know from the name.

This is a code generator issue. The generated types are correct but not consumer-friendly. The convenience layer should insulate consumers from these names entirely by providing domain types (see Problem 1).

## Summary of priorities

1. **`Chat.sendMessage` rich text** -- small fix, high impact, inconsistency with `Bluesky.post`
2. **Domain types for reads** -- the biggest gap, affects every consumer
3. **Missing common operations** -- `getAuthorFeed`, `searchPosts`, `searchActors`, mute/unmute, bookmarks, notifications seen, etc.
4. **Generated type naming** -- addressed by (2), consumers shouldn't see `Output` types
