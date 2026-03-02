---
title: Notifications
category: Guides
categoryindex: 1
index: 10
description: Fetch, count, and mark notifications as read
keywords: fsharp, atproto, bluesky, notifications, unread
---

# Notifications

The `Bluesky` module provides three functions for working with notifications: checking the unread count, fetching a page of notifications, and marking them as seen. All return domain types rather than raw protocol responses.

## Domain Types

Notifications are represented by two types:

```fsharp
type NotificationKind =
    | Like
    | Repost
    | Follow
    | Mention
    | Reply
    | Quote
    | StarterpackJoined
    | Unknown of string

type Notification =
    { Kind : NotificationKind
      Author : ProfileSummary
      SubjectUri : AtUri option
      IsRead : bool
      IndexedAt : DateTimeOffset }
```

`NotificationKind` uses `[<RequireQualifiedAccess>]`, so you always write `NotificationKind.Like`, not just `Like`. The `Unknown` case handles any new notification types Bluesky adds in the future. `SubjectUri` points to the post that was liked, replied to, etc. -- it is `None` for follow notifications since those have no subject post.

## Checking Unread Count

`Bluesky.getUnreadNotificationCount` returns the number of notifications the user has not yet seen:

```fsharp
taskResult {
    let! count = Bluesky.getUnreadNotificationCount agent
    printfn "You have %d unread notifications" count
}
```

This is useful for badge counts or deciding whether to fetch the full list.

## Fetching Notifications

`Bluesky.getNotifications` returns a `Page<Notification>` with an optional cursor for pagination. Pass `None` for both parameters to use server defaults:

```fsharp
taskResult {
    let! page = Bluesky.getNotifications agent (Some 25L) None
    for n in page.Items do
        match n.Kind with
        | NotificationKind.Like -> printfn "%s liked your post" n.Author.DisplayName
        | NotificationKind.Follow -> printfn "%s followed you" n.Author.DisplayName
        | NotificationKind.Reply -> printfn "%s replied" n.Author.DisplayName
        | NotificationKind.Mention -> printfn "%s mentioned you" n.Author.DisplayName
        | NotificationKind.Repost -> printfn "%s reposted" n.Author.DisplayName
        | NotificationKind.Quote -> printfn "%s quoted you" n.Author.DisplayName
        | _ -> printfn "Other notification from %s" n.Author.DisplayName
}
```

The `limit` parameter controls how many notifications to fetch per page (here, 25). The second parameter is the pagination cursor -- pass `None` to start from the most recent.

## Marking Notifications as Seen

After processing notifications, mark them as seen so the unread count resets:

```fsharp
taskResult {
    do! Bluesky.markNotificationsSeen agent
}
```

This marks all notifications as seen up to the current timestamp. There is no way to mark individual notifications -- the protocol treats it as a high-water mark.

## A Complete Workflow

A typical notification check: read the unread count, fetch if there are any, process them, then mark as seen.

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

taskResult {
    let! agent = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"

    let! unread = Bluesky.getUnreadNotificationCount agent

    if unread > 0L then
        let! page = Bluesky.getNotifications agent (Some 50L) None

        for n in page.Items do
            if not n.IsRead then
                match n.Kind with
                | NotificationKind.Follow ->
                    printfn "New follower: %s (%O)" n.Author.DisplayName n.Author.Handle
                | NotificationKind.Like
                | NotificationKind.Repost ->
                    n.SubjectUri |> Option.iter (fun uri ->
                        printfn "%s interacted with %O" n.Author.DisplayName uri)
                | NotificationKind.Reply
                | NotificationKind.Mention
                | NotificationKind.Quote ->
                    n.SubjectUri |> Option.iter (fun uri ->
                        printfn "%s wants your attention on %O" n.Author.DisplayName uri)
                | _ -> ()

        do! Bluesky.markNotificationsSeen agent
        printfn "Marked %d notifications as seen" unread
    else
        printfn "No new notifications"
}
```

## Paginating All Notifications

For bots or tools that need to process the entire notification history, use `Bluesky.paginateNotifications`. It returns an `IAsyncEnumerable<Result<Page<Notification>, XrpcError>>` that fetches pages on demand and stops when the server has no more results:

```fsharp
let pages = Bluesky.paginateNotifications agent (Some 50L)
```

See the [Pagination guide](pagination.html) for patterns on consuming `IAsyncEnumerable` from F#.

## Power Users: Raw XRPC

If you need access to fields the `Notification` domain type does not expose (such as `Labels` or the raw `Record` JSON), drop down to the generated XRPC wrapper:

```fsharp
open FSharp.ATProto.Bluesky.Generated

taskResult {
    let! output =
        AppBskyNotification.ListNotifications.query agent
            { Cursor = None
              Limit = Some 10L
              Priority = None
              Reasons = None
              SeenAt = None }

    for n in output.Notifications do
        printfn "%O (%A) - labels: %A" n.Uri n.Reason n.Labels
}
```

The raw `Notification` type includes `Labels`, `Record` (a `JsonElement`), `Cid`, and `Uri` fields that the convenience layer omits for simplicity.
