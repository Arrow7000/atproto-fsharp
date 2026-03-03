---
title: Rich Text
category: Advanced Guides
categoryindex: 3
index: 12
description: Understand and control rich text facet detection in FSharp.ATProto
keywords: rich text, facets, mentions, links, hashtags, utf-8
---

# Rich Text

> Most examples use `taskResult {}`. Some use `task {}` where noted -- `RichText` functions return bare `Task` (not `Task<Result<>>`), so `task {}` is appropriate when only calling those functions. See the [Error Handling guide](error-handling.html) for details.

Posts and messages on Bluesky support rich text through **[facets](../concepts.html)** -- annotations on byte ranges within the text that mark up @mentions, links, and #hashtags. The AT Protocol specifies facet positions in UTF-8 byte offsets, not character indices.

FSharp.ATProto handles all of this for you by default, but also gives you full control when you need it.

## The Easy Path: `Bluesky.post`

The simplest way to post with rich text is `Bluesky.post`, which auto-detects mentions, links, and hashtags, resolves mentions to DIDs, and posts everything in one call:

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

taskResult {
    let! result =
        Bluesky.post agent
            "Hello @other-user.bsky.social! Check https://atproto.com #atproto"
    return result
}
```

This detects three facets:
1. `@other-user.bsky.social` -- resolved to its [DID](../concepts.html) via the API
2. `https://atproto.com` -- marked as a link
3. `#atproto` -- marked as a hashtag

## How Facets Work

A facet is a triple of (byte range, feature type, value):

- **Mentions** (`app.bsky.richtext.facet#mention`): byte range covering `@handle`, value is the resolved DID
- **Links** (`app.bsky.richtext.facet#link`): byte range covering the URL, value is the URL itself
- **Tags** (`app.bsky.richtext.facet#tag`): byte range covering `#tag`, value is the tag text (without the `#`)

Byte offsets are in **UTF-8 bytes**, not characters. This matters for text with emoji or non-ASCII characters. For example, a single emoji like a red heart might be 1 character but 4+ UTF-8 bytes.

## Step-by-Step: Detect, Then Resolve

For more control, split the process into detection and resolution.

### Detection (Offline)

`RichText.detect` scans text for patterns and returns `DetectedFacet` values with byte offsets. No network calls are made:

```fsharp
let detected = RichText.detect "Hello @my-handle.bsky.social! #atproto"

// Returns:
// [ DetectedMention(6, 28, "my-handle.bsky.social")
//   DetectedTag(30, 38, "atproto") ]
```

The `DetectedFacet` type is a discriminated union:

```fsharp
type DetectedFacet =
    | DetectedMention of byteStart: int * byteEnd: int * handle: string
    | DetectedLink of byteStart: int * byteEnd: int * uri: string
    | DetectedTag of byteStart: int * byteEnd: int * tag: string
```

### Resolution (Network)

`RichText.resolve` takes detected facets and resolves mentions to DIDs via the API. Mentions that can't be resolved are silently dropped:

```fsharp
// task {} because RichText.resolve returns Task<Facet list>, not Task<Result<_,_>>
task {
    let! facets = RichText.resolve agent detected
    // facets : AppBskyRichtext.Facet.Facet list
    return facets
}
```

### Combined: `RichText.parse`

`RichText.parse` combines both steps -- detect and resolve in one call:

```fsharp
// task {} because RichText.parse returns Task<Facet list>, not Task<Result<_,_>>
task {
    let! facets = RichText.parse agent "Hello @my-handle.bsky.social! #atproto"
    return facets
}
```

## Posting with Pre-Computed Facets

If you've already computed facets (for example, from `RichText.parse` or constructed manually), use `Bluesky.postWithFacets` to skip auto-detection:

```fsharp
// task {} because RichText.parse returns bare Task, requiring manual match on postWithFacets result
task {
    let! facets = RichText.parse agent text

    // Maybe filter or modify facets here...
    let filteredFacets = facets |> List.filter (fun _ -> true)

    let! result = Bluesky.postWithFacets agent text filteredFacets
    match result with
    | Ok postRef -> printfn "Posted: %s" (AtUri.value postRef.Uri)
    | Error err -> printfn "Failed: %A" err
}
```

Note: `RichText.parse` and `RichText.resolve` return bare `Task<Facet list>` (not `Task<Result<_, _>>`), because unresolvable mentions are silently dropped rather than producing an error. When mixing these with result-returning functions like `Bluesky.postWithFacets`, use `task {}` and match the result manually.

This is useful when you want to:
- Cache resolved mention DIDs across multiple posts
- Filter out certain facet types
- Add custom facets

## Measuring Text Length

Bluesky enforces a 300-grapheme limit on posts (not 300 characters or 300 bytes). Check grapheme length before posting to avoid the server rejecting your post:

```fsharp
let len = RichText.graphemeLength "Hello world!"  // 12
let emojiLen = RichText.graphemeLength "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466"  // 1 (family emoji)
```

Byte length is used internally for facet offsets -- you rarely need it directly:

```fsharp
let bytes = RichText.byteLength "Hello!"  // 6
let emojiBytes = RichText.byteLength "\U0001F600"  // 4
```

## Rich Text in Chat Messages

`Chat.sendMessage` auto-detects mentions, links, and hashtags -- just like `Bluesky.post`:

```fsharp
taskResult {
    let! result =
        Chat.sendMessage agent convoId "Check out https://example.com! cc @friend.bsky.social"
    return result
}
```

If you need to supply custom or pre-computed facets, drop down to the raw API:

```fsharp
// task {} because RichText.parse returns bare Task, requiring manual match on the XRPC result
task {
    let text = "Check out https://example.com!"
    let! facets = RichText.parse agent text

    let! result =
        ChatBskyConvo.SendMessage.call (AtpAgent.withChatProxy agent)
            { ConvoId = convoId
              Message =
                { Text = text
                  Facets = if facets.IsEmpty then None else Some facets
                  Embed = None } }

    match result with
    | Ok msg -> printfn "Sent with custom facets"
    | Error err -> printfn "Failed: %A" err
}
```

See the [Chat / DMs](chat.html) guide for more on sending messages.
