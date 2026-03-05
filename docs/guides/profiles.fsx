(**
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
| `Bluesky.getProfile` | `agent:AtpAgent` `actor:Handle / Did / ProfileSummary / Profile` | `Result<Profile, XrpcError>` | Get a single full profile |
| `Bluesky.getProfiles` | `agent:AtpAgent` `actors:Did list` | `Result<Profile list, XrpcError>` | Get up to 25 profiles in one request |
| `Bluesky.getFollowers` | `agent:AtpAgent` `actor:Handle / Did / ProfileSummary / Profile` `limit:int64 option` `cursor:string option` | `Result<Page<ProfileSummary>, XrpcError>` | List an actor's followers |
| `Bluesky.getFollows` | `agent:AtpAgent` `actor:Handle / Did / ProfileSummary / Profile` `limit:int64 option` `cursor:string option` | `Result<Page<ProfileSummary>, XrpcError>` | List accounts an actor follows |
| `Bluesky.getSuggestedFollows` | `agent:AtpAgent` `actor:Handle / Did / ProfileSummary / Profile` | `Result<ProfileSummary list, XrpcError>` | Suggested follows based on an actor |
| `Bluesky.getSuggestions` | `agent:AtpAgent` `limit:int64 option` `cursor:string option` | `Result<Page<ProfileSummary>, XrpcError>` | General account suggestions |
| `Bluesky.searchActors` | `agent:AtpAgent` `query:string` `limit:int64 option` `cursor:string option` | `Result<Page<ProfileSummary>, XrpcError>` | Search users by name, handle, or bio |
| `Bluesky.searchActorsTypeahead` | `agent:AtpAgent` `query:string` `limit:int64 option` | `Result<ProfileSummary list, XrpcError>` | Lightweight typeahead search (no pagination) |
| `Bluesky.getBlocks` | `agent:AtpAgent` `limit:int64 option` `cursor:string option` | `Result<Page<ProfileSummary>, XrpcError>` | Get blocked users (paginated) |
| `Bluesky.getMutes` | `agent:AtpAgent` `limit:int64 option` `cursor:string option` | `Result<Page<ProfileSummary>, XrpcError>` | Get muted users (paginated) |
| `Bluesky.getRelationships` | `agent:AtpAgent` `did:Did` `others:Did list option` | `Result<Relationship list, XrpcError>` | Get relationship details between users |
| `Bluesky.getKnownFollowers` | `agent:AtpAgent` `actor:Handle / Did / ProfileSummary / Profile` `limit:int64 option` `cursor:string option` | `Result<Page<ProfileSummary>, XrpcError>` | Get followers you also follow (SRTP) |
| `Bluesky.getLists` | `agent:AtpAgent` `actor:Handle / Did / ProfileSummary / Profile` `limit:int64 option` `cursor:string option` | `Result<Page<ListView>, XrpcError>` | Get user's lists (SRTP) |

### Relationship

| Field | Type | Description |
|-------|------|-------------|
| `Did` | `Did` | The other user's DID |
| `Following` | `AtUri option` | AT-URI of follow record if you follow them |
| `FollowedBy` | `AtUri option` | AT-URI of follow record if they follow you |
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
let post = Unchecked.defaultof<TimelinePost>
let aliceHandle = Unchecked.defaultof<Handle>

(***)

taskResult {
    let! profile = Bluesky.getProfile agent aliceHandle
    printfn "%s: %d followers" profile.DisplayName profile.FollowersCount

    let! followers = Bluesky.getFollowers agent profile (Some 50L) None
    for f in followers.Items do
        printfn "  @%s" (Handle.value f.Handle)
}

(**
### Engagement Info

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.getLikes` | `agent:AtpAgent` `target:TimelinePost / PostRef / AtUri` `limit:int64 option` `cursor:string option` | `Result<Page<ProfileSummary>, XrpcError>` | Who liked a post |
| `Bluesky.getRepostedBy` | `agent:AtpAgent` `target:TimelinePost / PostRef / AtUri` `limit:int64 option` `cursor:string option` | `Result<Page<ProfileSummary>, XrpcError>` | Who reposted a post |
*)

taskResult {
    let! likers = Bluesky.getLikes agent post (Some 50L) None
    for p in likers.Items do
        printfn "@%s liked this" (Handle.value p.Handle)
}

(**
### Writing

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Bluesky.updateProfile` | `agent:AtpAgent` `transform:(Profile -> Profile)` | `Result<unit, XrpcError>` | Transform the profile record with optimistic concurrency |
| `Bluesky.setDisplayName` | `agent:AtpAgent` `name:string option` | `Result<unit, XrpcError>` | Set or clear display name (auto-retries on conflict) |
| `Bluesky.setDescription` | `agent:AtpAgent` `description:string option` | `Result<unit, XrpcError>` | Set or clear bio (auto-retries on conflict) |
| `Bluesky.setAvatar` | `agent:AtpAgent` `avatar:(byte[] * ImageMime) option` | `Result<unit, XrpcError>` | Upload and set avatar, or clear with `None` |
| `Bluesky.setBanner` | `agent:AtpAgent` `banner:(byte[] * ImageMime) option` | `Result<unit, XrpcError>` | Upload and set banner, or clear with `None` |
| `Bluesky.upsertProfile` | `agent:AtpAgent` `updateFn:(Profile option -> Profile)` | `Result<unit, XrpcError>` | Low-level read-modify-write with CAS retry |
| `Bluesky.updateHandle` | `agent:AtpAgent` `handle:Handle` | `Result<unit, XrpcError>` | Change the authenticated user's handle |

Field-specific setters (`setDisplayName`, `setDescription`, `setAvatar`, `setBanner`) auto-retry once on conflict -- safe because they touch a single field. `updateProfile` does not retry; the caller controls the transform.
*)

taskResult {
    // Simple field setters
    do! Bluesky.setDisplayName agent (Some "New Display Name")
    do! Bluesky.setDescription agent (Some "F# developer | AT Protocol enthusiast")

    // Set avatar from file
    let avatarBytes = System.IO.File.ReadAllBytes "avatar.jpg"
    do! Bluesky.setAvatar agent (Some (avatarBytes, Jpeg))

    // Full transform for multiple fields at once
    do! Bluesky.updateProfile agent (fun p ->
        { p with DisplayName = Some "Updated Name"
                 Description = Some "Updated bio" })
}

(**
Note: `updateProfile` and `upsertProfile` operate on the raw `AppBskyActor.Profile.Profile` type (all `Option` fields), not the convenience `Profile` domain type.

## SRTP Polymorphism

Read functions that take an actor accept multiple types via SRTP:

- `getProfile`, `getFollowers`, `getFollows`, `getSuggestedFollows`, `getAuthorFeed`, `getActorLikes` accept `Handle`, `Did`, `ProfileSummary`, or `Profile`
- `getProfiles` takes a `Did list`
- `getLikes`, `getRepostedBy` accept `TimelinePost`, `PostRef`, or `AtUri`
*)

taskResult {
    let! profile = Bluesky.getProfile agent aliceHandle

    // Pass the Profile directly to get followers
    let! followers = Bluesky.getFollowers agent profile (Some 50L) None

    // Pass a ProfileSummary from the result
    let first = followers.Items.Head
    let! fullProfile = Bluesky.getProfile agent first
    ()
}
