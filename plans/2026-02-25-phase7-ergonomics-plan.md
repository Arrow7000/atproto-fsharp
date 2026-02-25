# Phase 7: Ergonomics & Type Safety — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Transform the library from raw strings and opaque JsonElement into a fully typed F# API with discriminated unions, validated identifier types, and polished convenience methods.

**Architecture:** Three streams executed sequentially: (1) JSON converters for Syntax types, (2) codegen changes for typed unions and typed identifiers + regeneration, (3) convenience API polish. Each stream builds on the previous.

**Tech Stack:** F# 10, System.Text.Json, FSharp.SystemTextJson 1.4.36, Fabulous.AST 1.10.0, Expecto 10.2.3

**Build command:** `dotnet build`
**Test all:** `dotnet run --project tests/FSharp.ATProto.Syntax.Tests -- --summary && dotnet run --project tests/FSharp.ATProto.DRISL.Tests -- --summary && dotnet run --project tests/FSharp.ATProto.Lexicon.Tests -- --summary && dotnet run --project tests/FSharp.ATProto.CodeGen.Tests -- --summary && dotnet run --project tests/FSharp.ATProto.Core.Tests -- --summary && dotnet run --project tests/FSharp.ATProto.Bluesky.Tests -- --summary`

---

## Task 1: Add JSON converters to Syntax types

Add `[<JsonConverter>]` attributes directly on the Syntax type definitions. Each converter reads a string, parses it via the type's `parse` function, and writes via the type's `value` function. `System.Text.Json` is part of the .NET SDK — no package reference needed.

**Files:**
- Modify: `src/FSharp.ATProto.Syntax/Did.fs`
- Modify: `src/FSharp.ATProto.Syntax/Handle.fs`
- Modify: `src/FSharp.ATProto.Syntax/AtUri.fs`
- Modify: `src/FSharp.ATProto.Syntax/Cid.fs`
- Modify: `src/FSharp.ATProto.Syntax/Nsid.fs`
- Modify: `src/FSharp.ATProto.Syntax/Tid.fs`
- Modify: `src/FSharp.ATProto.Syntax/RecordKey.fs`
- Modify: `src/FSharp.ATProto.Syntax/AtDateTime.fs`
- Modify: `src/FSharp.ATProto.Syntax/Language.fs`
- Modify: `src/FSharp.ATProto.Syntax/Uri.fs`
- Test: `tests/FSharp.ATProto.Syntax.Tests/` (new test file or added to existing)

**Step 1: Write roundtrip converter tests**

Create or extend a test file with roundtrip serialization tests for each Syntax type. Pattern for each test:

```fsharp
testCase "Did roundtrips through JSON" <| fun () ->
    let did = Did.parse "did:plc:abc123" |> Result.defaultWith failwith
    let json = JsonSerializer.Serialize(did)
    Expect.equal json "\"did:plc:abc123\"" "serializes to JSON string"
    let deserialized = JsonSerializer.Deserialize<Did>(json)
    Expect.equal (Did.value deserialized) "did:plc:abc123" "deserializes back"

testCase "Did deserialization fails on invalid" <| fun () ->
    Expect.throws (fun () -> JsonSerializer.Deserialize<Did>("\"not-a-did\"") |> ignore)
        "rejects invalid DID"
```

Write similar pairs for all 10 types: Did, Handle, AtUri, Cid, Nsid, Tid, RecordKey, AtDateTime, Language, Uri.

**Step 2: Run tests to verify they fail**

Run: `dotnet run --project tests/FSharp.ATProto.Syntax.Tests -- --summary`
Expected: FAIL — types don't have converters yet, serialization will produce `{"Case":"Did","Fields":["did:plc:abc123"]}` instead of a string.

**Step 3: Add converter to each Syntax type**

The pattern is identical for all 10 types. For each file, add opens and a converter type before the main type, then add the `[<JsonConverter>]` attribute. Example for `Did.fs`:

