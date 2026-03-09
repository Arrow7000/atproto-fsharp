(**
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
(***)

open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

taskResult {
    let! result =
        Bluesky.post agent
            "Hello @other-user.bsky.social! Check https://atproto.com #atproto"
    return result
}

(**
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
*)

let detected = RichText.detect "Hello @my-handle.bsky.social! #atproto"

// Returns:
// [ DetectedMention(6, 28, "my-handle.bsky.social")
//   DetectedTag(30, 38, "atproto") ]

(**
The `DetectedFacet` type is a discriminated union:

```fsharp
type DetectedFacet =
    | DetectedMention of byteStart: int * byteEnd: int * handle: string
    | DetectedLink of byteStart: int * byteEnd: int * uri: string
    | DetectedTag of byteStart: int * byteEnd: int * tag: string
```

### Resolution (Network)

`RichText.resolve` takes detected facets and resolves mentions to DIDs via the API. Mentions that can't be resolved are silently dropped:
*)

// task {} because RichText.resolve returns Task<Facet list>, not Task<Result<_,_>>
task {
    let! facets = RichText.resolve agent detected
    // facets : AppBskyRichtext.Facet.Facet list
    return facets
}

(**
### Combined: `RichText.parse`

`RichText.parse` combines both steps -- detect and resolve in one call:
*)

// task {} because RichText.parse returns Task<Facet list>, not Task<Result<_,_>>
task {
    let! facets = RichText.parse agent "Hello @my-handle.bsky.social! #atproto"
    return facets
}

(**
## Posting with Pre-Computed Facets

If you've already computed facets (for example, from `RichText.parse` or constructed manually), use `Bluesky.postWithFacets` to skip auto-detection:
*)

(*** hide ***)
let text = "Check out https://example.com!"
(***)

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

(**
Note: `RichText.parse` and `RichText.resolve` return bare `Task<Facet list>` (not `Task<Result<_, _>>`), because unresolvable mentions are silently dropped rather than producing an error. When mixing these with result-returning functions like `Bluesky.postWithFacets`, use `task {}` and match the result manually.

This is useful when you want to:
- Cache resolved mention DIDs across multiple posts
- Filter out certain facet types
- Add custom facets

## Measuring Text Length

Bluesky enforces a 300-grapheme limit on posts (not 300 characters or 300 bytes). Check grapheme length before posting to avoid the server rejecting your post:
*)

let len = RichText.graphemeLength "Hello world!"  // 12
let emojiLen = RichText.graphemeLength "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466"  // 1 (family emoji)

(**
Byte length is used internally for facet offsets -- you rarely need it directly:
*)

let bytes = RichText.byteLength "Hello!"  // 6
let emojiBytes = RichText.byteLength "\U0001F600"  // 4

(**
## Rich Text in Chat Messages

`Chat.sendMessage` auto-detects mentions, links, and hashtags -- just like `Bluesky.post`:
*)

(*** hide ***)
let convoId = "some-convo-id"
(***)

taskResult {
    let! result =
        Chat.sendMessage agent convoId "Check out https://example.com! cc @friend.bsky.social"
    return result
}

(**
If you need to supply custom or pre-computed facets, drop down to the raw API:
*)

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

(**
See the [Chat / DMs](chat.html) guide for more on sending messages.

## Rich Text Manipulation

The `RichTextValue` type pairs text with its facets and supports safe manipulation that keeps byte offsets correct.

### RichTextValue

| Field | Type | Description |
|-------|------|-------------|
| `Text` | `string` | The text content |
| `Facets` | `Facet list` | Rich text annotations |

### Creating

| Function | Signature | Description |
|----------|-----------|-------------|
| `RichText.create` | `string -> Facet list -> RichTextValue` | Create from text and facets |
| `RichText.plain` | `string -> RichTextValue` | Create plain text (no facets) |

### Manipulating

| Function | Signature | Description |
|----------|-----------|-------------|
| `RichText.insert` | `int -> string -> RichTextValue -> RichTextValue` | Insert text at byte index, shifting facets |
| `RichText.delete` | `int -> int -> RichTextValue -> RichTextValue` | Delete byte range, adjusting facets |
| `RichText.segments` | `RichTextValue -> RichTextSegment list` | Split into annotated segments |
| `RichText.sanitize` | `RichTextValue -> RichTextValue` | Remove invalid or out-of-range facets |
| `RichText.truncate` | `int -> RichTextValue -> RichTextValue` | Truncate to grapheme length, trimming facets |

### Example
*)

(*** hide ***)
let facets = Unchecked.defaultof<AppBskyRichtext.Facet.Facet list>
(***)

let rt = RichText.create "Hello @alice.bsky.social!" facets

// Insert text -- facet offsets shift automatically
let updated = rt |> RichText.insert 0 "Hey! "

// Truncate to 50 graphemes -- facets that extend past the boundary are trimmed
let truncated = rt |> RichText.truncate 50

// Split into segments for rendering
let segments = rt |> RichText.segments
for seg in segments do
    match seg.Facet with
    | Some _ -> printfn "[rich] %s" seg.Text
    | None -> printfn "%s" seg.Text
