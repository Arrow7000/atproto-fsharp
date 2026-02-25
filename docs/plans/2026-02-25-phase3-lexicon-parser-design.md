# Phase 3: Lexicon Parser Design

Date: 2026-02-25

## Overview

Parse Lexicon JSON schema files into an F# domain model, and validate record data instances against parsed schemas. This is a development-time library used by the code generator (Phase 4) and a runtime library for data validation.

## Architecture

Three modules with clean separation:

```
LexiconTypes.fs    -- Domain model (pure types, no logic)
LexiconParser.fs   -- JSON -> domain model (System.Text.Json / JsonElement)
RecordValidator.fs -- Validate data instances against parsed schemas
```

Dependencies:
- `FSharp.ATProto.Syntax` -- for Nsid.parse, Did.parse, Handle.parse, etc. (format validation)
- `System.Text.Json` -- JSON parsing (matches existing codebase patterns)

No dependency on DRISL. No dependency on FSharp.SystemTextJson.

## Domain Model

### Document Structure

```fsharp
type LexiconDoc = {
    Lexicon: int                        // must be 1
    Id: Nsid                            // validated NSID
    Revision: int option
    Description: string option
    Defs: Map<string, LexDef>
}
```

### Definition Types

```fsharp
type LexDef =
    | Record of LexRecord
    | Query of LexQuery
    | Procedure of LexProcedure
    | Subscription of LexSubscription
    | Token of LexToken
    | PermissionSet of LexPermissionSet
    | DefType of LexType                // named type defs (object, string, etc.)
```

Primary types (record, query, procedure, subscription, permission-set) may only appear as the `"main"` def. Non-main defs must be token or a field type (object, string, integer, etc.).

### XRPC Endpoint Types

```fsharp
type LexRecord = {
    Key: string                         // "tid", "nsid", "any", or "literal:<value>"
    Description: string option
    Record: LexObject
}

type LexQuery = {
    Description: string option
    Parameters: LexParams option
    Output: LexBody option
    Errors: LexError list
}

type LexProcedure = {
    Description: string option
    Parameters: LexParams option
    Input: LexBody option
    Output: LexBody option
    Errors: LexError list
}

type LexSubscription = {
    Description: string option
    Parameters: LexParams option
    Message: LexSubscriptionMessage option
    Errors: LexError list
}

type LexBody = {
    Description: string option
    Encoding: string
    Schema: LexType option
}

type LexSubscriptionMessage = {
    Description: string option
    Schema: LexUnion
}

type LexError = {
    Name: string
    Description: string option
}

type LexToken = {
    Description: string option
}
```

### Permission Set

```fsharp
type LexPermissionSet = {
    Title: string
    TitleLang: Map<string, string>
    Detail: string option
    DetailLang: Map<string, string>
    Permissions: LexPermission list
}

type LexPermission = {
    Resource: string                    // "repo" or "rpc"
    Collection: string list
    Action: string list
    Lxm: string list
    Aud: string option
    InheritAud: bool option
}
```

### Field Types

```fsharp
type LexType =
    | Boolean of LexBoolean
    | Integer of LexInteger
    | String of LexString
    | Bytes of LexBytes
    | CidLink
    | Blob of LexBlob
    | Array of LexArray
    | Object of LexObject
    | Params of LexParams
    | Ref of LexRef
    | Union of LexUnion
    | Unknown

type LexBoolean = {
    Description: string option
    Default: bool option
    Const: bool option
}

type LexInteger = {
    Description: string option
    Default: int64 option
    Const: int64 option
    Enum: int64 list option
    Minimum: int64 option
    Maximum: int64 option
}

type LexString = {
    Description: string option
    Default: string option
    Const: string option
    Enum: string list option
    KnownValues: string list option
    Format: LexStringFormat option
    MinLength: int option
    MaxLength: int option
    MinGraphemes: int option
    MaxGraphemes: int option
}

type LexStringFormat =
    | Did | Handle | AtIdentifier | AtUri | Nsid
    | Cid | Datetime | Language | Uri | Tid | RecordKey

type LexBytes = {
    Description: string option
    MinLength: int option
    MaxLength: int option
}

type LexBlob = {
    Description: string option
    Accept: string list option
    MaxSize: int64 option
}

type LexArray = {
    Description: string option
    Items: LexType
    MinLength: int option
    MaxLength: int option
}

type LexObject = {
    Description: string option
    Properties: Map<string, LexType>
    Required: string list
    Nullable: string list
}

type LexParams = {
    Description: string option
    Properties: Map<string, LexType>
    Required: string list
}

type LexRef = {
    Description: string option
    Ref: string
}

type LexUnion = {
    Description: string option
    Refs: string list
    Closed: bool
}
```

