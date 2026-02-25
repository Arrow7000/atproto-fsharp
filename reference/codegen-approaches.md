# Code Generation Approaches for F#

## Decision: Standalone CLI tool (chosen)

## Options Evaluated

### Type Providers -- REJECTED
- Erased types can't cross assembly boundaries (fatal for multi-project SDK)
- Can't generate DUs or F# records (only .NET classes)
- Lexicon isn't JSON Schema -- custom parser needed regardless
- Multiple post-mortems from abandoned attempts
- Build nondeterminism issues (SwaggerProvider)

### Myriad Plugin -- CONSIDERED
- F#-native MSBuild code generation
- Generates idiomatic F# (records, DUs, modules)
- WoofWare.Myriad has useful plugins (JsonParse, HttpClient)
- Adds build dependency, less control than standalone tool

### Standalone CLI Tool -- CHOSEN
- Used by every mature AT Protocol SDK across all languages
- Maximum control over output
- Generated .fs files are inspectable, debuggable, searchable
- Deterministic (can be checked into repo)
- AOT/trimming compatible (no reflection)

## Implementation Stack

### Fabulous.AST (v1.10.0)
- DSL for constructing F# syntax trees
- Uses Fantomas.Core for formatting
- Example: `Oak() { AnonymousModule() { Record("Post") { Field("uri", "string") } } }`

### Fantomas.Core
- F# code formatter, used by Fabulous.AST internally
- Also usable directly for code generation via `Gen.mkOak` + `Gen.run`

### Precedent: Falanx (Protobuf for F#)
- Closest architectural precedent
- Parses .proto files with Froto
- Uses FsAst + Fantomas to emit formatted F# code
- Generates idiomatic F# records and DUs
- MSBuild integration for build-time generation

## Lexicon -> F# Type Mapping

| Lexicon Type | F# Type |
|-------------|---------|
| object | Record type |
| union | Discriminated union (with Unknown case for open unions) |
| string | string (with format-specific DU wrappers for did, handle, etc.) |
| integer | int64 |
| boolean | bool |
| bytes | byte array |
| cid-link | Cid |
| blob | BlobRef record |
| array | list |
| ref | Reference to another generated type |
| unknown | JsonElement |
| token | String constant |
| nullable field | option type |
| optional field | option type |
| required + nullable | Nullable<'T> or ValueOption? (design decision) |

## Key .NET Libraries

| Purpose | Package | Version |
|---------|---------|---------|
| Code gen AST | Fabulous.AST | 1.10.0 |
| Code formatting | Fantomas.Core | 7.0.1 (via Fabulous.AST) |
| JSON serialization | System.Text.Json | built-in |
| F# JSON support | FSharp.SystemTextJson | 1.4.36 |
| CBOR encoding | System.Formats.Cbor | 9.0.13 |
| HTTP client | System.Net.Http | built-in |
| Hashing | System.Security.Cryptography | built-in |
| Test framework | Expecto | 10.2.3 |
| Property testing | FsCheck | 2.16.6 |
| Expecto+FsCheck | Expecto.FsCheck | 10.2.3 |

## CBOR/DRISL Notes

- No existing .NET DAG-CBOR or DRISL library -- must build on System.Formats.Cbor
- CborConformanceMode.Canonical handles most requirements
- Must add: float rejection, sort validation on read, Tag 42 only, string-only keys
- CID implementation: hand-roll for ATProto subset (varint + SHA-256 + base32, ~50 lines)
- No multiformats library dependency needed
