---
title: Profiles
category: Guides
categoryindex: 1
index: 3
description: Fetch and understand Bluesky user profiles with FSharp.ATProto
keywords: profiles, user, actor, avatar, bluesky
---

# Profiles

Bluesky profiles contain display names, bios, avatars, follower counts, and relationship metadata. FSharp.ATProto provides a convenience method that accepts typed identifiers and generated XRPC wrappers for full control.

All examples assume you have an authenticated `AtpAgent` and these namespaces open:

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax
```

## Fetching a Profile

### Convenience Method

`Bluesky.getProfile` is an inline function that accepts a `Handle`, `Did`, or plain `string` thanks to SRTP (statically resolved type parameters). This means you can pass whatever identifier you have on hand without converting it first:

```fsharp
task {
    // Pass a string directly
    let! result = Bluesky.getProfile agent "my-handle.bsky.social"

    // Or pass a typed Handle
    let! result = Bluesky.getProfile agent someProfile.Handle

    // Or pass a typed Did
    let! result = Bluesky.getProfile agent someProfile.Did

    match result with
    | Ok profile ->
        let handle = Handle.value profile.Handle
        let name = profile.DisplayName |> Option.defaultValue "(no display name)"
        let bio = profile.Description |> Option.defaultValue ""
        let followers = profile.FollowersCount |> Option.defaultValue 0L
        let following = profile.FollowsCount |> Option.defaultValue 0L
        let posts = profile.PostsCount |> Option.defaultValue 0L

        printfn "%s (@%s)" name handle
        printfn "%s" bio
        printfn "%d followers, %d following, %d posts" followers following posts
    | Error err -> printfn "Failed: %A" err
}
```

### Low-Level XRPC Wrapper

If you prefer explicit control, use the generated `AppBskyActor.GetProfile.query` directly. The `Actor` field is a plain `string` that accepts either a handle or DID:

```fsharp
task {
    let! result = AppBskyActor.GetProfile.query agent { Actor = "my-handle.bsky.social" }

    match result with
    | Ok profile ->
        printfn "%s (@%s)" (profile.DisplayName |> Option.defaultValue "(none)") (Handle.value profile.Handle)
    | Error err -> printfn "Failed: %A" err
}
```

Both approaches return the same `AppBskyActor.GetProfile.Output` (which is a `ProfileViewDetailed`).

## Fetching Multiple Profiles

Use `AppBskyActor.GetProfiles.query` to fetch up to 25 profiles in a single request:

```fsharp
task {
    let! result =
        AppBskyActor.GetProfiles.query agent { Actors = [ "my-handle.bsky.social"; "other-user.bsky.social" ] }

    match result with
    | Ok output ->
        for profile in output.Profiles do
            let did = Did.value profile.Did
            let name = profile.DisplayName |> Option.defaultValue "(none)"
            printfn "%s -- %s" name did
    | Error err -> printfn "Failed: %A" err
}
```

Any actors that cannot be found are silently omitted from the response. The returned list may be shorter than the input list.

## Understanding Profile Types

The Bluesky Lexicon defines three profile view types at different levels of detail. Which one you encounter depends on the API endpoint:

| Type | Returned by | Key fields |
|------|-------------|------------|
| `ProfileViewDetailed` | `GetProfile`, `GetProfiles` | Full stats (follower/following/post counts), banner image, bio, pinned post |
| `ProfileView` | `SearchActors`, follower/following lists | Bio and indexed timestamp, but no counts or banner |
| `ProfileViewBasic` | Post authors, like lists, notification actors | Minimal: DID, handle, display name, avatar |

All three types share a common core of `Did`, `Handle`, `DisplayName`, `Avatar`, `Labels`, `Viewer`, and `Verification`. The detailed variant adds counts and the banner image; the basic variant omits the bio.

You do not need to convert between these types. The generated XRPC wrappers return the correct type for each endpoint.

## Viewer State

When you are authenticated, profile responses include a `Viewer` field that describes your relationship with the account. This is an `AppBskyActor.Defs.ViewerState option`:

```fsharp
task {
    let! result = Bluesky.getProfile agent "my-handle.bsky.social"

    match result with
    | Ok profile ->
        match profile.Viewer with
        | Some viewer ->
            // Do I follow this user?
            match viewer.Following with
            | Some followUri -> printfn "You follow them (record: %s)" (AtUri.value followUri)
            | None -> printfn "You do not follow them"

            // Do they follow me?
            match viewer.FollowedBy with
            | Some _ -> printfn "They follow you back"
            | None -> printfn "They do not follow you"

            // Blocking
            match viewer.Blocking with
            | Some _ -> printfn "You are blocking them"
            | None -> ()

            if viewer.BlockedBy = Some true then
                printfn "They are blocking you"

            // Muting
            if viewer.Muted = Some true then
                printfn "You have muted them"
        | None -> printfn "No viewer state (not authenticated?)"
    | Error err -> printfn "Failed: %A" err
}
```

The `Following` and `FollowedBy` fields contain AT-URIs pointing to the follow record. These URIs are useful for unfollowing (you need the record URI to delete it). See the [Social Actions Guide](social.html) for follow/unfollow operations.

## Searching for Users

Use `AppBskyActor.SearchActors.query` to search for accounts by name, handle, or bio text:

```fsharp
task {
    let! result =
        AppBskyActor.SearchActors.query
            agent
            { Q = Some "fsharp"
              Term = None
              Cursor = None
              Limit = Some 10L }

    match result with
    | Ok output ->
        printfn "Found %d users:" output.Actors.Length

        for actor in output.Actors do
            let handle = Handle.value actor.Handle
            let name = actor.DisplayName |> Option.defaultValue "(none)"
            printfn "  @%s -- %s" handle name

        match output.Cursor with
        | Some cursor -> printfn "More results available (cursor: %s)" cursor
        | None -> printfn "No more results"
    | Error err -> printfn "Search failed: %A" err
}
```

Search results return `ProfileView` (not `ProfileViewDetailed`), so they include bios but not follower counts. If you need full stats for a search result, pass its DID or handle to `Bluesky.getProfile`.

For paginating through results, see the [Pagination Guide](pagination.html).

## Working with Typed Identifiers

Profile fields like `Did` and `Handle` are not plain strings -- they are single-case discriminated unions from `FSharp.ATProto.Syntax` that guarantee the value has been validated at parse time.

To extract the underlying string, use the corresponding module's `value` function:

```fsharp
let didString : string = Did.value profile.Did // "did:plc:z72i..."
let handleString : string = Handle.value profile.Handle // "my-handle.bsky.social"
```

To create a typed identifier from a string (with validation):

```fsharp
match Did.parse "did:plc:z72i7hdynmk6r22z27h6tvur" with
| Ok did -> printfn "Valid DID: %s" (Did.value did)
| Error msg -> printfn "Invalid: %s" msg
```

The convenience method `Bluesky.getProfile` accepts typed identifiers directly, so you rarely need to extract strings just to pass them along:

```fsharp
// All three work -- no manual conversion needed
let! _ = Bluesky.getProfile agent "my-handle.bsky.social"
let! _ = Bluesky.getProfile agent someProfile.Handle
let! _ = Bluesky.getProfile agent someProfile.Did
```