```fsharp
// Add these opens at the top of the file (after the existing opens):
open System.Text.Json
open System.Text.Json.Serialization

// Add converter class before the Did type:
type DidConverter() =
    inherit JsonConverter<Did>()
    override _.Read(reader, _typeToConvert, _options) =
        let s = reader.GetString()
        match Did.parse s with
        | Ok did -> did
        | Error msg -> raise (JsonException(sprintf "Invalid DID '%s': %s" s msg))
    override _.Write(writer, value, _options) =
        writer.WriteStringValue(Did.value value)

// Add attribute on the type:
[<JsonConverter(typeof<DidConverter>)>]
type Did = private Did of string
```

**IMPORTANT:** The converter class must be defined **before** the type it converts because F# files compile top-to-bottom. However, the converter references `Did` in its type signature. This creates a circular dependency. The solution is to use `and` to make them mutually recursive, OR define the converter after the type using a module-level attribute registration approach.

Actually, the cleanest approach: define the converter **after** the type and module (it only needs `Did.parse` and `Did.value` which are in the companion module). Then apply `[<JsonConverter>]` on the type. Since the type is defined before the converter class, we need to use the `[<JsonConverterAttribute>]` approach differently.

**Revised approach:** Put all converters in a single new file in the Syntax project that compiles last. Apply converters via `JsonSerializerOptions` registration (in `Json.fs` in Core) rather than attributes on types. This avoids the circular reference issue entirely.

Create: `src/FSharp.ATProto.Syntax/Converters.fs` (added last in compile order)

```fsharp
namespace FSharp.ATProto.Syntax

open System
open System.Text.Json
open System.Text.Json.Serialization

type DidConverter() =
    inherit JsonConverter<Did>()
    override _.Read(reader, _, _) =
        let s = reader.GetString()
        match Did.parse s with
        | Ok v -> v
        | Error msg -> raise (JsonException(sprintf "Invalid DID '%s': %s" s msg))
    override _.Write(writer, v, _) =
        writer.WriteStringValue(Did.value v)

type HandleConverter() =
    inherit JsonConverter<Handle>()
    override _.Read(reader, _, _) =
        let s = reader.GetString()
        match Handle.parse s with
        | Ok v -> v
        | Error msg -> raise (JsonException(sprintf "Invalid Handle '%s': %s" s msg))
    override _.Write(writer, v, _) =
        writer.WriteStringValue(Handle.value v)

// ... same pattern for AtUri, Cid, Nsid, Tid, RecordKey, AtDateTime, Language, Uri
// Total: 10 converter classes
```

Then update `src/FSharp.ATProto.Syntax/FSharp.ATProto.Syntax.fsproj` to add `<Compile Include="Converters.fs" />` at the end.

Then register all converters in `src/FSharp.ATProto.Core/Json.fs`:

```fsharp
open FSharp.ATProto.Syntax

module Json =
    let options =
        let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        opts.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
        opts.Converters.Add(JsonFSharpConverter(JsonFSharpOptions.Default().WithUnionInternalTag().WithUnionNamedFields()))
        opts.Converters.Add(DidConverter())
        opts.Converters.Add(HandleConverter())
        opts.Converters.Add(AtUriConverter())
        opts.Converters.Add(CidConverter())
        opts.Converters.Add(NsidConverter())
        opts.Converters.Add(TidConverter())
        opts.Converters.Add(RecordKeyConverter())
        opts.Converters.Add(AtDateTimeConverter())
        opts.Converters.Add(LanguageConverter())
        opts.Converters.Add(UriConverter())
        opts
```

**IMPORTANT about UriConverter:** `System.Text.Json` has a built-in converter for `System.Uri`. Our type is `FSharp.ATProto.Syntax.Uri`, which is different. The converter class name should be unambiguous — use `SyntaxUriConverter` if needed to avoid name collisions with `System.UriConverter` or similar.

**Step 4: Run tests to verify they pass**

Run: `dotnet run --project tests/FSharp.ATProto.Syntax.Tests -- --summary`
Expected: All tests pass including the new roundtrip tests.

**Step 5: Build the full solution**

Run: `dotnet build`
Expected: 0 warnings, 0 errors. The converters are registered but not yet used by generated types (those still use `string`).

**Step 6: Commit**

