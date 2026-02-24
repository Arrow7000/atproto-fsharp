# Phase 1: Project Scaffold & Syntax (Identifiers) Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Set up the multi-project F# solution and implement all AT Protocol identifier types (TID, Handle, DID, NSID, RecordKey, CID, AT-URI) with full interop test coverage.

**Architecture:** Each identifier is a single-case DU with a private constructor and a smart constructor returning `Result<'T, string>`. Validation uses regex patterns from the AT Protocol spec. Tests load valid/invalid strings from the official interop test files.

**Tech Stack:** .NET 9, F#, Expecto 10.2.3, FsCheck 2.16.6, Expecto.FsCheck 10.2.3

---

### Task 1: Create solution and project scaffold

**Files:**
- Create: `FSharp.ATProto.sln`
- Create: `src/FSharp.ATProto.Syntax/FSharp.ATProto.Syntax.fsproj`
- Create: `tests/FSharp.ATProto.Syntax.Tests/FSharp.ATProto.Syntax.Tests.fsproj`
- Create: `.gitignore`

**Step 1: Create .gitignore**

Create `.gitignore` with standard .NET/F# ignores:

```
bin/
obj/
.vs/
.idea/
*.user
*.suo
.ionide/
```

**Step 2: Create the solution and initial projects**

Run from `/Users/aron/dev/atproto-fsharp/`:

```bash
dotnet new sln --name FSharp.ATProto
dotnet new classlib -lang F# -f net9.0 -o src/FSharp.ATProto.Syntax
dotnet new console -lang F# -f net9.0 -o tests/FSharp.ATProto.Syntax.Tests
```

**Step 3: Add projects to solution with solution folders**

```bash
dotnet sln FSharp.ATProto.sln add src/FSharp.ATProto.Syntax/FSharp.ATProto.Syntax.fsproj --solution-folder src
dotnet sln FSharp.ATProto.sln add tests/FSharp.ATProto.Syntax.Tests/FSharp.ATProto.Syntax.Tests.fsproj --solution-folder tests
```

**Step 4: Add project reference and test packages**

```bash
dotnet add tests/FSharp.ATProto.Syntax.Tests/FSharp.ATProto.Syntax.Tests.fsproj reference src/FSharp.ATProto.Syntax/FSharp.ATProto.Syntax.fsproj
dotnet add tests/FSharp.ATProto.Syntax.Tests/FSharp.ATProto.Syntax.Tests.fsproj package Expecto --version 10.2.3
dotnet add tests/FSharp.ATProto.Syntax.Tests/FSharp.ATProto.Syntax.Tests.fsproj package FsCheck --version 2.16.6
dotnet add tests/FSharp.ATProto.Syntax.Tests/FSharp.ATProto.Syntax.Tests.fsproj package Expecto.FsCheck --version 10.2.3
```

**Step 5: Add git submodules for test data**

```bash
git submodule add https://github.com/bluesky-social/atproto-interop-tests.git extern/atproto-interop-tests
```

We only need the interop tests for Phase 1. The full atproto repo (for Lexicons) will be added in Phase 3.

**Step 6: Set up the Syntax library source file**

Delete the template `Library.fs` in `src/FSharp.ATProto.Syntax/` and create a placeholder.

Replace the `<Compile>` items in `src/FSharp.ATProto.Syntax/FSharp.ATProto.Syntax.fsproj` with:

```xml
<Compile Include="Tid.fs" />
```

Create `src/FSharp.ATProto.Syntax/Tid.fs`:

```fsharp
namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

type Tid = private Tid of string

module Tid =
    let value (Tid s) = s
    let parse (s: string) : Result<Tid, string> =
        Error "not implemented"
```

**Step 7: Set up the test project entry point**

Delete the template `Program.fs` in `tests/FSharp.ATProto.Syntax.Tests/`.

Replace the `<Compile>` items in `tests/FSharp.ATProto.Syntax.Tests/FSharp.ATProto.Syntax.Tests.fsproj` with:

```xml
<Compile Include="TestHelpers.fs" />
<Compile Include="TidTests.fs" />
<Compile Include="Main.fs" />
```

Create `tests/FSharp.ATProto.Syntax.Tests/TestHelpers.fs`:

```fsharp
module TestHelpers

open System.IO

/// Load non-empty lines from an interop test file
let loadTestLines (relativePath: string) =
    let basePath =
        // Navigate from test bin output to repo root
        let rec findRoot (dir: DirectoryInfo) =
            if File.Exists(Path.Combine(dir.FullName, "FSharp.ATProto.sln")) then
                dir.FullName
            elif dir.Parent <> null then
                findRoot dir.Parent
            else
                failwith "Could not find solution root"
        findRoot (DirectoryInfo(Directory.GetCurrentDirectory()))
    let fullPath = Path.Combine(basePath, "extern", "atproto-interop-tests", relativePath)
    File.ReadAllLines(fullPath)
    |> Array.filter (fun line -> line.Length > 0 && not (line.StartsWith("#")))
```

Create `tests/FSharp.ATProto.Syntax.Tests/TidTests.fs`:

