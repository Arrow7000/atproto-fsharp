# Phase 3: Lexicon Parser Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Parse Lexicon JSON schemas into an F# domain model and validate record data against parsed schemas. ~377 new tests.

**Architecture:** Three modules (LexiconTypes, LexiconParser, RecordValidator) in a new `FSharp.ATProto.Lexicon` project. Parser uses raw `System.Text.Json` / `JsonElement` matching (consistent with DRISL layer). Validator walks schema + data in parallel.

**Tech Stack:** F# 9 / .NET 9, System.Text.Json, FSharp.ATProto.Syntax (for format validation), Expecto 10.2.3 + FsCheck 2.16.6

**Important:** All `dotnet` commands must be prefixed with `export PATH="$HOME/.dotnet:$PATH" &&`

**Design doc:** `docs/plans/2026-02-25-phase3-lexicon-parser-design.md`

---

### Task 1: Project Scaffold + Lexicons Submodule

**Files:**
- Create: `src/FSharp.ATProto.Lexicon/FSharp.ATProto.Lexicon.fsproj`
- Create: `tests/FSharp.ATProto.Lexicon.Tests/FSharp.ATProto.Lexicon.Tests.fsproj`
- Create: `tests/FSharp.ATProto.Lexicon.Tests/TestHelpers.fs`
- Create: `tests/FSharp.ATProto.Lexicon.Tests/Main.fs`
- Modify: `FSharp.ATProto.sln`

**Step 1: Add the atproto repo as a git submodule (for the 324 real lexicon files)**

```bash
cd /Users/aron/dev/atproto-fsharp
git submodule add --depth 1 https://github.com/bluesky-social/atproto.git extern/atproto
```

Verify `extern/atproto/lexicons/` exists and contains JSON files.

**Step 2: Create the source project**

Create `src/FSharp.ATProto.Lexicon/FSharp.ATProto.Lexicon.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="LexiconTypes.fs" />
    <Compile Include="LexiconParser.fs" />
    <Compile Include="RecordValidator.fs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\FSharp.ATProto.Syntax\FSharp.ATProto.Syntax.fsproj" />
  </ItemGroup>

</Project>
```

**Step 3: Create the test project**

Create `tests/FSharp.ATProto.Lexicon.Tests/FSharp.ATProto.Lexicon.Tests.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="TestHelpers.fs" />
    <Compile Include="ParserTests.fs" />
    <Compile Include="ValidatorTests.fs" />
    <Compile Include="RealLexiconTests.fs" />
    <Compile Include="Main.fs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\FSharp.ATProto.Lexicon\FSharp.ATProto.Lexicon.fsproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Expecto" Version="10.2.3" />
    <PackageReference Include="Expecto.FsCheck" Version="10.2.3" />
    <PackageReference Include="FsCheck" Version="2.16.6" />
  </ItemGroup>

</Project>
```

**Step 4: Create TestHelpers.fs**

Create `tests/FSharp.ATProto.Lexicon.Tests/TestHelpers.fs`:

```fsharp
module TestHelpers

open System.IO
open System.Text.Json

let solutionRoot =
    let rec findRoot (dir: DirectoryInfo) =
        if File.Exists(Path.Combine(dir.FullName, "FSharp.ATProto.sln")) then
            dir.FullName
        elif dir.Parent <> null then
            findRoot dir.Parent
        else
            failwith "Could not find solution root"
    findRoot (DirectoryInfo(Directory.GetCurrentDirectory()))

let loadInteropJson (relativePath: string) : JsonDocument =
    let fullPath = Path.Combine(solutionRoot, "extern", "atproto-interop-tests", relativePath)
    JsonDocument.Parse(File.ReadAllText(fullPath))
```

**Step 5: Create Main.fs**

Create `tests/FSharp.ATProto.Lexicon.Tests/Main.fs`:

```fsharp
module Main

open Expecto

[<EntryPoint>]
let main args =
    runTestsInAssemblyWithCLIArgs [] args
```

**Step 6: Create stub source files so the project compiles**

