# Phase 6: Rich Text, Identity Resolution & Convenience Methods — Design

Date: 2026-02-25

## Overview

Add three hand-written modules to `FSharp.ATProto.Bluesky` that provide high-level functionality on top of the generated XRPC wrappers: rich text facet detection/construction, identity resolution, and convenience methods for common Bluesky operations (post, like, follow, etc.).

## Scope

**In scope:**
- Rich text facet detection (mentions, links, hashtags) with correct UTF-8 byte indexing
- Handle-to-DID resolution for mention facets
- DID resolution (DID:PLC via plc.directory, DID:Web via .well-known)
- Handle resolution via XRPC `resolveHandle` endpoint
- Bidirectional identity verification
- Convenience methods: post, reply, like, repost, follow, block, delete, uploadBlob, postWithImages

**Out of scope (deferred to Phase 7):**
- WebSocket firehose (`com.atproto.sync.subscribeRepos`)
- Label behavior computation
- OAuth 2.1 + DPoP
- DNS TXT handle resolution (XRPC resolveHandle delegates to the PDS)
- Caching layer for identity resolution
- upsertProfile with CAS retry
- Video upload workflow

## Project Structure

All new code lives in `FSharp.ATProto.Bluesky` alongside the generated code:

```
src/FSharp.ATProto.Bluesky/
├── RichText.fs          # Facet detection + construction
├── Identity.fs          # DID/Handle resolution
├── Bluesky.fs           # Convenience methods
└── Generated/
    └── Generated.fs     # (existing) 228 XRPC wrappers + types

tests/FSharp.ATProto.Bluesky.Tests/
├── RichTextTests.fs     # Detection, byte indexing, resolution
├── IdentityTests.fs     # DID doc parsing, resolution
├── BlueskyTests.fs      # Convenience method integration
└── Main.fs
```

Compile order in fsproj: `Generated.fs` → `RichText.fs` → `Identity.fs` → `Bluesky.fs` (each may depend on the prior).

## Module 1: RichText

### Types

```fsharp
module RichText =
    /// A detected facet before handle resolution
    type DetectedFacet =
        | DetectedMention of byteStart: int * byteEnd: int * handle: string
        | DetectedLink of byteStart: int * byteEnd: int * uri: string
        | DetectedTag of byteStart: int * byteEnd: int * tag: string
```

### Functions

| Function | Signature | Purpose |
|----------|-----------|---------|
| `detect` | `string -> DetectedFacet list` | Pure. Detect mentions, links, hashtags with UTF-8 byte offsets. |
| `resolve` | `AtpAgent -> DetectedFacet list -> Task<Facet list>` | Async. Resolve mention handles to DIDs. Failed mentions silently dropped. |
| `parse` | `AtpAgent -> string -> Task<Facet list>` | Convenience: `detect` then `resolve`. |
| `graphemeLength` | `string -> int` | Count Unicode grapheme clusters via `StringInfo`. |
| `byteLength` | `string -> int` | Count UTF-8 bytes. |

### Detection Algorithm

Regex matching on the original string, then convert character match positions to UTF-8 byte offsets via `Encoding.UTF8.GetByteCount(text.Substring(0, charIndex))`.

**Mention pattern:** `(^|\s|\()(@)([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?` — requires at least one dot (valid domain-like handle).

**Link pattern:** `https?://[^\s)]+` with trailing punctuation cleanup (strip `.,:;!?` and unmatched `)` from end). Bare domain detection: `[a-z][a-z0-9]*(\.[a-z0-9]+)+[^\s]*` — gets `https://` prepended in the facet URI.

**Hashtag pattern:** `(^|\s)[#＃]([^\s\p{P}]*[^\d\s\p{P}]+[^\s\p{P}]*)` — must contain at least one non-digit non-punctuation char. Tag value stored without the `#` prefix. Max 64 graphemes / 640 bytes.

### UTF-8 Byte Indexing

All facet byte indices are offsets into the UTF-8 encoded text. The conversion function:

```fsharp
let charIndexToByteIndex (text: string) (charIndex: int) =
    Encoding.UTF8.GetByteCount(text, 0, charIndex)
```

This correctly handles multi-byte characters (accented Latin = 2 bytes, CJK = 3 bytes, emoji = 4 bytes).

### Handle Resolution for Mentions

`resolve` calls `com.atproto.identity.resolveHandle` for each `DetectedMention`. On failure (handle not found, network error), the mention is silently dropped — rendered as plain text with no facet.

## Module 2: Identity

### Types

```fsharp
module Identity =
    type AtprotoIdentity = {
        Did: string
        Handle: string option
        PdsEndpoint: string option
        SigningKey: string option
    }
```

### Functions

| Function | Signature | Purpose |
|----------|-----------|---------|
| `parseDidDocument` | `JsonElement -> Result<AtprotoIdentity, string>` | Pure. Extract AT Protocol fields from a DID document. |
| `resolveDid` | `AtpAgent -> string -> Task<Result<AtprotoIdentity, string>>` | Resolve DID via PLC directory or did:web. |
| `resolveHandle` | `AtpAgent -> string -> Task<Result<string, XrpcError>>` | Resolve handle to DID via XRPC. |
| `resolveIdentity` | `AtpAgent -> string -> Task<Result<AtprotoIdentity, string>>` | Full bidirectional resolution from handle or DID. |

