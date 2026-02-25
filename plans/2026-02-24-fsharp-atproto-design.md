# FSharp.ATProto Design Document

Date: 2026-02-24

## Overview

An idiomatic F# library for interacting with the AT Protocol (atproto), the decentralized social networking protocol behind Bluesky. The library uses code generation from the official Lexicon schema files to produce typed API clients, ensuring spec compliance and easy updates as the protocol evolves.

## Key Decisions

- **Primary use case**: Client library (bots, feed readers, post creation)
- **Code generation**: Standalone CLI tool reading Lexicon JSON, emitting .fs files via Fabulous.AST
- **Foundation**: Fully native F#, no C# library dependencies -- only low-level .NET primitives
- **Target**: .NET 9, multi-project solution
- **Implementation order**: Bottom-up, following "wall of correctness" -- each layer verified before the next builds on it

## Project Structure

```
atproto-fsharp/
├── src/
│   ├── FSharp.ATProto.Syntax/          # Identifiers: DID, Handle, NSID, TID, AT-URI, RecordKey, CID
│   ├── FSharp.ATProto.DRISL/           # DRISL/CBOR encoding + CID computation
│   ├── FSharp.ATProto.Lexicon/         # Lexicon JSON schema parser
│   ├── FSharp.ATProto.CodeGen/         # CLI tool: Lexicon model -> F# source files
│   ├── FSharp.ATProto.Core/            # XRPC client, auth, session management
│   └── FSharp.ATProto.Bluesky/         # Generated types + Bluesky-specific helpers
├── tests/
│   ├── FSharp.ATProto.Syntax.Tests/
│   ├── FSharp.ATProto.DRISL.Tests/
│   ├── FSharp.ATProto.Lexicon.Tests/
│   ├── FSharp.ATProto.CodeGen.Tests/
│   ├── FSharp.ATProto.Core.Tests/
│   └── FSharp.ATProto.Integration.Tests/
├── lexicons/                           # Git submodule: bluesky-social/atproto lexicons
├── interop-tests/                      # Git submodule: bluesky-social/atproto-interop-tests
├── docs/plans/
└── FSharp.ATProto.sln
```

### Dependency Graph

```
Syntax (zero deps)
  └─> DRISL (System.Formats.Cbor)
        └─> Core (System.Net.Http, System.Text.Json, FSharp.SystemTextJson)
              └─> Bluesky (generated code)

Lexicon (System.Text.Json) ──> CodeGen (Fabulous.AST, Fantomas.Core)
                                  │
                                  └─> emits source files into Bluesky at dev time
```

Lexicon and CodeGen are development-time tools. They are not runtime dependencies of the client library.

## Layer 1: Syntax (Identifiers)

Single-case discriminated unions with private constructors and smart constructors returning `Result`:

```fsharp
type Did = private Did of string
type Handle = private Handle of string
type Nsid = private Nsid of string
type Tid = private Tid of string
type AtUri = private AtUri of string
type RecordKey = private RecordKey of string
type Cid = private Cid of string
```

Each module exposes:
- `parse : string -> Result<'T, string>` -- validate and construct
- `value : 'T -> string` -- unwrap

Validation uses the regex patterns from the spec. AT-URI validation delegates to the component validators (Did/Handle for authority, Nsid for collection, RecordKey for rkey).

### Testing

- 200+ interop test vectors loaded from `interop-tests/syntax/` text files
- FsCheck roundtrip: `parse(value(x)) = Ok x` for generated valid instances
- FsCheck compositional: valid AT-URI components compose into valid AT-URIs

## Layer 2: DRISL/CBOR

### Data Model

```fsharp
[<RequireQualifiedAccess>]
type AtpValue =
    | Null
    | Bool of bool
    | Integer of int64
    | String of string
    | Bytes of byte array
    | Link of Cid
    | Array of AtpValue list
    | Object of Map<string, AtpValue>
    | Blob of BlobRef

type BlobRef = {
    Ref: Cid
    MimeType: string
    Size: int64
}
```

### Encoding/Decoding

Built on `System.Formats.Cbor` with `CborConformanceMode.Canonical`. Additional validation on top:

- Reject all floats (ATProto forbids them)
- String-only map keys
- Only Tag 42 allowed (CID links)
- Validate sort order on read (Canonical mode only sorts on write)
- No bignum tags (2, 3)
- All bytes consumed (no trailing data)
- CID validation inside Tag 42 (CIDv1, SHA-256, codec 0x71 or 0x55)

### CID Implementation

Minimal hand-rolled implementation covering only what ATProto needs:
- CIDv1 with varint encoding
- SHA-256 hashing (System.Security.Cryptography)
- Base32 string encoding with `b` prefix
- Codec 0x71 (DRISL) for data objects, 0x55 (raw) for blobs

### JSON Conversion

`AtpJson` module converts between standard JSON and `AtpValue`:
- `{"$link": "bafy..."}` <-> `AtpValue.Link`
- `{"$bytes": "base64..."}` <-> `AtpValue.Bytes`
- `{"$type": "..."}` preserved as a regular string field in objects

### Testing

- 126 test vectors (106 DASL fixtures + 20 atproto interop)
- FsCheck roundtrip: `decode(encode(x)) = Ok x`
- FsCheck no-float invariant
- FsCheck JSON conversion roundtrip: `fromJson(toJson(x)) = Ok x`
- Known CID verification against interop test expected values

## Layer 3: Lexicon Parser

Reads Lexicon JSON files into an F# domain model:

