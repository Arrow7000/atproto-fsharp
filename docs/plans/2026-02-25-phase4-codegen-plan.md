# Phase 4: Code Generator Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a CLI tool that generates idiomatic F# types from 324 AT Protocol Lexicon schema files.

**Architecture:** Parse lexicons with existing `LexiconParser` -> group by namespace -> emit F# records/DUs/constants via Fabulous.AST -> write formatted `.fs` files. One file per NSID namespace (~36 files).

**Tech Stack:** Fabulous.AST 1.10.0 (includes Fantomas.Core), FSharp.SystemTextJson 1.4.36, Expecto + FsCheck for testing.

**Key References:**
- Design doc: `docs/plans/2026-02-25-phase4-codegen-design.md`
- Lexicon types model: `src/FSharp.ATProto.Lexicon/LexiconTypes.fs`
- Lexicon parser: `src/FSharp.ATProto.Lexicon/LexiconParser.fs`
- Real lexicon schemas: `extern/atproto/lexicons/` (324 JSON files)
- Existing test patterns: `tests/FSharp.ATProto.Lexicon.Tests/` (follow these patterns exactly)

**Build command prefix:** All `dotnet` commands must be prefixed with `export PATH="$HOME/.dotnet:$PATH" &&`

---

## Generated Code Structure

Each generated `.fs` file uses a `namespace rec` to allow mutual references between types in the same namespace. Within each namespace, each lexicon doc gets a nested module. The `main` def's type is named after the doc (4th NSID segment, PascalCased).

Example for `app.bsky.feed`:
```fsharp
namespace rec FSharp.ATProto.Bluesky.AppBskyFeed

open System.Text.Json
open System.Text.Json.Serialization
open FSharp.ATProto.Syntax

// From app.bsky.feed.post (record type)
module Post =
    [<Literal>]
    let TypeId = "app.bsky.feed.post"

    type Post =
        { [<JsonPropertyName("text")>] Text: string
          [<JsonPropertyName("createdAt")>] CreatedAt: string
          [<JsonPropertyName("reply")>] Reply: ReplyRef option }

    type ReplyRef =
        { [<JsonPropertyName("root")>] Root: ComAtprotoRepo.StrongRef.StrongRef
          [<JsonPropertyName("parent")>] Parent: ComAtprotoRepo.StrongRef.StrongRef }

// From app.bsky.feed.like (record type)
module Like =
    [<Literal>]
    let TypeId = "app.bsky.feed.like"

    type Like =
        { [<JsonPropertyName("subject")>] Subject: ComAtprotoRepo.StrongRef.StrongRef
          [<JsonPropertyName("createdAt")>] CreatedAt: string }

// From app.bsky.feed.defs (defs-only, no main primary type)
module Defs =
    type FeedViewPost =
        { [<JsonPropertyName("post")>] Post: PostView
          [<JsonPropertyName("reply")>] Reply: ReplyRef option }
    // ... more types
```

## Naming Conventions

| Source | F# | Example |
|---|---|---|
| NSID namespace (all-but-last segments) | PascalCase concatenated | `app.bsky.feed` -> `AppBskyFeed` |
| Doc name (last NSID segment) | PascalCase module name | `post` -> `Post` |
| `main` def | Type named after doc | `Post.Post` |
| Non-main def | PascalCase type name | `replyRef` -> `Post.ReplyRef` |
| Property name | PascalCase field + `[<JsonPropertyName("original")>]` | `createdAt` -> `CreatedAt` |
| File name | Namespace concatenated + `.fs` | `AppBskyFeed.fs` |
| Cross-namespace ref | Open namespace + module.type | `ComAtprotoRepo.StrongRef.StrongRef` |

## Type Mapping Rules

| LexType | F# Type | Fabulous.AST |
|---|---|---|
| `Boolean` | `bool` | `Bool()` |
| `Integer` | `int64` | `Int64()` |
| `String` (no format) | `string` | `String()` |
| `String` (format: did) | `string` | `String()` |
| `String` (format: handle) | `string` | `String()` |
| `String` (format: at-uri) | `string` | `String()` |
| `String` (format: datetime) | `string` | `String()` |
| `String` (format: uri) | `string` | `String()` |
| `String` (format: language) | `string` | `String()` |
| `String` (format: cid) | `string` | `String()` |
| `String` (format: tid) | `string` | `String()` |
| `String` (format: nsid) | `string` | `String()` |
| `String` (format: record-key) | `string` | `String()` |
| `String` (format: at-identifier) | `string` | `String()` |
| `Bytes` | `byte[]` | `LongIdent("byte[]")` |
| `CidLink` | `string` | `String()` |
| `Blob` | `BlobRef` | `LongIdent("BlobRef")` (needs import) |
| `Array` | `'T list` | `ListPostfix(innerType)` |
| `Object` | Record type (inline or referenced) | Generate a record |
| `Ref` | Referenced type name | `LongIdent("Module.TypeName")` |
| `Union` | DU type | Generate a DU |
| `Unknown` | `JsonElement` | `LongIdent("JsonElement")` |
| Field not in `required` | `'T option` | `OptionPostfix(innerType)` |
| Field in `nullable` | `'T option` | `OptionPostfix(innerType)` |

**Note:** All string formats map to `string` for Phase 4. Typed wrappers (Did, Handle, etc.) will be added in Phase 5 when JsonConverters for Syntax types are available. This keeps Phase 4 focused on code generation correctness without needing custom serialization infrastructure.

## Union JSON Serialization

AT Protocol unions use `$type` as an internal tag discriminator. Use `FSharp.SystemTextJson`:

```fsharp
[<JsonFSharpConverter(JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.UnwrapSingleFieldCases, unionTagName = "$type")>]
type Embed =
    | [<JsonName("app.bsky.embed.images")>] Images of Images.Images
    | [<JsonName("app.bsky.embed.external")>] External of External.External
    | Unknown of string * JsonElement  // open unions only
```

**Important:** Verify that `JsonName` attribute works on DU cases with `FSharp.SystemTextJson` 1.4.36 during implementation. If it doesn't, fall back to generating custom `JsonConverter<T>` classes per union. The test in Task 5 will catch this.

## Dependency Ordering

