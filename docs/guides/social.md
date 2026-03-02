---
title: Social Actions
category: Guides
categoryindex: 1
index: 5
description: Like, repost, follow, block, mute, and report on Bluesky with FSharp.ATProto
keywords: like, repost, follow, block, mute, report, undo, social, bluesky
---

# Social Actions

Social actions on Bluesky -- likes, reposts, follows, and blocks -- are records in your repository. Creating the record performs the action; deleting the record undoes it. FSharp.ATProto wraps this with typed ref values (`LikeRef`, `RepostRef`, `FollowRef`, `BlockRef`) so the compiler keeps everything straight for you.

All examples below assume you have an authenticated agent:

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax
```

## Liking a Post

`Bluesky.like` takes a `PostRef` (a record with `Uri` and `Cid` fields) and returns a `LikeRef`. If you have a `PostView` from a feed or search result, build the `PostRef` from its fields:

```fsharp
task {
    let postRef =
        { PostRef.Uri = post.Uri
          Cid = post.Cid }

    let! result = Bluesky.like agent postRef

    match result with
    | Ok likeRef ->
        // Hold onto likeRef to unlike later
        printfn "Liked! Record: %s" (AtUri.value likeRef.Uri)
    | Error err -> printfn "Failed: %A" err
}
```

The `LikeRef` wraps the AT-URI of the like record in your repository. Pass it to one of the undo functions when you want to unlike.

## Reposting

`Bluesky.repost` also takes a `PostRef` and returns a `RepostRef`:

```fsharp
task {
    let postRef =
        { PostRef.Uri = post.Uri
          Cid = post.Cid }

    let! result = Bluesky.repost agent postRef

    match result with
    | Ok repostRef -> printfn "Reposted! Record: %s" (AtUri.value repostRef.Uri)
    | Error err -> printfn "Failed: %A" err
}
```

## Following a User

`Bluesky.follow` takes a typed `Did`. If you have a `ProfileView` or `ProfileViewDetailed`, use its `Did` field directly:

```fsharp
task {
    let! result = Bluesky.follow agent profile.Did

    match result with
    | Ok followRef -> printfn "Followed! Record: %s" (AtUri.value followRef.Uri)
    | Error err -> printfn "Failed: %A" err
}
```

If you have a DID as a raw string, parse it first:

```fsharp
let did = Did.parse "did:plc:z72i7hdynmk6r22z27h6tvur" |> Result.defaultWith failwith
let! result = Bluesky.follow agent did
```

When you only have a handle string (or a DID string and do not want to parse it yourself), use `followByHandle` -- it resolves the identifier for you:

```fsharp
let! result = Bluesky.followByHandle agent "alice.bsky.social"
```

`followByHandle` also accepts a DID string. If the string starts with `did:`, it is parsed directly rather than resolved as a handle.

## Blocking a User

`Bluesky.block` takes a typed `Did`, just like `follow`:

```fsharp
task {
    let! result = Bluesky.block agent profile.Did

    match result with
    | Ok blockRef -> printfn "Blocked! Record: %s" (AtUri.value blockRef.Uri)
    | Error err -> printfn "Failed: %A" err
}
```

Blocking prevents the other user from seeing your content and removes their content from your feeds. The block takes effect immediately on the server, but cached views in clients may take a moment to update.

There is a `blockByHandle` convenience as well:

```fsharp
let! result = Bluesky.blockByHandle agent "spammer.example.com"
```

## Undoing Actions

FSharp.ATProto provides several layers for undoing social actions, from simple to flexible. Pick the one that fits your situation.

### Quick undo (ref-based)

If you still have the ref value you got back when you created the action, these are the simplest option. They delete the record and return `unit`:

```fsharp
task {
    let! _ = Bluesky.unlike agent likeRef // LikeRef -> unit
    let! _ = Bluesky.unrepost agent repostRef // RepostRef -> unit
    let! _ = Bluesky.unfollow agent followRef // FollowRef -> unit
    let! _ = Bluesky.unblock agent blockRef // BlockRef -> unit
    return ()
}
```

Each function takes the matching ref type, so the compiler catches mix-ups -- you cannot accidentally pass a `LikeRef` to `unfollow`.

### Typed undo with UndoResult

The `undoLike`, `undoRepost`, `undoFollow`, and `undoBlock` functions return `UndoResult` instead of `unit`:

```fsharp
type UndoResult =
    | Undone
    | WasNotPresent