```fsharp
module TidTests

open Expecto
open FSharp.ATProto.Syntax

let tests =
    testList "Tid" [
        testCase "placeholder" <| fun () ->
            Expect.isError (Tid.parse "test") "should not be implemented yet"
    ]
```

Create `tests/FSharp.ATProto.Syntax.Tests/Main.fs`:

```fsharp
module Main

open Expecto

[<EntryPoint>]
let main args =
    runTestsInAssemblyWithCLIArgs [] args
```

**Step 8: Verify everything builds and the placeholder test passes**

```bash
dotnet build FSharp.ATProto.sln
dotnet run --project tests/FSharp.ATProto.Syntax.Tests
```

Expected: Build succeeds. 1 test passes.

**Step 9: Commit**

```bash
git add -A
git commit -m "Scaffold solution with Syntax library and test project"
```

---

### Task 2: Implement TID parser

**Spec:** https://atproto.com/specs/tid
**Regex from spec:** `^[234567abcdefghij][234567abcdefghijklmnopqrstuvwxyz]{12}$`
**Test data:** `extern/atproto-interop-tests/syntax/tid_syntax_valid.txt`, `tid_syntax_invalid.txt`

**Files:**
- Modify: `src/FSharp.ATProto.Syntax/Tid.fs`
- Modify: `tests/FSharp.ATProto.Syntax.Tests/TidTests.fs`

**Step 1: Write the failing tests**

Replace `tests/FSharp.ATProto.Syntax.Tests/TidTests.fs` with:

```fsharp
module TidTests

open Expecto
open FSharp.ATProto.Syntax

let validTids = TestHelpers.loadTestLines "syntax/tid_syntax_valid.txt"
let invalidTids = TestHelpers.loadTestLines "syntax/tid_syntax_invalid.txt"

let tests =
    testList "Tid" [
        testList "valid TIDs parse successfully" [
            for tid in validTids do
                testCase tid <| fun () ->
                    Expect.isOk (Tid.parse tid) (sprintf "should parse: %s" tid)
        ]
        testList "invalid TIDs are rejected" [
            for tid in invalidTids do
                testCase tid <| fun () ->
                    Expect.isError (Tid.parse tid) (sprintf "should reject: %s" tid)
        ]
        testList "roundtrip" [
            for tid in validTids do
                testCase (sprintf "roundtrip %s" tid) <| fun () ->
                    let parsed = Tid.parse tid |> Result.defaultWith failwith
                    Expect.equal (Tid.value parsed) tid "roundtrip should preserve value"
        ]
    ]
```

**Step 2: Run tests to verify they fail**

```bash
dotnet run --project tests/FSharp.ATProto.Syntax.Tests
```

Expected: Valid TID tests fail (parse returns Error for everything).

**Step 3: Implement TID parser**

Replace `src/FSharp.ATProto.Syntax/Tid.fs` with:

```fsharp
namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

type Tid = private Tid of string

module Tid =
    let private pattern = Regex(@"^[234567abcdefghij][234567abcdefghijklmnopqrstuvwxyz]{12}$", RegexOptions.Compiled)

    let value (Tid s) = s

    let parse (s: string) : Result<Tid, string> =
        if isNull s then
            Error "TID cannot be null"
        elif s.Length <> 13 then
            Error (sprintf "TID must be exactly 13 characters, got %d" s.Length)
        elif not (pattern.IsMatch(s)) then
            Error (sprintf "Invalid TID: %s" s)
        else
            Ok (Tid s)
```

**Step 4: Run tests to verify they pass**

```bash
dotnet run --project tests/FSharp.ATProto.Syntax.Tests
```