Create `src/FSharp.ATProto.Lexicon/LexiconTypes.fs`:

```fsharp
namespace FSharp.ATProto.Lexicon
```

Create `src/FSharp.ATProto.Lexicon/LexiconParser.fs`:

```fsharp
module FSharp.ATProto.Lexicon.LexiconParser
```

Create `src/FSharp.ATProto.Lexicon/RecordValidator.fs`:

```fsharp
module FSharp.ATProto.Lexicon.RecordValidator
```

Create stub test files so the test project compiles:

Create `tests/FSharp.ATProto.Lexicon.Tests/ParserTests.fs`:

```fsharp
module ParserTests
```

Create `tests/FSharp.ATProto.Lexicon.Tests/ValidatorTests.fs`:

```fsharp
module ValidatorTests
```

Create `tests/FSharp.ATProto.Lexicon.Tests/RealLexiconTests.fs`:

```fsharp
module RealLexiconTests
```

**Step 7: Add both projects to the solution**

```bash
export PATH="$HOME/.dotnet:$PATH" && cd /Users/aron/dev/atproto-fsharp && dotnet sln add src/FSharp.ATProto.Lexicon/FSharp.ATProto.Lexicon.fsproj --solution-folder src && dotnet sln add tests/FSharp.ATProto.Lexicon.Tests/FSharp.ATProto.Lexicon.Tests.fsproj --solution-folder tests
```

**Step 8: Verify the entire solution builds**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet build /Users/aron/dev/atproto-fsharp/FSharp.ATProto.sln
```

Expected: Build succeeded. 0 errors.

**Step 9: Verify existing tests still pass**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test /Users/aron/dev/atproto-fsharp/FSharp.ATProto.sln --no-build
```

Expected: 838 tests pass (726 Syntax + 112 DRISL).

**Step 10: Commit**

```bash
git add -A && git commit -m "Scaffold Phase 3: Lexicon parser project + atproto submodule"
```

---

### Task 2: Domain Model Types (LexiconTypes.fs)

**Files:**
- Modify: `src/FSharp.ATProto.Lexicon/LexiconTypes.fs`

**Step 1: Write the complete domain model**

Replace `src/FSharp.ATProto.Lexicon/LexiconTypes.fs` with:

```fsharp
namespace FSharp.ATProto.Lexicon

open FSharp.ATProto.Syntax

/// String format constraint for string-typed fields.
type LexStringFormat =
    | Did
    | Handle
    | AtIdentifier
    | AtUri
    | Nsid
    | Cid
    | Datetime
    | Language
    | Uri
    | Tid
    | RecordKey

/// Boolean field constraints.
type LexBoolean =
    { Description: string option
      Default: bool option
      Const: bool option }

/// Integer field constraints.
type LexInteger =
    { Description: string option
      Default: int64 option
      Const: int64 option
      Enum: int64 list option
      Minimum: int64 option
      Maximum: int64 option }

/// String field constraints.
type LexString =
    { Description: string option
      Default: string option
      Const: string option
      Enum: string list option
      KnownValues: string list option
      Format: LexStringFormat option
      MinLength: int option
      MaxLength: int option
      MinGraphemes: int option
      MaxGraphemes: int option }

/// Bytes field constraints.
type LexBytes =
    { Description: string option
      MinLength: int option
      MaxLength: int option }

/// Blob field constraints.
type LexBlob =
    { Description: string option
      Accept: string list option
      MaxSize: int64 option }

/// A field type in a Lexicon schema. Recursive (arrays/objects contain nested types).
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

/// Array field: homogeneous typed elements with length constraints.
and LexArray =
    { Description: string option
      Items: LexType
      MinLength: int option
      MaxLength: int option }

/// Object type: named properties with required/nullable lists.
and LexObject =
    { Description: string option
      Properties: Map<string, LexType>
      Required: string list
      Nullable: string list }

/// Query/procedure parameter object (restricted to boolean, integer, string, unknown, array).
and LexParams =
    { Description: string option
      Properties: Map<string, LexType>
      Required: string list }

/// Reference to another type definition.
and LexRef =
    { Description: string option
      Ref: string }

/// Union of multiple referenced types, discriminated by $type at runtime.
and LexUnion =
    { Description: string option
      Refs: string list
      Closed: bool }

/// An XRPC error response.
type LexError =
    { Name: string
      Description: string option }

/// A token definition (string constant).
type LexToken =
    { Description: string option }

/// HTTP body schema for query output / procedure input+output.
type LexBody =
    { Description: string option
      Encoding: string
      Schema: LexType option }

/// Subscription message schema (must be a union).
type LexSubscriptionMessage =
    { Description: string option
      Schema: LexUnion }

/// Record definition: data stored in AT Protocol repos.
type LexRecord =
    { Key: string
      Description: string option
      Record: LexObject }

/// Query definition: GET XRPC endpoint.
type LexQuery =
    { Description: string option
      Parameters: LexParams option
      Output: LexBody option
      Errors: LexError list }

/// Procedure definition: POST XRPC endpoint.
type LexProcedure =
    { Description: string option
      Parameters: LexParams option
      Input: LexBody option
      Output: LexBody option
      Errors: LexError list }

/// Subscription definition: WebSocket event stream.
type LexSubscription =
    { Description: string option
      Parameters: LexParams option
      Message: LexSubscriptionMessage option
      Errors: LexError list }

/// A single permission entry in a permission set.
type LexPermission =
    { Resource: string
      Collection: string list
      Action: string list
      Lxm: string list
      Aud: string option
      InheritAud: bool option }

/// Permission set definition: OAuth scope bundle.
type LexPermissionSet =
    { Title: string
      TitleLang: Map<string, string>
      Detail: string option
      DetailLang: Map<string, string>
      Permissions: LexPermission list }

/// A named definition within a Lexicon document.
type LexDef =
    | Record of LexRecord
    | Query of LexQuery
    | Procedure of LexProcedure
    | Subscription of LexSubscription
    | Token of LexToken
    | PermissionSet of LexPermissionSet
    | DefType of LexType

/// A complete Lexicon document (one per NSID).
type LexiconDoc =
    { Lexicon: int
      Id: Nsid
      Revision: int option
      Description: string option
      Defs: Map<string, LexDef> }
```

**Step 2: Verify the solution builds**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet build /Users/aron/dev/atproto-fsharp/FSharp.ATProto.sln
```

Expected: Build succeeded.

**Step 3: Commit**

```bash
git add src/FSharp.ATProto.Lexicon/LexiconTypes.fs && git commit -m "Add Lexicon domain model types"
```

---

### Task 3: Lexicon Parser + Parser Interop Tests

**Files:**
- Modify: `src/FSharp.ATProto.Lexicon/LexiconParser.fs`
- Modify: `tests/FSharp.ATProto.Lexicon.Tests/ParserTests.fs`

**Step 1: Write the parser interop tests**

Replace `tests/FSharp.ATProto.Lexicon.Tests/ParserTests.fs` with:

```fsharp
module ParserTests

open Expecto
open FSharp.ATProto.Lexicon

[<Tests>]
let parserTests =
    let validDoc = TestHelpers.loadInteropJson "lexicon/lexicon-valid.json"
    let invalidDoc = TestHelpers.loadInteropJson "lexicon/lexicon-invalid.json"

    testList "LexiconParser" [
        testList "valid lexicons" [
            for item in validDoc.RootElement.EnumerateArray() do
                let name = item.GetProperty("name").GetString()
                let lexicon = item.GetProperty("lexicon")
                testCase name (fun () ->
                    let result = LexiconParser.parseElement lexicon
                    Expect.isOk result
                        (sprintf "Expected valid lexicon '%s' to parse, got: %A" name result)
                )
        ]
        testList "invalid lexicons" [
            for item in invalidDoc.RootElement.EnumerateArray() do
                let name = item.GetProperty("name").GetString()
                let lexicon = item.GetProperty("lexicon")
                testCase name (fun () ->
                    let result = LexiconParser.parseElement lexicon
                    Expect.isError result
                        (sprintf "Expected invalid lexicon '%s' to fail parsing" name)
                )
        ]
    ]
