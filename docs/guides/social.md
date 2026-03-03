---
title: Social Actions
category: Type Reference
categoryindex: 2
index: 7
description: Follow, block, mute, and undo social actions on Bluesky
keywords: follow, block, mute, undo, social, bluesky
---

# Social Actions

Follow, block, and mute users on Bluesky, with typed refs and undo support.

## Domain Types

### FollowRef

Returned by `Bluesky.follow` -- pass to `unfollow` or `undoFollow` to undo.

| Field | Type | Description |
|-------|------|-------------|
| `Uri` | `AtUri` | AT-URI of the follow record |

### BlockRef

Returned by `Bluesky.block` -- pass to `unblock` or `undoBlock` to undo.

| Field | Type | Description |
|-------|------|-------------|
| `Uri` | `AtUri` | AT-URI of the block record |

### LikeRef

Returned by `Bluesky.like` -- pass to `unlike` or `undoLike` to undo.

| Field | Type | Description |
|-------|------|-------------|
| `Uri` | `AtUri` | AT-URI of the like record |

### RepostRef

Returned by `Bluesky.repost` -- pass to `unrepost` or `undoRepost` to undo.

| Field | Type | Description |
|-------|------|-------------|
| `Uri` | `AtUri` | AT-URI of the repost record |

### ListBlockRef

Returned by `Bluesky.blockModList` -- pass to `unblockModList` to undo.

| Field | Type | Description |
|-------|------|-------------|
| `Uri` | `AtUri` | AT-URI of the list block record |

### UndoResult

Discriminated union returned by `undo*` functions indicating whether the action was reversed.

| Case | Description |
|------|-------------|
| `Undone` | The record was deleted successfully |
| `WasNotPresent` | There was nothing to undo (e.g. the post was not liked) |

Only `unlikePost` and `unrepostPost` can return `WasNotPresent` (they check viewer state). The ref-based `undoLike`, `undoRepost`, `undoFollow`, and `undoBlock` always return `Undone` because `deleteRecord` is idempotent.

## Functions

### Following

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.follow` | `agent` `target:Did\|ProfileSummary\|Profile` | `Result<FollowRef, XrpcError>` | Follow a user |
| `Bluesky.followByHandle` | `agent` `identifier:string` | `Result<FollowRef, XrpcError>` | Follow by handle or DID string (resolves automatically) |
| `Bluesky.unfollow` | `agent` `followRef:FollowRef` | `Result<unit, XrpcError>` | Unfollow by ref |
| `Bluesky.undoFollow` | `agent` `followRef:FollowRef` | `Result<UndoResult, XrpcError>` | Unfollow by ref, returning `UndoResult` |

```fsharp
taskResult {
    // Follow using a profile's DID
    let! followRef = Bluesky.follow agent profile

    // Or by handle string
    let! followRef2 = Bluesky.followByHandle agent "alice.bsky.social"

    // Undo
    do! Bluesky.unfollow agent followRef
}
```

### Blocking

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.block` | `agent` `target:Did\|ProfileSummary\|Profile` | `Result<BlockRef, XrpcError>` | Block a user |
| `Bluesky.blockByHandle` | `agent` `identifier:string` | `Result<BlockRef, XrpcError>` | Block by handle or DID string (resolves automatically) |
| `Bluesky.unblock` | `agent` `blockRef:BlockRef` | `Result<unit, XrpcError>` | Unblock by ref |
| `Bluesky.undoBlock` | `agent` `blockRef:BlockRef` | `Result<UndoResult, XrpcError>` | Unblock by ref, returning `UndoResult` |
| `Bluesky.blockModList` | `agent` `listUri:AtUri` | `Result<ListBlockRef, XrpcError>` | Block an entire moderation list |
| `Bluesky.unblockModList` | `agent` `listBlockRef:ListBlockRef` | `Result<unit, XrpcError>` | Unblock a moderation list |

```fsharp
taskResult {
    let! blockRef = Bluesky.block agent profile
    let! blockRef2 = Bluesky.blockByHandle agent "spammer.example.com"

    // Moderation list
    let! listBlockRef = Bluesky.blockModList agent modListUri
    do! Bluesky.unblockModList agent listBlockRef
}
```

### Muting

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.muteUser` | `agent` `target:Did\|ProfileSummary\|Profile` | `Result<unit, XrpcError>` | Mute an account |
| `Bluesky.muteUserByHandle` | `agent` `identifier:string` | `Result<unit, XrpcError>` | Mute by handle or DID string |
| `Bluesky.unmuteUser` | `agent` `target:Did\|ProfileSummary\|Profile` | `Result<unit, XrpcError>` | Unmute an account |
| `Bluesky.unmuteUserByHandle` | `agent` `identifier:string` | `Result<unit, XrpcError>` | Unmute by handle or DID string |
| `Bluesky.muteModList` | `agent` `listUri:AtUri` | `Result<unit, XrpcError>` | Mute all accounts on a moderation list |
| `Bluesky.unmuteModList` | `agent` `listUri:AtUri` | `Result<unit, XrpcError>` | Unmute a moderation list |
| `Bluesky.muteThread` | `agent` `root:TimelinePost\|PostRef\|AtUri` | `Result<unit, XrpcError>` | Mute a thread |
| `Bluesky.unmuteThread` | `agent` `root:TimelinePost\|PostRef\|AtUri` | `Result<unit, XrpcError>` | Unmute a thread |

```fsharp
taskResult {
    do! Bluesky.muteUser agent profile
    do! Bluesky.muteUserByHandle agent "noisy.bsky.social"
    do! Bluesky.muteThread agent post
    do! Bluesky.muteModList agent modListUri
}
```

### Generic Undo

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.undo` | `agent` `ref:LikeRef\|RepostRef\|FollowRef\|BlockRef\|ListBlockRef` | `Result<UndoResult, XrpcError>` | Delete any ref type via SRTP |

```fsharp
taskResult {
    let! likeRef = Bluesky.like agent post
    let! followRef = Bluesky.follow agent profile

    // Generic undo works with any ref type
    let! _ = Bluesky.undo agent likeRef
    let! _ = Bluesky.undo agent followRef
    ()
}
```

## SRTP Polymorphism

Social action functions accept multiple types via SRTP:

- `follow`, `block`, `muteUser`, `unmuteUser` accept `Did`, `ProfileSummary`, or `Profile`
- `muteThread`, `unmuteThread` accept `TimelinePost`, `PostRef`, or `AtUri`
- `undo` accepts `LikeRef`, `RepostRef`, `FollowRef`, `BlockRef`, or `ListBlockRef`

```fsharp
taskResult {
    let! profile = Bluesky.getProfile agent "alice.bsky.social"

    // Pass the Profile directly -- no need to extract .Did
    let! followRef = Bluesky.follow agent profile
    do! Bluesky.muteUser agent profile

    // Pass a TimelinePost directly to mute its thread
    let! page = Bluesky.getTimeline agent (Some 1L) None
    do! Bluesky.muteThread agent page.Items.Head.Post
}
```
