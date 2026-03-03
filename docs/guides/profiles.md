---
title: Profiles
category: Type Reference
categoryindex: 2
index: 6
description: Fetch, search, and update Bluesky user profiles with FSharp.ATProto
keywords: profiles, user, actor, avatar, bluesky, search, followers
---

# Profiles

All examples use `taskResult {}`. See the [Error Handling guide](error-handling.html) for details.

Bluesky profiles contain display names, bios, avatars, follower counts, and relationship metadata. All examples assume an authenticated `AtpAgent` and these namespaces open:

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax
```

## Fetching a Profile

`Bluesky.getProfile` accepts a [Handle](../concepts.html), [DID](../concepts.html), or plain `string` via SRTP -- pass whatever identifier you have. It returns a `Profile` domain type with non-optional fields and boolean relationship flags:

```fsharp
taskResult {
    let! profile = Bluesky.getProfile agent "alice.bsky.social"
    let handle = Handle.value profile.Handle
    printfn "%s (@%s)" profile.DisplayName handle
    printfn "%s" profile.Description
    printfn "%d followers, %d following, %d posts" profile.FollowersCount profile.FollowsCount profile.PostsCount
}
```

Typed identifiers work directly -- `Bluesky.getProfile agent someProfile.Handle` and `Bluesky.getProfile agent someProfile.Did` both compile.

## Fetching Multiple Profiles

`Bluesky.getProfiles` fetches up to 25 profiles in a single request:

```fsharp
taskResult {
    let! profiles = Bluesky.getProfiles agent [ "alice.bsky.social"; "bob.bsky.social" ]
    for profile in profiles do
        printfn "%s -- %s" profile.DisplayName (Did.value profile.Did)
}
```

Unknown actors are silently omitted -- the returned list may be shorter than the input.

## Understanding Profile Types

The convenience layer uses two domain types:

| Domain type | Returned by | Key fields |
|-------------|-------------|------------|
| `Profile` | `getProfile`, `getProfiles` | Full stats (counts), banner, bio, boolean relationship flags |
| `ProfileSummary` | `searchActors`, `getSuggestedFollows`, `getFollowers`, `getFollows` | Minimal: DID, handle, display name, avatar |

`Profile` flattens `ProfileViewDetailed`: counts default to `0`, description to `""`, and viewer state is collapsed into boolean fields. `ProfileSummary` is the lightweight type used in lists -- pass its `Did` or `Handle` to `getProfile` if you need full stats.

## Viewer State

`Profile` flattens viewer state into simple booleans:

```fsharp
taskResult {
    let! profile = Bluesky.getProfile agent "alice.bsky.social"
    if profile.IsFollowing then printfn "You follow them"
    if profile.IsFollowedBy then printfn "They follow you back"
    if profile.IsBlocking then printfn "You are blocking them"
    if profile.IsBlockedBy then printfn "They are blocking you"
    if profile.IsMuted then printfn "You have muted them"

    if profile.IsFollowing && profile.IsFollowedBy then
        printfn "Mutual follow!"
}
```

For the underlying AT-URIs for follow/block records, use `AppBskyActor.GetProfile.query` and inspect `Viewer.Following` / `Viewer.Blocking`. See [Social Actions](social.html) for follow/unfollow operations.

## Followers and Follows

`Bluesky.getFollowers` and `Bluesky.getFollows` return paginated lists of `ProfileSummary`:

```fsharp
taskResult {
    let! followers = Bluesky.getFollowers agent "alice.bsky.social" (Some 50L) None
    for profile in followers.Items do
        printfn "@%s" (Handle.value profile.Handle)

    let! follows = Bluesky.getFollows agent "alice.bsky.social" (Some 50L) None
    for profile in follows.Items do
        printfn "@%s" (Handle.value profile.Handle)
}
```

For continuous pagination, see the [Pagination guide](pagination.html).

## Searching for Users

`Bluesky.searchActors` searches by name, handle, or bio text:

```fsharp
taskResult {
    let! page = Bluesky.searchActors agent "fsharp" (Some 10L) None
    printfn "Found %d users:" page.Items.Length
    for actor in page.Items do
        printfn "  @%s -- %s" (Handle.value actor.Handle) actor.DisplayName
}
```

Pass `None` for `limit` and `cursor` to use server defaults. Pass the cursor from a previous result to fetch the next page.

## Suggested Follows

`Bluesky.getSuggestedFollows` returns suggestions based on a given actor:

```fsharp
taskResult {
    let! suggestions = Bluesky.getSuggestedFollows agent "alice.bsky.social"
    for suggestion in suggestions do
        printfn "  @%s -- %s" (Handle.value suggestion.Handle) suggestion.DisplayName
}
```

This returns a `ProfileSummary list` (not paginated).

## Updating Your Profile

`Bluesky.upsertProfile` performs a read-modify-write with automatic retry on conflicts. Pass a function that receives the current profile (or `None`) and returns the updated profile:

```fsharp
taskResult {
    do! Bluesky.upsertProfile agent (fun existing ->
        let current =
            existing |> Option.defaultValue
                { DisplayName = None; Description = None; Avatar = None
                  Banner = None; Labels = None; CreatedAt = None
                  PinnedPost = None; JoinedViaStarterPack = None
                  Pronouns = None; Website = None }
        { current with DisplayName = Some "New Display Name" })
}
```

## Who Liked / Who Reposted

`Bluesky.getLikes` and `Bluesky.getRepostedBy` return the users who liked or reposted a given post:

```fsharp
taskResult {
    let! likers = Bluesky.getLikes agent post.Uri (Some 50L) None
    for profile in likers.Items do
        printfn "@%s liked this" (Handle.value profile.Handle)
}
```

`Bluesky.getRepostedBy` works the same way with the same signature.

## Power Users: Raw XRPC Wrappers

For fields the domain types do not expose (labels, verification, pinned post), use the generated wrappers:

```fsharp
taskResult {
    let! profile = AppBskyActor.GetProfile.query agent { Actor = "alice.bsky.social" }
    printfn "%s" (profile.DisplayName |> Option.defaultValue "(none)")
}
```

The Lexicon defines three profile view types:

| Type | Returned by | Key fields |
|------|-------------|------------|
| `ProfileViewDetailed` | `GetProfile`, `GetProfiles` | Full stats, banner, bio, pinned post, labels, verification |
| `ProfileView` | `SearchActors`, follower/following lists | Bio and indexed timestamp, no counts or banner |
| `ProfileViewBasic` | Post authors, like lists, notification actors | Minimal: DID, handle, display name, avatar |