### DID Resolution

**DID:PLC:** HTTP GET to `https://plc.directory/{did}`, parse response as DID document JSON.

**DID:Web:** Convert `did:web:{domain}` to `https://{domain}/.well-known/did.json`, HTTP GET.

Dispatch by DID method prefix (`did:plc:` vs `did:web:`).

### DID Document Parsing

Extract from the JSON:
- `id` → Did
- `alsoKnownAs` → first entry matching `at://{handle}` → Handle
- `verificationMethod` → entry with `id` ending `#atproto` → `publicKeyMultibase` → SigningKey
- `service` → entry with `id` ending `#atproto_pds` and `type` = `AtprotoPersonalDataServer` → `serviceEndpoint` → PdsEndpoint

### Bidirectional Verification

```
resolveIdentity(identifier):
  if looks like a handle:
    did = resolveHandle(handle)
    identity = resolveDid(did)
    if identity.Handle <> Some handle: identity with Handle = None
  if looks like a DID:
    identity = resolveDid(did)
    if identity.Handle is Some h:
      reverseDid = resolveHandle(h)
      if reverseDid <> did: identity with Handle = None
  return identity
```

### DNS TXT — Deferred

DNS TXT record lookup (`_atproto.{handle}`) is deferred. The XRPC `resolveHandle` endpoint delegates to the PDS, which performs DNS resolution server-side. Direct DNS resolution (via DNS-over-HTTPS) can be added later for self-sovereign resolution without trusting a PDS.

## Module 3: Bluesky (Convenience Methods)

### Functions

| Function | Boilerplate Abstracted |
|----------|----------------------|
| `post agent text` | Auto-detect facets, inject `createdAt` + session DID, serialize to JsonElement, wrap in CreateRecord |
| `postWith agent text facets` | Same but with pre-built facets (skip detection) |
| `postWithImages agent text images` | Upload blobs, construct `embed.images`, create post. `images` is `(byte[] * string * string) list` = (data, mimeType, altText) |
| `reply agent text parentUri parentCid rootUri rootCid` | Construct ReplyRef with root + parent strong refs |
| `like agent uri cid` | Construct `feed.like` record with StrongRef subject |
| `repost agent uri cid` | Construct `feed.repost` record with StrongRef subject |
| `follow agent did` | Construct `graph.follow` record with subject DID |
| `block agent did` | Construct `graph.block` record with subject DID |
| `deleteRecord agent atUri` | Parse AT-URI, construct DeleteRecord with correct collection + rkey |
| `uploadBlob agent data mimeType` | POST raw bytes with Content-Type header |

### Internal Helpers

```fsharp
/// Create a record in the logged-in user's repo
let private createRecord (agent: AtpAgent) (collection: string) (record: JsonElement) =
    // Injects agent.Session.Did as repo, sets collection, serializes record
    ComAtprotoRepo.CreateRecord.call agent { ... }

/// Get current ISO 8601 timestamp
let private nowTimestamp () = DateTimeOffset.UtcNow.ToString("o")
```

All convenience methods return `Task<Result<'Output, XrpcError>>`, consistent with the rest of the API.

### Record Serialization

Each convenience method constructs the appropriate Bluesky record type, then serializes it to `JsonElement` via `JsonSerializer.SerializeToElement(record, Json.options)` for the `CreateRecord.Input.Record` field.

## Testing Strategy

### New Project: FSharp.ATProto.Bluesky.Tests

**RichText tests:**
- Detection: mentions, links, hashtags in simple ASCII text
- Multi-byte: correct byte offsets with emoji, CJK, accented characters
- Edge cases: overlapping patterns, bare domains, trailing punctuation, `@handle` at start/end of text
- Resolution: mocked agent, successful mention → facet with DID, failed mention → dropped
- `graphemeLength`: emoji sequences, combining characters, ZWJ sequences
- `byteLength`: multi-byte strings
- FsCheck: all detected byte ranges within `[0, byteLength(text)]`, non-overlapping, sorted

**Identity tests:**
- `parseDidDocument`: real DID document JSON samples (PLC + Web), extract all fields
- `parseDidDocument`: missing fields handled gracefully (None, not error)
- `resolveDid`: mocked HTTP for PLC and did:web responses
- `resolveIdentity`: mocked bidirectional verification (pass and fail cases)

**Bluesky convenience tests:**
- Each method: mocked agent, verify correct XRPC call with right collection name, record shape, timestamps
- `deleteRecord`: verify AT-URI parsed correctly into repo + collection + rkey
- `postWithImages`: verify blob upload called for each image, embed constructed correctly
- `post`: verify facets auto-detected and included

### Test Infrastructure

Reuse the `MockHandler` pattern from `Core.Tests` — custom `HttpMessageHandler` that captures requests and returns configured responses.