```

```fsharp
task {
    let! result = Bluesky.undoLike agent likeRef

    match result with
    | Ok Undone -> printfn "Like removed"
    | Ok WasNotPresent -> printfn "Was not liked"
    | Error err -> printfn "Failed: %A" err
}
```

Because the AT Protocol's `deleteRecord` is idempotent, these ref-based functions always return `Undone` even if the record was already deleted. The `WasNotPresent` case only comes into play with target-based undo (below).

The full set: `Bluesky.undoLike`, `Bluesky.undoRepost`, `Bluesky.undoFollow`, `Bluesky.undoBlock`.

### Target-based undo

What if you do not have the original `LikeRef` but you do have the post? `unlikePost` and `unrepostPost` take a `PostRef`, fetch the viewer state to find your like or repost, and delete it:

```fsharp
task {
    let postRef =
        { PostRef.Uri = post.Uri
          Cid = post.Cid }

    let! result = Bluesky.unlikePost agent postRef

    match result with
    | Ok Undone -> printfn "Unliked"
    | Ok WasNotPresent -> printfn "You had not liked this post"
    | Error err -> printfn "Failed: %A" err
}
```

These are the only undo functions where `WasNotPresent` is genuinely meaningful -- the library checks the server state and tells you whether there was anything to undo.

`Bluesky.unrepostPost` works the same way for reposts.

### Generic undo

`Bluesky.undo` is an inline SRTP function that works with any ref type -- `LikeRef`, `RepostRef`, `FollowRef`, or `BlockRef`. It is useful in generic code where you want to undo different kinds of actions uniformly:

```fsharp
task {
    let! result = Bluesky.undo agent likeRef // works with LikeRef
    let! result = Bluesky.undo agent followRef // works with FollowRef
    // ... and so on for any ref type
    return ()
}
```

It returns `Task<Result<UndoResult, XrpcError>>`, always yielding `Undone` on success (same semantics as the ref-based typed undo functions).

### Low-level: deleteRecord

At the bottom of the stack, `Bluesky.deleteRecord` takes a raw `AtUri` and deletes whatever record it points to. This works for any record type -- likes, reposts, follows, blocks, posts, or anything else:

```fsharp
let! result = Bluesky.deleteRecord agent someAtUri
```

### Which one should I use?

| Situation | Recommended function |
|---|---|
| You have the ref from when you created the action | `unlike` / `unrepost` / `unfollow` / `unblock` |
| You want to know if the undo actually did anything | `undoLike` / `undoRepost` / `undoFollow` / `undoBlock` |
| You only have the post, not the original ref | `unlikePost` / `unrepostPost` |
| You are writing generic code over different action types | `undo` |
| You have a raw AT-URI from somewhere | `deleteRecord` |

## Checking Viewer State

Before creating a like or follow, you may want to check whether you have already performed that action. The AT Protocol includes **viewer state** on posts and profiles that tells you your relationship with that content.

### Posts

`PostView.Viewer` is an `AppBskyFeed.Defs.ViewerState option` with fields that indicate your actions:

```fsharp
match post.Viewer with
| Some viewer ->
    match viewer.Like with
    | Some likeUri -> printfn "You already liked this (record: %s)" (AtUri.value likeUri)
    | None -> printfn "You have not liked this"

    match viewer.Repost with
    | Some repostUri -> printfn "You already reposted this (record: %s)" (AtUri.value repostUri)
    | None -> printfn "You have not reposted this"
| None -> printfn "No viewer state available"
```

The `Like` and `Repost` fields are `AtUri option`. When `Some`, the value is the AT-URI of your like or repost record. This makes it straightforward to implement toggle behavior:

```fsharp
task {
    let postRef =
        { PostRef.Uri = post.Uri
          Cid = post.Cid }

    match post.Viewer |> Option.bind (fun v -> v.Like) with
    | Some _ ->
        // Already liked -- unlike it
        let! _ = Bluesky.unlikePost agent postRef
        ()
    | None ->
        // Not liked -- like it
        let! _ = Bluesky.like agent postRef
        ()
}
```

### Profiles

`ProfileViewDetailed.Viewer` is an `AppBskyActor.Defs.ViewerState option` with relationship fields:

```fsharp
match profile.Viewer with
| Some viewer ->
    // Are you following them?
    match viewer.Following with
    | Some followUri -> printfn "You follow them (record: %s)" (AtUri.value followUri)
    | None -> printfn "You do not follow them"

    // Do they follow you?
    match viewer.FollowedBy with
    | Some _ -> printfn "They follow you"
    | None -> printfn "They do not follow you"

    // Are you blocking them?
    match viewer.Blocking with
    | Some blockUri -> printfn "You are blocking them (record: %s)" (AtUri.value blockUri)
    | None -> printfn "You are not blocking them"

    // Are they blocking you?
    match viewer.BlockedBy with
    | Some true -> printfn "They are blocking you"
    | _ -> printfn "They are not blocking you"

    // Are they muted?
    match viewer.Muted with
    | Some true -> printfn "They are muted"
    | _ -> printfn "They are not muted"
