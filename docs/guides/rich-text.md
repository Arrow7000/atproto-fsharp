---
title: Rich Text
category: Guides
categoryindex: 1
index: 7
description: Understand and control rich text facet detection in FSharp.ATProto
keywords: rich text, facets, mentions, links, hashtags, utf-8
---

# Rich Text

Posts and messages on Bluesky support rich text through **facets** -- annotations on byte ranges within the text that mark up @mentions, links, and #hashtags. The AT Protocol specifies facet positions in UTF-8 byte offsets, not character indices.

FSharp.ATProto handles all of this for you by default, but also gives you full control when you need it.

## The Easy Path: `Bluesky.post`

The simplest way to post with rich text is `Bluesky.post`, which auto-detects mentions, links, and hashtags, resolves mentions to DIDs, and posts everything in one call:

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

let! result =
    Bluesky.post agent
        "Hello @other-user.bsky.social! Check https://atproto.com #atproto"
```

This detects three facets:
1. `@other-user.bsky.social` -- resolved to its DID via the API
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
let! facets = RichText.resolve agent detected

// facets : AppBskyRichtext.Facet.Facet list
```

### Combined: `RichText.parse`

`RichText.parse` combines both steps -- detect and resolve in one call:

```fsharp
let! facets = RichText.parse agent "Hello @my-handle.bsky.social! #atproto"
```

## Posting with Pre-Computed Facets

If you've already computed facets (for example, from `RichText.parse` or constructed manually), use `Bluesky.postWithFacets` to skip auto-detection:

```fsharp
let! facets = RichText.parse agent text

// Maybe filter or modify facets here...
let filteredFacets = facets |> List.filter (fun _ -> true)

let! result = Bluesky.postWithFacets agent text filteredFacets
```

This is useful when you want to:
- Cache resolved mention DIDs across multiple posts
- Filter out certain facet types
- Add custom facets

## Measuring Text Length

Bluesky enforces a 300-grapheme limit on posts (not 300 characters or 300 bytes). Use `RichText.graphemeLength` to check:

```fsharp
let len = RichText.graphemeLength "Hello world!"  // 12
let emojiLen = RichText.graphemeLength "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466"  // 1 (family emoji)
```

For byte length (used in facet offsets):

```fsharp
let bytes = RichText.byteLength "Hello!"  // 6
let emojiBytes = RichText.byteLength "\U0001F600"  // 4
```

## Rich Text in Chat Messages

The same facet system works for DMs. Use `RichText.parse` to compute facets, then pass them in the `MessageInput`:

```fsharp
let text = "Check out https://example.com!"
let! facets = RichText.parse agent text

let! result =
    ChatBskyConvo.SendMessage.call (AtpAgent.withChatProxy agent)
        { ConvoId = convoId
          Message =
            { Text = text
              Facets = if facets.IsEmpty then None else Some facets
              Embed = None } }
```

See the [Chat / DMs](chat.html) guide for more on sending messages.
