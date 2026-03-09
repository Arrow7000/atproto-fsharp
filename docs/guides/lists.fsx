(**
---
title: Lists
category: Type Reference
categoryindex: 2
index: 17
description: Create, manage, and read from lists and starter packs
keywords: fsharp, atproto, bluesky, lists, starter-packs, moderation
---

# Lists

FSharp.ATProto provides convenience functions for creating and managing Bluesky lists (moderation, curation, and reference lists) and starter packs.

All examples use `taskResult {}`. See the [Error Handling guide](error-handling.html) for details.
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
let someHandle = Unchecked.defaultof<Handle>
let listUri = Unchecked.defaultof<AtUri>
let user1Did = Unchecked.defaultof<Did>
let user2Did = Unchecked.defaultof<Did>
(***)

open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax

(**
## Domain Types

| Type | Fields | Description |
|---|---|---|
| `ListRef` | `Uri : AtUri` | Reference to a list record |
| `ListItemRef` | `Uri : AtUri` | Reference to a list item record |
| `StarterPackRef` | `Uri : AtUri` | Reference to a starter pack record |
| `ListView` | `Uri`, `Name`, `Purpose`, `Description`, `Avatar`, `Creator : ProfileSummary`, `ListItemCount`, `IsMuted`, `IsBlocked` | Summary of a list |
| `ListDetail` | `List : ListView`, `Items : ProfileSummary list`, `Cursor : string option` | A list with its member profiles |

## List Purpose

The `AppBskyGraph.Defs.ListPurpose` DU controls what kind of list you are creating:

| Case | Description |
|---|---|
| `Modlist` | Moderation list -- used to mute or block all members |
| `Curatelist` | Curation list -- used for custom feeds and starter packs |
| `Referencelist` | General-purpose reference list |

## Reading Lists

| Function | Description |
|---|---|
| `Bluesky.getList` | Get list details and members |
| `Bluesky.getLists` | Get lists created by a user (SRTP: `Handle`, `Did`, `ProfileSummary`, `Profile`) |
| `Bluesky.getListFeed` | Get posts from a list-based feed |
*)

taskResult {
    let! agent = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"

    // Get lists created by someone
    let! page = Bluesky.getLists agent someHandle None None

    for list in page.Items do
        printfn "%s (%d members)" list.Name list.ListItemCount

    // Get a specific list with its members
    let! detail = Bluesky.getList agent listUri None None

    for m in detail.Items do
        printfn "  - %s" (Handle.value m.Handle)
}

(**
## Managing Lists

| Function | Description |
|---|---|
| `Bluesky.createList` | Create a new list (name, purpose, description) |
| `Bluesky.deleteList` | Delete a list |
| `Bluesky.addListItem` | Add a user to a list |
| `Bluesky.removeListItem` | Remove a user from a list |

### Creating a List and Adding Members
*)

taskResult {
    let! agent = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"

    // Create a curation list
    let! listRef =
        Bluesky.createList agent
            "F# Developers"
            AppBskyGraph.Defs.ListPurpose.Curatelist
            (Some "People building cool things with F#")

    // Add members to the list
    let! item1 = Bluesky.addListItem agent listRef.Uri user1Did
    let! item2 = Bluesky.addListItem agent listRef.Uri user2Did

    // Remove a member later
    do! Bluesky.removeListItem agent item1.Uri

    // Delete the entire list
    do! Bluesky.deleteList agent listRef.Uri
}

(**
### List Feed

For curation lists, you can read posts from all list members as a feed:
*)

taskResult {
    let! page = Bluesky.getListFeed agent listUri (Some 25L) None

    for item in page.Items do
        printfn "%s: %s" (Handle.value item.Post.Author.Handle) item.Post.Text
}

(**
## Starter Packs

Starter packs are built on top of curation lists. Create a list first, then wrap it in a starter pack:

| Function | Description |
|---|---|
| `Bluesky.createStarterPack` | Create a starter pack from a list |
| `Bluesky.deleteStarterPack` | Delete a starter pack |
*)

taskResult {
    let! agent = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"

    // Create the underlying list first
    let! listRef =
        Bluesky.createList agent
            "Welcome to F# on Bluesky"
            AppBskyGraph.Defs.ListPurpose.Curatelist
            (Some "Great accounts to follow if you're into F#")

    // Add members to the list
    let! _ = Bluesky.addListItem agent listRef.Uri user1Did
    let! _ = Bluesky.addListItem agent listRef.Uri user2Did

    // Create the starter pack wrapping the list
    let! starterPackRef =
        Bluesky.createStarterPack agent
            "F# Starter Pack"
            listRef.Uri
            (Some "Follow these accounts to get started with F# on Bluesky")
            None // optional feed URIs

    printfn "Starter pack created: %s" (AtUri.value starterPackRef.Uri)
}
