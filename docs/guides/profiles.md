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

`Bluesky.getProfile` is an inline function that accepts a `Handle`, `Did`, or plain `string` thanks to SRTP (statically resolved type parameters). This means you can pass whatever identifier you have on hand without converting it first. It returns a `Profile` domain type with non-optional fields and boolean relationship flags:

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
        printfn "%s (@%s)" profile.DisplayName handle
        printfn "%s" profile.Description
        printfn "%d followers, %d following, %d posts" profile.FollowersCount profile.FollowsCount profile.PostsCount
    | Error err -> printfn "Failed: %A" err
}
```

### Power Users: Raw XRPC Wrapper

If you need access to every field on `ProfileViewDetailed` (labels, pinned post, verification, etc.), use the generated wrapper directly:

```fsharp
task {
    let! result = AppBskyActor.GetProfile.query agent { Actor = "my-handle.bsky.social" }

    match result with
    | Ok profile ->
        printfn "%s (@%s)" (profile.DisplayName |> Option.defaultValue "(none)") (Handle.value profile.Handle)
    | Error err -> printfn "Failed: %A" err
}
```

The convenience method returns a `Profile` domain type with flattened, non-optional fields. The raw wrapper returns `AppBskyActor.GetProfile.Output` (a `ProfileViewDetailed`) with all protocol-level detail.

## Fetching Multiple Profiles

`Bluesky.getProfiles` fetches up to 25 profiles in a single request and returns a `Profile list`:

```fsharp
task {
    let! result =
        Bluesky.getProfiles agent [ "my-handle.bsky.social"; "other-user.bsky.social" ]

    match result with
    | Ok profiles ->
        for profile in profiles do
            printfn "%s -- %s" profile.DisplayName (Did.value profile.Did)
    | Error err -> printfn "Failed: %A" err
}
```

Any actors that cannot be found are silently omitted from the response. The returned list may be shorter than the input list.

## Understanding Profile Types

The convenience layer uses two domain types that simplify the three underlying Lexicon profile views:

| Domain type | Returned by | Key fields |
|-------------|-------------|------------|
| `Profile` | `getProfile`, `getProfiles` | Full stats (counts), banner, bio, boolean relationship flags |
| `ProfileSummary` | `searchActors`, `getSuggestedFollows`, feed/notification contexts | Minimal: DID, handle, display name, avatar |

`Profile` flattens the `ProfileViewDetailed` response: counts and description are non-optional (defaulting to `0` and `""` when absent), and viewer state is collapsed into boolean fields like `IsFollowing` and `IsMuted`.

`ProfileSummary` is the lightweight type you encounter in lists -- search results, suggested follows, and feed items. If you need full stats for a summary, pass its `Did` or `Handle` to `Bluesky.getProfile`.

### Power Users: Raw Profile Views

Under the hood, the Bluesky Lexicon defines three profile view types:

| Type | Returned by | Key fields |
|------|-------------|------------|
| `ProfileViewDetailed` | `GetProfile`, `GetProfiles` | Full stats, banner image, bio, pinned post, labels, verification |
| `ProfileView` | `SearchActors`, follower/following lists | Bio and indexed timestamp, but no counts or banner |
| `ProfileViewBasic` | Post authors, like lists, notification actors | Minimal: DID, handle, display name, avatar |

You can access these directly through the generated XRPC wrappers when you need fields (labels, verification, pinned post) that the domain types do not expose.

## Viewer State

The `Profile` domain type flattens viewer state into simple boolean fields. No more nested option matching:

```fsharp
task {
    let! result = Bluesky.getProfile agent "my-handle.bsky.social"

    match result with
    | Ok profile ->
        if profile.IsFollowing then printfn "You follow them"
        if profile.IsFollowedBy then printfn "They follow you back"
        if profile.IsBlocking then printfn "You are blocking them"
        if profile.IsBlockedBy then printfn "They are blocking you"
        if profile.IsMuted then printfn "You have muted them"

        // Mutual follow check
        if profile.IsFollowing && profile.IsFollowedBy then
            printfn "Mutual follow!"
    | Error err -> printfn "Failed: %A" err
}
```

If you need the underlying AT-URIs for follow/block records (e.g., to delete them), use the raw `AppBskyActor.GetProfile.query` and inspect `Viewer.Following` / `Viewer.Blocking`. See the [Social Actions Guide](social.html) for follow/unfollow operations.

## Searching for Users

`Bluesky.searchActors` searches for accounts by name, handle, or bio text. It returns a `Page<ProfileSummary>`:

```fsharp
task {
    let! result = Bluesky.searchActors agent "fsharp" (Some 10L) None

    match result with
    | Ok page ->
        printfn "Found %d users:" page.Items.Length

        for actor in page.Items do
            let handle = Handle.value actor.Handle
            printfn "  @%s -- %s" handle actor.DisplayName

        match page.Cursor with
        | Some cursor -> printfn "More results available (cursor: %s)" cursor
        | None -> printfn "No more results"
    | Error err -> printfn "Search failed: %A" err
}
```

The `limit` and `cursor` parameters are optional. Pass `None` for both to use server defaults. To paginate, pass the cursor from the previous page:

```fsharp
let! nextPage = Bluesky.searchActors agent "fsharp" (Some 10L) (Some previousCursor)
```

Search results return `ProfileSummary` (DID, handle, display name, avatar). If you need full stats for a search result, pass its DID or handle to `Bluesky.getProfile`.

For more on pagination patterns, see the [Pagination Guide](pagination.html).

## Suggested Follows

`Bluesky.getSuggestedFollows` returns follow suggestions based on a given actor:

```fsharp
task {
    let! result = Bluesky.getSuggestedFollows agent "my-handle.bsky.social"

    match result with
    | Ok suggestions ->
        printfn "Suggested follows:"

        for suggestion in suggestions do
            printfn "  @%s -- %s" (Handle.value suggestion.Handle) suggestion.DisplayName
    | Error err -> printfn "Failed: %A" err
}
```

This returns a `ProfileSummary list` (not paginated).

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