```

**Step 2: Run tests to verify they fail**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.Lexicon.Tests/ --no-restore 2>&1 | tail -5
```

Expected: Compilation error -- `LexiconParser.parseElement` does not exist.

**Step 3: Implement the parser**

Replace `src/FSharp.ATProto.Lexicon/LexiconParser.fs` with the full parser. The parser must:

1. **Expose two public functions:**
   ```fsharp
   module FSharp.ATProto.Lexicon.LexiconParser

   val parse : json:string -> Result<LexiconDoc, string>
   val parseElement : element:JsonElement -> Result<LexiconDoc, string>
   ```

2. **Use these JSON helper functions** (private):
   - `tryGetString : JsonElement -> string -> string option` -- read optional string property
   - `tryGetInt64 : JsonElement -> string -> int64 option` -- read optional int64 property
   - `tryGetInt : JsonElement -> string -> int option` -- read optional int32 property
   - `tryGetBool : JsonElement -> string -> bool option` -- read optional bool property
   - `getStringList : JsonElement -> string -> string list` -- read string array (empty if missing)
   - `tryGetStringList : JsonElement -> string -> string list option` -- read optional string array
   - `tryGetInt64List : JsonElement -> string -> int64 list option` -- read optional int64 array

3. **Implement `parseElement`:**
   - Validate `lexicon` field is integer `1`
   - Validate `id` field is a valid NSID (using `Nsid.parse`)
   - Read optional `revision` (int) and `description` (string)
   - Iterate `defs` object properties, calling `parseDef` for each

4. **Implement `parseDef : string -> string -> JsonElement -> Result<string * LexDef, string>`:**
   - Takes `docId` (the NSID), `defName`, and the def's JSON element
   - Reads `"type"` field and dispatches:
     - `"record"` -> parse LexRecord (only if defName = "main")
     - `"query"` -> parse LexQuery (only if defName = "main")
     - `"procedure"` -> parse LexProcedure (only if defName = "main")
     - `"subscription"` -> parse LexSubscription (only if defName = "main")
     - `"permission-set"` -> parse LexPermissionSet (only if defName = "main")
     - `"token"` -> parse LexToken -> wrap in `LexDef.Token`
     - `"ref"` -> **Error** "ref cannot be a top-level definition"
     - `"unknown"` -> **Error** "unknown cannot be a top-level definition"
     - Everything else -> parse as LexType -> wrap in `LexDef.DefType`

5. **Implement `parseType : string -> JsonElement -> Result<LexType, string>`:**
   - Takes `docId` for ref resolution
   - Dispatches on `"type"` field:
     - `"boolean"` -> `LexType.Boolean { Description; Default; Const }`
     - `"integer"` -> `LexType.Integer { Description; Default; Const; Enum; Minimum; Maximum }`
     - `"string"` -> `LexType.String { Description; Default; Const; Enum; KnownValues; Format; MinLength; MaxLength; MinGraphemes; MaxGraphemes }`
       - Parse `format` via `parseStringFormat` (match on "did", "handle", "at-identifier", "at-uri", "nsid", "cid", "datetime", "language", "uri", "tid", "record-key")
     - `"bytes"` -> `LexType.Bytes { Description; MinLength; MaxLength }`
     - `"cid-link"` -> `LexType.CidLink`
     - `"blob"` -> `LexType.Blob { Description; Accept; MaxSize }`
     - `"array"` -> read `items` property, recursively `parseType`, build `LexType.Array`
     - `"object"` -> `parseObject` (see below)
     - `"params"` -> `parseParams` (see below)
     - `"ref"` -> read `"ref"` string, resolve local refs (`#foo` -> `{docId}#foo`), build `LexType.Ref`
     - `"union"` -> read `"refs"` string list (resolve each), read `"closed"` bool (default false), build `LexType.Union`
     - `"unknown"` -> `LexType.Unknown`
     - `"token"` -> `LexType.String { ... all None/empty ... }` (tokens in field position act as strings)