Generated `.fs` files must be listed in the `.fsproj` in dependency order (F# requires forward declaration). The code generator must:
1. Build a dependency graph: for each namespace, find all cross-namespace refs
2. Topological sort the namespaces
3. Emit `<Compile Include>` entries in that order

Known foundation namespaces (most-depended-on): `com.atproto.label`, `com.atproto.repo`, `app.bsky.richtext`, `app.bsky.actor`.

---

## Task 1: Scaffold Projects

**Goal:** Create `FSharp.ATProto.CodeGen` (CLI), `FSharp.ATProto.CodeGen.Tests`, and `FSharp.ATProto.Bluesky` (generated types library). Verify everything builds.

**Files:**
- Create: `src/FSharp.ATProto.CodeGen/FSharp.ATProto.CodeGen.fsproj`
- Create: `src/FSharp.ATProto.CodeGen/Program.fs`
- Create: `tests/FSharp.ATProto.CodeGen.Tests/FSharp.ATProto.CodeGen.Tests.fsproj`
- Create: `tests/FSharp.ATProto.CodeGen.Tests/TestHelpers.fs`
- Create: `tests/FSharp.ATProto.CodeGen.Tests/Main.fs`
- Create: `src/FSharp.ATProto.Bluesky/FSharp.ATProto.Bluesky.fsproj`
- Modify: `FSharp.ATProto.sln`

**Step 1: Create the CodeGen project**

`src/FSharp.ATProto.CodeGen/FSharp.ATProto.CodeGen.fsproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Naming.fs" />
    <Compile Include="TypeMapping.fs" />
    <Compile Include="TypeGen.fs" />
    <Compile Include="NamespaceGen.fs" />
    <Compile Include="Program.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\FSharp.ATProto.Lexicon\FSharp.ATProto.Lexicon.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Fabulous.AST" Version="1.10.0" />
  </ItemGroup>
</Project>
```

`src/FSharp.ATProto.CodeGen/Naming.fs` (stub):
```fsharp
module FSharp.ATProto.CodeGen.Naming
```

`src/FSharp.ATProto.CodeGen/TypeMapping.fs` (stub):
```fsharp
module FSharp.ATProto.CodeGen.TypeMapping
```

`src/FSharp.ATProto.CodeGen/TypeGen.fs` (stub):
```fsharp
module FSharp.ATProto.CodeGen.TypeGen
```

`src/FSharp.ATProto.CodeGen/NamespaceGen.fs` (stub):
```fsharp
module FSharp.ATProto.CodeGen.NamespaceGen
```

`src/FSharp.ATProto.CodeGen/Program.fs` (stub):
```fsharp
module FSharp.ATProto.CodeGen.Program

[<EntryPoint>]
let main _args = 0
```

**Step 2: Create the CodeGen test project**

`tests/FSharp.ATProto.CodeGen.Tests/FSharp.ATProto.CodeGen.Tests.fsproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="TestHelpers.fs" />
    <Compile Include="NamingTests.fs" />
    <Compile Include="TypeMappingTests.fs" />
    <Compile Include="TypeGenTests.fs" />
    <Compile Include="IntegrationTests.fs" />
    <Compile Include="Main.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\FSharp.ATProto.CodeGen\FSharp.ATProto.CodeGen.fsproj" />
    <ProjectReference Include="..\..\src\FSharp.ATProto.Lexicon\FSharp.ATProto.Lexicon.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Expecto" Version="10.2.3" />
    <PackageReference Include="Expecto.FsCheck" Version="10.2.3" />
    <PackageReference Include="FsCheck" Version="2.16.6" />
  </ItemGroup>
</Project>
```

`tests/FSharp.ATProto.CodeGen.Tests/TestHelpers.fs` — follow the pattern in `tests/FSharp.ATProto.Lexicon.Tests/TestHelpers.fs`:
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

let lexiconDir =
    Path.Combine(solutionRoot, "extern", "atproto", "lexicons")
```

`tests/FSharp.ATProto.CodeGen.Tests/Main.fs`:
```fsharp
module Main

open Expecto

[<EntryPoint>]
let main args =
    runTestsInAssemblyWithCLIArgs [] args
```

Create stub test files (`NamingTests.fs`, `TypeMappingTests.fs`, `TypeGenTests.fs`, `IntegrationTests.fs`) each with a placeholder test:
```fsharp
module NamingTests  // (or TypeMappingTests, etc.)

open Expecto

[<Tests>]
let tests =
    testList "Naming" [
        testCase "placeholder" <| fun () ->
            Expect.isTrue true "placeholder"
    ]
```

**Step 3: Create the Bluesky project**

`src/FSharp.ATProto.Bluesky/FSharp.ATProto.Bluesky.fsproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <!-- Generated files will be added here by the code generator -->
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\FSharp.ATProto.Syntax\FSharp.ATProto.Syntax.fsproj" />
    <ProjectReference Include="..\FSharp.ATProto.DRISL\FSharp.ATProto.DRISL.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="FSharp.SystemTextJson" Version="1.4.36" />
  </ItemGroup>
</Project>
```

**Step 4: Add all projects to solution and verify build**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet sln add src/FSharp.ATProto.CodeGen/FSharp.ATProto.CodeGen.fsproj
dotnet sln add src/FSharp.ATProto.Bluesky/FSharp.ATProto.Bluesky.fsproj
dotnet sln add tests/FSharp.ATProto.CodeGen.Tests/FSharp.ATProto.CodeGen.Tests.fsproj
dotnet build
dotnet test tests/FSharp.ATProto.CodeGen.Tests
```

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.CodeGen/ src/FSharp.ATProto.Bluesky/ tests/FSharp.ATProto.CodeGen.Tests/ FSharp.ATProto.sln
git commit -m "Scaffold CodeGen, Bluesky, and CodeGen.Tests projects for Phase 4"
```

---

## Task 2: Naming Utilities

**Goal:** Implement all naming/conversion functions that the code generator needs to map NSID components and refs to F# identifiers.

**Files:**
- Modify: `src/FSharp.ATProto.CodeGen/Naming.fs`
- Modify: `tests/FSharp.ATProto.CodeGen.Tests/NamingTests.fs`

**Step 1: Write tests for `toPascalCase`**

```fsharp
// NamingTests.fs
module NamingTests

open Expecto
open FSharp.ATProto.CodeGen.Naming

[<Tests>]
let tests =
    testList "Naming" [
        testList "toPascalCase" [
            testCase "simple word" <| fun () ->
                Expect.equal (toPascalCase "post") "Post" ""
            testCase "camelCase" <| fun () ->
                Expect.equal (toPascalCase "replyRef") "ReplyRef" ""
            testCase "already PascalCase" <| fun () ->
                Expect.equal (toPascalCase "ReplyRef") "ReplyRef" ""
            testCase "single char" <| fun () ->
                Expect.equal (toPascalCase "x") "X" ""
            testCase "with numbers" <| fun () ->
                Expect.equal (toPascalCase "feedViewPost") "FeedViewPost" ""
            testCase "acronym-like" <| fun () ->
                Expect.equal (toPascalCase "authFullApp") "AuthFullApp" ""
        ]
    ]
```

**Step 2: Implement `toPascalCase`**

```fsharp
// Naming.fs
module FSharp.ATProto.CodeGen.Naming

open System
open FSharp.ATProto.Syntax

/// Convert a camelCase or lowercase string to PascalCase.
/// Handles transitions like "replyRef" -> "ReplyRef" by detecting lowercase->uppercase transitions.
let toPascalCase (s: string) : string =
    if String.IsNullOrEmpty(s) then s
    else
        string (Char.ToUpperInvariant s.[0]) + s.[1..]
```

Wait, that simple approach doesn't handle `feedViewPost` -> `FeedViewPost` correctly because it's already camelCase with internal capitals. Actually it does — `feedViewPost` starts with `f`, we uppercase to `F`, rest stays `eedViewPost` = `FeedViewPost`. That's correct!

But `replyRef` -> `ReplyRef` works too: uppercase `r` -> `R`, rest `eplyRef` stays = `ReplyRef`. Correct.

Actually the simple approach works for camelCase input. Let me keep it simple.

**Step 3: Add tests for `nsidToNamespace`, `nsidToModuleName`, `nsidToFileName`**

```fsharp
        testList "nsidToNamespace" [
            testCase "4-segment NSID" <| fun () ->
                Expect.equal (nsidToNamespace "app.bsky.feed.post") "AppBskyFeed" ""
            testCase "3-segment NSID" <| fun () ->
                Expect.equal (nsidToNamespace "app.bsky.authFullApp") "AppBsky" ""
            testCase "5-segment NSID" <| fun () ->
                Expect.equal (nsidToNamespace "com.atproto.server.createSession") "ComAtprotoServer" ""
        ]
        testList "nsidToModuleName" [
            testCase "4-segment NSID" <| fun () ->
                Expect.equal (nsidToModuleName "app.bsky.feed.post") "Post" ""
            testCase "defs file" <| fun () ->
                Expect.equal (nsidToModuleName "app.bsky.feed.defs") "Defs" ""
        ]
        testList "nsidToFileName" [
            testCase "standard namespace" <| fun () ->
                Expect.equal (nsidToFileName "app.bsky.feed") "AppBskyFeed.fs" ""
        ]
```

**Step 4: Implement namespace/module/file naming functions**

```fsharp
/// Extract the namespace portion of an NSID (all segments except the last) and PascalCase-concatenate.
/// "app.bsky.feed.post" -> "AppBskyFeed"
let nsidToNamespace (nsid: string) : string =
    let parts = nsid.Split('.')
    parts.[.. parts.Length - 2]
    |> Array.map toPascalCase
    |> String.concat ""

/// Extract the module name from an NSID (last segment, PascalCased).
/// "app.bsky.feed.post" -> "Post"
let nsidToModuleName (nsid: string) : string =
    let parts = nsid.Split('.')
    toPascalCase parts.[parts.Length - 1]

/// Convert a namespace name to a file name.
/// "AppBskyFeed" -> "AppBskyFeed.fs"
let nsidToFileName (nsNamespace: string) : string =
    nsNamespace + ".fs"

/// The full F# namespace for a given NSID namespace.
/// "AppBskyFeed" -> "FSharp.ATProto.Bluesky.AppBskyFeed"
let fullNamespace (nsNamespace: string) : string =
    "FSharp.ATProto.Bluesky." + nsNamespace
```

**Step 5: Add tests for `defToTypeName` and `refToQualifiedType`**

```fsharp
        testList "defToTypeName" [
            testCase "main def uses module name" <| fun () ->
                Expect.equal (defToTypeName "Post" "main") "Post" ""
            testCase "non-main def PascalCased" <| fun () ->
                Expect.equal (defToTypeName "Post" "replyRef") "ReplyRef" ""
            testCase "non-main def already PascalCase" <| fun () ->
                Expect.equal (defToTypeName "Post" "FeedViewPost") "FeedViewPost" ""
        ]
        testList "refToQualifiedType" [
            testCase "same namespace main ref" <| fun () ->
                // From within AppBskyFeed, referencing app.bsky.feed.like (same NS)
                Expect.equal
                    (refToQualifiedType "AppBskyFeed" "app.bsky.feed.like")
                    ("AppBskyFeed", "Like.Like")
                    ""
            testCase "same namespace non-main ref" <| fun () ->
                Expect.equal
                    (refToQualifiedType "AppBskyFeed" "app.bsky.feed.defs#feedViewPost")
                    ("AppBskyFeed", "Defs.FeedViewPost")
                    ""
            testCase "cross-namespace ref" <| fun () ->
                Expect.equal
                    (refToQualifiedType "AppBskyFeed" "com.atproto.repo.strongRef")
                    ("ComAtprotoRepo", "StrongRef.StrongRef")
                    ""
            testCase "cross-namespace non-main ref" <| fun () ->
                Expect.equal
                    (refToQualifiedType "AppBskyFeed" "com.atproto.label.defs#label")
                    ("ComAtprotoLabel", "Defs.Label")
                    ""
        ]
```

**Step 6: Implement ref resolution**

```fsharp
/// Convert a def name to a type name. "main" uses the module name; others are PascalCased.
let defToTypeName (moduleName: string) (defName: string) : string =
    if defName = "main" then moduleName
    else toPascalCase defName

/// Resolve a Lexicon ref string to (targetNamespace, qualifiedTypeName).
/// The ref is already fully qualified (LexiconParser resolves local refs).
/// Returns (namespace, "Module.TypeName") pair.
let refToQualifiedType (currentNamespace: string) (ref: string) : string * string =
    let nsid, defName =
        match ref.Split('#') with
        | [| nsid; def |] -> nsid, def
        | [| nsid |] -> nsid, "main"
        | _ -> failwithf "Invalid ref: %s" ref
    let targetNs = nsidToNamespace nsid
    let moduleName = nsidToModuleName nsid
    let typeName = defToTypeName moduleName defName
    (targetNs, sprintf "%s.%s" moduleName typeName)
```

**Step 7: Run tests and verify**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.CodeGen.Tests
```

**Step 8: Commit**

```bash
git add src/FSharp.ATProto.CodeGen/Naming.fs tests/FSharp.ATProto.CodeGen.Tests/NamingTests.fs
git commit -m "Add naming utilities for code generator"
```

---

## Task 3: Type Mapping

**Goal:** Map `LexType` variants to F# type strings usable in Fabulous.AST field definitions.

**Files:**
- Modify: `src/FSharp.ATProto.CodeGen/TypeMapping.fs`
- Modify: `tests/FSharp.ATProto.CodeGen.Tests/TypeMappingTests.fs`

**Step 1: Write tests**

```fsharp
module TypeMappingTests

open Expecto
open FSharp.ATProto.Lexicon
open FSharp.ATProto.CodeGen.TypeMapping

let emptyStringConstraints: LexString =
    { Description = None; Default = None; Const = None; Enum = None
      KnownValues = None; Format = None; MinLength = None; MaxLength = None
      MinGraphemes = None; MaxGraphemes = None }

[<Tests>]
let tests =
    testList "TypeMapping" [
        testCase "Boolean -> bool" <| fun () ->
            let t = LexType.Boolean { Description = None; Default = None; Const = None }
            Expect.equal (lexTypeToFSharpType "AppBskyFeed" t) "bool" ""

        testCase "Integer -> int64" <| fun () ->
            let t = LexType.Integer { Description = None; Default = None; Const = None; Enum = None; Minimum = None; Maximum = None }
            Expect.equal (lexTypeToFSharpType "AppBskyFeed" t) "int64" ""

        testCase "String no format -> string" <| fun () ->
            let t = LexType.String emptyStringConstraints
            Expect.equal (lexTypeToFSharpType "AppBskyFeed" t) "string" ""

        testCase "String with format -> string" <| fun () ->
            let t = LexType.String { emptyStringConstraints with Format = Some LexStringFormat.Did }
            Expect.equal (lexTypeToFSharpType "AppBskyFeed" t) "string" ""

        testCase "Bytes -> byte[]" <| fun () ->
            let t = LexType.Bytes { Description = None; MinLength = None; MaxLength = None }
            Expect.equal (lexTypeToFSharpType "AppBskyFeed" t) "byte[]" ""

        testCase "CidLink -> string" <| fun () ->
            Expect.equal (lexTypeToFSharpType "AppBskyFeed" LexType.CidLink) "string" ""

        testCase "Unknown -> JsonElement" <| fun () ->
            Expect.equal (lexTypeToFSharpType "AppBskyFeed" LexType.Unknown) "JsonElement" ""

        testCase "Array of strings -> string list" <| fun () ->
            let t = LexType.Array { Description = None; Items = LexType.String emptyStringConstraints; MinLength = None; MaxLength = None }
            Expect.equal (lexTypeToFSharpType "AppBskyFeed" t) "string list" ""

        testCase "Ref same namespace -> Module.Type" <| fun () ->
            let t = LexType.Ref { Description = None; Ref = "app.bsky.feed.defs#feedViewPost" }
            Expect.equal (lexTypeToFSharpType "AppBskyFeed" t) "Defs.FeedViewPost" ""

        testCase "Ref cross namespace -> Module.Type" <| fun () ->
            let t = LexType.Ref { Description = None; Ref = "com.atproto.repo.strongRef" }
            Expect.equal (lexTypeToFSharpType "AppBskyFeed" t) "StrongRef.StrongRef" ""

        testCase "Blob -> Blob" <| fun () ->
            let t = LexType.Blob { Description = None; Accept = None; MaxSize = None }
            Expect.equal (lexTypeToFSharpType "AppBskyFeed" t) "Blob" ""
    ]
```

**Step 2: Implement `lexTypeToFSharpType`**

```fsharp
module FSharp.ATProto.CodeGen.TypeMapping

open FSharp.ATProto.Lexicon
open FSharp.ATProto.CodeGen.Naming

/// Map a LexType to its F# type name string.
/// currentNamespace is needed for resolving Ref types.
/// Returns the type name as a string (for use with LongIdent in Fabulous.AST).
let rec lexTypeToFSharpType (currentNamespace: string) (lexType: LexType) : string =
    match lexType with
    | LexType.Boolean _ -> "bool"
    | LexType.Integer _ -> "int64"
    | LexType.String _ -> "string"
    | LexType.Bytes _ -> "byte[]"
    | LexType.CidLink -> "string"
    | LexType.Blob _ -> "Blob"
    | LexType.Unknown -> "JsonElement"
    | LexType.Array arr ->
        let itemType = lexTypeToFSharpType currentNamespace arr.Items
        sprintf "%s list" itemType
    | LexType.Ref r ->
        let _targetNs, qualifiedName = refToQualifiedType currentNamespace r.Ref
        qualifiedName
    | LexType.Union _ -> "JsonElement" // Inline unions handled separately in TypeGen
    | LexType.Object _ -> "JsonElement" // Inline objects handled separately in TypeGen
    | LexType.Params _ -> "JsonElement" // Params handled separately
```

**Note:** `Union`, `Object`, and `Params` appearing as inline types (not top-level defs) are edge cases. Inline objects should be extracted into their own named type during generation. Inline unions should be generated as named DU types. The `"JsonElement"` fallback is a placeholder — `TypeGen` will handle these by generating named types and using the generated type name instead.

**Step 3: Add function to collect cross-namespace dependencies from a ref**

```fsharp
/// Collect the set of namespaces that a given LexType depends on (for open statements).
let rec collectNamespaceDeps (currentNamespace: string) (lexType: LexType) : Set<string> =
    match lexType with
    | LexType.Ref r ->
        let targetNs, _ = refToQualifiedType currentNamespace r.Ref
        if targetNs <> currentNamespace then Set.singleton targetNs
        else Set.empty
    | LexType.Array arr -> collectNamespaceDeps currentNamespace arr.Items
    | LexType.Union u ->
        u.Refs
        |> List.map (fun ref ->
            let targetNs, _ = refToQualifiedType currentNamespace ref
            if targetNs <> currentNamespace then Set.singleton targetNs
            else Set.empty)
        |> Set.unionMany
    | LexType.Object obj ->
        obj.Properties
        |> Map.values
        |> Seq.map (collectNamespaceDeps currentNamespace)
        |> Set.unionMany
    | _ -> Set.empty
```

**Step 4: Run tests, commit**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.CodeGen.Tests
git add src/FSharp.ATProto.CodeGen/TypeMapping.fs tests/FSharp.ATProto.CodeGen.Tests/TypeMappingTests.fs
git commit -m "Add type mapping from LexType to F# type names"
```

---

## Task 4: Record and Token Generation

**Goal:** Generate F# record types from `LexObject` and string constant modules from tokens/knownValues, using Fabulous.AST.

**Files:**
- Modify: `src/FSharp.ATProto.CodeGen/TypeGen.fs`
- Modify: `tests/FSharp.ATProto.CodeGen.Tests/TypeGenTests.fs`

**Step 1: Write record generation tests**

Test that the generated F# source for a simple record is correct:

```fsharp
module TypeGenTests

open Expecto
open FSharp.ATProto.Lexicon
open FSharp.ATProto.CodeGen.TypeGen

[<Tests>]
let tests =
    testList "TypeGen" [
        testList "record generation" [
            testCase "simple record with required string fields" <| fun () ->
                let obj: LexObject =
                    { Description = None
                      Properties = Map.ofList [
                          "text", LexType.String { Description = None; Default = None; Const = None; Enum = None; KnownValues = None; Format = None; MinLength = None; MaxLength = None; MinGraphemes = None; MaxGraphemes = None }
                          "createdAt", LexType.String { Description = None; Default = None; Const = None; Enum = None; KnownValues = None; Format = Some LexStringFormat.Datetime; MinLength = None; MaxLength = None; MinGraphemes = None; MaxGraphemes = None }
                      ]
                      Required = [ "text"; "createdAt" ]
                      Nullable = [] }
                let source = generateRecord "AppBskyFeed" "Post" None obj
                // Verify key elements are present in the generated source
                Expect.stringContains source "type Post =" "should have type name"
                Expect.stringContains source "[<JsonPropertyName(\"text\")>]" "should have JsonPropertyName for text"
                Expect.stringContains source "Text: string" "should have Text field"
                Expect.stringContains source "[<JsonPropertyName(\"createdAt\")>]" "should have JsonPropertyName for createdAt"
                Expect.stringContains source "CreatedAt: string" "should have CreatedAt field"

            testCase "record with optional field" <| fun () ->
                let obj: LexObject =
                    { Description = Some "A test record"
                      Properties = Map.ofList [
                          "name", LexType.String { Description = None; Default = None; Const = None; Enum = None; KnownValues = None; Format = None; MinLength = None; MaxLength = None; MinGraphemes = None; MaxGraphemes = None }
                          "bio", LexType.String { Description = None; Default = None; Const = None; Enum = None; KnownValues = None; Format = None; MinLength = None; MaxLength = None; MinGraphemes = None; MaxGraphemes = None }
                      ]
                      Required = [ "name" ]
                      Nullable = [] }
                let source = generateRecord "AppBskyActor" "Profile" None obj
                Expect.stringContains source "Name: string" "required field is not option"
                Expect.stringContains source "Bio: string option" "optional field is option"

            testCase "record with description generates XML doc" <| fun () ->
                let obj: LexObject =
                    { Description = Some "A user profile"
                      Properties = Map.ofList [ "name", LexType.String { Description = None; Default = None; Const = None; Enum = None; KnownValues = None; Format = None; MinLength = None; MaxLength = None; MinGraphemes = None; MaxGraphemes = None } ]
                      Required = [ "name" ]
                      Nullable = [] }
                let source = generateRecord "AppBskyActor" "Profile" None obj
                Expect.stringContains source "A user profile" "should have description"
        ]

        testList "token generation" [
            testCase "token module generates literal constant" <| fun () ->
                let source = generateToken "app.bsky.feed.defs" "feedViewPost" { Description = Some "A feed view post" }
                Expect.stringContains source "[<Literal>]" "should have Literal attribute"
                Expect.stringContains source "FeedViewPost" "should have PascalCase name"
                Expect.stringContains source "\"app.bsky.feed.defs#feedViewPost\"" "should have NSID#def value"
        ]

        testList "knownValues generation" [
            testCase "knownValues generates module with constants" <| fun () ->
                let source = generateKnownValues "Sort" [ "app.bsky.feed.defs#sortHot"; "app.bsky.feed.defs#sortNew" ]
                Expect.stringContains source "module Sort" "should have module name"
                Expect.stringContains source "SortHot" "should have PascalCase constant"
                Expect.stringContains source "SortNew" "should have PascalCase constant"
        ]
    ]
```

**Step 2: Implement record generation**

The `generateRecord` function should:
1. Create a Fabulous.AST `Record` widget with the given type name
2. For each property in the `LexObject`:
   - Determine the F# type using `TypeMapping.lexTypeToFSharpType`
   - If the property is NOT in `required` OR IS in `nullable`, wrap with `option`
   - Add `[<JsonPropertyName("originalName")>]` attribute
   - PascalCase the field name
3. Add XML docs from description
4. Render via `Gen.mkOak |> Gen.run`

```fsharp
module FSharp.ATProto.CodeGen.TypeGen

open Fabulous.AST
open type Fabulous.AST.Ast
open FSharp.ATProto.Lexicon
open FSharp.ATProto.CodeGen.Naming
open FSharp.ATProto.CodeGen.TypeMapping

/// Generate F# source for a record type from a LexObject.
/// typeName: the PascalCase name for this type
/// description: optional XML doc
/// Returns the generated F# source as a string fragment (just the type, not a full file).
let generateRecord (currentNamespace: string) (typeName: string) (description: string option) (obj: LexObject) : string =
    let fields =
        obj.Properties
        |> Map.toList
        |> List.map (fun (propName, lexType) ->
            let fsharpType = lexTypeToFSharpType currentNamespace lexType
            let isOptional = not (List.contains propName obj.Required)
            let isNullable = List.contains propName obj.Nullable
            let finalType =
                if isOptional || isNullable then sprintf "%s option" fsharpType
                else fsharpType
            (propName, toPascalCase propName, finalType))

    let recordWidget =
        let r =
            Record(typeName) {
                for (origName, pascalName, fsharpType) in fields do
                    Field(pascalName, LongIdent(fsharpType))
                        .attribute(Attribute("JsonPropertyName", ParenExpr(ConstantExpr(String(origName)))))
            }
        match description with
        | Some desc -> r.xmlDocs([desc])
        | None -> r

    let source =
        Oak() { AnonymousModule() { recordWidget } }
        |> Gen.mkOak
        |> Gen.run
    source

/// Generate a token constant (Literal let binding).
let generateToken (nsid: string) (defName: string) (token: LexToken) : string =
    let name = toPascalCase defName
    let value = sprintf "%s#%s" nsid defName
    let binding =
        Value(name, ConstantExpr(String(value)))
            .attribute(Attribute("Literal"))
    match token.Description with
    | Some desc ->
        let b = binding.xmlDocs([desc])
        Oak() { AnonymousModule() { b } } |> Gen.mkOak |> Gen.run
    | None ->
        Oak() { AnonymousModule() { binding } } |> Gen.mkOak |> Gen.run

/// Generate a module with knownValues constants.
let generateKnownValues (fieldName: string) (values: string list) : string =
    let constants =
        values
        |> List.map (fun v ->
            let name =
                // Extract the part after # if present, otherwise use the whole value
                match v.Split('#') with
                | [| _; defPart |] -> toPascalCase defPart
                | _ -> toPascalCase (v.Replace(".", "_"))
            (name, v))
    let source =
        Oak() {
            AnonymousModule() {
                Module(toPascalCase fieldName) {
                    for (name, value) in constants do
                        Value(name, ConstantExpr(String(value)))
                            .attribute(Attribute("Literal"))
                }
            }
        }
        |> Gen.mkOak
        |> Gen.run
    source
```

**Step 3: Run tests, iterate until passing**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.CodeGen.Tests
```

**Important implementation notes:**
- The `Record` widget field order matters. `Map.toList` returns alphabetical order, which is deterministic and good for snapshot stability.
- Fabulous.AST may format records differently depending on field count (inline vs multi-line). Both are fine.
- If `Attribute("JsonPropertyName", ParenExpr(ConstantExpr(String(origName))))` doesn't work exactly, experiment with the Fabulous.AST attribute API. The key shapes are documented in the Fabulous.AST API reference.

**Step 4: Commit**

```bash
git add src/FSharp.ATProto.CodeGen/TypeGen.fs tests/FSharp.ATProto.CodeGen.Tests/TypeGenTests.fs
git commit -m "Add record and token generation via Fabulous.AST"
```

---

## Task 5: Union Generation

**Goal:** Generate F# discriminated unions from `LexUnion`, with JSON serialization attributes for `$type` discrimination.

**Files:**
- Modify: `src/FSharp.ATProto.CodeGen/TypeGen.fs`
- Modify: `tests/FSharp.ATProto.CodeGen.Tests/TypeGenTests.fs`

**Step 1: Write union generation tests**

```fsharp
        testList "union generation" [
            testCase "open union generates DU with Unknown case" <| fun () ->
                let union: LexUnion =
                    { Description = Some "Post embed"
                      Refs = [
                          "app.bsky.embed.images"
                          "app.bsky.embed.external"
                          "app.bsky.embed.record"
                      ]
                      Closed = false }
                let source = generateUnion "AppBskyFeed" "PostEmbed" union
                Expect.stringContains source "type PostEmbed =" "should have type name"
                Expect.stringContains source "Images" "should have Images case"
                Expect.stringContains source "External" "should have External case"
                Expect.stringContains source "Record" "should have Record case"
                Expect.stringContains source "Unknown" "open union should have Unknown case"

            testCase "closed union has no Unknown case" <| fun () ->
                let union: LexUnion =
                    { Description = None
                      Refs = [
                          "app.bsky.embed.images"
                          "app.bsky.embed.external"
                      ]
                      Closed = true }
                let source = generateUnion "AppBskyFeed" "PostEmbed" union
                Expect.isFalse (source.Contains("Unknown")) "closed union should not have Unknown case"

            testCase "union cases have JsonName attributes" <| fun () ->
                let union: LexUnion =
                    { Description = None
                      Refs = [ "app.bsky.embed.images"; "app.bsky.embed.external" ]
                      Closed = true }
                let source = generateUnion "AppBskyFeed" "PostEmbed" union
                Expect.stringContains source "app.bsky.embed.images" "should have NSID in JsonName"
                Expect.stringContains source "app.bsky.embed.external" "should have NSID in JsonName"
        ]
```

**Step 2: Implement union generation**

```fsharp
/// Generate a union case name from a ref string.
/// "app.bsky.embed.images" -> "Images"
/// "app.bsky.embed.images#main" -> "Images"
/// "app.bsky.feed.defs#feedViewPost" -> "FeedViewPost"
let unionCaseName (ref: string) : string =
    match ref.Split('#') with
    | [| nsid; defName |] ->
        if defName = "main" then nsidToModuleName nsid
        else toPascalCase defName
    | [| nsid |] -> nsidToModuleName nsid
    | _ -> failwithf "Invalid ref for union case: %s" ref

/// Generate F# source for a discriminated union from a LexUnion.
let generateUnion (currentNamespace: string) (typeName: string) (union: LexUnion) : string =
    let cases =
        union.Refs
        |> List.map (fun ref ->
            let caseName = unionCaseName ref
            let _targetNs, qualifiedType = refToQualifiedType currentNamespace ref
            // The $type tag value: for "nsid#main" or bare "nsid", use just the nsid
            // For "nsid#defName", use "nsid#defName"
            let tagValue =
                match ref.Split('#') with
                | [| _nsid; "main" |] | [| _ |] -> ref.Split('#').[0]
                | _ -> ref
            (caseName, qualifiedType, tagValue))

    let unionWidget =
        let u =
            Union(typeName) {
                for (caseName, qualifiedType, tagValue) in cases do
                    UnionCase(caseName, [ Field(LongIdent(qualifiedType)) ])
                        .attribute(Attribute("JsonName", ParenExpr(ConstantExpr(String(tagValue)))))
                if not union.Closed then
                    // Unknown case for open unions: Unknown of string * JsonElement
                    // Note: this won't serialize/deserialize correctly with FSharp.SystemTextJson
                    // until Phase 5 adds a custom converter. It's here for type completeness.
                    UnionCase("Unknown", [ Field(String()); Field(LongIdent("JsonElement")) ])
            }
        // Add JsonFSharpConverter attribute for $type discrimination
        let u =
            u.attribute(
                Attribute("JsonFSharpConverter",
                    // This attribute syntax may need adjustment based on Fabulous.AST capabilities.
                    // The goal is: [<JsonFSharpConverter(JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.UnwrapSingleFieldCases, unionTagName = "$type")>]
                    // If Fabulous.AST can't express complex attribute arguments, generate the attribute
                    // as a raw string or use a simpler attribute approach.
                    ParenExpr(ConstantExpr(Constant("JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.UnwrapSingleFieldCases, unionTagName = \"$type\"")))
                )
            )
        match union.Description with
        | Some desc -> u.xmlDocs([desc])
        | None -> u

    Oak() { AnonymousModule() { unionWidget } }
    |> Gen.mkOak
    |> Gen.run
```

**Important:** The `JsonFSharpConverter` attribute argument is complex. Fabulous.AST may not support arbitrary expression trees in attributes. If the attribute generation doesn't work with the DSL, you have two fallback options:

1. **Post-process the generated source**: Use string replacement to insert the attribute
2. **Generate a separate converter type**: Instead of using the attribute, generate a `JsonConverter<T>` class

Test the attribute generation first. If it works, great. If not, fall back to approach 1 (simpler) or 2 (more robust).

**Step 3: Handle union case name conflicts**

Add deduplication logic: if two refs produce the same case name, disambiguate by prepending the namespace:

```fsharp
let deduplicateCaseNames (cases: (string * string * string) list) =
    let names = cases |> List.map (fun (name, _, _) -> name)
    let duplicates = names |> List.groupBy id |> List.filter (fun (_, g) -> g.Length > 1) |> List.map fst |> Set.ofList
    cases |> List.map (fun (name, qualType, tagValue) ->
        if Set.contains name duplicates then
            // Disambiguate by prefixing with namespace module
            let nsModule = tagValue.Split('.') |> Array.toList |> List.rev |> List.skip 1 |> List.head |> toPascalCase
            (nsModule + name, qualType, tagValue)
        else (name, qualType, tagValue))
```

**Step 4: Run tests, commit**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.CodeGen.Tests
git add src/FSharp.ATProto.CodeGen/TypeGen.fs tests/FSharp.ATProto.CodeGen.Tests/TypeGenTests.fs
git commit -m "Add union generation with JsonFSharpConverter attributes"
```

---

## Task 6: Namespace Assembly and Dependency Ordering

**Goal:** Group lexicon docs by namespace, generate complete `.fs` files with proper imports, and order files for compilation.

**Files:**
- Modify: `src/FSharp.ATProto.CodeGen/NamespaceGen.fs`
- Modify: `tests/FSharp.ATProto.CodeGen.Tests/IntegrationTests.fs` (rename from placeholder)

**Step 1: Write grouping and ordering tests**

```fsharp
module IntegrationTests

open Expecto
open System.IO
open FSharp.ATProto.Lexicon
open FSharp.ATProto.CodeGen.Naming
open FSharp.ATProto.CodeGen.NamespaceGen

[<Tests>]
let tests =
    testList "Integration" [
        testList "namespace grouping" [
            testCase "groups docs by 3-segment namespace" <| fun () ->
                let docs = [
                    { Lexicon = 1; Id = Nsid.parse "app.bsky.feed.post" |> Result.defaultWith failwith
                      Revision = None; Description = None
                      Defs = Map.ofList [ "main", LexDef.Record { Key = "tid"; Description = None; Record = { Description = None; Properties = Map.empty; Required = []; Nullable = [] } } ] }
                    { Lexicon = 1; Id = Nsid.parse "app.bsky.feed.like" |> Result.defaultWith failwith
                      Revision = None; Description = None
                      Defs = Map.ofList [ "main", LexDef.Record { Key = "tid"; Description = None; Record = { Description = None; Properties = Map.empty; Required = []; Nullable = [] } } ] }
                    { Lexicon = 1; Id = Nsid.parse "com.atproto.repo.strongRef" |> Result.defaultWith failwith
                      Revision = None; Description = None
                      Defs = Map.ofList [ "main", LexDef.DefType (LexType.Object { Description = None; Properties = Map.empty; Required = []; Nullable = [] }) ] }
                ]
                let groups = groupByNamespace docs
                Expect.equal (Map.count groups) 2 "should have 2 namespaces"
                Expect.equal (groups.["AppBskyFeed"].Length) 2 "AppBskyFeed should have 2 docs"
                Expect.equal (groups.["ComAtprotoRepo"].Length) 1 "ComAtprotoRepo should have 1 doc"
        ]

        testList "dependency ordering" [
            testCase "foundation namespaces come first" <| fun () ->
                let deps = Map.ofList [
                    "AppBskyFeed", Set.ofList ["ComAtprotoRepo"; "AppBskyActor"]
                    "AppBskyActor", Set.ofList ["ComAtprotoLabel"]
                    "ComAtprotoRepo", Set.empty
                    "ComAtprotoLabel", Set.empty
                ]
                let order = topologicalSort deps
                let indexOf ns = List.findIndex ((=) ns) order
                Expect.isTrue (indexOf "ComAtprotoRepo" < indexOf "AppBskyFeed") "repo before feed"
                Expect.isTrue (indexOf "ComAtprotoLabel" < indexOf "AppBskyActor") "label before actor"
                Expect.isTrue (indexOf "AppBskyActor" < indexOf "AppBskyFeed") "actor before feed"
        ]

        testList "real lexicons" [
            testCase "all 324 lexicon files parse and generate namespace groups" <| fun () ->
                let lexiconDir = TestHelpers.lexiconDir
                let files = Directory.GetFiles(lexiconDir, "*.json", SearchOption.AllDirectories)
                Expect.isGreaterThan files.Length 300 "should have 300+ lexicon files"

                let docs =
                    files
                    |> Array.choose (fun f ->
                        let json = File.ReadAllText(f)
                        match LexiconParser.parse json with
                        | Ok doc -> Some doc
                        | Error _ -> None)
                    |> Array.toList

                Expect.isGreaterThan docs.Length 300 "should parse 300+ docs"

                let groups = groupByNamespace docs
                Expect.isGreaterThan (Map.count groups) 30 "should have 30+ namespaces"
        ]
    ]
```

**Step 2: Implement namespace grouping**

```fsharp
module FSharp.ATProto.CodeGen.NamespaceGen

open System.IO
open Fabulous.AST
open type Fabulous.AST.Ast
open FSharp.ATProto.Syntax
open FSharp.ATProto.Lexicon
open FSharp.ATProto.CodeGen.Naming
open FSharp.ATProto.CodeGen.TypeMapping
open FSharp.ATProto.CodeGen.TypeGen

/// Group lexicon docs by their namespace (all-but-last NSID segments).
let groupByNamespace (docs: LexiconDoc list) : Map<string, LexiconDoc list> =
    docs
    |> List.groupBy (fun doc -> nsidToNamespace (Nsid.value doc.Id))
    |> Map.ofList
```

**Step 3: Implement topological sort**

```fsharp
/// Topological sort of namespaces by their dependencies.
/// deps: Map from namespace to set of namespaces it depends on.
/// Returns namespaces in compilation order (dependencies first).
let topologicalSort (deps: Map<string, Set<string>>) : string list =
    let mutable visited = Set.empty
    let mutable result = []
    let mutable visiting = Set.empty // cycle detection

    let rec visit ns =
        if Set.contains ns visiting then
            // Cycle detected — break by not revisiting
            ()
        elif not (Set.contains ns visited) then
            visiting <- Set.add ns visiting
            match Map.tryFind ns deps with
            | Some dependencies ->
                for dep in dependencies do
                    visit dep
            | None -> ()
            visiting <- Set.remove ns visiting
            visited <- Set.add ns visited
            result <- ns :: result

    for ns in Map.keys deps do
        visit ns

    List.rev result
```

**Step 4: Implement full namespace file generation**

This is the core function that assembles a complete `.fs` file for a namespace:

```fsharp
/// Collect all cross-namespace dependencies for a list of docs in a namespace.
let collectDependencies (nsName: string) (docs: LexiconDoc list) : Set<string> =
    docs
    |> List.collect (fun doc ->
        doc.Defs
        |> Map.values
        |> Seq.toList
        |> List.collect (fun def ->
            match def with
            | LexDef.Record r -> collectObjectDeps nsName r.Record
            | LexDef.DefType (LexType.Object obj) -> collectObjectDeps nsName obj
            | LexDef.DefType (LexType.Union u) -> collectUnionDeps nsName u
            | LexDef.Query q -> collectQueryDeps nsName q
            | LexDef.Procedure p -> collectProcedureDeps nsName p
            | LexDef.Subscription s -> collectSubscriptionDeps nsName s
            | _ -> []))
    |> Set.ofList

// Helper functions to collect deps from each def type
// (Implement these by walking the LexType trees and collecting cross-namespace refs)

/// Generate a complete .fs file for a namespace.
/// Returns the file content as a string.
let generateNamespaceFile (nsName: string) (docs: LexiconDoc list) : string =
    // 1. Collect cross-namespace dependencies for open statements
    let deps = collectDependencies nsName docs

    // 2. Generate the Oak with namespace, opens, and all type modules
    let source =
        Oak() {
            (Namespace(fullNamespace nsName) {
                // Standard opens
                Open("System.Text.Json")
                Open("System.Text.Json.Serialization")

                // Cross-namespace opens
                for dep in deps |> Set.toList |> List.sort do
                    Open(fullNamespace dep)

                // For each doc in this namespace, generate a module with its types
                for doc in docs |> List.sortBy (fun d -> Nsid.value d.Id) do
                    let moduleName = nsidToModuleName (Nsid.value doc.Id)
                    let nsid = Nsid.value doc.Id

                    Module(moduleName) {
                        // Generate TypeId constant for record-type main defs
                        match Map.tryFind "main" doc.Defs with
                        | Some (LexDef.Record _) ->
                            Value(ConstantPat(Constant("TypeId")), ConstantExpr(String(nsid)))
                                .attribute(Attribute("Literal"))
                        | _ -> ()

                        // Generate types for each def
                        for KeyValue(defName, def) in doc.Defs do
                            let typeName = defToTypeName moduleName defName
                            match def with
                            | LexDef.Record r ->
                                // The record object type
                                yield! generateRecordWidget nsName typeName r.Record.Description r.Record
                            | LexDef.DefType (LexType.Object obj) ->
                                yield! generateRecordWidget nsName typeName obj.Description obj
                            | LexDef.DefType (LexType.Union u) ->
                                yield! generateUnionWidget nsName typeName u
                            | LexDef.Token t ->
                                yield! generateTokenWidget nsid defName t
                            | LexDef.DefType (LexType.String s) when s.KnownValues.IsSome ->
                                // String with knownValues: generate module with constants
                                yield! generateKnownValuesWidget typeName s.KnownValues.Value
                            | LexDef.Query q ->
                                // Generate Params and Output types if they have schemas
                                yield! generateQueryTypes nsName q
                            | LexDef.Procedure p ->
                                yield! generateProcedureTypes nsName p
                            | LexDef.Subscription s ->
                                yield! generateSubscriptionTypes nsName s
                            | _ -> ()
                    }
            }).toRecursive() // namespace rec for mutual references
        }
        |> Gen.mkOak
        |> Gen.run
    source
```

**Important note on Fabulous.AST integration:** The code above is pseudocode showing the intended structure. The actual implementation will need to work within Fabulous.AST's computation expression constraints. Key challenges:

1. **Yielding different widget types**: The computation expression for `Module()` may not support yielding both `Record` and `Union` widgets in the same builder. You may need to use `AnonymousModule()` instead or restructure.

2. **Conditional generation**: `if/then` and `match` inside computation expressions need to yield the same widget type. Use helper functions that return widget builders.

3. **Alternative approach**: If assembling widgets inside a single Oak computation is too constrained, generate each type as a separate string via `Gen.mkOak |> Gen.run`, then concatenate all the type strings with proper namespace/module wrapping via string templates. This is less elegant but more flexible.

The test ("all 324 lexicons parse and generate") validates the end result regardless of approach.

**Step 5: Implement the complete generation pipeline**

```fsharp
/// Generate all namespace files and return them in compilation order.
/// Returns: list of (fileName, content) pairs in dependency order.
let generateAll (docs: LexiconDoc list) : (string * string) list =
    let groups = groupByNamespace docs
    let deps =
        groups
        |> Map.map (fun nsName nsDocs -> collectDependencies nsName nsDocs)
    let order = topologicalSort deps

    order
    |> List.map (fun nsName ->
        let fileName = nsidToFileName nsName
        let content = generateNamespaceFile nsName groups.[nsName]
        (fileName, content))
```

**Step 6: Run tests, commit**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.CodeGen.Tests
git add src/FSharp.ATProto.CodeGen/NamespaceGen.fs tests/FSharp.ATProto.CodeGen.Tests/IntegrationTests.fs
git commit -m "Add namespace assembly and dependency ordering"
```

---

## Task 7: CLI Entry Point and End-to-End Validation

**Goal:** Wire up the CLI, run the generator against all 324 lexicons, write output to the Bluesky project, update the `.fsproj`, and verify the generated code compiles.

**Files:**
- Modify: `src/FSharp.ATProto.CodeGen/Program.fs`
- Modify: `src/FSharp.ATProto.Bluesky/FSharp.ATProto.Bluesky.fsproj` (add generated Compile entries)
- Create: `src/FSharp.ATProto.Bluesky/Generated/` directory with generated `.fs` files
- Modify: `tests/FSharp.ATProto.CodeGen.Tests/IntegrationTests.fs` (add E2E test)

**Step 1: Implement CLI argument parsing**

```fsharp
module FSharp.ATProto.CodeGen.Program

open System
open System.IO
open FSharp.ATProto.Lexicon
open FSharp.ATProto.CodeGen.NamespaceGen

type Args =
    { LexDir: string
      OutDir: string }

let parseArgs (args: string[]) : Result<Args, string> =
    let mutable lexDir = None
    let mutable outDir = None
    let mutable i = 0
    while i < args.Length do
        match args.[i] with
        | "--lexdir" when i + 1 < args.Length ->
            lexDir <- Some args.[i + 1]
            i <- i + 2
        | "--outdir" when i + 1 < args.Length ->
            outDir <- Some args.[i + 1]
            i <- i + 2
        | other ->
            i <- i + 1
    match lexDir, outDir with
    | Some l, Some o -> Ok { LexDir = l; OutDir = o }
    | _ -> Error "Usage: codegen --lexdir <path> --outdir <path>"

[<EntryPoint>]
let main args =
    match parseArgs args with
    | Error msg ->
        eprintfn "%s" msg
        1
    | Ok config ->
        // 1. Find all JSON files
        let files = Directory.GetFiles(config.LexDir, "*.json", SearchOption.AllDirectories)
        printfn "Found %d lexicon files" files.Length

        // 2. Parse all lexicons
        let docs =
            files
            |> Array.choose (fun f ->
                let json = File.ReadAllText(f)
                match LexiconParser.parse json with
                | Ok doc -> Some doc
                | Error e ->
                    eprintfn "Warning: Failed to parse %s: %s" f e
                    None)
            |> Array.toList
        printfn "Parsed %d lexicon docs" docs.Length

        // 3. Generate all namespace files
        let generated = generateAll docs
        printfn "Generated %d namespace files" generated.Length

        // 4. Write output files
        Directory.CreateDirectory(config.OutDir) |> ignore
        for (fileName, content) in generated do
            let path = Path.Combine(config.OutDir, fileName)
            File.WriteAllText(path, content)
            printfn "  Wrote %s" fileName

        // 5. Print fsproj Compile entries in dependency order
        printfn "\nAdd these to your .fsproj in order:"
        for (fileName, _) in generated do
            printfn "    <Compile Include=\"Generated/%s\" />" fileName

        0
```

**Step 2: Run the generator**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project src/FSharp.ATProto.CodeGen -- \
  --lexdir extern/atproto/lexicons \
  --outdir src/FSharp.ATProto.Bluesky/Generated
```

**Step 3: Update Bluesky .fsproj with generated files**

Take the list of `<Compile Include>` entries from the CLI output and add them to `src/FSharp.ATProto.Bluesky/FSharp.ATProto.Bluesky.fsproj` in the generated order.

**Step 4: Verify compilation**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet build src/FSharp.ATProto.Bluesky
```

This is the critical test. If the generated code compiles, the types are structurally valid. Fix any compilation errors — common issues:
- Missing `open` statements
- Type name conflicts
- Circular references not handled by `namespace rec`
- Invalid F# identifiers (reserved words used as type/field names)
- Fabulous.AST formatting issues

**Step 5: Add compilation test to test suite**

```fsharp
        testCase "generated code compiles" <| fun () ->
            // This test verifies that the code generator produces valid F# code.
            // We parse all lexicons, generate, write to a temp dir, create a project, and build.
            let docs = parseAllLexicons ()
            let generated = generateAll docs

            let tempDir = Path.Combine(Path.GetTempPath(), "codegen-test-" + Guid.NewGuid().ToString("N").[..7])
            let genDir = Path.Combine(tempDir, "Generated")
            Directory.CreateDirectory(genDir) |> ignore

            try
                // Write generated files
                for (fileName, content) in generated do
                    File.WriteAllText(Path.Combine(genDir, fileName), content)

                // Create a test .fsproj
                let compileEntries =
                    generated
                    |> List.map (fun (f, _) -> sprintf "    <Compile Include=\"Generated/%s\" />" f)
                    |> String.concat "\n"

                let fsproj = sprintf """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
%s
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="%s" />
    <ProjectReference Include="%s" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="FSharp.SystemTextJson" Version="1.4.36" />
  </ItemGroup>
</Project>""" compileEntries
                        (Path.GetFullPath("src/FSharp.ATProto.Syntax/FSharp.ATProto.Syntax.fsproj"))
                        (Path.GetFullPath("src/FSharp.ATProto.DRISL/FSharp.ATProto.DRISL.fsproj"))

                File.WriteAllText(Path.Combine(tempDir, "CompileTest.fsproj"), fsproj)

                // Build
                let psi = System.Diagnostics.ProcessStartInfo("dotnet", "build")
                psi.WorkingDirectory <- tempDir
                psi.RedirectStandardOutput <- true
                psi.RedirectStandardError <- true
                psi.UseShellExecute <- false

                // Set PATH for .NET
                let path = Environment.GetEnvironmentVariable("HOME") + "/.dotnet:" + Environment.GetEnvironmentVariable("PATH")
                psi.Environment.["PATH"] <- path

                let proc = System.Diagnostics.Process.Start(psi)
                let stdout = proc.StandardOutput.ReadToEnd()
                let stderr = proc.StandardError.ReadToEnd()
                proc.WaitForExit()

                Expect.equal proc.ExitCode 0
                    (sprintf "Generated code should compile.\nstdout: %s\nstderr: %s" stdout stderr)
            finally
                // Cleanup
                try Directory.Delete(tempDir, true) with _ -> ()
```

**Step 6: Run all tests**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.CodeGen.Tests
```

**Step 7: Final commit**

```bash
git add src/FSharp.ATProto.CodeGen/ src/FSharp.ATProto.Bluesky/ tests/FSharp.ATProto.CodeGen.Tests/
git commit -m "Complete Phase 4: code generator with CLI and generated Bluesky types"
```

---

## Implementation Notes

### Fabulous.AST Tips

Required opens:
```fsharp
open Fabulous.AST
open type Fabulous.AST.Ast
```

Key patterns:
```fsharp
// Record with JsonPropertyName
Record("Post") {
    Field("Text", String())
        .attribute(Attribute("JsonPropertyName", ParenExpr(ConstantExpr(String("text")))))
}

// Union with named fields
Union("Embed") {
    UnionCase("Images", [ Field(LongIdent("Images.Images")) ])
}

// Module with Literal constants
Module("TokenValues") {
    Value("MyToken", ConstantExpr(String("value")))
        .attribute(Attribute("Literal"))
}

// Render to string
Oak() { AnonymousModule() { ... } } |> Gen.mkOak |> Gen.run
```

### Reserved Word Handling

F# has reserved words that might appear as NSID segments or def names. If a generated name matches a reserved word, escape it with double backticks:

```fsharp
let fsharpReservedWords = Set.ofList [
    "type"; "module"; "namespace"; "open"; "let"; "in"; "do"; "if"; "then"; "else"
    "match"; "with"; "for"; "while"; "true"; "false"; "null"; "and"; "or"; "not"
    "begin"; "end"; "done"; "rec"; "mutable"; "lazy"; "abstract"; "class"; "struct"
    "interface"; "override"; "default"; "member"; "static"; "val"; "new"; "as"
    "base"; "global"; "void"; "of"; "to"; "use"; "yield"; "return"; "fun"
]

let escapeReservedWord (name: string) =
    if Set.contains (name.ToLowerInvariant()) fsharpReservedWords then
        sprintf "``%s``" name
    else name
```

### Inline Objects and Unions

When a `LexType.Object` or `LexType.Union` appears as a field type (not a top-level def), extract it into a named type within the same module. Name it by combining the parent type name and field name:

- Field `reply` of type `Object` in record `Post` -> generate type `PostReply`
- Field `embed` of type `Union` in record `Post` -> generate type `PostEmbed`

This requires a pre-processing pass over each doc's defs to extract inline types before generating the module.

### Blob Type

The `Blob` type references `BlobRef` from the DRISL layer. The generated code needs `open FSharp.ATProto.DRISL` (or wherever BlobRef is defined). Check the actual module path and add it to the standard opens.

If BlobRef isn't directly accessible, use a simpler representation:
```fsharp
type Blob =
    { [<JsonPropertyName("$type")>] Type: string
      [<JsonPropertyName("ref")>] Ref: BlobLink
      [<JsonPropertyName("mimeType")>] MimeType: string
      [<JsonPropertyName("size")>] Size: int64 }
```

Check the DRISL project's public API to determine the exact type to use.

### Query/Procedure/Subscription Type Generation

For queries:
- `parameters` (LexParams) -> generate `Params` record type
- `output.schema` (LexType, if encoding is `application/json`) -> generate `Output` record type

For procedures:
- `parameters` (LexParams) -> generate `Params` record type (if present)
- `input.schema` (LexType, if encoding is `application/json`) -> generate `Input` record type
- `output.schema` (LexType, if encoding is `application/json`) -> generate `Output` record type

For subscriptions:
- `parameters` (LexParams) -> generate `Params` record type
- `message.schema` (LexUnion) -> generate `Message` union type

Errors: generate a module with `[<Literal>]` constants for each named error.
