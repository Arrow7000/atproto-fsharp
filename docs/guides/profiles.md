---
title: Profiles
category: Type Reference
categoryindex: 2
index: 6
description: Fetch, search, and update Bluesky user profiles with FSharp.ATProto
keywords: profiles, user, actor, avatar, bluesky, search, followers
---

# Profiles

Fetch, search, and update Bluesky user profiles.

## Domain Types

### Profile

A full user profile with engagement counts and relationship state, returned by `getProfile` and `getProfiles`.

| Field | Type | Description |
|-------|------|-------------|
| `Did` | `Did` | Decentralized identifier |
| `Handle` | `Handle` | User handle (e.g. `alice.bsky.social`) |
| `DisplayName` | `string` | Display name (defaults to `""`) |
| `Description` | `string` | Bio text (defaults to `""`) |
| `Avatar` | `string option` | Avatar image URL |
| `Banner` | `string option` | Banner image URL |
| `PostsCount` | `int64` | Total number of posts |
| `FollowersCount` | `int64` | Number of followers |
| `FollowsCount` | `int64` | Number of accounts followed |
| `IsFollowing` | `bool` | Whether you follow this user |
| `IsFollowedBy` | `bool` | Whether this user follows you |
| `IsBlocking` | `bool` | Whether you are blocking this user |
| `IsBlockedBy` | `bool` | Whether this user is blocking you |
| `IsMuted` | `bool` | Whether you have muted this user |

### ProfileSummary

A lightweight profile used in feeds, notifications, search results, and follower lists.

| Field | Type | Description |
|-------|------|-------------|
| `Did` | `Did` | Decentralized identifier |
| `Handle` | `Handle` | User handle |
| `DisplayName` | `string` | Display name (defaults to `""`) |
| `Avatar` | `string option` | Avatar image URL |

Pass a `ProfileSummary` to `getProfile` if you need the full `Profile` with counts and relationship flags.

## Functions

### Reading Profiles

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.getProfile` | `agent` `actor:Handle\|Did\|ProfileSummary\|Profile` | `Result<Profile, XrpcError>` | Get a single full profile |
| `Bluesky.getProfiles` | `agent` `actors:Did list` | `Result<Profile list, XrpcError>` | Get up to 25 profiles in one request |
| `Bluesky.getFollowers` | `agent` `actor:Handle\|Did\|ProfileSummary\|Profile` `limit:int64 option` `cursor:string option` | `Result<Page<ProfileSummary>, XrpcError>` | List an actor's followers |
| `Bluesky.getFollows` | `agent` `actor:Handle\|Did\|ProfileSummary\|Profile` `limit:int64 option` `cursor:string option` | `Result<Page<ProfileSummary>, XrpcError>` | List accounts an actor follows |
| `Bluesky.getSuggestedFollows` | `agent` `actor:Handle\|Did\|ProfileSummary\|Profile` | `Result<ProfileSummary list, XrpcError>` | Suggested follows based on an actor |
| `Bluesky.getSuggestions` | `agent` `limit:int64 option` `cursor:string option` | `Result<Page<ProfileSummary>, XrpcError>` | General account suggestions |
| `Bluesky.searchActors` | `agent` `query:string` `limit:int64 option` `cursor:string option` | `Result<Page<ProfileSummary>, XrpcError>` | Search users by name, handle, or bio |
| `Bluesky.searchActorsTypeahead` | `agent` `query:string` `limit:int64 option` | `Result<ProfileSummary list, XrpcError>` | Lightweight typeahead search (no pagination) |

```fsharp
taskResult {
    let! profile = Bluesky.getProfile agent "alice.bsky.social"
    printfn "%s: %d followers" profile.DisplayName profile.FollowersCount

    let! followers = Bluesky.getFollowers agent profile (Some 50L) None
    for f in followers.Items do
        printfn "  @%s" (Handle.value f.Handle)
}
```

### Engagement Info

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.getLikes` | `agent` `target:TimelinePost\|PostRef\|AtUri` `limit:int64 option` `cursor:string option` | `Result<Page<ProfileSummary>, XrpcError>` | Who liked a post |
| `Bluesky.getRepostedBy` | `agent` `target:TimelinePost\|PostRef\|AtUri` `limit:int64 option` `cursor:string option` | `Result<Page<ProfileSummary>, XrpcError>` | Who reposted a post |

```fsharp
taskResult {
    let! likers = Bluesky.getLikes agent post (Some 50L) None
    for p in likers.Items do
        printfn "@%s liked this" (Handle.value p.Handle)
}
```

### Writing

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.upsertProfile` | `agent` `updateFn:(Profile option -> Profile)` | `Result<unit, XrpcError>` | Read-modify-write profile with CAS retry |
| `Bluesky.updateHandle` | `agent` `handle:Handle` | `Result<unit, XrpcError>` | Change the authenticated user's handle |

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

Note: `upsertProfile` operates on the raw `AppBskyActor.Profile.Profile` type (all `Option` fields), not the convenience `Profile` domain type.

## SRTP Polymorphism

Read functions that take an actor accept multiple types via SRTP:

- `getProfile`, `getFollowers`, `getFollows`, `getSuggestedFollows`, `getAuthorFeed`, `getActorLikes` accept `Handle`, `Did`, `ProfileSummary`, or `Profile`
- `getProfiles` takes a `Did list`
- `getLikes`, `getRepostedBy` accept `TimelinePost`, `PostRef`, or `AtUri`

```fsharp
taskResult {
    let! profile = Bluesky.getProfile agent "alice.bsky.social"

    // Pass the Profile directly to get followers
    let! followers = Bluesky.getFollowers agent profile (Some 50L) None

    // Pass a ProfileSummary from the result
    let first = followers.Items.Head
    let! fullProfile = Bluesky.getProfile agent first
}
```