| None -> printfn "No viewer state available"
```

The `Following` and `Blocking` fields contain AT-URIs of your records, so you can pass them to `deleteRecord` if needed -- or, more idiomatically, use `unfollow` / `unblock` with the corresponding ref types.

## Listing Followers and Follows

FSharp.ATProto provides convenience wrappers around the generated XRPC queries for common social graph lookups.

### Who a User Follows

```fsharp
task {
    let! result = Bluesky.getFollows agent "alice.bsky.social" (Some 50L) None

    match result with
    | Ok page ->
        printfn "Follows %d accounts (this page):" page.Items.Length

        for profile in page.Items do
            printfn "  @%s (%s)" (Handle.value profile.Handle) (Did.value profile.Did)

        match page.Cursor with
        | Some cursor -> printfn "More results available (cursor: %s)" cursor
        | None -> printfn "End of list"
    | Error err -> printfn "Failed: %A" err
}
```

The first string parameter accepts either a handle (e.g. `"alice.bsky.social"`) or a DID string (e.g. `"did:plc:z72i7hdynmk6r22z27h6tvur"`). The second parameter is an optional limit, and the third is an optional cursor for pagination. Results come back as a `Page<ProfileSummary>` with `Items` and `Cursor` fields.

### Who Follows a User

```fsharp
task {
    let! result = Bluesky.getFollowers agent "alice.bsky.social" (Some 50L) None

    match result with
    | Ok page ->
        printfn "%d followers (this page):" page.Items.Length

        for profile in page.Items do
            printfn "  @%s" (Handle.value profile.Handle)

        match page.Cursor with
        | Some cursor -> printfn "More results available (cursor: %s)" cursor
        | None -> printfn "End of list"
    | Error err -> printfn "Failed: %A" err
}
```

### Paginating Followers

For fetching all followers across multiple pages, use the pre-built paginator:

```fsharp
let pages = Bluesky.paginateFollowers agent "alice.bsky.social" (Some 100L)
let enumerator = pages.GetAsyncEnumerator ()
let mutable hasMore = true

while hasMore do
    let! moved = enumerator.MoveNextAsync ()
    hasMore <- moved

    if hasMore then
        match enumerator.Current with
        | Ok page ->
            for profile in page.Items do
                printfn "  @%s" (Handle.value profile.Handle)
        | Error err ->
            printfn "Page error: %A" err
            hasMore <- false