```bash
git add src/FSharp.ATProto.Syntax/Converters.fs src/FSharp.ATProto.Syntax/FSharp.ATProto.Syntax.fsproj src/FSharp.ATProto.Core/Json.fs tests/
git commit -m "Add JSON converters for all Syntax identifier types

10 converters (Did, Handle, AtUri, Cid, Nsid, Tid, RecordKey, AtDateTime,
Language, Uri) in Converters.fs. Registered in Json.options. Each reads
via parse (rejecting invalid values) and writes via value."
```

---

## Task 2: Update code generator — typed string formats

Change `TypeMapping.lexTypeToFSharpType` to emit typed identifiers instead of `string` for formatted string fields. Also update `collectNamespaceDeps` to track Syntax type dependencies.

**Files:**
- Modify: `src/FSharp.ATProto.CodeGen/TypeMapping.fs` (lines 11, 13)
- Test: `tests/FSharp.ATProto.CodeGen.Tests/WrapperGenTests.fs`

**Step 1: Write failing codegen test**

Add a test fixture document with a string field that has a format specifier, and assert the generated code uses the typed name:

```fsharp
let queryWithTypedStringDoc =
    { Id = mkNsid "com.example.typedStrings"
      Revision = None
      Description = None
      Defs =
        Map.ofList [
            "main", LexDef.Query
                { Description = None
                  Parameters = Some
                    { Description = None
                      Properties = Map.ofList [
                          "actor", LexType.String
                              { emptyStringConstraints with
                                  Format = Some LexStringFormat.Did }
                          "uri", LexType.String
                              { emptyStringConstraints with
                                  Format = Some LexStringFormat.AtUri }
                      ]
                      Required = [ "actor"; "uri" ] }
                  Output = Some
                    { Description = None
                      Encoding = "application/json"
                      Schema = Some (LexType.Object
                          { Description = None
                            Properties = Map.ofList [
                                "handle", LexType.String
                                    { emptyStringConstraints with
                                        Format = Some LexStringFormat.Handle }
                                "cid", LexType.CidLink
                            ]
                            Required = [ "handle"; "cid" ]
                            Nullable = [] }) }
                  Errors = [] }
        ] }

// Test:
testCase "typed string formats in generated code" <| fun () ->
    let result = NamespaceGen.generateAll [ queryWithTypedStringDoc ]
    let (_, content) = result.[0]
    Expect.stringContains content "Actor: Did" "DID param uses Did type"
    Expect.stringContains content "Uri: AtUri" "AT-URI param uses AtUri type"
    Expect.stringContains content "Handle: Handle" "Handle output uses Handle type"
    Expect.stringContains content "Cid: Cid" "CidLink uses Cid type"
    Expect.isFalse (content.Contains "Actor: string") "DID is not string"
```

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/FSharp.ATProto.CodeGen.Tests -- --summary`
Expected: FAIL — generated code will have `Actor: string` not `Actor: Did`.

**Step 3: Update TypeMapping.fs**

Replace lines 11 and 13 of `TypeMapping.fs`:

```fsharp
let rec lexTypeToFSharpType (currentNamespace: string) (lexType: LexType) : string =
    match lexType with
    | Boolean _ -> "bool"
    | Integer _ -> "int64"
    | String s ->
        match s.Format with
        | Some LexStringFormat.Did -> "Did"
        | Some LexStringFormat.Handle -> "Handle"
        | Some LexStringFormat.AtUri -> "AtUri"
        | Some LexStringFormat.Cid -> "Cid"
        | Some LexStringFormat.Nsid -> "Nsid"
        | Some LexStringFormat.Tid -> "Tid"
        | Some LexStringFormat.RecordKey -> "RecordKey"
        | Some LexStringFormat.Datetime -> "AtDateTime"
        | Some LexStringFormat.Language -> "Language"
        | Some LexStringFormat.Uri -> "Uri"
        | Some LexStringFormat.AtIdentifier -> "string"
        | None -> "string"
    | Bytes _ -> "byte[]"
    | CidLink -> "Cid"
    | Blob _ -> "JsonElement"
    | Unknown -> "JsonElement"
    | Array arr ->
        let inner = lexTypeToFSharpType currentNamespace arr.Items
        sprintf "%s list" inner
    | Ref r ->
        let (_targetNamespace, qualifiedName) = Naming.refToQualifiedType currentNamespace r.Ref
        qualifiedName
    | Union _ -> "JsonElement"
    | Object _ -> "JsonElement"
    | Params _ -> "JsonElement"
