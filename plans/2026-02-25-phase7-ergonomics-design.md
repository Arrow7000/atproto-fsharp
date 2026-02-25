# Phase 7: Ergonomics & Type Safety — Design

## Goal

Transform the library's user-facing API from raw strings and opaque `JsonElement` fields into a fully typed, self-documenting F# API with discriminated unions, validated identifier types, and polished convenience methods.

## Current State

The library works but the consumer experience has three major gaps:

1. **~73 inline unions are `JsonElement`** — embeds, thread nodes, facet features, reply refs, preferences, moderation subjects. Consumers must manually inspect `$type` and deserialize.
2. **~608 identifier fields are `string`** — DIDs, Handles, AT-URIs, CIDs, datetimes, NSIDs, TIDs, record keys, languages. The Syntax layer has validated single-case DU types (726 tests) but they're completely disconnected from generated code.
3. **Convenience API roughness** — bare tuples for image uploads, 4-string `reply` signature, inconsistent error types, `uploadBlob` returns `JsonElement`.

## Architecture

### Stream 1: Typed Unions (Codegen)

**Root cause:** `TypeMapping.lexTypeToFSharpType` returns `"JsonElement"` for all `Union _` cases (line 22 of TypeMapping.fs).

**Fix:** When `generateRecordWidget` encounters a union property, instead of emitting `JsonElement`:

1. Generate a companion DU type using the existing `generateUnionWidget` function (already works for subscription messages)
2. Name it `{PropertyName}` scoped to the parent def — e.g., the `embed` field on `PostView` generates a DU type within the same module
3. Emit the field type as that DU name

**DU structure** (reuses existing pattern):
```fsharp
[<JsonFSharpConverter(JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.UnwrapSingleFieldCases, unionTagName = "$type")>]
type PostViewEmbed =
    | [<JsonName("app.bsky.embed.images#view")>] ImagesView of AppBskyEmbed.Images.View
    | [<JsonName("app.bsky.embed.external#view")>] ExternalView of AppBskyEmbed.External.View
    | [<JsonName("app.bsky.embed.record#view")>] RecordView of AppBskyEmbed.Record.View
    | [<JsonName("app.bsky.embed.recordWithMedia#view")>] RecordWithMediaView of AppBskyEmbed.RecordWithMedia.View
    | [<JsonName("app.bsky.embed.video#view")>] VideoView of AppBskyEmbed.Video.View
    | Unknown of string * System.Text.Json.JsonElement
```

**Deduplication:** When two fields in the same record have identical union refs (e.g., `ReplyRef.Parent` and `ReplyRef.Root`), emit one shared DU type. Cross-module duplicates get their own copies (simpler, no coupling).

**Closed unions:** The 2 `"closed": true` unions (both in `applyWrites`) omit the `Unknown` fallback case.

**`unknown` type fields:** Fields with Lexicon type `"unknown"` (dynamically-typed record payloads, ~41 occurrences) remain `JsonElement` — these are genuinely untyped.

**`blob` type fields:** Blob references (~10 occurrences) remain `JsonElement` for now.

### Stream 2: Typed Identifiers (Codegen + JSON Converters)

**Root cause:** `TypeMapping.lexTypeToFSharpType` matches `String _ -> "string"` ignoring the `Format` field (line 11 of TypeMapping.fs).

**Fix Part A — Type mapping:**

| Lexicon Format | F# Type | Count |
|---------------|---------|-------|
| `did` | `Did` | 156 |
| `handle` | `Handle` | 27 |
| `at-uri` | `AtUri` | 103 |
| `cid` | `Cid` | 56 |
| `nsid` | `Nsid` | 21 |
| `tid` | `Tid` | 11 |
| `record-key` | `RecordKey` | 8 |
| `datetime` | `AtDateTime` | 151 |
| `language` | `Language` | 10 |
| `uri` | `Uri` | 32 |
| `at-identifier` | `string` (ambiguous) | 33 |
| `CidLink` | `Cid` | ~10 |

10 formats get typed. Only `at-identifier` stays as `string` (could be DID or Handle).

**Fix Part B — JSON converters:**

New file with ~10 converters, all following the same pattern:
```fsharp
type DidConverter() =
    inherit JsonConverter<Did>()
    override _.Read(reader, _, _) =
        match Did.parse (reader.GetString()) with
        | Ok did -> did
        | Error _ -> failwithf "Invalid DID: %s" (reader.GetString())
    override _.Write(writer, value, _) =
        writer.WriteStringValue(Did.value value)
```

Register via `[<JsonConverter(typeof<DidConverter>)>]` attribute on each Syntax type. This is cleanest — works everywhere automatically without modifying serializer options.

### Stream 3: Convenience API Polish

**A. Named types for compound parameters:**
```fsharp
type ImageUpload = { Data: byte[]; MimeType: string; AltText: string }
type PostRef = { Uri: AtUri; Cid: Cid }
```

**B. Typed parameters and returns:**
```fsharp
val post:           agent -> text: string -> Task<Result<PostRef, XrpcError>>
val reply:          agent -> text: string -> parent: PostRef -> root: PostRef -> Task<Result<PostRef, XrpcError>>
val like:           agent -> uri: AtUri -> cid: Cid -> Task<Result<AtUri, XrpcError>>
val repost:         agent -> uri: AtUri -> cid: Cid -> Task<Result<AtUri, XrpcError>>
val follow:         agent -> did: Did -> Task<Result<AtUri, XrpcError>>
val block:          agent -> did: Did -> Task<Result<AtUri, XrpcError>>
val deleteRecord:   agent -> atUri: AtUri -> Task<Result<unit, XrpcError>>
val uploadBlob:     agent -> data: byte[] -> mimeType: string -> Task<Result<BlobRef, XrpcError>>
val postWithImages: agent -> text: string -> images: ImageUpload list -> Task<Result<PostRef, XrpcError>>
```

**C. Consistent error types:**
```fsharp
type IdentityError =
    | XrpcError of XrpcError
    | VerificationFailed of string
    | DocumentParseError of string
```

All Identity methods return `Result<_, IdentityError>`.

**D. Typed `uploadBlob`:** Returns a proper record instead of `JsonElement`.

### Stream 4: Consumer Documentation (deferred)

Written after streams 1-3 stabilize the API. Structure:
```
docs/
  index.md, quickstart.md
  guides/ posts.md, profiles.md, feeds.md, social.md, chat.md,
          identity.md, rich-text.md, media.md, pagination.md
```

## Execution Order

1. **JSON converters** — must exist before regeneration so generated code can use Syntax types
2. **Codegen changes** — TypeMapping.fs (formats + unions) + NamespaceGen.fs (inline DU emission). Regenerate.
3. **Convenience API polish** — Bluesky.fs, Chat.fs, Identity.fs, RichText.fs. Update example + tests.
4. **Consumer docs** — rewrite guides against final API.

## Testing Strategy

- All 1,472 existing tests must pass (types change but behavior preserved)
- New converter tests: roundtrip serialize/deserialize for each Syntax type
- New codegen tests: DU emission for inline unions, typed string emission
- Updated convenience tests for new signatures
- Compile-check: Generated.fs must build with typed fields and DU types