```

The paginator returns an `IAsyncEnumerable` of pages. Each page is a `Result` -- the stream stops automatically when the server returns no cursor. See the [Pagination guide](pagination.html) for more details.

### Who Liked a Post

`Bluesky.getLikes` takes an `AtUri` and returns a `Page<ProfileSummary>` of users who liked the post:

```fsharp
task {
    let! result = Bluesky.getLikes agent post.Uri (Some 50L) None

    match result with
    | Ok page ->
        printfn "%d likes:" page.Items.Length

        for profile in page.Items do
            printfn "  @%s" (Handle.value profile.Handle)
    | Error err -> printfn "Failed: %A" err
}
```

### Who Reposted a Post

`Bluesky.getRepostedBy` works the same way:

```fsharp
task {
    let! result = Bluesky.getRepostedBy agent post.Uri (Some 50L) None

    match result with
    | Ok page ->
        printfn "%d reposts:" page.Items.Length

        for profile in page.Items do
            printfn "  @%s" (Handle.value profile.Handle)
    | Error err -> printfn "Failed: %A" err
}
```

Both functions accept an optional limit and an optional cursor for pagination.

### Suggested Follows

`Bluesky.getSuggestedFollows` returns follow suggestions for a given user (no pagination -- it returns a flat list):

```fsharp
task {
    let! result = Bluesky.getSuggestedFollows agent "alice.bsky.social"

    match result with
    | Ok suggestions ->
        printfn "%d suggestions:" suggestions.Length

        for profile in suggestions do
            printfn "  @%s - %s" (Handle.value profile.Handle) profile.DisplayName
    | Error err -> printfn "Failed: %A" err
}
```

## Muting

Muting hides content without the other user knowing. There are two kinds of mutes: user mutes and thread mutes.

### Muting a User

`Bluesky.muteUser` takes a handle or DID string. Muted users' posts are hidden from your feeds and notifications:

```fsharp
task {
    let! result = Bluesky.muteUser agent "annoying.bsky.social"

    match result with
    | Ok () -> printfn "Muted"
    | Error err -> printfn "Failed: %A" err
}
```

To unmute:

```fsharp
let! result = Bluesky.unmuteUser agent "annoying.bsky.social"
```

Unlike blocking, muting is invisible to the other user and does not prevent them from interacting with your content. You can check whether a user is muted via `viewer.Muted` on their profile (see [Checking Viewer State](#checking-viewer-state) above).

### Muting a Thread

`Bluesky.muteThread` takes the `AtUri` of the thread root post. Posts in the muted thread are hidden from your notifications:

```fsharp
task {
    let! result = Bluesky.muteThread agent threadRootUri

    match result with
    | Ok () -> printfn "Thread muted"
    | Error err -> printfn "Failed: %A" err
}
```

To unmute a thread:

```fsharp
let! result = Bluesky.unmuteThread agent threadRootUri
```

Thread muting is useful for conversations you started or participated in that have become noisy.

## Reporting Content

`Bluesky.reportContent` sends a moderation report to the server. It takes a `ReportSubject` (what to report), a `ReasonType` (why), and an optional free-text description.

The `ReportSubject` DU has two cases:

```fsharp
[<RequireQualifiedAccess>]
type ReportSubject =
    | Account of Did     // Report an entire account
    | Record of PostRef  // Report a specific post
```

### Reporting a Post

```fsharp
task {
    let postRef =
        { PostRef.Uri = post.Uri
          Cid = post.Cid }

    let! result =
        Bluesky.reportContent
            agent
            (ReportSubject.Record postRef)
            ComAtprotoModeration.Defs.ReasonType.ReasonSpam
            (Some "This post is spam")

    match result with
    | Ok reportId -> printfn "Report filed (ID: %d)" reportId
    | Error err -> printfn "Failed: %A" err
}
```

### Reporting an Account

```fsharp
task {
    let! result =
        Bluesky.reportContent
            agent
            (ReportSubject.Account profile.Did)
            ComAtprotoModeration.Defs.ReasonType.ReasonViolation
            (Some "Harassment and abuse")

    match result with
    | Ok reportId -> printfn "Report filed (ID: %d)" reportId
    | Error err -> printfn "Failed: %A" err
}
```

Common reason types include `ReasonSpam`, `ReasonViolation`, `ReasonMisleading`, `ReasonSexual`, `ReasonRude`, and `ReasonOther`. See the `ComAtprotoModeration.Defs.ReasonType` DU for the full list.

### Power Users: Raw XRPC

All the convenience functions above wrap the generated XRPC queries. If you need access to fields that the convenience layer does not expose (e.g. timestamps on likes, or extra parameters), drop down to the raw query:

```fsharp
task {
    let! result =
        AppBskyFeed.GetLikes.query
            agent
            { Uri = post.Uri
              Cid = None
              Cursor = None
              Limit = Some 50L }

    match result with
    | Ok output ->
        for like in output.Likes do
            printfn "  @%s at %s" (Handle.value like.Actor.Handle) (AtDateTime.value like.CreatedAt)
    | Error err -> printfn "Failed: %A" err
}
```

## Notifications

FSharp.ATProto wraps the notification APIs with typed domain models so you can work with notifications without touching raw XRPC types.

### Domain Types

Notifications are represented by two types:

```fsharp
[<RequireQualifiedAccess>]
type NotificationKind =
    | Like | Repost | Follow | Mention | Reply | Quote | StarterpackJoined
    | Unknown of string