```fsharp
type LexiconDoc = {
    Lexicon: int
    Id: Nsid
    Description: string option
    Defs: Map<string, LexDef>
}

type LexDef =
    | Record of LexRecord
    | Query of LexQuery
    | Procedure of LexProcedure
    | Subscription of LexSubscription
    | Token of LexToken
    | PermissionSet of LexPermissionSet

type LexType =
    | Boolean of LexBoolean
    | Integer of LexInteger
    | String of LexString
    | Bytes of LexBytes
    | CidLink
    | Blob of LexBlob
    | Array of LexArray
    | Object of LexObject
    | Ref of string
    | Union of LexUnion
    | Unknown
```

Each type variant carries its constraints (min/max length, format, enum, knownValues, default, etc.).

The parser:
1. Deserializes JSON with `System.Text.Json`
2. Resolves `#local` refs to fully-qualified `nsid#defName` form
3. Validates structural constraints (required fields, valid ref targets)

### Testing

- 10 interop test vectors for document validation (3 valid, 7 invalid)
- 59 record-data validation vectors (3 valid, 56 invalid)
- All 324 real Lexicon files must parse without error
- Snapshot tests: parsed model matches expected structure for key schemas

## Layer 4: Code Generator

CLI tool invoked as:
```
dotnet run --project src/FSharp.ATProto.CodeGen -- --lexdir ./lexicons --outdir ./src/FSharp.ATProto.Bluesky/Generated
```

Uses Fabulous.AST + Fantomas.Core to emit formatted F# source files.

### What It Generates

- **F# record types** for each Lexicon `object` schema
- **Discriminated unions** for each `union` type
  - Open unions include `Unknown of string * JsonElement` case
  - Closed unions do not
- **Module hierarchy** mirroring NSID namespaces (e.g., `module App.Bsky.Feed`)
- **XRPC method signatures**: queries become `GET`, procedures become `POST`, both returning `Task<Result<'Output, XrpcError>>`
- **JSON serialization attributes/converters** using `System.Text.Json` + `FSharp.SystemTextJson`
  - Custom `$type` discriminator handling for unions/records
- **String constants** for token types

### Design Decisions

- `nullable` + `required` maps to F# `option` with `[<Required>]` attribute where needed
- `default` values emitted as default parameter values on record construction helpers
- `unknown` type maps to `JsonElement`
- `knownValues` generates a module with string constants but the field type remains `string`
- Generated code is checked into the repo (not regenerated on every build)

### Testing

- The generated code must compile (compilation is a test)
- Serialization roundtrip: generate types, serialize to JSON, deserialize, compare
- Spot-check against known Lexicon schemas (app.bsky.feed.post, com.atproto.repo.createRecord)

## Layer 5: XRPC Client & Core Runtime

### Agent

```fsharp
type AtpAgent = {
    Client: HttpClient
    Session: AtpSession option
    BaseUrl: Uri
}

module AtpAgent =
    let create (baseUrl: string) : AtpAgent = ...
    let login (agent: AtpAgent) (identifier: string) (password: string) : Task<Result<AtpAgent, XrpcError>> = ...
```

### XRPC

```fsharp
module Xrpc =
    let query<'P, 'O> (agent: AtpAgent) (nsid: Nsid) (params: 'P) : Task<Result<'O, XrpcError>> = ...
    let procedure<'I, 'O> (agent: AtpAgent) (nsid: Nsid) (input: 'I) : Task<Result<'O, XrpcError>> = ...
```

- Query params serialized to URL query string
- Procedure input serialized to JSON request body
- Responses deserialized via the generated types
- Automatic session token refresh on 401
- Cursor-based pagination helper
- Rate limit handling (429 with Retry-After)

### Auth

Phase 1: Legacy session auth (createSession/refreshSession with app passwords).
Future: OAuth 2.1 + DPoP in a separate module.

### Testing

- Unit tests with mocked HttpClient for XRPC serialization/error handling
- Integration tests against local PDS Docker container (`ghcr.io/bluesky-social/pds:0.4`)
- End-to-end: create account, create post, read it back, verify content matches

## Layer 6: Bluesky Helpers (Future)

Deferred to after the core is working:
- Rich text / facet parsing and construction
- Identity resolution (DID:PLC, DID:Web, handle via DNS/HTTPS)
- WebSocket firehose consumption
- Label behavior computation
- Convenience methods (post, like, follow, etc.)

## Testing Strategy Summary

| Layer | Test Source | Method | Vector Count |
|-------|-----------|--------|-------------|
| Syntax | interop-tests/syntax/ | Line-by-line valid/invalid + FsCheck | 200+ |
| DRISL | DASL fixtures + interop-tests/data-model/ | Encode/decode/reject + FsCheck roundtrips | 126 |
| Lexicon | interop-tests/lexicon/ + all 324 real files | Parse + validate | 69 + 324 |
| Codegen | Compilation + serialization roundtrip | Generated code compiles and roundtrips | N/A |
| XRPC | Mock HTTP + local PDS Docker | Unit + integration | N/A |
| E2E | Local PDS Docker | Full workflow | N/A |

Test framework: Expecto + FsCheck.

## Key Dependencies

| Purpose | Package |
|---------|---------|
| CBOR encoding | System.Formats.Cbor |
| JSON serialization | System.Text.Json + FSharp.SystemTextJson |
| HTTP | System.Net.Http (built-in) |
| Hashing | System.Security.Cryptography (built-in) |
| Code gen AST | Fabulous.AST |
| Code formatting | Fantomas.Core |
| Test framework | Expecto |
| Property testing | FsCheck |

## Implementation Phases

1. **Identifiers + scaffold**: Project structure, CI, Syntax library with full test coverage
2. **DRISL/CBOR**: Binary encoding layer with CID computation
3. **Lexicon parser**: Read and model all 324 schema files
4. **Code generator**: Lexicon -> F# source via Fabulous.AST
5. **XRPC client + generated API**: HTTP layer, auth, generated typed methods
6. **Higher-level features**: Identity resolution, firehose, rich text, convenience API
