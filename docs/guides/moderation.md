---
title: Moderation
category: Guides
categoryindex: 1
index: 11
description: Mute users and threads, block mod lists, and report content
keywords: fsharp, atproto, bluesky, moderation, mute, block, report
---

# Moderation

FSharp.ATProto provides convenience functions for muting users and threads, subscribing to moderation lists, and filing reports. All actions are server-side and persist across devices.

All examples assume you have an authenticated agent:

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax
```

## Muting a User

`Bluesky.muteUser` takes a typed [DID](../concepts.html). Muted users' posts are hidden from your feeds and notifications, but the muted user is never notified:

```fsharp
taskResult {
    do! Bluesky.muteUser agent profile.Did

    // later...
    do! Bluesky.unmuteUser agent profile.Did
}
```

When you only have a handle string, use `muteUserByHandle` -- it resolves the identifier for you:

```fsharp
do! Bluesky.muteUserByHandle agent "spammer.bsky.social"
```

Muting is invisible. The muted user can still see and interact with your posts -- you just will not see theirs.

## Muting a Thread

`Bluesky.muteThread` takes the [AT-URI](../concepts.html) of the thread root post. Posts in the muted thread are hidden from your notifications:

```fsharp
taskResult {
    do! Bluesky.muteThread agent post.Uri

    // later...
    do! Bluesky.unmuteThread agent post.Uri
}
```

Useful for silencing noisy threads you have been mentioned in. Thread muting only affects notifications -- the thread remains visible if you navigate to it.

## Moderation Lists

Bluesky supports community-maintained moderation lists. You can subscribe to a list to mute or block every account on it.

### Mute lists

Subscribing to a mute list mutes all accounts on it. As the list owner updates membership, the effect follows automatically:

```fsharp
taskResult {
    do! Bluesky.muteModList agent listUri

    // later...
    do! Bluesky.unmuteModList agent listUri
}
```

### Block lists

`blockModList` creates a `listblock` record and returns a `ListBlockRef` you pass to `unblockModList` to undo:

```fsharp
taskResult {
    let! blockRef = Bluesky.blockModList agent listUri

    // later...
    do! Bluesky.unblockModList agent blockRef
}
```

Block lists apply the same restrictions as individual blocks.

## Reporting Content

`Bluesky.reportContent` takes a `ReportSubject` (what to report), a `ReasonType` (why), and an optional description. On success it returns the report ID:

```fsharp
taskResult {
    // Report a post
    let! reportId =
        Bluesky.reportContent agent
            (ReportSubject.Record post)
            ComAtprotoModeration.Defs.ReasonType.ReasonSpam
            (Some "This is spam content")

    printfn "Report filed (ID: %d)" reportId
}
```

`ReportSubject` has two cases:

```fsharp
[<RequireQualifiedAccess>]
type ReportSubject =
    | Account of Did     // report an entire account
    | Record of PostRef  // report a specific post
```

To report an account instead of a post:

```fsharp
taskResult {
    let! reportId =
        Bluesky.reportContent agent
            (ReportSubject.Account userDid)
            ComAtprotoModeration.Defs.ReasonType.ReasonViolation
            None

    printfn "Report filed (ID: %d)" reportId
}
```

### Reason types

The `ComAtprotoModeration.Defs.ReasonType` DU includes these common cases:

| Case | When to use |
|---|---|
| `ReasonSpam` | Unsolicited or repetitive content |
| `ReasonViolation` | Terms of service or community guideline violation |
| `ReasonMisleading` | Deceptive or misleading content |
| `ReasonSexual` | Unwanted sexual content |
| `ReasonRude` | Rude or disrespectful behavior |
| `ReasonOther` | Does not fit other categories (provide a description) |
| `ReasonAppeal` | Appealing a previous moderation decision |

The DU also includes finer-grained Ozone reason types (e.g. `ReasonHarassmentTargeted`, `ReasonMisleadingScam`) and an `Unknown of string` fallback for forward compatibility.

## Checking Mute and Block Status

The `Profile` domain type returned by `Bluesky.getProfile` includes fields that reflect your current moderation state:

- `IsMuted` -- whether you have muted this user
- `IsBlocking` -- whether you are blocking this user
- `IsBlockedBy` -- whether this user is blocking you

```fsharp
taskResult {
    let! profile = Bluesky.getProfile agent "someone.bsky.social"

    if profile.IsMuted then printfn "You have muted this user"
    if profile.IsBlocking then printfn "You are blocking this user"
    if profile.IsBlockedBy then printfn "This user is blocking you"
}
```

See the [Profiles guide](profiles.html) for more on the `Profile` domain type.

## Power Users: Raw XRPC

If you need access to response fields the convenience layer does not expose, drop to the raw XRPC call:

```fsharp
taskResult {
    let! output =
        ComAtprotoModeration.CreateReport.call agent
            { Subject =
                ComAtprotoModeration.CreateReport.InputSubjectUnion.RepoRef
                    { Did = userDid }
              ReasonType = ComAtprotoModeration.Defs.ReasonType.ReasonSpam
              Reason = Some "Detailed description here"
              ModTool = None }

    printfn "Report %d filed at %s" output.Id output.CreatedAt
}
```

This gives access to the full response, including `CreatedAt`, `ReportedBy`, and the resolved `Subject` union.