type Notification =
    { Kind : NotificationKind
      Author : ProfileSummary
      SubjectUri : AtUri option
      IsRead : bool
      IndexedAt : DateTimeOffset }
```

`NotificationKind` covers all known Bluesky notification reasons. The `Unknown of string` case ensures forward compatibility -- if the server introduces a new reason string, it is preserved rather than dropped. `SubjectUri` is the AT-URI of the content that triggered the notification (e.g. the post that was liked), and is `None` for notification types like `Follow` that have no subject.

### Checking Unread Count

```fsharp
task {
    let! result = Bluesky.getUnreadNotificationCount agent

    match result with
    | Ok count -> printfn "You have %d unread notifications" count
    | Error err -> printfn "Failed: %A" err
}
```

`getUnreadNotificationCount` returns the total number of unread notifications as an `int64`.

### Fetching Notifications

```fsharp
task {
    let! result = Bluesky.getNotifications agent (Some 25L) None

    match result with
    | Ok page ->
        for notification in page.Items do
            let author = Handle.value notification.Author.Handle

            match notification.Kind with
            | NotificationKind.Like ->
                printfn "  %s liked your post" author
            | NotificationKind.Repost ->
                printfn "  %s reposted your post" author
            | NotificationKind.Follow ->
                printfn "  %s followed you" author
            | NotificationKind.Mention ->
                printfn "  %s mentioned you" author
            | NotificationKind.Reply ->
                printfn "  %s replied to your post" author
            | NotificationKind.Quote ->
                printfn "  %s quoted your post" author
            | NotificationKind.StarterpackJoined ->
                printfn "  %s joined via your starter pack" author
            | NotificationKind.Unknown reason ->
                printfn "  %s: %s" author reason

        match page.Cursor with
        | Some cursor -> printfn "More notifications available (cursor: %s)" cursor
        | None -> printfn "All caught up"
    | Error err -> printfn "Failed: %A" err
}
```

The first parameter after the agent is an optional page size (`int64 option`), and the second is an optional cursor (`string option`) for pagination. Results come back as a `Page<Notification>`.

### Marking Notifications as Seen

After displaying notifications to the user, mark them as seen so the unread count resets:

```fsharp
task {
    let! result = Bluesky.markNotificationsSeen agent

    match result with
    | Ok () -> printfn "Notifications marked as seen"
    | Error err -> printfn "Failed: %A" err
}
```

`markNotificationsSeen` uses the current UTC time as the "seen at" timestamp.

### A Complete Workflow

Here is a typical pattern: check the unread count, fetch and display notifications if there are any, then mark them as seen.

```fsharp
taskResult {
    let! count = Bluesky.getUnreadNotificationCount agent

    if count > 0L then
        printfn "%d new notifications:" count
        let! page = Bluesky.getNotifications agent (Some count) None

        for n in page.Items do
            if not n.IsRead then
                let author = Handle.value n.Author.Handle
                match n.Kind with
                | NotificationKind.Like -> printfn "  %s liked your post" author
                | NotificationKind.Follow -> printfn "  %s followed you" author
                | NotificationKind.Reply -> printfn "  %s replied" author
                | _ -> printfn "  %s: %A" author n.Kind

        do! Bluesky.markNotificationsSeen agent
        printfn "All marked as seen"
    else
        printfn "No new notifications"
}
```

This example uses the `taskResult` computation expression to chain the three calls with automatic error short-circuiting. If any call fails, the remaining steps are skipped and the error propagates.

### Paginating All Notifications

For fetching notifications across multiple pages, use the pre-built paginator:

```fsharp
let pages = Bluesky.paginateNotifications agent (Some 50L)
let enumerator = pages.GetAsyncEnumerator ()
let mutable hasMore = true

while hasMore do
    let! moved = enumerator.MoveNextAsync ()
    hasMore <- moved

    if hasMore then
        match enumerator.Current with
        | Ok page ->
            for n in page.Items do
                printfn "  [%s] %A from @%s"
                    (n.IndexedAt.ToString ("g"))
                    n.Kind
                    (Handle.value n.Author.Handle)
        | Error err ->
            printfn "Page error: %A" err
            hasMore <- false
```

The paginator returns an `IAsyncEnumerable<Result<Page<Notification>, XrpcError>>`. The stream stops automatically when the server returns no cursor. See the [Pagination guide](pagination.html) for more details on working with paginators.