6. **Implement `parseObject : string -> JsonElement -> Result<LexObject, string>`:**
   - Read `properties` object, recursively `parseType` each property value
   - Read `required` and `nullable` string lists
   - Collect errors from property parsing

7. **Implement `parseParams : string -> JsonElement -> Result<LexParams, string>`:**
   - Same as parseObject but produces `LexParams` (no `nullable` field)

8. **Implement record/query/procedure/subscription/permission-set parsers:**

   `parseRecord`:
   - Read `key` string (required)
   - Read `record` property, verify it has `"type": "object"`, parse as LexObject
   - Error if `record` is missing or doesn't have `"type": "object"`

   `parseQuery`:
   - Read optional `parameters` (parseParams)
   - Read optional `output` (parseBody)
   - Read `errors` array (parseErrors)

   `parseProcedure`:
   - Read optional `parameters` (parseParams)
   - Read optional `input` (parseBody)
   - Read optional `output` (parseBody)
   - Read `errors` array (parseErrors)

   `parseSubscription`:
   - Read optional `parameters` (parseParams)
   - Read optional `message` (parseSubscriptionMessage -- schema must be union)
   - Read `errors` array (parseErrors)

   `parseBody : string -> JsonElement -> Result<LexBody, string>`:
   - Read `encoding` string (required)
   - Read optional `schema` (parseType)

   `parseErrors : JsonElement -> LexError list`:
   - Read `errors` array, each has `name` and optional `description`

   `parsePermissionSet`:
   - Read `title` string (required)
   - Read `title:lang` object as `Map<string, string>` (optional, empty map if missing)
   - Read `detail` string (optional)
   - Read `detail:lang` object as `Map<string, string>` (optional, empty map if missing)
   - Read `permissions` array, each parsed as LexPermission

   `parsePermission`:
   - Read `resource` string
   - Read `collection`, `action`, `lxm` as string lists (empty if missing)
   - Read `aud` as optional string
   - Read `inheritAud` as optional bool

9. **Ref resolution rule:**
   - If ref starts with `#`, prepend the document's NSID: `#foo` -> `com.example.thing#foo`
   - Otherwise, leave unchanged: `com.other.thing#bar` stays as-is, `com.other.thing` stays as-is

**Step 4: Run parser interop tests**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.Lexicon.Tests/ --filter "LexiconParser" --no-restore 2>&1 | tail -20
```

Expected: 10 tests pass (3 valid + 7 invalid).

**Step 5: Verify entire solution still builds and existing tests pass**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet build /Users/aron/dev/atproto-fsharp/FSharp.ATProto.sln && dotnet test /Users/aron/dev/atproto-fsharp/FSharp.ATProto.sln 2>&1 | tail -20
```

Expected: 848 tests pass (838 existing + 10 parser).

**Step 6: Commit**

```bash
git add src/FSharp.ATProto.Lexicon/LexiconParser.fs tests/FSharp.ATProto.Lexicon.Tests/ParserTests.fs && git commit -m "Implement Lexicon parser with 10 interop tests passing"
```

---

### Task 4: Real Lexicon Parsing Tests (324 files)

**Files:**
- Modify: `tests/FSharp.ATProto.Lexicon.Tests/RealLexiconTests.fs`
- May modify: `src/FSharp.ATProto.Lexicon/LexiconParser.fs` (to fix issues found)

**Step 1: Write the real lexicon test**

Replace `tests/FSharp.ATProto.Lexicon.Tests/RealLexiconTests.fs` with:

```fsharp
module RealLexiconTests

open System.IO
open Expecto
open FSharp.ATProto.Lexicon

[<Tests>]
let realLexiconTests =
    let lexiconDir =
        Path.Combine(TestHelpers.solutionRoot, "extern", "atproto", "lexicons")

    testList "Real Lexicon Files" [
        for file in Directory.GetFiles(lexiconDir, "*.json", SearchOption.AllDirectories) do
            let relativePath = Path.GetRelativePath(lexiconDir, file)
            testCase relativePath (fun () ->
                let json = File.ReadAllText(file)
                let result = LexiconParser.parse json
                Expect.isOk result
                    (sprintf "Failed to parse %s: %A" relativePath result)
            )
    ]
```