```

**Step 4: Update `collectNamespaceDeps`**

The `collectNamespaceDeps` function also needs updating — typed identifiers don't create cross-namespace deps (they're from the Syntax assembly, not generated code), so the `String` and `CidLink` cases can remain returning `Set.empty`. No change needed here.

**Step 5: Update Generated.fs opens**

The generated file needs to open `FSharp.ATProto.Syntax` so the typed names resolve. In `NamespaceGen.fs`, find the `generateAll` function (around line 653) and add an `Open("FSharp.ATProto.Syntax")` alongside the existing opens.

```fsharp
// In generateAll, add after the existing Open statements:
Open("FSharp.ATProto.Syntax")
```

**Step 6: Run tests to verify they pass**

Run: `dotnet run --project tests/FSharp.ATProto.CodeGen.Tests -- --summary`
Expected: All tests pass including the new typed string test.

**Step 7: Commit**

```bash
git add src/FSharp.ATProto.CodeGen/TypeMapping.fs src/FSharp.ATProto.CodeGen/NamespaceGen.fs tests/FSharp.ATProto.CodeGen.Tests/WrapperGenTests.fs
git commit -m "Codegen: emit typed identifiers for formatted string fields

Map Lexicon string formats to Syntax types: did->Did, handle->Handle,
at-uri->AtUri, cid->Cid, nsid->Nsid, tid->Tid, record-key->RecordKey,
datetime->AtDateTime, language->Language, uri->Uri. CidLink also maps
to Cid. at-identifier stays as string (ambiguous DID/Handle)."
```

---

## Task 3: Update code generator — typed discriminated unions for inline union fields

When a record property has type `Union`, generate a named DU type and use it instead of `JsonElement`. This reuses the existing `generateUnionWidget` function.

**Files:**
- Modify: `src/FSharp.ATProto.CodeGen/TypeMapping.fs`
- Modify: `src/FSharp.ATProto.CodeGen/NamespaceGen.fs` (generateRecordWidget, generateParamsWidget)
- Test: `tests/FSharp.ATProto.CodeGen.Tests/WrapperGenTests.fs`

**Step 1: Write failing codegen test**

```fsharp
let recordWithInlineUnionDoc =
    { Id = mkNsid "com.example.inlineUnion"
      Revision = None
      Description = None
      Defs =
        Map.ofList [
            "main", LexDef.Record
                { Description = None
                  Key = "tid"
                  Record =
                    { Description = None
                      Properties = Map.ofList [
                          "text", LexType.String emptyStringConstraints
                          "embed", LexType.Union
                              { Description = None
                                Refs = [ "com.example.embedA#main"; "com.example.embedB#main" ]
                                Closed = false }
                      ]
                      Required = [ "text" ]
                      Nullable = [] } }
        ] }

testCase "inline union generates DU type" <| fun () ->
    let result = NamespaceGen.generateAll [ recordWithInlineUnionDoc ]
    let (_, content) = result.[0]
    // Should generate a DU type for the embed field
    Expect.stringContains content "JsonFSharpConverter" "has union converter attribute"
    Expect.stringContains content "| EmbedA of" "has first union case"
    Expect.stringContains content "| EmbedB of" "has second union case"
    Expect.stringContains content "| Unknown of string * System.Text.Json.JsonElement" "has Unknown fallback"
    // The field should reference the DU, not JsonElement
    Expect.isFalse (content.Contains "Embed: JsonElement") "embed is not JsonElement"

let recordWithClosedUnionDoc =
    { Id = mkNsid "com.example.closedUnion"
      Revision = None
      Description = None
      Defs =
        Map.ofList [
            "main", LexDef.Record
                { Description = None
                  Key = "tid"
                  Record =
                    { Description = None
                      Properties = Map.ofList [
                          "action", LexType.Union
                              { Description = None
                                Refs = [ "com.example.create#main"; "com.example.delete#main" ]
                                Closed = true }
                      ]
                      Required = [ "action" ]
                      Nullable = [] } }
        ] }