## Parser (LexiconParser.fs)

### API

```fsharp
module LexiconParser =
    val parse : json:string -> Result<LexiconDoc, string list>
    val parseElement : element:JsonElement -> Result<LexiconDoc, string list>
```

### Behavior

1. Parse JSON into `JsonElement`
2. Validate envelope: `lexicon` must be `1`, `id` must be valid NSID, `defs` must exist
3. For each def, dispatch on `"type"` field:
   - Primary types (record, query, procedure, subscription, permission-set): only allowed in `"main"`
   - ref, union, unknown: not allowed as top-level defs
   - Everything else: wrap in `DefType`
4. Resolve local refs: `#foo` becomes `{docId}#foo`
5. Validate record's `record` field has `"type": "object"`
6. Return accumulated errors or the parsed model

### JSON Parsing Pattern

Uses raw `System.Text.Json.JsonElement` pattern matching, consistent with the DRISL layer's `AtpJson` module. No deserializer attributes or custom converters.

## Record Data Validator (RecordValidator.fs)

### API

```fsharp
module RecordValidator =
    val validate :
        catalog:Map<string, LexiconDoc> ->
        data:JsonElement ->
        Result<unit, string list>
```

### Behavior

Takes a catalog of parsed lexicon documents and a JSON data instance. Walks the schema and data in parallel:

1. Read `$type` from data to find the schema
2. Look up the record def in the catalog
3. Walk each property against its schema type:
   - **Type checking**: JSON value kind matches expected type
   - **Required fields**: all required properties present
   - **Nullable**: only nullable fields may be null
   - **Constraints**: const, enum, min/max, minLength/maxLength, minGraphemes/maxGraphemes
   - **Format validation**: delegates to Syntax parsers (Did.parse, Handle.parse, etc.)
   - **Arrays**: validate element type + length constraints
   - **Objects**: recursive property validation
   - **Refs**: resolve and validate recursively
   - **Unions**: check `$type` discriminator, validate against resolved schema
     - Open unions: unknown `$type` passes (no inner validation)
     - Closed unions: unknown `$type` fails
   - **Blobs**: validate structure (`$type: "blob"`, mimeType, size, ref), check accept/maxSize
   - **Unknown**: must be a JSON object without `$bytes`, `$link`, or `$type: "blob"`
4. Return Ok or accumulated error list

### Grapheme Counting

For `minGraphemes`/`maxGraphemes` validation, use .NET's `StringInfo.GetTextElementEnumerator` to count user-perceived characters (grapheme clusters).

## Project Setup

### New Projects

```
src/FSharp.ATProto.Lexicon/
    FSharp.ATProto.Lexicon.fsproj
    LexiconTypes.fs
    LexiconParser.fs
    RecordValidator.fs

tests/FSharp.ATProto.Lexicon.Tests/
    FSharp.ATProto.Lexicon.Tests.fsproj
    TestHelpers.fs
    ParserTests.fs
    ValidatorTests.fs
    RealLexiconTests.fs
    Main.fs
```

### Dependencies

Source project:
- `FSharp.ATProto.Syntax` (project reference)
- `System.Text.Json` (framework)

Test project:
- `Expecto 10.2.3`
- `Expecto.FsCheck 10.2.3`
- `FsCheck 2.16.6`

### Lexicon Files Submodule

Add `bluesky-social/atproto` as a git submodule at `extern/atproto/`. Reference lexicon files at `extern/atproto/lexicons/`. This provides the 324 real-world schemas for testing.

## Testing Strategy

### Interop Tests (ParserTests.fs)

- **lexicon-valid.json**: 3 valid docs must parse to `Ok`
- **lexicon-invalid.json**: 7 invalid docs must parse to `Error`
- Load from `extern/atproto-interop-tests/lexicon/`

### Interop Tests (ValidatorTests.fs)

- **record-data-valid.json**: 3 valid instances must validate to `Ok`
- **record-data-invalid.json**: ~40 invalid instances must validate to `Error`
- Validator uses the catalog schemas from `extern/atproto-interop-tests/lexicon/catalog/`

### Real Lexicon Parsing (RealLexiconTests.fs)

- Load all 324 `.json` files from `extern/atproto/lexicons/`
- Each must parse without error
- Generates one test case per file for clear failure reporting

### Test Counts

| Test File | Expected Count |
|-----------|---------------|
| ParserTests.fs | 10 (3 valid + 7 invalid) |
| ValidatorTests.fs | ~43 (3 valid + ~40 invalid) |
| RealLexiconTests.fs | 324 |
| **Total** | **~377** |