Expected: All tests pass.

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.Syntax/Tid.fs tests/FSharp.ATProto.Syntax.Tests/TidTests.fs
git commit -m "Implement TID parser with interop tests"
```

---

### Task 3: Implement Handle parser

**Spec:** https://atproto.com/specs/handle
**Regex from spec:** `^([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$`
**Additional constraints:** Max 253 chars. Disallowed TLDs: `.alt`, `.arpa`, `.example`, `.internal`, `.invalid`, `.local`, `.localhost`, `.onion`
**Test data:** `syntax/handle_syntax_valid.txt`, `syntax/handle_syntax_invalid.txt`

**Files:**
- Create: `src/FSharp.ATProto.Syntax/Handle.fs`
- Create: `tests/FSharp.ATProto.Syntax.Tests/HandleTests.fs`
- Modify: `src/FSharp.ATProto.Syntax/FSharp.ATProto.Syntax.fsproj` (add `Handle.fs` to `<Compile>`)
- Modify: `tests/FSharp.ATProto.Syntax.Tests/FSharp.ATProto.Syntax.Tests.fsproj` (add `HandleTests.fs` to `<Compile>`)

**Step 1: Add files to project and write failing tests**

Add `<Compile Include="Handle.fs" />` after `Tid.fs` in the Syntax .fsproj.
Add `<Compile Include="HandleTests.fs" />` after `TidTests.fs` in the Tests .fsproj.

Create `tests/FSharp.ATProto.Syntax.Tests/HandleTests.fs`:

```fsharp
module HandleTests

open Expecto
open FSharp.ATProto.Syntax

let validHandles = TestHelpers.loadTestLines "syntax/handle_syntax_valid.txt"
let invalidHandles = TestHelpers.loadTestLines "syntax/handle_syntax_invalid.txt"

let tests =
    testList "Handle" [
        testList "valid handles parse successfully" [
            for h in validHandles do
                testCase h <| fun () ->
                    Expect.isOk (Handle.parse h) (sprintf "should parse: %s" h)
        ]
        testList "invalid handles are rejected" [
            for h in invalidHandles do
                testCase h <| fun () ->
                    Expect.isError (Handle.parse h) (sprintf "should reject: %s" h)
        ]
        testList "roundtrip" [
            for h in validHandles do
                testCase (sprintf "roundtrip %s" h) <| fun () ->
                    let parsed = Handle.parse h |> Result.defaultWith failwith
                    Expect.equal (Handle.value parsed) h "roundtrip should preserve value"
        ]
    ]
```

**Step 2: Run tests to verify they fail**

```bash
dotnet run --project tests/FSharp.ATProto.Syntax.Tests
```

**Step 3: Implement Handle parser**

Create `src/FSharp.ATProto.Syntax/Handle.fs`:

```fsharp
namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

type Handle = private Handle of string

module Handle =
    let private pattern =
        Regex(@"^([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$", RegexOptions.Compiled)

    let private disallowedTlds =
        set [ ".alt"; ".arpa"; ".example"; ".internal"; ".invalid"; ".local"; ".localhost"; ".onion" ]

    let value (Handle s) = s

    let parse (s: string) : Result<Handle, string> =
        if isNull s then
            Error "Handle cannot be null"
        elif s.Length > 253 then
            Error (sprintf "Handle exceeds max length of 253: %d" s.Length)
        elif not (pattern.IsMatch(s)) then
            Error (sprintf "Invalid handle syntax: %s" s)
        else
            Ok (Handle s)
```

Note: The spec distinguishes between syntax validation and registration validation. Disallowed TLDs are a registration-level check. The interop test vectors for syntax validation do not test TLD restrictions, so we only apply the regex + length check here. A separate `Handle.isDisallowedTld` function can be added for registration-level validation if needed.

**Step 4: Run tests to verify they pass**

```bash
dotnet run --project tests/FSharp.ATProto.Syntax.Tests
```

Expected: All tests pass. If any fail, inspect the specific test case and adjust. The regex is from the spec so it should match. Common issues: the test file may contain handles with characters outside the regex, or edge cases around segment length.

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.Syntax/Handle.fs tests/FSharp.ATProto.Syntax.Tests/HandleTests.fs src/FSharp.ATProto.Syntax/FSharp.ATProto.Syntax.fsproj tests/FSharp.ATProto.Syntax.Tests/FSharp.ATProto.Syntax.Tests.fsproj
git commit -m "Implement Handle parser with interop tests"
```

---

### Task 4: Implement NSID parser

**Spec:** https://atproto.com/specs/nsid
**Regex from spec:** `^[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)+(\.[a-zA-Z]([a-zA-Z0-9]{0,62})?)$`
**Additional constraints:** Max 317 chars total, max 253 chars for domain authority portion.
**Test data:** `syntax/nsid_syntax_valid.txt`, `syntax/nsid_syntax_invalid.txt`

**Files:**
- Create: `src/FSharp.ATProto.Syntax/Nsid.fs`
- Create: `tests/FSharp.ATProto.Syntax.Tests/NsidTests.fs`
- Modify: both .fsproj files (add new .fs files to `<Compile>`)

**Step 1: Add files and write failing tests**

Follow the same pattern as Handle: add `Nsid.fs` to Syntax .fsproj, `NsidTests.fs` to Tests .fsproj.

Create `tests/FSharp.ATProto.Syntax.Tests/NsidTests.fs`:

```fsharp
module NsidTests

open Expecto
open FSharp.ATProto.Syntax

let validNsids = TestHelpers.loadTestLines "syntax/nsid_syntax_valid.txt"
let invalidNsids = TestHelpers.loadTestLines "syntax/nsid_syntax_invalid.txt"

let tests =
    testList "Nsid" [
        testList "valid NSIDs parse successfully" [
            for n in validNsids do
                testCase n <| fun () ->
                    Expect.isOk (Nsid.parse n) (sprintf "should parse: %s" n)
        ]
        testList "invalid NSIDs are rejected" [
            for n in invalidNsids do
                testCase n <| fun () ->
                    Expect.isError (Nsid.parse n) (sprintf "should reject: %s" n)
        ]
        testList "roundtrip" [
            for n in validNsids do
                testCase (sprintf "roundtrip %s" n) <| fun () ->
                    let parsed = Nsid.parse n |> Result.defaultWith failwith
                    Expect.equal (Nsid.value parsed) n "roundtrip should preserve value"
        ]
    ]
```

**Step 2: Run tests to verify they fail**

```bash
dotnet run --project tests/FSharp.ATProto.Syntax.Tests
```

**Step 3: Implement NSID parser**

Create `src/FSharp.ATProto.Syntax/Nsid.fs`:

```fsharp
namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

type Nsid = private Nsid of string

module Nsid =
    let private pattern =
        Regex(@"^[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)+(\.[a-zA-Z]([a-zA-Z0-9]{0,62})?)$", RegexOptions.Compiled)

    let value (Nsid s) = s

    let parse (s: string) : Result<Nsid, string> =
        if isNull s then
            Error "NSID cannot be null"
        elif s.Length > 317 then
            Error (sprintf "NSID exceeds max length of 317: %d" s.Length)
        elif not (pattern.IsMatch(s)) then
            Error (sprintf "Invalid NSID syntax: %s" s)
        else
            // Check domain authority length (everything except last segment)
            let lastDot = s.LastIndexOf('.')
            let authority = s.Substring(0, lastDot)
            if authority.Length > 253 then
                Error (sprintf "NSID authority exceeds max length of 253: %d" authority.Length)
            else
                Ok (Nsid s)

    /// Extract the domain authority portion (reversed domain, all segments except last)
    let authority (Nsid s) =
        let lastDot = s.LastIndexOf('.')
        s.Substring(0, lastDot)

    /// Extract the name portion (last segment)
    let name (Nsid s) =
        let lastDot = s.LastIndexOf('.')
        s.Substring(lastDot + 1)
```

**Step 4: Run tests, verify pass**

```bash
dotnet run --project tests/FSharp.ATProto.Syntax.Tests
```

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.Syntax/Nsid.fs tests/FSharp.ATProto.Syntax.Tests/NsidTests.fs src/FSharp.ATProto.Syntax/FSharp.ATProto.Syntax.fsproj tests/FSharp.ATProto.Syntax.Tests/FSharp.ATProto.Syntax.Tests.fsproj
git commit -m "Implement NSID parser with interop tests"
```

---

### Task 5: Implement DID parser

**Spec:** https://atproto.com/specs/did
**Regex from spec:** `^did:[a-z]+:[a-zA-Z0-9._:%-]*[a-zA-Z0-9._-]$`
**Additional constraints:** Max 2048 chars. `%` must be followed by exactly two hex digits.
**Test data:** `syntax/did_syntax_valid.txt`, `syntax/did_syntax_invalid.txt`

**Files:**
- Create: `src/FSharp.ATProto.Syntax/Did.fs`
- Create: `tests/FSharp.ATProto.Syntax.Tests/DidTests.fs`
- Modify: both .fsproj files

**Step 1: Add files and write failing tests**

Same pattern. Create `tests/FSharp.ATProto.Syntax.Tests/DidTests.fs`:

```fsharp
module DidTests

open Expecto
open FSharp.ATProto.Syntax

let validDids = TestHelpers.loadTestLines "syntax/did_syntax_valid.txt"
let invalidDids = TestHelpers.loadTestLines "syntax/did_syntax_invalid.txt"

let tests =
    testList "Did" [
        testList "valid DIDs parse successfully" [
            for d in validDids do
                testCase d <| fun () ->
                    Expect.isOk (Did.parse d) (sprintf "should parse: %s" d)
        ]
        testList "invalid DIDs are rejected" [
            for d in invalidDids do
                testCase d <| fun () ->
                    Expect.isError (Did.parse d) (sprintf "should reject: %s" d)
        ]
        testList "roundtrip" [
            for d in validDids do
                testCase (sprintf "roundtrip %s" d) <| fun () ->
                    let parsed = Did.parse d |> Result.defaultWith failwith
                    Expect.equal (Did.value parsed) d "roundtrip should preserve value"
        ]
    ]
```

**Step 2: Run tests to verify they fail**

```bash
dotnet run --project tests/FSharp.ATProto.Syntax.Tests
```

**Step 3: Implement DID parser**

Create `src/FSharp.ATProto.Syntax/Did.fs`:

```fsharp
namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

type Did = private Did of string

module Did =
    let private pattern =
        Regex(@"^did:[a-z]+:[a-zA-Z0-9._:%-]*[a-zA-Z0-9._-]$", RegexOptions.Compiled)

    let value (Did s) = s

    let private hasValidPercentEncoding (s: string) =
        let mutable i = 0
        let mutable valid = true
        while valid && i < s.Length do
            if s.[i] = '%' then
                if i + 2 >= s.Length then
                    valid <- false
                elif not (System.Uri.IsHexDigit(s.[i + 1]) && System.Uri.IsHexDigit(s.[i + 2])) then
                    valid <- false
                else
                    i <- i + 3
            else
                i <- i + 1
        valid

    let parse (s: string) : Result<Did, string> =
        if isNull s then
            Error "DID cannot be null"
        elif s.Length > 2048 then
            Error (sprintf "DID exceeds max length of 2048: %d" s.Length)
        elif not (pattern.IsMatch(s)) then
            Error (sprintf "Invalid DID syntax: %s" s)
        elif not (hasValidPercentEncoding s) then
            Error (sprintf "Invalid percent-encoding in DID: %s" s)
        else
            Ok (Did s)

    /// Extract the DID method (e.g., "plc" from "did:plc:...")
    let method (Did s) =
        let firstColon = s.IndexOf(':')
        let secondColon = s.IndexOf(':', firstColon + 1)
        s.Substring(firstColon + 1, secondColon - firstColon - 1)
```

**Step 4: Run tests, verify pass**

```bash
dotnet run --project tests/FSharp.ATProto.Syntax.Tests
```

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.Syntax/Did.fs tests/FSharp.ATProto.Syntax.Tests/DidTests.fs src/FSharp.ATProto.Syntax/FSharp.ATProto.Syntax.fsproj tests/FSharp.ATProto.Syntax.Tests/FSharp.ATProto.Syntax.Tests.fsproj
git commit -m "Implement DID parser with interop tests"
```

---

### Task 6: Implement RecordKey parser

**Spec:** https://atproto.com/specs/record-key
**Regex:** `^[a-zA-Z0-9._~:@!$&')(*+,;=-]{1,512}$` -- NOTE: check the actual interop test file, the spec character set is `[a-zA-Z0-9._~:-]` (narrower). Use the narrow set from the spec.
**Additional constraints:** Max 512 chars. Reserved values `.` and `..` are rejected.
**Test data:** `syntax/recordkey_syntax_valid.txt`, `syntax/recordkey_syntax_invalid.txt`

**Files:**
- Create: `src/FSharp.ATProto.Syntax/RecordKey.fs`
- Create: `tests/FSharp.ATProto.Syntax.Tests/RecordKeyTests.fs`
- Modify: both .fsproj files

**Step 1: Write failing tests**

Create `tests/FSharp.ATProto.Syntax.Tests/RecordKeyTests.fs`:

```fsharp
module RecordKeyTests

open Expecto
open FSharp.ATProto.Syntax

let validKeys = TestHelpers.loadTestLines "syntax/recordkey_syntax_valid.txt"
let invalidKeys = TestHelpers.loadTestLines "syntax/recordkey_syntax_invalid.txt"

let tests =
    testList "RecordKey" [
        testList "valid record keys parse successfully" [
            for k in validKeys do
                testCase k <| fun () ->
                    Expect.isOk (RecordKey.parse k) (sprintf "should parse: %s" k)
        ]
        testList "invalid record keys are rejected" [
            for k in invalidKeys do
                testCase k <| fun () ->
                    Expect.isError (RecordKey.parse k) (sprintf "should reject: %s" k)
        ]
        testList "roundtrip" [
            for k in validKeys do
                testCase (sprintf "roundtrip %s" k) <| fun () ->
                    let parsed = RecordKey.parse k |> Result.defaultWith failwith
                    Expect.equal (RecordKey.value parsed) k "roundtrip should preserve value"
        ]
    ]
```

**Step 2: Run tests to verify they fail**

```bash
dotnet run --project tests/FSharp.ATProto.Syntax.Tests
```

**Step 3: Implement RecordKey parser**

Create `src/FSharp.ATProto.Syntax/RecordKey.fs`:

```fsharp
namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

type RecordKey = private RecordKey of string

module RecordKey =
    let private pattern =
        Regex(@"^[a-zA-Z0-9._~:-]{1,512}$", RegexOptions.Compiled)

    let value (RecordKey s) = s

    let parse (s: string) : Result<RecordKey, string> =
        if isNull s then
            Error "RecordKey cannot be null"
        elif s = "." || s = ".." then
            Error (sprintf "RecordKey cannot be '%s'" s)
        elif not (pattern.IsMatch(s)) then
            Error (sprintf "Invalid RecordKey: %s" s)
        else
            Ok (RecordKey s)
```

**Step 4: Run tests, verify pass**

```bash
dotnet run --project tests/FSharp.ATProto.Syntax.Tests
```

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.Syntax/RecordKey.fs tests/FSharp.ATProto.Syntax.Tests/RecordKeyTests.fs src/FSharp.ATProto.Syntax/FSharp.ATProto.Syntax.fsproj tests/FSharp.ATProto.Syntax.Tests/FSharp.ATProto.Syntax.Tests.fsproj
git commit -m "Implement RecordKey parser with interop tests"
```

---

### Task 7: Implement CID parser (shallow syntax validation)

**Spec:** CID is a multiformats concept. ATProto uses CIDv1 with base32 encoding.
**Approach:** Shallow syntax validation (regex) matching the Go implementation: `^[a-zA-Z0-9+/=]{8,256}$` plus reject CIDv0 (`Qmb` prefix). Deep CID parsing (multibase, multicodec, multihash) will be implemented in the DRISL layer.
**Test data:** `syntax/cid_syntax_valid.txt`, `syntax/cid_syntax_invalid.txt`

**Files:**
- Create: `src/FSharp.ATProto.Syntax/Cid.fs`
- Create: `tests/FSharp.ATProto.Syntax.Tests/CidTests.fs`
- Modify: both .fsproj files

**Step 1: Write failing tests**

Create `tests/FSharp.ATProto.Syntax.Tests/CidTests.fs`:

```fsharp
module CidTests

open Expecto
open FSharp.ATProto.Syntax

let validCids = TestHelpers.loadTestLines "syntax/cid_syntax_valid.txt"
let invalidCids = TestHelpers.loadTestLines "syntax/cid_syntax_invalid.txt"

let tests =
    testList "Cid" [
        testList "valid CIDs parse successfully" [
            for c in validCids do
                testCase c <| fun () ->
                    Expect.isOk (Cid.parse c) (sprintf "should parse: %s" c)
        ]
        testList "invalid CIDs are rejected" [
            for c in invalidCids do
                testCase c <| fun () ->
                    Expect.isError (Cid.parse c) (sprintf "should reject: %s" c)
        ]
        testList "roundtrip" [
            for c in validCids do
                testCase (sprintf "roundtrip %s" c) <| fun () ->
                    let parsed = Cid.parse c |> Result.defaultWith failwith
                    Expect.equal (Cid.value parsed) c "roundtrip should preserve value"
        ]
    ]
```

**Step 2: Run tests to verify they fail**

```bash
dotnet run --project tests/FSharp.ATProto.Syntax.Tests
```

**Step 3: Implement CID parser**

Create `src/FSharp.ATProto.Syntax/Cid.fs`:

```fsharp
namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

type Cid = private Cid of string

module Cid =
    let private pattern =
        Regex(@"^[a-zA-Z0-9+/=]{8,256}$", RegexOptions.Compiled)

    let value (Cid s) = s

    let parse (s: string) : Result<Cid, string> =
        if isNull s then
            Error "CID cannot be null"
        elif s.StartsWith("Qmb") then
            Error "CIDv0 is not supported"
        elif not (pattern.IsMatch(s)) then
            Error (sprintf "Invalid CID syntax: %s" s)
        else
            Ok (Cid s)

    /// Internal constructor for when CID is computed/validated at a deeper level (DRISL layer)
    let internal fromValidated (s: string) = Cid s
```

Note: `fromValidated` is `internal` so the DRISL layer can construct CIDs after full binary validation, but external consumers must go through `parse`.

**Step 4: Run tests, verify pass**

```bash
dotnet run --project tests/FSharp.ATProto.Syntax.Tests
```

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.Syntax/Cid.fs tests/FSharp.ATProto.Syntax.Tests/CidTests.fs src/FSharp.ATProto.Syntax/FSharp.ATProto.Syntax.fsproj tests/FSharp.ATProto.Syntax.Tests/FSharp.ATProto.Syntax.Tests.fsproj
git commit -m "Implement CID parser (shallow syntax) with interop tests"
```

---

### Task 8: Implement AT-URI parser

**Spec:** https://atproto.com/specs/at-uri-scheme
**Format:** `at://AUTHORITY[/COLLECTION[/RKEY]]`
**Authority:** DID or Handle. **Collection:** NSID. **RKEY:** RecordKey.
**Additional constraints:** Max 8KB. No query/fragment. No trailing slashes.
**Test data:** `syntax/aturi_syntax_valid.txt`, `syntax/aturi_syntax_invalid.txt`

**Files:**
- Create: `src/FSharp.ATProto.Syntax/AtUri.fs`
- Create: `tests/FSharp.ATProto.Syntax.Tests/AtUriTests.fs`
- Modify: both .fsproj files

**Step 1: Write failing tests**

Create `tests/FSharp.ATProto.Syntax.Tests/AtUriTests.fs`:

```fsharp
module AtUriTests

open Expecto
open FSharp.ATProto.Syntax

let validUris = TestHelpers.loadTestLines "syntax/aturi_syntax_valid.txt"
let invalidUris = TestHelpers.loadTestLines "syntax/aturi_syntax_invalid.txt"

let tests =
    testList "AtUri" [
        testList "valid AT-URIs parse successfully" [
            for u in validUris do
                testCase u <| fun () ->
                    Expect.isOk (AtUri.parse u) (sprintf "should parse: %s" u)
        ]
        testList "invalid AT-URIs are rejected" [
            for u in invalidUris do
                testCase u <| fun () ->
                    Expect.isError (AtUri.parse u) (sprintf "should reject: %s" u)
        ]
        testList "roundtrip" [
            for u in validUris do
                testCase (sprintf "roundtrip %s" u) <| fun () ->
                    let parsed = AtUri.parse u |> Result.defaultWith failwith
                    Expect.equal (AtUri.value parsed) u "roundtrip should preserve value"
        ]
    ]
```

**Step 2: Run tests to verify they fail**

```bash
dotnet run --project tests/FSharp.ATProto.Syntax.Tests
```

**Step 3: Implement AT-URI parser**

Create `src/FSharp.ATProto.Syntax/AtUri.fs`:

```fsharp
namespace FSharp.ATProto.Syntax

type AtUriAuthority =
    | DidAuthority of Did
    | HandleAuthority of Handle

type AtUri = private AtUri of string

module AtUri =
    let value (AtUri s) = s

    let parse (s: string) : Result<AtUri, string> =
        if isNull s then
            Error "AT-URI cannot be null"
        elif s.Length > 8192 then
            Error "AT-URI exceeds max length of 8KB"
        elif not (s.StartsWith("at://")) then
            Error "AT-URI must start with 'at://'"
        elif s.Contains('?') || s.Contains('#') then
            Error "AT-URI must not contain query or fragment"
        else
            let rest = s.Substring(5) // strip "at://"
            if rest.Length = 0 then
                Error "AT-URI must have an authority"
            elif rest.EndsWith("/") then
                // Allow "at://authority" but not "at://authority/"
                // unless it's "at://authority/collection" or "at://authority/collection/rkey"
                // Actually: no trailing slash allowed
                Error "AT-URI must not have a trailing slash"
            else
                let parts = rest.Split('/', 3) // at most: authority, collection, rkey
                let authorityStr = parts.[0]
                // Validate authority as DID or Handle
                let authorityResult =
                    if authorityStr.StartsWith("did:") then
                        Did.parse authorityStr |> Result.map DidAuthority
                    else
                        Handle.parse authorityStr |> Result.map HandleAuthority
                match authorityResult with
                | Error e -> Error (sprintf "Invalid AT-URI authority: %s" e)
                | Ok _ ->
                    if parts.Length >= 2 then
                        // Validate collection as NSID
                        match Nsid.parse parts.[1] with
                        | Error e -> Error (sprintf "Invalid AT-URI collection: %s" e)
                        | Ok _ ->
                            if parts.Length >= 3 then
                                // Validate rkey as RecordKey
                                match RecordKey.parse parts.[2] with
                                | Error e -> Error (sprintf "Invalid AT-URI record key: %s" e)
                                | Ok _ -> Ok (AtUri s)
                            else
                                Ok (AtUri s)
                    else
                        Ok (AtUri s)

    /// Extract the authority portion as a DID or Handle
    let authority (AtUri s) : AtUriAuthority =
        let rest = s.Substring(5)
        let authorityStr = rest.Split('/').[0]
        if authorityStr.StartsWith("did:") then
            DidAuthority (Did.parse authorityStr |> Result.defaultWith failwith)
        else
            HandleAuthority (Handle.parse authorityStr |> Result.defaultWith failwith)

    /// Extract the collection NSID, if present
    let collection (AtUri s) : Nsid option =
        let parts = s.Substring(5).Split('/', 3)
        if parts.Length >= 2 && parts.[1].Length > 0 then
            Nsid.parse parts.[1] |> Result.toOption
        else
            None

    /// Extract the record key, if present
    let recordKey (AtUri s) : RecordKey option =
        let parts = s.Substring(5).Split('/', 3)
        if parts.Length >= 3 && parts.[2].Length > 0 then
            RecordKey.parse parts.[2] |> Result.toOption
        else
            None
```

**Step 4: Run tests, verify pass**

```bash
dotnet run --project tests/FSharp.ATProto.Syntax.Tests
```

Expected: All tests pass. If any AT-URI test cases fail, inspect the specific case. Common issues:
- Some valid AT-URIs may have authority-only form (`at://did:plc:asdf`)
- Some invalid cases may test for double slashes, empty segments, etc.
- The trailing-slash check may need refinement based on specific test vectors

If a specific test case fails, read the test line, understand what it's testing, and adjust the parser logic. The component validators (Did, Handle, Nsid, RecordKey) are already verified.

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.Syntax/AtUri.fs tests/FSharp.ATProto.Syntax.Tests/AtUriTests.fs src/FSharp.ATProto.Syntax/FSharp.ATProto.Syntax.fsproj tests/FSharp.ATProto.Syntax.Tests/FSharp.ATProto.Syntax.Tests.fsproj
git commit -m "Implement AT-URI parser with interop tests"
```

---

### Task 9: Add remaining interop-tested syntax types

**Test data:** `syntax/datetime_syntax_valid.txt`, `syntax/datetime_syntax_invalid.txt`, `syntax/atidentifier_syntax_valid.txt`, `syntax/atidentifier_syntax_invalid.txt`, `syntax/language_syntax_valid.txt`, `syntax/language_syntax_invalid.txt`, `syntax/uri_syntax_valid.txt`, `syntax/uri_syntax_invalid.txt`

These are secondary identifier types used in record field validation (not as standalone protocol types). Implement them with the same pattern.

**Files:**
- Create: `src/FSharp.ATProto.Syntax/AtDateTime.fs` -- ATProto datetime format (subset of ISO 8601)
- Create: `src/FSharp.ATProto.Syntax/AtIdentifier.fs` -- union of DID or Handle
- Create: `src/FSharp.ATProto.Syntax/Language.fs` -- BCP 47 language tag
- Create: `src/FSharp.ATProto.Syntax/Uri.fs` -- general URI validation
- Create corresponding test files
- Modify: both .fsproj files

**Step 1: Create each type following the same pattern**

Each type follows the identical pattern:
1. Single-case DU with private constructor
2. `parse` returning `Result<'T, string>`
3. `value` unwrapping the string
4. Tests loading valid/invalid lines from interop test files

For **AtDateTime**: The spec requires a subset of RFC 3339. Pattern: must include date, time with seconds, and timezone (either `Z` or `+HH:MM`/`-HH:MM`). Use `System.DateTimeOffset.TryParse` with `RoundtripKind` as a baseline, plus additional regex constraints from the spec.

For **AtIdentifier**: Simply try `Did.parse`, fall back to `Handle.parse`. This is a union type:
```fsharp
type AtIdentifier =
    | AtDid of Did
    | AtHandle of Handle

module AtIdentifier =
    let parse (s: string) : Result<AtIdentifier, string> =
        match Did.parse s with
        | Ok d -> Ok (AtDid d)
        | Error _ ->
            match Handle.parse s with
            | Ok h -> Ok (AtHandle h)
            | Error _ -> Error (sprintf "Invalid AT Identifier (not a DID or Handle): %s" s)
```

For **Language**: BCP 47 language tag. Regex: `^(i|[a-z]{2,3})(-[a-zA-Z0-9]+)*$`. Max 128 chars.

For **Uri**: General URI. Use `System.Uri.TryCreate` with `UriKind.Absolute`. Max 8KB.

**Step 2: Run all tests**

```bash
dotnet run --project tests/FSharp.ATProto.Syntax.Tests
```

**Step 3: Commit**

```bash
git add -A
git commit -m "Implement DateTime, AtIdentifier, Language, and URI syntax types with interop tests"
```

---

### Task 10: Add FsCheck property-based tests

**Files:**
- Create: `tests/FSharp.ATProto.Syntax.Tests/PropertyTests.fs`
- Modify: `tests/FSharp.ATProto.Syntax.Tests/FSharp.ATProto.Syntax.Tests.fsproj` (add to `<Compile>`)

**Step 1: Write property tests**

Create `tests/FSharp.ATProto.Syntax.Tests/PropertyTests.fs`:

```fsharp
module PropertyTests

open Expecto
open FsCheck
open FSharp.ATProto.Syntax

// Generators for valid instances

let tidCharset = "234567abcdefghijklmnopqrstuvwxyz"
let tidFirstCharset = "234567abcdefghij"

let genTidString =
    gen {
        let! first = Gen.elements (tidFirstCharset |> Seq.toList)
        let! rest = Gen.arrayOfLength 12 (Gen.elements (tidCharset |> Seq.toList))
        return System.String(Array.append [|first|] rest)
    }

let genValidTid = genTidString |> Gen.map (Tid.parse >> Result.defaultWith failwith)

let tests =
    testList "property tests" [
        testList "Tid" [
            testProperty "parse(value(tid)) = Ok tid for valid TIDs" <|
                fun () ->
                    let prop = Prop.forAll (Arb.fromGen genValidTid) (fun tid ->
                        let s = Tid.value tid
                        match Tid.parse s with
                        | Ok t2 -> Tid.value t2 = s
                        | Error _ -> false
                    )
                    prop

            testProperty "arbitrary strings rarely parse as valid TIDs" <|
                fun (s: string) ->
                    // Most random strings should not be valid TIDs
                    // This is a sanity check, not a strict property
                    s = null || s.Length <> 13 || (Tid.parse s |> Result.isError)
                    |> Prop.classify (s <> null && s.Length = 13) "length-13 strings"
        ]

        testList "roundtrip properties" [
            testProperty "valid DID roundtrips" <|
                fun () ->
                    // Generate a simple did:plc:... pattern
                    let prop = Prop.forAll
                        (Arb.fromGen (gen {
                            let! chars = Gen.arrayOfLength 24 (Gen.elements (['a'..'z'] @ ['0'..'9']))
                            return sprintf "did:plc:%s" (System.String(chars))
                        }))
                        (fun s ->
                            match Did.parse s with
                            | Ok d -> Did.value d = s
                            | Error _ -> false)
                    prop
        ]
    ]
```

Note: FsCheck property tests are more useful once we have the DRISL layer (roundtrip encode/decode). For the Syntax layer, the interop test vectors already provide strong coverage. These property tests add confidence for the generator-based roundtrip pattern and catch edge cases the test vectors might miss.

**Step 2: Run all tests including property tests**

```bash
dotnet run --project tests/FSharp.ATProto.Syntax.Tests
```

Expected: All tests pass (interop + properties).

**Step 3: Commit**

```bash
git add tests/FSharp.ATProto.Syntax.Tests/PropertyTests.fs tests/FSharp.ATProto.Syntax.Tests/FSharp.ATProto.Syntax.Tests.fsproj
git commit -m "Add FsCheck property-based tests for syntax types"
```

---

### Task 11: Final verification and cleanup

**Step 1: Run the full test suite**

```bash
dotnet build FSharp.ATProto.sln
dotnet run --project tests/FSharp.ATProto.Syntax.Tests
```

All tests should pass. Count expected: roughly 200+ interop vector tests + property tests.

**Step 2: Verify solution builds clean**

```bash
dotnet build FSharp.ATProto.sln --no-incremental 2>&1
```

Expected: 0 warnings, 0 errors.

**Step 3: Review and commit any cleanup**

Review for any TODO comments, dead code, or inconsistencies. Clean up and commit:

```bash
git add -A
git commit -m "Phase 1 complete: project scaffold and all syntax identifier types"
```

---

## What's Next

After Phase 1 is complete, Phase 2 (DRISL/CBOR encoding) will be planned in a separate document. It will cover:

- `AtpValue` discriminated union for the AT Protocol data model
- DRISL encoder/decoder on top of `System.Formats.Cbor`
- CID computation (SHA-256 + CIDv1 multicodec)
- JSON `$link`/`$bytes` conversion
- Testing against 126 DASL + atproto interop test vectors
- FsCheck roundtrip properties

The DRISL layer depends on the `Cid` type from Syntax (Phase 1), so it must come after.
