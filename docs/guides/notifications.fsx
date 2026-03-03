(**
---
title: Notifications
category: Type Reference
categoryindex: 2
index: 10
description: Fetch, count, and mark notifications as read
keywords: fsharp, atproto, bluesky, notifications, unread
---

# Notifications

Fetch, count, and mark notifications as read through the `Bluesky` module.

All examples use `taskResult {}` -- see the [Error Handling guide](error-handling.html) for details.

## Domain Types

### Notification

A notification from the user's notification feed.

| Field | Type | Description |
|-------|------|-------------|
| `Kind` | `NotificationKind` | The type of notification |
| `Author` | `ProfileSummary` | The user who triggered the notification |
| `SubjectUri` | `AtUri option` | The post that was liked, replied to, etc. `None` for follows |
| `IsRead` | `bool` | Whether the notification has been seen |
| `IndexedAt` | `DateTimeOffset` | When the notification was indexed |

### NotificationKind

Discriminated union for notification types. Uses `[<RequireQualifiedAccess>]`, so all cases must be qualified (e.g. `NotificationKind.Like`).

| Case | Description |
|------|-------------|
| `NotificationKind.Like` | Someone liked your post |
| `NotificationKind.Repost` | Someone reposted your post |
| `NotificationKind.Follow` | Someone followed you |
| `NotificationKind.Mention` | Someone mentioned you in a post |
| `NotificationKind.Reply` | Someone replied to your post |
| `NotificationKind.Quote` | Someone quoted your post |
| `NotificationKind.StarterpackJoined` | Someone joined via your starter pack |
| `NotificationKind.Unknown of string` | A notification type not yet recognized by the library |

## Functions

### Reading

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.getNotifications` | `agent`, `limit: int64 option`, `cursor: string option` | `Result<Page<Notification>, XrpcError>` | Fetch a page of notifications |
| `Bluesky.getUnreadNotificationCount` | `agent` | `Result<int64, XrpcError>` | Get the number of unseen notifications |
*)

(*** hide ***)
#nowarn "20"
#r "../../src/FSharp.ATProto.Syntax/bin/Release/net10.0/FSharp.ATProto.Syntax.dll"
#r "../../src/FSharp.ATProto.Core/bin/Release/net10.0/FSharp.ATProto.Core.dll"
#r "../../src/FSharp.ATProto.Bluesky/bin/Release/net10.0/FSharp.ATProto.Bluesky.dll"
open FSharp.ATProto.Syntax
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

let agent = Unchecked.defaultof<AtpAgent>

(***)

taskResult {
    let! count = Bluesky.getUnreadNotificationCount agent
    printfn "You have %d unread notifications" count
    ()
}

(***)

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

(**
### Actions

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.markNotificationsSeen` | `agent` | `Result<unit, XrpcError>` | Mark all notifications as seen up to the current time |

The protocol treats "seen" as a high-water mark -- there is no way to mark individual notifications. All notifications up to the current timestamp are marked as seen.
*)

taskResult {
    do! Bluesky.markNotificationsSeen agent
}

(**
### Pagination

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.paginateNotifications` | `agent`, `pageSize: int64 option` | `IAsyncEnumerable<Result<Page<Notification>, XrpcError>>` | Lazily paginate all notifications |

The paginator returns an `IAsyncEnumerable` that fetches pages on demand and stops when the server has no more results:
*)

task {
    let pages = Bluesky.paginateNotifications agent (Some 50L)

    let enumerator = pages.GetAsyncEnumerator()

    let rec loop () = task {
        let! hasNext = enumerator.MoveNextAsync()
        if hasNext then
            match enumerator.Current with
            | Ok page ->
                for n in page.Items do
                    printfn "%s: %A" n.Author.DisplayName n.Kind
            | Error err ->
                printfn "Error: %A" err
            do! loop ()
    }

    do! loop ()
    do! enumerator.DisposeAsync()
}

(**
See the [Pagination guide](pagination.html) for more patterns on consuming `IAsyncEnumerable` from F#.

## Complete Workflow

A typical notification check: read the unread count, fetch if there are any, process them, then mark as seen.
*)

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

(**
## Power Users: Raw XRPC

For fields the `Notification` domain type does not expose (such as `Labels` or the raw `Record` JSON), drop to the generated XRPC wrapper:
*)

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
