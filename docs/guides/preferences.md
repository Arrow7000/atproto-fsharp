---
title: Preferences
category: Type Reference
categoryindex: 2
index: 18
description: Manage saved feeds, muted words, content filtering, and thread view preferences
keywords: fsharp, atproto, bluesky, preferences, settings, muted-words, content-filter
---

# Preferences

FSharp.ATProto provides convenience functions for reading and modifying Bluesky user preferences. Each function performs a read-modify-write under the hood via `upsertPreferences`, so concurrent modifications are safe.

All examples use `taskResult {}`. See the [Error Handling guide](error-handling.html) for details.

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax
```

## Functions

| Function | Description |
|---|---|
| `Bluesky.getPreferences` | Read current preferences as raw JSON elements |
| `Bluesky.upsertPreferences` | Read-modify-write preferences atomically |
| `Bluesky.addSavedFeed` | Add a feed to saved feeds |
| `Bluesky.removeSavedFeed` | Remove a feed from saved feeds by ID |
| `Bluesky.addMutedWord` | Add a muted word |
| `Bluesky.removeMutedWord` | Remove a muted word by value |
| `Bluesky.setContentLabelPref` | Set visibility for a content label |
| `Bluesky.setAdultContentEnabled` | Enable or disable adult content |
| `Bluesky.setThreadViewPref` | Set thread sorting preference |
| `Bluesky.addHiddenPost` | Hide a post from your feeds |
| `Bluesky.removeHiddenPost` | Unhide a post |

## Muted Words

Muted words hide posts containing the word from your feeds and notifications. The `addMutedWord` function takes an `AppBskyActor.Defs.MutedWord` record:

```fsharp
taskResult {
    let! agent = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"

    // Mute a word in post content and tags
    do! Bluesky.addMutedWord agent
            { Value = "spoilers"
              Targets = [ AppBskyActor.Defs.MutedWordTarget.Content
                          AppBskyActor.Defs.MutedWordTarget.Tag ]
              ActorTarget = Some AppBskyActor.Defs.MutedWordActorTarget.All
              ExpiresAt = None
              Id = None }

    // Remove it later
    do! Bluesky.removeMutedWord agent "spoilers"
}
```

## Saved Feeds

Add or remove feeds from the user's saved feeds list:

```fsharp
taskResult {
    let feed : AppBskyActor.Defs.SavedFeed =
        { Id = System.Guid.NewGuid().ToString()
          Type = "feed"
          Value = AtUri.value feedGeneratorUri
          Pinned = true }

    do! Bluesky.addSavedFeed agent feed

    // Remove by the feed ID
    do! Bluesky.removeSavedFeed agent feed.Id
}
```

## Content Filtering

Control how content labels affect your experience. The `ContentLabelPrefVisibility` DU has these cases:

| Case | Effect |
|---|---|
| `Show` | Always show labeled content |
| `Warn` | Show a warning overlay (default for most labels) |
| `Hide` | Hide labeled content entirely |
| `Ignore` | Ignore the label |

```fsharp
taskResult {
    // Warn on NSFW content instead of hiding
    do! Bluesky.setContentLabelPref agent
            "nsfw"
            AppBskyActor.Defs.ContentLabelPrefVisibility.Warn
            None // None = Bluesky's built-in labeler

    // Set preference for a custom labeler
    do! Bluesky.setContentLabelPref agent
            "spoiler"
            AppBskyActor.Defs.ContentLabelPrefVisibility.Hide
            (Some labelerDid)
}
```

### Adult Content

Toggle the adult content master switch:

```fsharp
do! Bluesky.setAdultContentEnabled agent true
```

## Thread Sorting

Set how replies are sorted when viewing a thread. The `ThreadViewPrefSort` DU options:

| Case | Description |
|---|---|
| `Oldest` | Chronological order (oldest first) |
| `Newest` | Reverse chronological (newest first) |
| `MostLikes` | Sort by like count |
| `Random` | Random order |
| `Hotness` | Sort by engagement |

```fsharp
do! Bluesky.setThreadViewPref agent AppBskyActor.Defs.ThreadViewPrefSort.Oldest
```

## Hidden Posts

Hide or unhide individual posts from your feeds:

```fsharp
taskResult {
    do! Bluesky.addHiddenPost agent postUri
    // later...
    do! Bluesky.removeHiddenPost agent postUri
}
```

## Custom Preference Updates

For modifications not covered by the convenience functions, use `upsertPreferences` directly. It reads the current preferences, applies your update function, and writes the result back:

```fsharp
taskResult {
    do! Bluesky.upsertPreferences agent (fun prefs ->
        // prefs is a JsonElement list -- filter, modify, or append
        prefs |> List.filter (fun el ->
            // your custom logic here
            true))
}
```
