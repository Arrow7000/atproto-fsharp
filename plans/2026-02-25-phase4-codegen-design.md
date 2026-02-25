# Phase 4: Code Generator Design

Date: 2026-02-25

## Scope

Generate F# types from Lexicon schemas. Types only — no XRPC method stubs (those belong to Phase 5).

Two new projects:
- `src/FSharp.ATProto.CodeGen/` — CLI tool (console app)
- `tests/FSharp.ATProto.CodeGen.Tests/` — Expecto tests

## Pipeline

```
324 lexicon JSONs
  → LexiconParser.parse (Phase 3)
  → List<LexiconDoc>
  → Group by namespace (e.g., app.bsky.feed)
  → For each namespace, emit types via Fabulous.AST
  → Fantomas formatting → .fs file
```

CLI invocation:
```
dotnet run --project src/FSharp.ATProto.CodeGen -- \
  --lexdir ./extern/atproto/lexicons \
  --outdir ./src/FSharp.ATProto.Bluesky/Generated
```

## Type Mapping

| Lexicon | F# | Notes |
|---|---|---|
| `object` | Record type | `option` wrapping for optional/nullable fields |
| `union` (open) | DU | Cases from refs + `Unknown of string * JsonElement` |
| `union` (closed) | DU | Cases from refs only |
| `string` | `string` | Format-specific: `Did`, `Handle`, `Nsid`, `AtUri`, `Tid`, `RecordKey`, `Cid` |
| `integer` | `int64` | |
| `boolean` | `bool` | |
| `bytes` | `byte[]` | |
| `cid-link` | `Cid` | From Syntax layer |
| `blob` | `BlobRef` | From DRISL layer |
| `array` | `'T list` | |
| `ref` | Named type reference | Resolved to generated type's qualified name |
| `unknown` | `JsonElement` | |
| `token` (top-level) | `let [<Literal>]` in module | Value = `"nsid#name"` |
| `knownValues` | Module with string constants | Field type stays `string` |
| optional (not in `required`) | `'T option` | Omitted from JSON when None |
| nullable (in `nullable`) | `'T option` | Serialized as null when None |
| required + nullable | `'T option` | Always serialized, null when None |

## JSON Serialization

Use `FSharp.SystemTextJson` with `JsonFSharpConverter` for automatic F# type support. Custom handling for:

1. **Union `$type` discriminator** — Generate a `JsonConverter` per DU that reads/writes `$type` to select the correct case.
2. **Record `$type` field** — Records participating in unions carry a `[<JsonPropertyName("$type")>]` field.
3. **Option serialization** — Omit `None` by default; serialize `null` for required+nullable fields.

## Naming Conventions

- NSID segments → PascalCase: `app.bsky.feed` → `App.Bsky.Feed`
- Def names → PascalCase: `replyRef` → `ReplyRef`
- Property names → camelCase via `[<JsonPropertyName("originalName")>]`
- Cross-namespace refs → fully qualified: `ComAtprotoLabelDefs.Label`
- File names → namespace concatenated: `AppBskyFeed.fs`

## Output Structure

```
Generated/
├── AppBskyFeed.fs
├── AppBskyActor.fs
├── ComAtprotoRepo.fs
├── ComAtprotoLabel.fs
├── ...  (~30-40 files)
```

One file per NSID namespace (3rd segment), containing all types from all lexicons in that namespace.

## Testing

1. **All 324 lexicons generate without error** — every real schema must be handled
2. **Generated code compiles** — add generated output to a project and build
3. **Serialization roundtrip** — for key schemas, create instances, serialize, deserialize, compare
4. **Snapshot tests** — compare generated output for specific schemas against expected strings