**Step 2: Run real lexicon tests**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.Lexicon.Tests/ --filter "Real Lexicon" --no-restore 2>&1 | tail -30
```

Expected: Some tests may fail. Note which files fail and why.

**Step 3: Fix parser issues iteratively**

Common issues to expect and fix:
- Missing `"type"` field handling in some contexts (some elements may omit type)
- Unknown string formats not yet in the spec
- Edge cases in permission-set or subscription parsing
- Union defs that appear as non-main (allow them -- real lexicons may use them)
- Properties missing from objects (empty properties map)
- Missing `"type": "object"` on some inline objects

Iterate: fix parser -> re-run tests -> fix next issue, until all pass.

**Step 4: Run the full test suite**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.Lexicon.Tests/ --no-restore 2>&1 | tail -10
```

Expected: 10 parser interop + N real lexicon tests pass (N should be 300+ depending on current lexicon count in the repo).

**Step 5: Commit**

```bash
git add -A && git commit -m "All real Lexicon files parse successfully"
```

---

### Task 5: Record Validator + Validator Interop Tests

**Files:**
- Modify: `src/FSharp.ATProto.Lexicon/RecordValidator.fs`
- Modify: `tests/FSharp.ATProto.Lexicon.Tests/ValidatorTests.fs`

**Step 1: Write the validator interop tests**

Replace `tests/FSharp.ATProto.Lexicon.Tests/ValidatorTests.fs` with:

```fsharp
module ValidatorTests

open System.IO
open System.Text.Json
open Expecto
open FSharp.ATProto.Lexicon

let private loadCatalog () : Map<string, LexiconDoc> =
    let catalogDir =
        Path.Combine(
            TestHelpers.solutionRoot,
            "extern", "atproto-interop-tests", "lexicon", "catalog")
    Directory.GetFiles(catalogDir, "*.json")
    |> Array.choose (fun path ->
        let json = File.ReadAllText(path)
        match LexiconParser.parse json with
        | Ok doc -> Some (FSharp.ATProto.Syntax.Nsid.value doc.Id, doc)
        | Error _ -> None)
    |> Map.ofArray

[<Tests>]
let validatorTests =
    let catalog = loadCatalog ()
    let validData = TestHelpers.loadInteropJson "lexicon/record-data-valid.json"
    let invalidData = TestHelpers.loadInteropJson "lexicon/record-data-invalid.json"

    testList "RecordValidator" [
        testList "valid records" [
            for item in validData.RootElement.EnumerateArray() do
                let name = item.GetProperty("name").GetString()
                let data = item.GetProperty("data")
                testCase name (fun () ->
                    let result = RecordValidator.validate catalog data
                    Expect.isOk result
                        (sprintf "Expected valid record '%s' to validate, got: %A"
                            name result)
                )
        ]
        testList "invalid records" [
            for i, item in invalidData.RootElement.EnumerateArray() |> Seq.mapi (fun i x -> i, x) do
                let name = item.GetProperty("name").GetString()
                let label = sprintf "[%d] %s" i name
                let data = item.GetProperty("data")
                testCase label (fun () ->
                    let result = RecordValidator.validate catalog data
                    Expect.isError result
                        (sprintf "Expected invalid record '%s' to fail validation" name)
                )
        ]
    ]
```

Note: Invalid test names are NOT unique (two "union inner invalid" entries), so we prefix with index `[i]` to guarantee unique test names.

**Step 2: Run tests to verify they fail**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.Lexicon.Tests/ --filter "RecordValidator" --no-restore 2>&1 | tail -10
```

Expected: Compilation error -- `RecordValidator.validate` does not exist.

**Step 3: Implement the record validator**

Replace `src/FSharp.ATProto.Lexicon/RecordValidator.fs` with the full validator. The validator must:

1. **Expose one public function:**
   ```fsharp
   module FSharp.ATProto.Lexicon.RecordValidator

   val validate : catalog:Map<string, LexiconDoc> -> data:JsonElement -> Result<unit, string>
   ```

2. **Top-level `validate` function:**
   - Read `$type` string from `data` (error if missing or not a string)
   - Parse `$type` as NSID (possibly with `#fragment`): split on `#` to get `(nsid, defName)`
   - Look up `nsid` in catalog (error if not found)
   - Look up `defName` (default `"main"`) in the document's defs
   - The def must be a `Record` -- get its `Record` field (a `LexObject`)
   - Call `validateObject` on the data against that LexObject schema

