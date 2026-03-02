---
title: Social Actions
category: Guides
categoryindex: 1
index: 5
description: Like, repost, follow, block, and undo social actions on Bluesky
keywords: like, repost, follow, block, undo, social, bluesky
---

# Social Actions

Social actions on Bluesky -- likes, reposts, follows, and blocks -- are records in your repository. Creating the record performs the action; deleting it undoes it. FSharp.ATProto wraps this with typed ref values (`LikeRef`, `RepostRef`, `FollowRef`, `BlockRef`) so the compiler keeps everything straight.

All examples use `taskResult {}`. See the [Error Handling guide](error-handling.html) for details.

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax
```

## Liking a Post

`Bluesky.like` accepts a `TimelinePost` (or `PostRef`) and returns a `LikeRef`. Hold onto the ref to unlike later:

```fsharp
taskResult {
    let! likeRef = Bluesky.like agent post
    printfn "Liked! Record: %s" (AtUri.value likeRef.Uri)
}
```

## Reposting

`Bluesky.repost` also accepts a `TimelinePost` (or `PostRef`) and returns a `RepostRef`:

```fsharp
taskResult {
    let! repostRef = Bluesky.repost agent post
    printfn "Reposted! Record: %s" (AtUri.value repostRef.Uri)
}
```

## Following a User

`Bluesky.follow` takes a typed [DID](../concepts.html). Use `profile.Did` from any profile view:

```fsharp
taskResult {
    let! followRef = Bluesky.follow agent profile.Did
    printfn "Followed! Record: %s" (AtUri.value followRef.Uri)
}
```

When you only have a handle string, use `followByHandle` -- it resolves the identifier for you. It also accepts DID strings:

```fsharp
let! followRef = Bluesky.followByHandle agent "alice.bsky.social"
```

## Blocking a User

`Bluesky.block` takes a typed DID, just like `follow`. Blocking prevents the other user from seeing your content and removes their content from your feeds:

```fsharp
taskResult {
    let! blockRef = Bluesky.block agent profile.Did
    printfn "Blocked! Record: %s" (AtUri.value blockRef.Uri)
}
```

There is also `blockByHandle` and `blockModList` (which takes an [AT-URI](../concepts.html) of a moderation list):

```fsharp
let! blockRef = Bluesky.blockByHandle agent "spammer.example.com"
let! listBlockRef = Bluesky.blockModList agent modListUri
```

## Undoing Actions

FSharp.ATProto provides several layers for undoing social actions. The simplest: pass the ref you got back when creating the action:

```fsharp
taskResult {
    do! Bluesky.unlike agent likeRef       // LikeRef -> unit
    do! Bluesky.unrepost agent repostRef   // RepostRef -> unit
    do! Bluesky.unfollow agent followRef   // FollowRef -> unit
    do! Bluesky.unblock agent blockRef     // BlockRef -> unit
}
```

Each function requires its matching ref type, so the compiler prevents mix-ups.

For more control, the `undoLike` / `undoRepost` / `undoFollow` / `undoBlock` variants return an `UndoResult` DU (`Undone | WasNotPresent`). If you do not have the original ref but you do have the post, `unlikePost` and `unrepostPost` look up the viewer state and delete the record for you -- these are where `WasNotPresent` is genuinely meaningful. The generic `Bluesky.undo` (inline SRTP) accepts any ref type for polymorphic code. And at the lowest level, `Bluesky.deleteRecord` takes a raw `AtUri`.

| Situation | Function |
|---|---|
| You have the ref from when you created the action | `unlike` / `unrepost` / `unfollow` / `unblock` |
| You want to know if the undo did anything | `undoLike` / `undoRepost` / `undoFollow` / `undoBlock` |
| You only have the post, not the original ref | `unlikePost` / `unrepostPost` |
| Writing generic code over different action types | `undo` |
| You have a raw AT-URI | `deleteRecord` |

## Checking Viewer State

The AT Protocol includes viewer state on posts and profiles that tells you your relationship with that content. This is useful for toggle behavior (like/unlike) or checking existing relationships before acting.

### Posts

With domain types, use `TimelinePost.IsLiked` / `IsReposted` directly. At the raw XRPC layer, `PostView.Viewer` contains `Like` and `Repost` fields (`AtUri option`). When `Some`, the value is the AT-URI of your record:

```fsharp
taskResult {
    let postRef = { PostRef.Uri = post.Uri; Cid = post.Cid }

    match post.Viewer |> Option.bind (fun v -> v.Like) with
    | Some _ ->
        let! _ = Bluesky.unlikePost agent postRef
        ()
    | None ->
        let! _ = Bluesky.like agent postRef
        ()
}
```

### Profiles

`ProfileViewDetailed.Viewer` has `Following`, `FollowedBy`, `Blocking`, `BlockedBy`, and `Muted` fields. The `Following` and `Blocking` fields are `AtUri option` pointing to your records:

```fsharp
match profile.Viewer with
| Some viewer ->
    match viewer.Following with
    | Some _ -> printfn "You follow them"
    | None -> printfn "You do not follow them"

    match viewer.BlockedBy with
    | Some true -> printfn "They are blocking you"
    | _ -> ()
| None -> ()
```

## Related Guides

- [Notifications](notifications.html) -- unread count, fetching, and marking as seen
- [Moderation](moderation.html) -- muting, reporting, and moderation lists
- [Profiles](profiles.html) -- searching, listing followers/follows, suggested follows
- [Posts](posts.html) -- creating, replying, quoting, searching, threads
