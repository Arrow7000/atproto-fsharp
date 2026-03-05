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
| `RecordUri` | `AtUri` | AT-URI of the notification record |
| `Author` | `ProfileSummary` | The user who triggered the notification |
| `Content` | `NotificationContent` | Rich discriminated union with type-specific data |
| `IsRead` | `bool` | Whether the notification has been seen |
| `IndexedAt` | `DateTimeOffset` | When the notification was indexed |

### NotificationContent

Rich discriminated union for notification types. Uses `[<RequireQualifiedAccess>]`, so all cases must be qualified (e.g. `NotificationContent.Like`). Each case carries the data relevant to that notification kind.

| Case | Fields | Description |
|------|--------|-------------|
| `NotificationContent.Like` | `post: PostRef` | Someone liked your post |
| `NotificationContent.Repost` | `post: PostRef` | Someone reposted your post |
| `NotificationContent.Follow` | -- | Someone followed you |
| `NotificationContent.Reply` | `text: string * inReplyTo: PostRef` | Someone replied to your post |
| `NotificationContent.Mention` | `text: string` | Someone mentioned you in a post |
| `NotificationContent.Quote` | `text: string * quotedPost: PostRef` | Someone quoted your post |
| `NotificationContent.StarterpackJoined` | `starterPackUri: AtUri` | Someone joined via your starter pack |
| `NotificationContent.Unknown` | `reason: string` | A notification type not yet recognized by the library |

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
        match n.Content with
        | NotificationContent.Like postRef ->
            printfn "%s liked your post (%O)" n.Author.DisplayName postRef.Uri
        | NotificationContent.Follow ->
            printfn "%s followed you" n.Author.DisplayName
        | NotificationContent.Reply (text, _) ->
            printfn "%s replied: %s" n.Author.DisplayName text
        | NotificationContent.Mention text ->
            printfn "%s mentioned you: %s" n.Author.DisplayName text
        | NotificationContent.Repost postRef ->
            printfn "%s reposted (%O)" n.Author.DisplayName postRef.Uri
        | NotificationContent.Quote (text, _) ->
            printfn "%s quoted you: %s" n.Author.DisplayName text
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
                    printfn "%s: %A" n.Author.DisplayName n.Content
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
                match n.Content with
                | NotificationContent.Follow ->
                    printfn "New follower: %s (%O)" n.Author.DisplayName n.Author.Handle
                | NotificationContent.Like postRef
                | NotificationContent.Repost postRef ->
                    printfn "%s interacted with %O" n.Author.DisplayName postRef.Uri
                | NotificationContent.Reply (text, _) ->
                    printfn "%s replied: %s" n.Author.DisplayName text
                | NotificationContent.Mention text ->
                    printfn "%s mentioned you: %s" n.Author.DisplayName text
                | NotificationContent.Quote (text, _) ->
                    printfn "%s quoted you: %s" n.Author.DisplayName text
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
