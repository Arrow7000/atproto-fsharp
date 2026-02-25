# Existing AT Protocol Libraries Reference

## Official Implementations

### TypeScript (github.com/bluesky-social/atproto) -- 9k+ stars
- Monorepo: @atproto/api, @atproto/lexicon, @atproto/lex-cli, @atproto/xrpc, etc.
- Codegen via `lex-cli`: reads Lexicon JSON, emits TS types + validators + client/server stubs
- Full SDK: HTTP, identifiers, crypto, MST, lexicon, identity, streaming, OAuth

### Go / Indigo (github.com/bluesky-social/indigo) -- 1.3k stars
- Contains BGS/relay implementation
- Codegen via `cmd/lexgen`: reads Lexicon JSON, emits Go structs + CBOR marshaling
- Full SDK: HTTP, identifiers, crypto, MST, streaming

## Community Libraries (Key Ones)

### Python -- MarshalX/atproto (639 stars)
- Full codegen from Lexicons via `atproto_codegen`
- Only community SDK on official ATProto SDKs page
- Modular: client, server, lexicon, codegen, core, identity, crypto, firehose

### Rust -- atrium-rs/atrium (408 stars)
- Codegen via `lexgen` crate
- Trait-based HTTP client abstraction

### Kotlin -- christiandeange/ozone (122 stars)
- Gradle Plugin for Lexicon -> Kotlin Multiplatform bindings
- Reusable codegen plugin

### Dart -- myConsciousness/atproto.dart (205 stars)
- Codegen with `freezed` for type-safe unions

## .NET Ecosystem (Most Relevant)

### FishyFlip (113 stars, v4.2.0) -- github.com/drasticactions/FishyFlip
- C# with Roslyn Source Generator (FFSourceGen) reading Lexicons
- Most complete/maintained .NET library
- NuGet: FishyFlip

### idunno.Bluesky (83 stars, v1.1.0+) -- github.com/blowdart/idunno.Bluesky
- C#, hand-written, layered architecture
- AtProtoHttpResult<T> resembles F# Result
- By Barry Dorrans (well-known .NET security expert)

### Others
- Bluesky.Net (48 stars, alpha)
- atprotosharp (15 stars, early stage)
- atompds -- .NET PDS implementation (proof of concept)

**No existing F# library.**

## Codegen Pattern (Universal)

Every mature SDK follows the same pattern:
1. Clone canonical Lexicon JSON files from bluesky-social/atproto/lexicons/
2. Parse JSON into internal Lexicon type model
3. Map Lexicon types to language-native types
4. Emit: data types/models, validation, API client methods
5. Handle union types (the trickiest part -- each language differs)
