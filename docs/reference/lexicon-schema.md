# Lexicon Schema System Reference

## Overview

Lexicon is ATProto's JSON-based schema/IDL. Every API endpoint, record type, and event stream
is defined in a Lexicon JSON file. 324 files in the canonical repo.

## File Format

Each .json file = one NSID. Directory structure mirrors namespace hierarchy.
```
lexicons/app/bsky/feed/post.json  ->  app.bsky.feed.post
lexicons/com/atproto/repo/createRecord.json  ->  com.atproto.repo.createRecord
```

## Document Structure

```json
{
  "lexicon": 1,
  "id": "com.example.someMethod",
  "description": "Optional",
  "defs": {
    "main": { ... },
    "helperType": { ... }
  }
}
```

## Definition Types (5 primary)

1. **record** -- data in repos. Has `key` (tid/nsid/any) + `record` (object schema)
2. **query** -- GET endpoint. Has `parameters`, `output`, `errors`
3. **procedure** -- POST endpoint. Has `parameters`, `input`, `output`, `errors`
4. **subscription** -- WebSocket stream. Has `parameters`, `message` (must be union), `errors`
5. **permission-set** -- OAuth scope bundles

## Field Types

- Primitives: boolean, integer, string, bytes, cid-link, blob
- Containers: array (homogeneous), object (properties, required, nullable)
- Meta: token, ref, union, unknown
- Special: params (query params, restricted types)

## String Formats

at-identifier, at-uri, cid, datetime, did, handle, nsid, tid, record-key, uri, language

## Namespace Breakdown (324 files)

| Namespace | Count | Purpose |
|-----------|-------|---------|
| app.bsky.* | 148 | Bluesky app layer (posts, feeds, profiles, graphs) |
| com.atproto.* | 95 | Core protocol (repos, identity, sync, auth) |
| tools.ozone.* | 54 | Moderation tools |
| chat.bsky.* | 26 | Direct messaging |

## Ref Resolution

- Local: `#defName` -> same document's defs map
- Global: `com.example.nsid#defName` -> another document
- Bare NSID: `com.example.nsid` -> that document's `main` def

## Union Semantics

- Discriminated by `$type` string field at runtime
- Open (default): unknown $type values tolerated
- Closed (`closed: true`): only listed $type values valid
- All variants must be object or record types

## Known Corner Cases (Discussion #4343)

- Named ref/union as top-level defs: TS disallows, Go allows (to be disallowed)
- null type: TS doesn't support, Go does (unclear)
- Ref-to-ref chaining: not addressed (to be forbidden)
- Array of arrays: spec gap
- Token magic in knownValues: TS bug being removed
- Impossible constraints (maxLength < minLength): no validation

## Meta-Schema

No formal JSON Schema or meta-schema exists. The TypeScript `types.ts` in @atproto/lexicon
is the de facto formal definition. Must reverse-engineer from that + prose spec.