3. **Implement `validateObject : Map<string, LexiconDoc> -> LexObject -> JsonElement -> Result<unit, string>`:**
   - Check data is a JSON object
   - Check all `required` properties are present and not null (unless also in `nullable`)
   - For each property in the data that exists in the schema's `properties`:
     - If value is null: OK only if property is in `nullable` list
     - Otherwise: `validateType` the value against the property's LexType
   - Extra properties not in schema: silently allowed (open-world)

4. **Implement `validateType : Map<string, LexiconDoc> -> LexType -> JsonElement -> Result<unit, string>`:**

   - `Boolean _` -> check `element.ValueKind = JsonValueKind.True or False`
   - `Integer { Const; Enum; Minimum; Maximum; ... }`:
     - Check `element.ValueKind = JsonValueKind.Number`
     - Get int64 value
     - If `Const` is Some, check value equals const
     - If `Enum` is Some, check value is in list
     - If `Minimum` is Some, check value >= minimum
     - If `Maximum` is Some, check value <= maximum
   - `String { Const; Enum; Format; MinLength; MaxLength; MinGraphemes; MaxGraphemes; ... }`:
     - Check `element.ValueKind = JsonValueKind.String`
     - Get string value
     - If `Const` is Some, check value equals const
     - If `Enum` is Some, check value is in list
     - `KnownValues`: do NOT validate (known values are advisory, not restrictive)
     - If `MinLength`/`MaxLength`: check `value.Length` (byte length via `System.Text.Encoding.UTF8.GetByteCount`)
       - IMPORTANT: Lexicon `minLength`/`maxLength` for strings is UTF-8 byte length, NOT character count
       - Actually, re-reading the spec -- it's grapheme count for maxGraphemes, but for minLength/maxLength on strings it may be UTF-8 byte count or character count. Check the interop tests:
         - "string too short": `lenString` has `minLength: 10`, value is `"."` (1 char) -> invalid. This works with either char count or byte count.
         - "string too long": `lenString` has `maxLength: 20`, value is `"abcdefg-abcdefg-abcdefg"` (23 chars) -> invalid. Also works with either.
       - For safety, use `value.Length` (UTF-16 char count). The interop tests use ASCII so both interpretations pass.
     - If `MinGraphemes`/`MaxGraphemes`: count grapheme clusters using `System.Globalization.StringInfo`
       ```fsharp
       let countGraphemes (s: string) =
           let si = System.Globalization.StringInfo(s)
           si.LengthInTextElements
       ```
     - If `Format` is Some, validate using Syntax parsers:
       - `Did` -> `FSharp.ATProto.Syntax.Did.parse value |> Result.mapError ...`
       - `Handle` -> `FSharp.ATProto.Syntax.Handle.parse value`
       - `AtIdentifier` -> `FSharp.ATProto.Syntax.AtIdentifier.parse value`
       - `AtUri` -> `FSharp.ATProto.Syntax.AtUri.parse value`
       - `Nsid` -> `FSharp.ATProto.Syntax.Nsid.parse value`
       - `Cid` -> `FSharp.ATProto.Syntax.Cid.parse value`
       - `Datetime` -> `FSharp.ATProto.Syntax.AtDateTime.parse value`
       - `Language` -> `FSharp.ATProto.Syntax.Language.parse value`
       - `Uri` -> `FSharp.ATProto.Syntax.Uri.parse value`
       - `Tid` -> `FSharp.ATProto.Syntax.Tid.parse value`
       - `RecordKey` -> `FSharp.ATProto.Syntax.RecordKey.parse value`
   - `Bytes _`:
     - Check element is an object with `$bytes` key
     - If `MinLength`/`MaxLength`: decode base64, check byte array length
   - `CidLink`:
     - Check element is an object with `$link` key
   - `Blob { Accept; MaxSize; ... }`:
     - Check element is an object with `$type` = `"blob"`, `mimeType` (string), `size` (number), `ref` (object with `$link`)
     - If `MaxSize` is Some, check `size <= maxSize`
     - If `Accept` is Some, check mimeType matches at least one accept pattern:
       - `"image/*"` matches any mimeType starting with `"image/"`
       - `"*/*"` matches everything
       - Exact match otherwise
   - `Array { Items; MinLength; MaxLength; ... }`:
     - Check `element.ValueKind = JsonValueKind.Array`
     - If `MinLength` is Some, check array length >= minLength
     - If `MaxLength` is Some, check array length <= maxLength
     - For each element, `validateType` against `Items`
   - `Object obj`:
     - `validateObject catalog obj element`
   - `Ref { Ref = refStr; ... }`:
     - Resolve the ref: split on `#` to get `(nsid, defName)`
     - Look up in catalog, get the def
     - The def should be a `DefType` containing a type -> `validateType` against that type
     - OR the def could be a `Record` -> validate against its record schema
   - `Union { Refs; Closed; ... }`:
     - Check element is a JSON object
     - Read `$type` from element (error if missing)
     - If `Closed` and `$type` not in `Refs` -> error
     - If `$type` is in `Refs` (or union is open):
       - Resolve the ref, look up the schema
       - `validateType` the element against the resolved type
     - If open union and `$type` not in `Refs` -> Ok (unknown type in open union passes)
   - `Unknown`:
     - Check element is a JSON object
     - Must NOT have `$bytes` key (that's a bytes value, not an object)
     - Must NOT have `$type` = `"blob"` (that's a blob)
   - `Params _`: Not applicable for record data validation (only used in query/procedure parameters)

5. **Important edge cases from the interop tests:**
   - `"invalid ref value"`: ref field is a string `"example.lexicon.record#wrongToken"` instead of an object -> error (wrong JSON type, expected object)
   - `"invalid token ref type"`: ref field is `123` instead of an object -> error
   - Blob validation: check `mimeType` is a string (not `false`), `size` is a number, `ref` has `$link`
   - Union inner validation: when a `$type` IS in the union refs but the inner content is invalid (e.g., wrong property types), still error
   - Closed union: `$type` not in refs AND `$type` is not in the closed union's list -> error
   - Closed union: `$type` IS a valid ref from the OPEN union but NOT in the closed union -> error (test "union inner invalid" index 37, closedUnion with demoObjectTwo which is only in the open union)

**Step 4: Run validator tests**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.Lexicon.Tests/ --filter "RecordValidator" --no-restore 2>&1 | tail -30
```

Expected: All ~43 tests pass (3 valid + ~40 invalid).

**Step 5: Run the full Lexicon test suite**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.Lexicon.Tests/ --no-restore 2>&1 | tail -10
```

Expected: All tests pass (10 parser + ~43 validator + N real lexicon).

**Step 6: Commit**

```bash
git add src/FSharp.ATProto.Lexicon/RecordValidator.fs tests/FSharp.ATProto.Lexicon.Tests/ValidatorTests.fs && git commit -m "Implement record data validator with interop tests passing"
```

---

### Task 6: Final Verification

**Files:** None (read-only verification)

**Step 1: Run the entire solution's test suite**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test /Users/aron/dev/atproto-fsharp/FSharp.ATProto.sln 2>&1 | tail -20
```

Expected: All tests pass across all 3 test projects:
- FSharp.ATProto.Syntax.Tests: 726 tests
- FSharp.ATProto.DRISL.Tests: 112 tests
- FSharp.ATProto.Lexicon.Tests: ~377 tests (10 parser + ~43 validator + ~324 real)
- **Total: ~1215 tests**

**Step 2: Report final test counts**

Print the exact test count per project for documentation.