testCase "closed inline union has no Unknown case" <| fun () ->
    let result = NamespaceGen.generateAll [ recordWithClosedUnionDoc ]
    let (_, content) = result.[0]
    Expect.stringContains content "| Create of" "has first case"
    Expect.stringContains content "| Delete of" "has second case"
    Expect.isFalse (content.Contains "Unknown") "no Unknown case for closed union"
```

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/FSharp.ATProto.CodeGen.Tests -- --summary`
Expected: FAIL — inline unions still produce `JsonElement`.

**Step 3: Implement inline union DU generation**

The approach: when generating a record and a property is a `Union`, call `generateUnionWidget` to emit a companion DU type, then reference that DU's name in the record field.

**Modify `NamespaceGen.fs`:**

Add a helper function that collects inline union DU widgets from a record's properties:

```fsharp
/// Generate DU types for any Union-typed properties in a record.
/// Returns a list of (propertyName, duTypeName, widget) tuples.
let private collectInlineUnionWidgets
    (currentNamespace: string)
    (parentTypeName: string)
    (properties: Map<string, LexType>)
    =
    properties
    |> Map.toList
    |> List.choose (fun (propName, lexType) ->
        match lexType with
        | Union u ->
            let duName = (Naming.toPascalCase propName)
            let widget = generateUnionWidget currentNamespace duName u
            Some (propName, duName, widget)
        | Array { Items = Union u } ->
            let duName = (Naming.toPascalCase propName) + "Item"
            let widget = generateUnionWidget currentNamespace duName u
            Some (propName, duName, widget)
        | _ -> None)
```

**Modify `generateRecordWidget`** to emit companion DU types and use their names:

In the `fields` computation within `generateRecordWidget`, change the `TypeMapping.lexTypeToFSharpType` call to check for unions and use the DU type name instead. The generated code must emit the DU type widgets **before** the record type (F# requires types to be defined before use in non-recursive namespaces, but we use `namespace rec` so order doesn't matter within the namespace — but it's cleaner to emit DUs first).

The actual change: modify `TypeMapping.lexTypeToFSharpType` so that `Union` returns a placeholder that `generateRecordWidget` can override, OR modify `generateRecordWidget` to handle union fields specially.

**Recommended approach:** Modify `TypeMapping.lexTypeToFSharpType` to accept an optional override map for union fields. Actually simpler: just handle it in `generateRecordWidget` directly by checking the `LexType` before calling `lexTypeToFSharpType`:

```fsharp
let private generateRecordWidget
    (currentNamespace: string)
    (typeName: string)
    (description: string option)
    (lexObj: LexObject)
    =
    // Collect inline union DU widgets first
    let inlineUnions = collectInlineUnionWidgets currentNamespace typeName lexObj.Properties

    // Build a lookup from property name to DU type name
    let unionLookup =
        inlineUnions
        |> List.map (fun (propName, duName, _) -> (propName, duName))
        |> Map.ofList

    let fields =
        lexObj.Properties
        |> Map.toList
        |> List.map (fun (propName, lexType) ->
            let baseType =
                match Map.tryFind propName unionLookup with
                | Some duName -> duName
                | None ->
                    match lexType with
                    | Array { Items = Union _ } ->
                        // Array of union — find the DU name from lookup
                        match Map.tryFind propName unionLookup with
                        | Some duName -> sprintf "%s list" duName
                        | None -> TypeMapping.lexTypeToFSharpType currentNamespace lexType
                    | _ -> TypeMapping.lexTypeToFSharpType currentNamespace lexType
            let fieldName = Naming.toPascalCase propName |> Naming.escapeReservedWord
            let isOpt =
                not (List.contains propName lexObj.Required)
                || List.contains propName lexObj.Nullable
            let fieldType =
                if isOpt then sprintf "%s option" baseType
                else baseType
            (propName, fieldName, fieldType))
    // ... rest of record generation unchanged ...
```

Wait — this needs to also handle the array-of-union case properly. Let me simplify. The `collectInlineUnionWidgets` already handles both `Union` and `Array { Items = Union }`. The lookup needs to store both the DU name AND whether it's an array:

```fsharp
let private collectInlineUnionWidgets
    (currentNamespace: string)
    (parentTypeName: string)
    (properties: Map<string, LexType>)
    =
    properties
    |> Map.toList
    |> List.choose (fun (propName, lexType) ->
        match lexType with
        | Union u ->
            let duName = Naming.toPascalCase propName
            let widget = generateUnionWidget currentNamespace duName u
            Some (propName, duName, false, widget)  // false = not array
        | Array { Items = Union u } ->
            let duName = (Naming.toPascalCase propName) + "Item"
            let widget = generateUnionWidget currentNamespace duName u
            Some (propName, duName, true, widget)  // true = array
        | _ -> None)
```

And in the record generation:

```fsharp
let unionLookup =
    inlineUnions
    |> List.map (fun (propName, duName, isArray, _) ->
        (propName, if isArray then sprintf "%s list" duName else duName))
    |> Map.ofList

let fields =
    lexObj.Properties
    |> Map.toList
    |> List.map (fun (propName, lexType) ->
        let baseType =
            match Map.tryFind propName unionLookup with
            | Some typeName -> typeName
            | None -> TypeMapping.lexTypeToFSharpType currentNamespace lexType
        // ... rest unchanged
    )
```

The DU widgets themselves need to be returned/emitted. The question is how. `generateRecordWidget` currently returns a single widget. It needs to return multiple widgets (the DUs + the record).

**Best approach:** Change `generateRecordWidget` to return a list of widgets instead of a single widget. The caller wraps them in a module. Let me look at how the callers use it...

Actually, the callers already use it inside module builders where they can emit multiple items. So the cleanest change is: `generateRecordWidget` returns `WidgetBuilder list` — the inline DU widgets followed by the record widget. Each call site wraps with `for widget in widgets do widget`.

Alternatively, return a tuple: `(duWidgets: WidgetBuilder list, recordWidget: WidgetBuilder)`. The caller emits duWidgets first, then the record.

**Actually, the simplest approach given `namespace rec`:** Since the entire Generated.fs uses `namespace rec`, definition order doesn't matter. We can emit DUs anywhere. So we could have a separate pass that collects all inline union DUs and emits them at the top of the file, OR we can emit them right before the record in the same module.

Let's go with: `generateRecordWidget` returns `WidgetBuilder list` where the inline DU widgets come first and the record widget comes last. Callers iterate over all widgets.

**Step 4: Apply the same treatment to `generateParamsWidget`**

Params can also have union fields (though rare in practice). Apply the same `collectInlineUnionWidgets` + lookup pattern.

**Step 5: Handle deduplication within a record**

When two fields in the same record have identical union refs (e.g., `ReplyRef.Parent` and `ReplyRef.Root` both ref `[postView, notFoundPost, blockedPost]`), emit one shared DU type. Deduplicate by comparing the sorted `Refs` list:

```fsharp
let uniqueUnions =
    inlineUnions
    |> List.distinctBy (fun (_, _, _, union) -> union.Refs |> List.sort)
```

Actually, simpler: in the `collectInlineUnionWidgets` function, group by `union.Refs |> Set.ofList`, take the first property name for each group, and have the other properties reference the same DU name.

**Step 6: Run tests**

Run: `dotnet run --project tests/FSharp.ATProto.CodeGen.Tests -- --summary`
Expected: All tests pass.

**Step 7: Commit**

```bash
git add src/FSharp.ATProto.CodeGen/TypeMapping.fs src/FSharp.ATProto.CodeGen/NamespaceGen.fs tests/FSharp.ATProto.CodeGen.Tests/WrapperGenTests.fs
git commit -m "Codegen: generate typed DUs for inline union fields

When a record property is a Union type, emit a companion discriminated
union type with JsonFSharpConverter attribute and Unknown fallback (unless
closed). Reuses existing generateUnionWidget. Handles both direct union
fields and array-of-union fields. Deduplicates within a record when
multiple fields share the same union refs."
```

---

## Task 4: Regenerate Generated.fs and fix compilation

Run the code generator with the updated TypeMapping and NamespaceGen to produce a new `Generated.fs` with typed identifiers and typed unions. Fix any compilation issues.

**Files:**
- Regenerate: `src/FSharp.ATProto.Bluesky/Generated/Generated.fs`
- Possibly modify: `src/FSharp.ATProto.Bluesky/RichText.fs`, `Identity.fs`, `Bluesky.fs`, `Chat.fs` (if they reference generated types that changed)
- Possibly modify: `tests/FSharp.ATProto.Bluesky.Tests/` (if tests reference generated types)

**Step 1: Regenerate**

Run the code generator CLI:

```bash
dotnet run --project src/FSharp.ATProto.CodeGen -- --lexdir extern/atproto/lexicons --outdir src/FSharp.ATProto.Bluesky/Generated
```

This reads all 324 lexicon JSON files and produces a new `Generated.fs`.

**Step 2: Attempt to build**

Run: `dotnet build`

This will likely produce errors because:
- `RichText.fs`, `Bluesky.fs`, `Chat.fs`, `Identity.fs` construct generated types with raw strings where the types now expect `Did`, `AtUri`, etc.
- Test files may reference generated types with string fields that are now typed

**Step 3: Fix compilation errors**

For each error, update the calling code to use the typed identifiers. Common patterns:

```fsharp
// Before:
{ Uri = someUri; Cid = someCid }

// After (if someUri is a string):
{ Uri = AtUri.parse someUri |> Result.defaultWith failwith
  Cid = Cid.parse someCid |> Result.defaultWith failwith }

// Or if the value comes from user input in convenience methods,
// accept the typed value directly (will be addressed in Task 5).
```

For convenience methods that currently accept `string` and construct records, they may need intermediate parsing. This is expected — Task 5 will clean up the public API signatures.

**Step 4: Fix any `JsonElement` → DU type mismatches**

Generated union fields that were `JsonElement` are now proper DU types. Code that accessed these via `GetProperty("$type")` etc. needs to use pattern matching instead. This primarily affects:
- The example file (`examples/BskyBotExample/Program.fs`)
- Possibly `RichText.fs` if it constructs facet features as `JsonElement`

**Step 5: Run full test suite**

Run all 6 test projects. Fix any test failures caused by type changes.

**Step 6: Commit**

```bash
git add src/FSharp.ATProto.Bluesky/ tests/ examples/
git commit -m "Regenerate types with typed identifiers and union DUs

Generated.fs now uses Did, Handle, AtUri, Cid, Nsid, Tid, RecordKey,
AtDateTime, Language, Uri instead of string for formatted fields.
Inline unions are proper F# discriminated unions with Unknown fallback.
Updated convenience methods and tests for new types."
```

---

## Task 5: Polish convenience API — named types, typed signatures, consistent errors

Clean up the high-level convenience API with proper types, named record parameters, and consistent error handling.

**Files:**
- Modify: `src/FSharp.ATProto.Bluesky/Bluesky.fs`
- Modify: `src/FSharp.ATProto.Bluesky/Chat.fs`
- Modify: `src/FSharp.ATProto.Bluesky/Identity.fs`
- Modify: `src/FSharp.ATProto.Core/Types.fs` (add helper types)
- Test: `tests/FSharp.ATProto.Bluesky.Tests/`
- Modify: `examples/BskyBotExample/Program.fs`

**Step 1: Add helper types**

In `src/FSharp.ATProto.Bluesky/Bluesky.fs` (or a new types file), add:

```fsharp
/// A reference to a specific version of a record (post, like, etc.)
type PostRef = { Uri: AtUri; Cid: Cid }

/// Image data for upload with a post.
type ImageUpload = { Data: byte[]; MimeType: string; AltText: string }
```

**Step 2: Update Bluesky.fs function signatures**

```fsharp
val post:           agent: AtpAgent -> text: string -> Task<Result<PostRef, XrpcError>>
val postWith:       agent: AtpAgent -> text: string -> facets: Facet list -> Task<Result<PostRef, XrpcError>>
val reply:          agent: AtpAgent -> text: string -> parent: PostRef -> root: PostRef -> Task<Result<PostRef, XrpcError>>
val like:           agent: AtpAgent -> uri: AtUri -> cid: Cid -> Task<Result<AtUri, XrpcError>>
val repost:         agent: AtpAgent -> uri: AtUri -> cid: Cid -> Task<Result<AtUri, XrpcError>>
val follow:         agent: AtpAgent -> did: Did -> Task<Result<AtUri, XrpcError>>
val block:          agent: AtpAgent -> did: Did -> Task<Result<AtUri, XrpcError>>
val deleteRecord:   agent: AtpAgent -> atUri: AtUri -> Task<Result<unit, XrpcError>>
val uploadBlob:     agent: AtpAgent -> data: byte[] -> mimeType: string -> Task<Result<ComAtprotoRepo.UploadBlob.Output, XrpcError>>
val postWithImages: agent: AtpAgent -> text: string -> images: ImageUpload list -> Task<Result<PostRef, XrpcError>>
```

Internally, each function converts the `CreateRecord.Output` to `PostRef`:

```fsharp
let private toPostRef (output: ComAtprotoRepo.CreateRecord.Output) : PostRef =
    { Uri = output.Uri; Cid = output.Cid }  // Already typed after regeneration
```

**Step 3: Update Identity.fs error types**

```fsharp
type IdentityError =
    | XrpcError of XrpcError
    | VerificationFailed of string
    | DocumentParseError of string

val parseDidDocument: doc: JsonElement -> Result<AtprotoIdentity, string>
val resolveDid:       agent: AtpAgent -> did: Did -> Task<Result<AtprotoIdentity, IdentityError>>
val resolveHandle:    agent: AtpAgent -> handle: Handle -> Task<Result<Did, IdentityError>>
val resolveIdentity:  agent: AtpAgent -> identifier: string -> Task<Result<AtprotoIdentity, IdentityError>>
```

**Step 4: Update Chat.fs signatures**

Chat methods that accept `string` for members/convoId — these stay as `string` since convo IDs and message IDs are opaque server-generated strings with no Lexicon format annotation. But `getConvoForMembers` takes member DIDs, which should be typed:

```fsharp
val getConvoForMembers: agent: AtpAgent -> members: Did list -> Task<Result<...>>
```

**Step 5: Update the example file**

Update `examples/BskyBotExample/Program.fs` to use the new typed API — pattern matching on union DUs, using `PostRef`, `ImageUpload`, etc.

**Step 6: Update tests**

Update `tests/FSharp.ATProto.Bluesky.Tests/` for the new type signatures.

**Step 7: Run full test suite**

Run all 6 test projects. All must pass.

**Step 8: Commit**

```bash
git add src/FSharp.ATProto.Bluesky/ src/FSharp.ATProto.Core/ tests/ examples/
git commit -m "Polish convenience API: named types, typed params, consistent errors

Add PostRef and ImageUpload records. Convenience methods accept/return
typed identifiers (Did, AtUri, Cid). Identity module uses IdentityError
DU for consistent error handling. Chat.getConvoForMembers accepts Did list."
```

---

## Task 6: Final verification and cleanup

Full build, full test suite, verify example compiles, verify fsdocs builds.

**Step 1: Full build**

Run: `dotnet build`
Expected: 0 warnings, 0 errors

**Step 2: Full test suite**

Run all 6 test projects.
Expected: All pass (count may differ from 1,472 due to new tests added).

**Step 3: Verify fsdocs**

Run: `dotnet fsdocs build --clean --output output 2>&1`
Expected: Builds successfully. API reference pages now show typed identifiers and DU types.

**Step 4: Verify example**

Run: `dotnet build examples/BskyBotExample/BskyBotExample.fsproj`
Expected: Compiles cleanly.

**Step 5: Commit any remaining cleanup**

```bash
git add -A
git commit -m "Phase 7 complete: ergonomics & type safety

All inline unions are typed DUs with Unknown fallback. All formatted
string fields use validated Syntax types. Convenience API uses named
records and consistent error types."
```
