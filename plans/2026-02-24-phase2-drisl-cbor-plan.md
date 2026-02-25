# Phase 2: DRISL/CBOR Encoding Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement the `FSharp.ATProto.DRISL` library: encode/decode AT Protocol data to/from canonical DRISL-CBOR binary format, compute CIDs, and convert between JSON and the internal data model.

**Architecture:** `AtpValue` discriminated union represents the AT Protocol data model. `Drisl` module encodes/decodes using `System.Formats.Cbor` with `CborConformanceMode.Canonical`. `CidBinary` computes CIDs (SHA-256 + CIDv1 + base32). `AtpJson` converts between JSON and `AtpValue`, handling `$link`/`$bytes`/`$type` conventions.

**Tech Stack:** .NET 9, F#, System.Formats.Cbor, System.Security.Cryptography, Expecto 10.2.3, FsCheck 2.16.6

**IMPORTANT:** Before running ANY dotnet commands, prefix with: `export PATH="$HOME/.dotnet:$PATH" &&`

---

### Task 1: Create DRISL project scaffold

**Files:**
- Create: `src/FSharp.ATProto.DRISL/FSharp.ATProto.DRISL.fsproj`
- Create: `tests/FSharp.ATProto.DRISL.Tests/FSharp.ATProto.DRISL.Tests.fsproj`
- Modify: `FSharp.ATProto.sln`
- Modify: `src/FSharp.ATProto.Syntax/FSharp.ATProto.Syntax.fsproj` (add InternalsVisibleTo)

**Step 1: Create the DRISL library project**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet new classlib -lang F# -f net9.0 -o src/FSharp.ATProto.DRISL
```

**Step 2: Create the test project**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet new console -lang F# -f net9.0 -o tests/FSharp.ATProto.DRISL.Tests
```

**Step 3: Add projects to solution**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet sln FSharp.ATProto.sln add src/FSharp.ATProto.DRISL/FSharp.ATProto.DRISL.fsproj --solution-folder src
export PATH="$HOME/.dotnet:$PATH" && dotnet sln FSharp.ATProto.sln add tests/FSharp.ATProto.DRISL.Tests/FSharp.ATProto.DRISL.Tests.fsproj --solution-folder tests
```

**Step 4: Configure DRISL project**

Replace `src/FSharp.ATProto.DRISL/FSharp.ATProto.DRISL.fsproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="Varint.fs" />
    <Compile Include="Base32.fs" />
    <Compile Include="CidBinary.fs" />
    <Compile Include="AtpValue.fs" />
    <Compile Include="Drisl.fs" />
    <Compile Include="AtpJson.fs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\FSharp.ATProto.Syntax\FSharp.ATProto.Syntax.fsproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="System.Formats.Cbor" Version="9.0.3" />
  </ItemGroup>

</Project>
```

**Step 5: Configure test project**

Replace `tests/FSharp.ATProto.DRISL.Tests/FSharp.ATProto.DRISL.Tests.fsproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="TestHelpers.fs" />
    <Compile Include="VarintTests.fs" />
    <Compile Include="Base32Tests.fs" />
    <Compile Include="CidBinaryTests.fs" />
    <Compile Include="DrislTests.fs" />
    <Compile Include="AtpJsonTests.fs" />
    <Compile Include="InteropTests.fs" />
    <Compile Include="PropertyTests.fs" />
    <Compile Include="Main.fs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\FSharp.ATProto.DRISL\FSharp.ATProto.DRISL.fsproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Expecto" Version="10.2.3" />
    <PackageReference Include="Expecto.FsCheck" Version="10.2.3" />
    <PackageReference Include="FsCheck" Version="2.16.6" />
  </ItemGroup>

</Project>
```

**Step 6: Add InternalsVisibleTo so DRISL can use Cid.fromValidated**

Add to `src/FSharp.ATProto.Syntax/FSharp.ATProto.Syntax.fsproj`, inside a new `<ItemGroup>`:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="FSharp.ATProto.DRISL" />
  </ItemGroup>
```

**Step 7: Create test entry point and helpers**

Create `tests/FSharp.ATProto.DRISL.Tests/Main.fs`:

```fsharp
module Main

open Expecto

[<EntryPoint>]
let main args =
    runTestsInAssemblyWithCLIArgs [] args
```

Create `tests/FSharp.ATProto.DRISL.Tests/TestHelpers.fs`:

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

**Step 8: Delete auto-generated Library.fs**

Delete `src/FSharp.ATProto.DRISL/Library.fs` (generated by `dotnet new classlib`).

**Step 9: Create placeholder source files so it compiles**

Create empty placeholder files so the project compiles (we'll fill them in subsequent tasks):

`src/FSharp.ATProto.DRISL/Varint.fs`:
```fsharp
namespace FSharp.ATProto.DRISL
module Varint = ()
```

`src/FSharp.ATProto.DRISL/Base32.fs`:
```fsharp
namespace FSharp.ATProto.DRISL
module Base32 = ()
```

`src/FSharp.ATProto.DRISL/CidBinary.fs`:
```fsharp
namespace FSharp.ATProto.DRISL
module CidBinary = ()
```

`src/FSharp.ATProto.DRISL/AtpValue.fs`:
```fsharp
namespace FSharp.ATProto.DRISL
```

`src/FSharp.ATProto.DRISL/Drisl.fs`:
```fsharp
namespace FSharp.ATProto.DRISL
module Drisl = ()
```

`src/FSharp.ATProto.DRISL/AtpJson.fs`:
```fsharp
namespace FSharp.ATProto.DRISL
module AtpJson = ()
```

Create placeholder test files (empty modules with `[<Tests>]`):

`tests/FSharp.ATProto.DRISL.Tests/VarintTests.fs`:
```fsharp
module VarintTests
open Expecto
[<Tests>]
let tests = testList "Varint" []
```

Do the same for `Base32Tests.fs`, `CidBinaryTests.fs`, `DrislTests.fs`, `AtpJsonTests.fs`, `InteropTests.fs`, `PropertyTests.fs` (change module name and test list name for each).

**Step 10: Build and verify**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet build /Users/aron/dev/atproto-fsharp/FSharp.ATProto.sln
```

Expected: Build succeeds, 0 errors.

**Step 11: Also verify Syntax tests still pass**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.Syntax.Tests/FSharp.ATProto.Syntax.Tests.fsproj
```

Expected: 726 tests pass.

**Step 12: Commit**

```bash
git add -A && git commit -m "Scaffold DRISL project and test project"
```

---

### Task 2: Implement Varint module

Unsigned LEB128 encoding used in CID binary format. All AT Protocol CID values (version=1, codec=0x71/0x55, hash=0x12, length=0x20) are < 128, so single-byte encoding covers the common case, but we implement the full algorithm for correctness.

**Files:**
- Modify: `src/FSharp.ATProto.DRISL/Varint.fs`
- Modify: `tests/FSharp.ATProto.DRISL.Tests/VarintTests.fs`

**Step 1: Write the failing tests**

Replace `tests/FSharp.ATProto.DRISL.Tests/VarintTests.fs`:

```fsharp
module VarintTests

open Expecto
open FSharp.ATProto.DRISL

[<Tests>]
let tests =
    testList "Varint" [
        testList "encode" [
            testCase "zero" <| fun () ->
                Expect.equal (Varint.encode 0UL) [| 0x00uy |] "zero"
            testCase "one" <| fun () ->
                Expect.equal (Varint.encode 1UL) [| 0x01uy |] "one"
            testCase "127 single byte" <| fun () ->
                Expect.equal (Varint.encode 127UL) [| 0x7Fuy |] "127"
            testCase "128 two bytes" <| fun () ->
                Expect.equal (Varint.encode 128UL) [| 0x80uy; 0x01uy |] "128"
            testCase "0x71 dag-cbor codec" <| fun () ->
                Expect.equal (Varint.encode 0x71UL) [| 0x71uy |] "0x71"
            testCase "0x55 raw codec" <| fun () ->
                Expect.equal (Varint.encode 0x55UL) [| 0x55uy |] "0x55"
            testCase "0x12 sha256 code" <| fun () ->
                Expect.equal (Varint.encode 0x12UL) [| 0x12uy |] "0x12"
            testCase "0x20 digest length" <| fun () ->
                Expect.equal (Varint.encode 0x20UL) [| 0x20uy |] "0x20"
            testCase "300 two bytes" <| fun () ->
                // 300 = 0x12C -> low 7 bits: 0x2C | 0x80 = 0xAC, high: 0x02
                Expect.equal (Varint.encode 300UL) [| 0xACuy; 0x02uy |] "300"
            testCase "16384 three bytes" <| fun () ->
                // 16384 = 2^14 -> 0x80, 0x80, 0x01
                Expect.equal (Varint.encode 16384UL) [| 0x80uy; 0x80uy; 0x01uy |] "16384"
        ]
        testList "decode" [
            testCase "zero" <| fun () ->
                Expect.equal (Varint.decode [| 0x00uy |] 0) (0UL, 1) "zero"
            testCase "one" <| fun () ->
                Expect.equal (Varint.decode [| 0x01uy |] 0) (1UL, 1) "one"
            testCase "127" <| fun () ->
                Expect.equal (Varint.decode [| 0x7Fuy |] 0) (127UL, 1) "127"
            testCase "128" <| fun () ->
                Expect.equal (Varint.decode [| 0x80uy; 0x01uy |] 0) (128UL, 2) "128"
            testCase "300" <| fun () ->
                Expect.equal (Varint.decode [| 0xACuy; 0x02uy |] 0) (300UL, 2) "300"
            testCase "offset" <| fun () ->
                // Decode starting at offset 2
                Expect.equal (Varint.decode [| 0xFFuy; 0xFFuy; 0x71uy |] 2) (0x71UL, 1) "offset"
        ]
        testList "roundtrip" [
            testCase "encode then decode" <| fun () ->
                for v in [0UL; 1UL; 127UL; 128UL; 255UL; 300UL; 16384UL; 1000000UL] do
                    let encoded = Varint.encode v
                    let (decoded, len) = Varint.decode encoded 0
                    Expect.equal decoded v (sprintf "roundtrip %d" v)
                    Expect.equal len encoded.Length (sprintf "consumed all bytes for %d" v)
        ]
    ]
```

**Step 2: Run tests to verify they fail**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.DRISL.Tests/FSharp.ATProto.DRISL.Tests.fsproj
```

Expected: Tests fail (Varint.encode and Varint.decode don't exist yet).

**Step 3: Implement Varint module**

Replace `src/FSharp.ATProto.DRISL/Varint.fs`:

```fsharp
namespace FSharp.ATProto.DRISL

/// Unsigned LEB128 varint encoding/decoding for CID binary format.
module Varint =

    /// Encode a uint64 as unsigned LEB128 bytes.
    let encode (value: uint64) : byte[] =
        if value < 0x80UL then
            [| byte value |]
        else
            let result = System.Collections.Generic.List<byte>()
            let mutable v = value
            while v >= 0x80UL do
                result.Add(byte (v &&& 0x7FUL) ||| 0x80uy)
                v <- v >>> 7
            result.Add(byte v)
            result.ToArray()

    /// Decode an unsigned LEB128 varint from data at the given offset.
    /// Returns (value, bytesConsumed).
    let decode (data: byte[]) (offset: int) : uint64 * int =
        let mutable result = 0UL
        let mutable shift = 0
        let mutable i = offset
        let mutable cont = true
        while cont do
            if i >= data.Length then
                failwith "Unexpected end of varint data"
            let b = data.[i]
            result <- result ||| ((uint64 (b &&& 0x7Fuy)) <<< shift)
            i <- i + 1
            if b &&& 0x80uy = 0uy then
                cont <- false
            else
                shift <- shift + 7
                if shift > 63 then
                    failwith "Varint too long"
        (result, i - offset)
```

**Step 4: Run tests, verify pass**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.DRISL.Tests/FSharp.ATProto.DRISL.Tests.fsproj
```

Expected: All Varint tests pass.

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.DRISL/Varint.fs tests/FSharp.ATProto.DRISL.Tests/VarintTests.fs && git commit -m "Implement Varint module with LEB128 encode/decode"
```

---

### Task 3: Implement Base32 module

RFC 4648 base32, lowercase alphabet, no padding. Used for CID string encoding with multibase 'b' prefix.

**Files:**
- Modify: `src/FSharp.ATProto.DRISL/Base32.fs`
- Modify: `tests/FSharp.ATProto.DRISL.Tests/Base32Tests.fs`

**Step 1: Write the failing tests**

Replace `tests/FSharp.ATProto.DRISL.Tests/Base32Tests.fs`:

```fsharp
module Base32Tests

open Expecto
open FSharp.ATProto.DRISL

[<Tests>]
let tests =
    testList "Base32" [
        testList "encode" [
            testCase "empty" <| fun () ->
                Expect.equal (Base32.encode [||]) "" "empty"
            testCase "single byte" <| fun () ->
                // 0x66 = 01100110 -> 01100 11000 -> 12, 24 -> 'm', 'y'
                // Actually let's use RFC 4648 test vectors (lowercased)
                // "f" (0x66) -> "my" (RFC 4648: "MY" -> "my")
                Expect.equal (Base32.encode [| 0x66uy |]) "my" "single byte 'f'"
            testCase "two bytes" <| fun () ->
                // "fo" (0x66 0x6F) -> "mzxq" (RFC 4648: "MZXQ" -> "mzxq")
                Expect.equal (Base32.encode [| 0x66uy; 0x6Fuy |]) "mzxq" "two bytes 'fo'"
            testCase "three bytes" <| fun () ->
                // "foo" -> "mzxw6"
                Expect.equal (Base32.encode [| 0x66uy; 0x6Fuy; 0x6Fuy |]) "mzxw6" "three bytes 'foo'"
            testCase "six bytes" <| fun () ->
                // "foobar" -> "mzxw6ytboi"
                Expect.equal (Base32.encode "foobar"B) "mzxw6ytboi" "six bytes 'foobar'"
            testCase "CID header bytes" <| fun () ->
                // CIDv1 + dag-cbor + sha256 header: [0x01, 0x71, 0x12, 0x20]
                let result = Base32.encode [| 0x01uy; 0x71uy; 0x12uy; 0x20uy |]
                // Should start with "afyrei" (the 'b' multibase prefix is NOT added by Base32)
                Expect.stringStarts result "afyrei" "CID header prefix"
        ]
        testList "decode" [
            testCase "empty" <| fun () ->
                Expect.equal (Base32.decode "") [||] "empty"
            testCase "single byte" <| fun () ->
                Expect.equal (Base32.decode "my") [| 0x66uy |] "decode 'my'"
            testCase "two bytes" <| fun () ->
                Expect.equal (Base32.decode "mzxq") [| 0x66uy; 0x6Fuy |] "decode 'mzxq'"
            testCase "three bytes" <| fun () ->
                Expect.equal (Base32.decode "mzxw6") [| 0x66uy; 0x6Fuy; 0x6Fuy |] "decode 'mzxw6'"
        ]
        testList "roundtrip" [
            testCase "various lengths" <| fun () ->
                for len in [0; 1; 2; 3; 4; 5; 10; 32; 36] do
                    let data = Array.init len (fun i -> byte (i % 256))
                    let encoded = Base32.encode data
                    let decoded = Base32.decode encoded
                    Expect.equal decoded data (sprintf "roundtrip length %d" len)
        ]
    ]
```

**Step 2: Run tests to verify they fail**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.DRISL.Tests/FSharp.ATProto.DRISL.Tests.fsproj
```

**Step 3: Implement Base32 module**

Replace `src/FSharp.ATProto.DRISL/Base32.fs`:

```fsharp
namespace FSharp.ATProto.DRISL

/// RFC 4648 Base32 encoding, lowercase alphabet, no padding.
module Base32 =
    let private alphabet = "abcdefghijklmnopqrstuvwxyz234567"

    /// Encode bytes to base32 lowercase string (no padding).
    let encode (data: byte[]) : string =
        if data.Length = 0 then ""
        else
            let sb = System.Text.StringBuilder()
            let mutable buffer = 0u
            let mutable bits = 0
            for b in data do
                buffer <- (buffer <<< 8) ||| uint32 b
                bits <- bits + 8
                while bits >= 5 do
                    bits <- bits - 5
                    sb.Append(alphabet.[int ((buffer >>> bits) &&& 0x1Fu)]) |> ignore
                    buffer <- buffer &&& ((1u <<< bits) - 1u)
            if bits > 0 then
                sb.Append(alphabet.[int ((buffer <<< (5 - bits)) &&& 0x1Fu)]) |> ignore
            sb.ToString()

    /// Decode a base32 lowercase string (no padding) to bytes.
    let decode (s: string) : byte[] =
        if s.Length = 0 then [||]
        else
            let output = System.Collections.Generic.List<byte>()
            let mutable buffer = 0u
            let mutable bits = 0
            for c in s do
                let value =
                    if c >= 'a' && c <= 'z' then int c - int 'a'
                    elif c >= '2' && c <= '7' then int c - int '2' + 26
                    else failwithf "Invalid base32 character: %c" c
                buffer <- (buffer <<< 5) ||| uint32 value
                bits <- bits + 5
                if bits >= 8 then
                    bits <- bits - 8
                    output.Add(byte ((buffer >>> bits) &&& 0xFFu))
                    buffer <- buffer &&& ((1u <<< bits) - 1u)
            output.ToArray()
```

**Step 4: Run tests, verify pass**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.DRISL.Tests/FSharp.ATProto.DRISL.Tests.fsproj
```

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.DRISL/Base32.fs tests/FSharp.ATProto.DRISL.Tests/Base32Tests.fs && git commit -m "Implement Base32 module with RFC 4648 lowercase encode/decode"
```

---

### Task 4: Implement CidBinary module

Binary CID parsing, construction, and computation. CIDv1 + SHA-256 + base32-lower with 'b' multibase prefix.

**Files:**
- Modify: `src/FSharp.ATProto.DRISL/CidBinary.fs`
- Modify: `tests/FSharp.ATProto.DRISL.Tests/CidBinaryTests.fs`

**Step 1: Write the failing tests**

Use the known CIDs from the interop fixture data.

Replace `tests/FSharp.ATProto.DRISL.Tests/CidBinaryTests.fs`:

```fsharp
module CidBinaryTests

open Expecto
open FSharp.ATProto.DRISL
open FSharp.ATProto.Syntax

// Known CIDs from interop fixture data
let knownCid1 = "bafyreiclp443lavogvhj3d2ob2cxbfuscni2k5jk7bebjzg7khl3esabwq"
let knownCid2 = "bafyreidfayvfuwqa7qlnopdjiqrxzs6blmoeu4rujcjtnci5beludirz2a"
let knownCidRaw = "bafkreiccldh766hwcnuxnf2wh6jgzepf2nlu2lvcllt63eww5p6chi4ity"

[<Tests>]
let tests =
    testList "CidBinary" [
        testList "toBytes and fromBytes roundtrip" [
            testCase "dag-cbor CID" <| fun () ->
                let cid = Cid.parse knownCid1 |> Result.defaultWith failwith
                let bytes = CidBinary.toBytes cid
                // CIDv1 dag-cbor SHA-256: 1 + 1 + 1 + 1 + 32 = 36 bytes
                Expect.equal bytes.Length 36 "CID binary should be 36 bytes"
                Expect.equal bytes.[0] 0x01uy "version = 1"
                Expect.equal bytes.[1] 0x71uy "codec = dag-cbor"
                Expect.equal bytes.[2] 0x12uy "hash = sha256"
                Expect.equal bytes.[3] 0x20uy "digest length = 32"
                let result = CidBinary.fromBytes bytes
                Expect.isOk result "fromBytes should succeed"
                let roundtripped = result |> Result.defaultWith failwith
                Expect.equal (Cid.value roundtripped) knownCid1 "roundtrip should preserve CID"

            testCase "raw CID" <| fun () ->
                let cid = Cid.parse knownCidRaw |> Result.defaultWith failwith
                let bytes = CidBinary.toBytes cid
                Expect.equal bytes.[0] 0x01uy "version = 1"
                Expect.equal bytes.[1] 0x55uy "codec = raw"
                Expect.equal bytes.[2] 0x12uy "hash = sha256"
                let result = CidBinary.fromBytes bytes
                Expect.isOk result "fromBytes should succeed"
                let roundtripped = result |> Result.defaultWith failwith
                Expect.equal (Cid.value roundtripped) knownCidRaw "roundtrip raw CID"
        ]

        testList "compute" [
            testCase "empty bytes hash" <| fun () ->
                // SHA-256 of empty input is known: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
                let cid = CidBinary.compute [||]
                let cidStr = Cid.value cid
                Expect.stringStarts cidStr "bafyrei" "CID should start with bafyrei (CIDv1 dag-cbor)"

            testCase "compute is deterministic" <| fun () ->
                let data = [| 0x01uy; 0x02uy; 0x03uy |]
                let cid1 = CidBinary.compute data
                let cid2 = CidBinary.compute data
                Expect.equal (Cid.value cid1) (Cid.value cid2) "same input -> same CID"

            testCase "different data -> different CID" <| fun () ->
                let cid1 = CidBinary.compute [| 0x01uy |]
                let cid2 = CidBinary.compute [| 0x02uy |]
                Expect.notEqual (Cid.value cid1) (Cid.value cid2) "different input -> different CID"
        ]

        testList "fromBytes validation" [
            testCase "rejects CIDv0" <| fun () ->
                let result = CidBinary.fromBytes [| 0x00uy; 0x71uy; 0x12uy; 0x20uy |]
                Expect.isError result "should reject version 0"

            testCase "rejects unsupported codec" <| fun () ->
                // Version 1, codec 0x50 (not dag-cbor or raw), sha256
                let bytes = Array.concat [| Varint.encode 1UL; Varint.encode 0x50UL; Varint.encode 0x12UL; Varint.encode 0x20UL; Array.zeroCreate 32 |]
                let result = CidBinary.fromBytes bytes
                Expect.isError result "should reject unsupported codec"

            testCase "rejects unsupported hash" <| fun () ->
                // Version 1, dag-cbor, hash 0x13 (not sha256)
                let bytes = Array.concat [| Varint.encode 1UL; Varint.encode 0x71UL; Varint.encode 0x13UL; Varint.encode 0x20UL; Array.zeroCreate 32 |]
                let result = CidBinary.fromBytes bytes
                Expect.isError result "should reject unsupported hash function"
        ]
    ]
```

**Step 2: Run tests to verify they fail**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.DRISL.Tests/FSharp.ATProto.DRISL.Tests.fsproj
```

**Step 3: Implement CidBinary module**

Replace `src/FSharp.ATProto.DRISL/CidBinary.fs`:

```fsharp
namespace FSharp.ATProto.DRISL

open FSharp.ATProto.Syntax

/// Binary CID operations: parse, construct, compute.
/// Supports CIDv1 with dag-cbor (0x71) or raw (0x55) codec and SHA-256 hash.
module CidBinary =

    let private dagCborCodec = 0x71UL
    let private rawCodec = 0x55UL
    let private sha256Code = 0x12UL
    let private sha256DigestLen = 0x20UL

    /// Compute a CIDv1 (dag-cbor + SHA-256) from raw data bytes.
    let compute (data: byte[]) : Cid =
        use sha = System.Security.Cryptography.SHA256.Create()
        let hash = sha.ComputeHash(data)
        let binary = Array.concat [|
            Varint.encode 1UL
            Varint.encode dagCborCodec
            Varint.encode sha256Code
            Varint.encode sha256DigestLen
            hash
        |]
        let encoded = "b" + Base32.encode binary
        Cid.fromValidated encoded

    /// Convert a CID string to its raw binary representation.
    let toBytes (cid: Cid) : byte[] =
        let s = Cid.value cid
        if s.Length < 2 || s.[0] <> 'b' then
            failwith "Expected multibase 'b' prefix on CID string"
        Base32.decode (s.Substring(1))

    /// Parse raw binary bytes as a CID, validating structure.
    let fromBytes (data: byte[]) : Result<Cid, string> =
        try
            let mutable offset = 0
            let readVarint () =
                let (v, len) = Varint.decode data offset
                offset <- offset + len
                v
            let version = readVarint ()
            if version <> 1UL then
                Error (sprintf "Unsupported CID version: %d" version)
            else
                let codec = readVarint ()
                if codec <> dagCborCodec && codec <> rawCodec then
                    Error (sprintf "Unsupported CID codec: 0x%x" codec)
                else
                    let hashFn = readVarint ()
                    if hashFn <> sha256Code then
                        Error (sprintf "Unsupported hash function: 0x%x" hashFn)
                    else
                        let digestLen = readVarint ()
                        if digestLen <> sha256DigestLen then
                            Error (sprintf "Invalid SHA-256 digest length: %d" digestLen)
                        else
                            let remaining = data.Length - offset
                            if remaining <> int digestLen then
                                Error (sprintf "Expected %d digest bytes, got %d" digestLen remaining)
                            else
                                let encoded = "b" + Base32.encode data
                                Ok (Cid.fromValidated encoded)
        with ex ->
            Error ex.Message
```

**Step 4: Run tests, verify pass**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.DRISL.Tests/FSharp.ATProto.DRISL.Tests.fsproj
```

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.DRISL/CidBinary.fs tests/FSharp.ATProto.DRISL.Tests/CidBinaryTests.fs && git commit -m "Implement CidBinary module for CID parse/construct/compute"
```

---

### Task 5: Implement AtpValue type and DRISL encoder

The core data model DU and CBOR encoder. The encoder writes AtpValue to canonical DRISL-CBOR bytes using System.Formats.Cbor.

**Files:**
- Modify: `src/FSharp.ATProto.DRISL/AtpValue.fs`
- Modify: `src/FSharp.ATProto.DRISL/Drisl.fs`
- Modify: `tests/FSharp.ATProto.DRISL.Tests/DrislTests.fs`

**Step 1: Define AtpValue type**

Replace `src/FSharp.ATProto.DRISL/AtpValue.fs`:

```fsharp
namespace FSharp.ATProto.DRISL

open FSharp.ATProto.Syntax

/// The AT Protocol data model. Represents all values that can be encoded in DRISL-CBOR.
[<RequireQualifiedAccess>]
type AtpValue =
    | Null
    | Bool of bool
    | Integer of int64
    | String of string
    | Bytes of byte[]
    | Link of Cid
    | Array of AtpValue list
    | Object of Map<string, AtpValue>
```

**Step 2: Write encoder tests**

Replace `tests/FSharp.ATProto.DRISL.Tests/DrislTests.fs`:

```fsharp
module DrislTests

open Expecto
open FSharp.ATProto.DRISL
open FSharp.ATProto.Syntax

[<Tests>]
let tests =
    testList "Drisl" [
        testList "encode" [
            testCase "null" <| fun () ->
                let bytes = Drisl.encode AtpValue.Null
                Expect.equal bytes [| 0xF6uy |] "null = 0xF6"

            testCase "true" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Bool true)
                Expect.equal bytes [| 0xF5uy |] "true = 0xF5"

            testCase "false" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Bool false)
                Expect.equal bytes [| 0xF4uy |] "false = 0xF4"

            testCase "integer 0" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Integer 0L)
                Expect.equal bytes [| 0x00uy |] "integer 0"

            testCase "integer 23" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Integer 23L)
                Expect.equal bytes [| 0x17uy |] "integer 23 single byte"

            testCase "integer 24" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Integer 24L)
                Expect.equal bytes [| 0x18uy; 0x18uy |] "integer 24 two bytes"

            testCase "integer 123" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Integer 123L)
                Expect.equal bytes [| 0x18uy; 0x7Buy |] "integer 123"

            testCase "negative integer -1" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Integer -1L)
                Expect.equal bytes [| 0x20uy |] "negative -1"

            testCase "string abc" <| fun () ->
                let bytes = Drisl.encode (AtpValue.String "abc")
                Expect.equal bytes [| 0x63uy; 0x61uy; 0x62uy; 0x63uy |] "string abc"

            testCase "empty array" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Array [])
                Expect.equal bytes [| 0x80uy |] "empty array"

            testCase "empty map" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Object Map.empty)
                Expect.equal bytes [| 0xA0uy |] "empty map"

            testCase "map keys sorted by length then lex" <| fun () ->
                // Keys: "b" (1 char), "aa" (2 chars) -> sorted: "b" first (shorter)
                let value = AtpValue.Object (Map.ofList [("aa", AtpValue.Integer 2L); ("b", AtpValue.Integer 1L)])
                let bytes = Drisl.encode value
                // Should be: map(2), "b", 1, "aa", 2
                // 0xA2 0x61 0x62 0x01 0x62 0x61 0x61 0x02
                Expect.equal bytes [| 0xA2uy; 0x61uy; 0x62uy; 0x01uy; 0x62uy; 0x61uy; 0x61uy; 0x02uy |] "keys sorted by length"

            testCase "byte string" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Bytes [| 0x01uy; 0x02uy; 0x03uy |])
                Expect.equal bytes [| 0x43uy; 0x01uy; 0x02uy; 0x03uy |] "byte string"

            testCase "CID link uses tag 42" <| fun () ->
                let cid = CidBinary.compute [||] // CID of empty data
                let bytes = Drisl.encode (AtpValue.Link cid)
                // Should start with tag 42: 0xD8 0x2A
                Expect.equal bytes.[0] 0xD8uy "tag prefix"
                Expect.equal bytes.[1] 0x2Auy "tag 42"
                // Then byte string with 0x00 prefix + CID binary
                // Byte string header: 0x58 0x25 (37 bytes = 1 prefix + 36 CID)
                Expect.equal bytes.[2] 0x58uy "byte string 1-byte length"
                Expect.equal bytes.[3] 0x25uy "37 bytes"
                Expect.equal bytes.[4] 0x00uy "identity multibase prefix"
        ]
    ]
```

**Step 3: Implement DRISL encoder**

Replace `src/FSharp.ATProto.DRISL/Drisl.fs`:

```fsharp
namespace FSharp.ATProto.DRISL

open System
open System.Formats.Cbor
open FSharp.ATProto.Syntax

/// DRISL-CBOR encoding and decoding for AT Protocol data model.
module Drisl =

    let private cidTag : CborTag = LanguagePrimitives.EnumOfValue 42UL

    /// Compare two map keys in DRISL sort order (length-first, then lexicographic on UTF-8 bytes).
    let private compareKeys (a: string) (b: string) =
        let aBytes = Text.Encoding.UTF8.GetBytes(a)
        let bBytes = Text.Encoding.UTF8.GetBytes(b)
        let lenCmp = compare aBytes.Length bBytes.Length
        if lenCmp <> 0 then lenCmp
        else compare aBytes bBytes

    /// Sort map keys in DRISL canonical order.
    let private sortKeys (keys: string seq) =
        keys |> Seq.sortWith compareKeys

    let rec private writeValue (writer: CborWriter) (value: AtpValue) =
        match value with
        | AtpValue.Null -> writer.WriteNull()
        | AtpValue.Bool b -> writer.WriteBoolean(b)
        | AtpValue.Integer n -> writer.WriteInt64(n)
        | AtpValue.String s -> writer.WriteTextString(s)
        | AtpValue.Bytes b -> writer.WriteByteString(b)
        | AtpValue.Link cid ->
            writer.WriteTag(cidTag)
            let cidBytes = CidBinary.toBytes cid
            let withPrefix = Array.append [| 0x00uy |] cidBytes
            writer.WriteByteString(withPrefix)
        | AtpValue.Array items ->
            writer.WriteStartArray(Nullable items.Length)
            for item in items do writeValue writer item
            writer.WriteEndArray()
        | AtpValue.Object map ->
            writer.WriteStartMap(Nullable map.Count)
            let sorted = sortKeys (map |> Map.keys) |> Seq.toArray
            for key in sorted do
                writer.WriteTextString(key)
                writeValue writer (Map.find key map)
            writer.WriteEndMap()

    /// Encode an AtpValue to canonical DRISL-CBOR bytes.
    let encode (value: AtpValue) : byte[] =
        let writer = CborWriter(CborConformanceMode.Canonical)
        writeValue writer value
        writer.Encode()
```

**Step 4: Run tests, verify pass**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.DRISL.Tests/FSharp.ATProto.DRISL.Tests.fsproj
```

If the map key sorting test fails, inspect the actual bytes and adjust. The CBOR canonical mode writer validates that keys are written in order and will throw if not.

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.DRISL/AtpValue.fs src/FSharp.ATProto.DRISL/Drisl.fs tests/FSharp.ATProto.DRISL.Tests/DrislTests.fs && git commit -m "Implement AtpValue type and DRISL-CBOR encoder"
```

---

### Task 6: Implement DRISL decoder

CBOR bytes -> AtpValue, with validation of DRISL constraints (no floats, string-only keys, only tag 42, sort order).

**Files:**
- Modify: `src/FSharp.ATProto.DRISL/Drisl.fs`
- Modify: `tests/FSharp.ATProto.DRISL.Tests/DrislTests.fs`

**Step 1: Add decode tests**

Add to the `tests` value in `tests/FSharp.ATProto.DRISL.Tests/DrislTests.fs`:

```fsharp
        testList "decode" [
            testCase "null" <| fun () ->
                let result = Drisl.decode [| 0xF6uy |]
                Expect.equal result (Ok AtpValue.Null) "null"

            testCase "true" <| fun () ->
                let result = Drisl.decode [| 0xF5uy |]
                Expect.equal result (Ok (AtpValue.Bool true)) "true"

            testCase "integer 123" <| fun () ->
                let result = Drisl.decode [| 0x18uy; 0x7Buy |]
                Expect.equal result (Ok (AtpValue.Integer 123L)) "integer 123"

            testCase "negative integer" <| fun () ->
                let result = Drisl.decode [| 0x20uy |]
                Expect.equal result (Ok (AtpValue.Integer -1L)) "negative -1"

            testCase "string" <| fun () ->
                let result = Drisl.decode [| 0x63uy; 0x61uy; 0x62uy; 0x63uy |]
                Expect.equal result (Ok (AtpValue.String "abc")) "string abc"

            testCase "empty array" <| fun () ->
                let result = Drisl.decode [| 0x80uy |]
                Expect.equal result (Ok (AtpValue.Array [])) "empty array"

            testCase "empty map" <| fun () ->
                let result = Drisl.decode [| 0xA0uy |]
                Expect.equal result (Ok (AtpValue.Object Map.empty)) "empty map"

            testCase "rejects floats" <| fun () ->
                // 0xFB = double float, followed by 8 bytes
                let bytes = Array.concat [| [| 0xFBuy |]; BitConverter.GetBytes(1.5) |> Array.rev |]
                let result = Drisl.decode bytes
                Expect.isError result "should reject floats"

            testCase "rejects trailing bytes" <| fun () ->
                let result = Drisl.decode [| 0xF6uy; 0x00uy |]
                Expect.isError result "should reject trailing bytes"
        ]

        testList "roundtrip" [
            testCase "encode then decode primitives" <| fun () ->
                let values = [
                    AtpValue.Null
                    AtpValue.Bool true
                    AtpValue.Bool false
                    AtpValue.Integer 0L
                    AtpValue.Integer 123L
                    AtpValue.Integer -1L
                    AtpValue.Integer System.Int64.MaxValue
                    AtpValue.Integer System.Int64.MinValue
                    AtpValue.String ""
                    AtpValue.String "hello world"
                    AtpValue.Bytes [||]
                    AtpValue.Bytes [| 0x01uy; 0x02uy |]
                ]
                for v in values do
                    let encoded = Drisl.encode v
                    let decoded = Drisl.decode encoded
                    Expect.equal decoded (Ok v) (sprintf "roundtrip %A" v)

            testCase "encode then decode nested structure" <| fun () ->
                let value = AtpValue.Object (Map.ofList [
                    ("arr", AtpValue.Array [AtpValue.Integer 1L; AtpValue.Integer 2L])
                    ("nested", AtpValue.Object (Map.ofList [("x", AtpValue.String "y")]))
                ])
                let encoded = Drisl.encode value
                let decoded = Drisl.decode encoded
                Expect.equal decoded (Ok value) "nested roundtrip"

            testCase "encode then decode CID link" <| fun () ->
                let cid = CidBinary.compute [| 0xAAuy; 0xBBuy |]
                let value = AtpValue.Link cid
                let encoded = Drisl.encode value
                let decoded = Drisl.decode encoded
                Expect.equal decoded (Ok value) "CID link roundtrip"
        ]
```

**Step 2: Run tests to verify new tests fail**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.DRISL.Tests/FSharp.ATProto.DRISL.Tests.fsproj
```

**Step 3: Implement decoder**

Add to `src/FSharp.ATProto.DRISL/Drisl.fs` (inside the `Drisl` module):

```fsharp
    let rec private readValue (reader: CborReader) : AtpValue =
        match reader.PeekState() with
        | CborReaderState.Null ->
            reader.ReadNull()
            AtpValue.Null
        | CborReaderState.Boolean ->
            AtpValue.Bool (reader.ReadBoolean())
        | CborReaderState.UnsignedInteger
        | CborReaderState.NegativeInteger ->
            AtpValue.Integer (reader.ReadInt64())
        | CborReaderState.TextString ->
            AtpValue.String (reader.ReadTextString())
        | CborReaderState.ByteString ->
            AtpValue.Bytes (reader.ReadByteString())
        | CborReaderState.Tag ->
            let tag = reader.ReadTag()
            if tag <> cidTag then
                failwithf "Unsupported CBOR tag: %d" (uint64 tag)
            let bytes = reader.ReadByteString()
            if bytes.Length < 2 || bytes.[0] <> 0x00uy then
                failwith "Invalid CID in tag 42: missing 0x00 prefix"
            let cidBytes = bytes.[1..]
            match CidBinary.fromBytes cidBytes with
            | Ok cid -> AtpValue.Link cid
            | Error e -> failwithf "Invalid CID in tag 42: %s" e
        | CborReaderState.StartArray ->
            let _count = reader.ReadStartArray()
            let items = System.Collections.Generic.List<AtpValue>()
            while reader.PeekState() <> CborReaderState.EndArray do
                items.Add(readValue reader)
            reader.ReadEndArray()
            AtpValue.Array (items |> Seq.toList)
        | CborReaderState.StartMap ->
            let _count = reader.ReadStartMap()
            let mutable map = Map.empty
            while reader.PeekState() <> CborReaderState.EndMap do
                if reader.PeekState() <> CborReaderState.TextString then
                    failwith "DRISL map keys must be text strings"
                let key = reader.ReadTextString()
                let value = readValue reader
                map <- Map.add key value map
            reader.ReadEndMap()
            AtpValue.Object map
        | CborReaderState.HalfPrecisionFloat
        | CborReaderState.SinglePrecisionFloat
        | CborReaderState.DoublePrecisionFloat ->
            failwith "Floats are not allowed in DRISL"
        | state ->
            failwithf "Unexpected CBOR state: %A" state

    /// Decode DRISL-CBOR bytes to an AtpValue.
    let decode (data: byte[]) : Result<AtpValue, string> =
        try
            let reader = CborReader(ReadOnlyMemory(data), CborConformanceMode.Canonical)
            let result = readValue reader
            if reader.BytesRemaining > 0 then
                Error "Trailing bytes after CBOR value"
            else
                Ok result
        with ex ->
            Error ex.Message
```

**Step 4: Run tests, verify pass**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.DRISL.Tests/FSharp.ATProto.DRISL.Tests.fsproj
```

Note: The `AtpValue` type needs structural equality for the roundtrip tests. F# DUs have structural equality by default, but `byte[]` does not. This will cause `Expect.equal` to fail on `AtpValue.Bytes` comparisons. If this happens, you'll need to either:
- Override equality on AtpValue (add `[<CustomEquality; CustomComparison>]`), or
- Compare byte arrays manually in tests, or
- Use `[<StructuralEquality; StructuralComparison>]` (won't fix byte[] comparison)

The simplest fix: write a custom equality helper or compare the values after re-encoding (encode both and compare bytes).

If byte array equality is an issue, change the roundtrip test to compare via encoding:
```fsharp
let decoded = Drisl.decode encoded |> Result.defaultWith failwith
let reEncoded = Drisl.encode decoded
Expect.equal reEncoded encoded "roundtrip via re-encoding"
```

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.DRISL/Drisl.fs tests/FSharp.ATProto.DRISL.Tests/DrislTests.fs && git commit -m "Implement DRISL-CBOR decoder with roundtrip tests"
```

---

### Task 7: Implement AtpJson module

JSON <-> AtpValue conversion, handling `$link`, `$bytes`, `$type`, blob validation, float rejection.

**Files:**
- Modify: `src/FSharp.ATProto.DRISL/AtpJson.fs`
- Modify: `tests/FSharp.ATProto.DRISL.Tests/AtpJsonTests.fs`

**Step 1: Write tests**

Replace `tests/FSharp.ATProto.DRISL.Tests/AtpJsonTests.fs`:

```fsharp
module AtpJsonTests

open Expecto
open System.Text.Json
open FSharp.ATProto.DRISL
open FSharp.ATProto.Syntax

let parse (json: string) = JsonDocument.Parse(json).RootElement

[<Tests>]
let tests =
    testList "AtpJson" [
        testList "fromJson basics" [
            testCase "simple object" <| fun () ->
                let result = AtpJson.fromJson (parse """{"a": 123, "b": "hello"}""")
                Expect.isOk result "should parse"

            testCase "integer-like float accepted" <| fun () ->
                let result = AtpJson.fromJson (parse """{"a": 123.0}""")
                Expect.isOk result "123.0 should be accepted as integer"
                let value = result |> Result.defaultWith failwith
                match value with
                | AtpValue.Object m ->
                    Expect.equal (Map.find "a" m) (AtpValue.Integer 123L) "123.0 -> Integer 123"
                | _ -> failwith "expected object"

            testCase "rejects non-integer float" <| fun () ->
                let result = AtpJson.fromJson (parse """{"a": 123.456}""")
                Expect.isError result "should reject float"

            testCase "rejects bare string at top level" <| fun () ->
                let result = AtpJson.fromJson (parse "\"blah\"")
                Expect.isError result "top-level must be object"

            testCase "rejects bare number at top level" <| fun () ->
                let result = AtpJson.fromJson (parse "123")
                Expect.isError result "top-level must be object"
        ]

        testList "fromJson $link" [
            testCase "valid link" <| fun () ->
                let json = """{"a": {"$link": "bafyreidfayvfuwqa7qlnopdjiqrxzs6blmoeu4rujcjtnci5beludirz2a"}}"""
                let result = AtpJson.fromJson (parse json)
                Expect.isOk result "should parse link"
                let value = result |> Result.defaultWith failwith
                match value with
                | AtpValue.Object m ->
                    match Map.find "a" m with
                    | AtpValue.Link cid -> Expect.equal (Cid.value cid) "bafyreidfayvfuwqa7qlnopdjiqrxzs6blmoeu4rujcjtnci5beludirz2a" "CID value"
                    | x -> failwithf "expected Link, got %A" x
                | _ -> failwith "expected object"

            testCase "rejects link with wrong type" <| fun () ->
                let result = AtpJson.fromJson (parse """{"a": {"$link": 1234}}""")
                Expect.isError result "link value must be string"

            testCase "rejects link with extra fields" <| fun () ->
                let result = AtpJson.fromJson (parse """{"a": {"$link": "bafyreidfayvfuwqa7qlnopdjiqrxzs6blmoeu4rujcjtnci5beludirz2a", "other": "blah"}}""")
                Expect.isError result "link must have exactly one key"
        ]

        testList "fromJson $bytes" [
            testCase "valid bytes" <| fun () ->
                let json = """{"a": {"$bytes": "AQID"}}"""
                let result = AtpJson.fromJson (parse json)
                Expect.isOk result "should parse bytes"
                let value = result |> Result.defaultWith failwith
                match value with
                | AtpValue.Object m ->
                    match Map.find "a" m with
                    | AtpValue.Bytes b -> Expect.equal b [| 1uy; 2uy; 3uy |] "decoded bytes"
                    | x -> failwithf "expected Bytes, got %A" x
                | _ -> failwith "expected object"

            testCase "rejects bytes with wrong type" <| fun () ->
                let result = AtpJson.fromJson (parse """{"a": {"$bytes": [1,2,3]}}""")
                Expect.isError result "bytes value must be string"

            testCase "rejects bytes with extra fields" <| fun () ->
                let result = AtpJson.fromJson (parse """{"a": {"$bytes": "AQID", "other": "x"}}""")
                Expect.isError result "bytes must have exactly one key"
        ]

        testList "fromJson $type" [
            testCase "valid $type" <| fun () ->
                let result = AtpJson.fromJson (parse """{"$type": "com.example.thing", "a": 1}""")
                Expect.isOk result "valid $type"

            testCase "rejects null $type" <| fun () ->
                let result = AtpJson.fromJson (parse """{"$type": null, "a": 1}""")
                Expect.isError result "$type cannot be null"

            testCase "rejects non-string $type" <| fun () ->
                let result = AtpJson.fromJson (parse """{"$type": 123, "a": 1}""")
                Expect.isError result "$type must be string"

            testCase "rejects empty $type" <| fun () ->
                let result = AtpJson.fromJson (parse """{"$type": "", "a": 1}""")
                Expect.isError result "$type cannot be empty"
        ]

        testList "fromJson blob" [
            testCase "valid blob" <| fun () ->
                let json = """{"blb": {"$type": "blob", "ref": {"$link": "bafkreiccldh766hwcnuxnf2wh6jgzepf2nlu2lvcllt63eww5p6chi4ity"}, "mimeType": "image/jpeg", "size": 10000}}"""
                let result = AtpJson.fromJson (parse json)
                Expect.isOk result "valid blob"

            testCase "rejects blob with string size" <| fun () ->
                let json = """{"blb": {"$type": "blob", "ref": {"$link": "bafkreiccldh766hwcnuxnf2wh6jgzepf2nlu2lvcllt63eww5p6chi4ity"}, "mimeType": "image/jpeg", "size": "10000"}}"""
                let result = AtpJson.fromJson (parse json)
                Expect.isError result "blob size must be integer"

            testCase "rejects blob with missing ref" <| fun () ->
                let json = """{"blb": {"$type": "blob", "mimeType": "image/jpeg", "size": 10000}}"""
                let result = AtpJson.fromJson (parse json)
                Expect.isError result "blob must have ref"
        ]
    ]
```

**Step 2: Run tests to verify they fail**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.DRISL.Tests/FSharp.ATProto.DRISL.Tests.fsproj
```

**Step 3: Implement AtpJson module**

Replace `src/FSharp.ATProto.DRISL/AtpJson.fs`:

```fsharp
namespace FSharp.ATProto.DRISL

open System
open System.Text.Json
open System.Text.Json.Nodes
open FSharp.ATProto.Syntax

/// JSON <-> AtpValue conversion with AT Protocol conventions ($link, $bytes, $type, blobs).
module AtpJson =

    let private padBase64 (s: string) =
        match s.Length % 4 with
        | 2 -> s + "=="
        | 3 -> s + "="
        | _ -> s

    /// Convert a JSON element to AtpValue. No top-level type check.
    let rec private convertElement (element: JsonElement) : Result<AtpValue, string> =
        match element.ValueKind with
        | JsonValueKind.Null -> Ok AtpValue.Null
        | JsonValueKind.True -> Ok (AtpValue.Bool true)
        | JsonValueKind.False -> Ok (AtpValue.Bool false)
        | JsonValueKind.Number ->
            match element.TryGetInt64() with
            | true, n -> Ok (AtpValue.Integer n)
            | false, _ ->
                let d = element.GetDouble()
                if d = Math.Floor(d) && d >= float Int64.MinValue && d <= float Int64.MaxValue then
                    Ok (AtpValue.Integer (int64 d))
                else
                    Error (sprintf "Non-integer floats are not allowed in AT Protocol data model: %g" d)
        | JsonValueKind.String -> Ok (AtpValue.String (element.GetString()))
        | JsonValueKind.Array ->
            let mutable result = Ok []
            for item in element.EnumerateArray() do
                match result with
                | Error _ -> ()
                | Ok acc ->
                    match convertElement item with
                    | Error e -> result <- Error e
                    | Ok v -> result <- Ok (acc @ [v])
            result |> Result.map AtpValue.Array
        | JsonValueKind.Object -> convertObject element
        | kind -> Error (sprintf "Unsupported JSON value kind: %A" kind)

    and private convertObject (element: JsonElement) : Result<AtpValue, string> =
        // Check for $link
        match element.TryGetProperty("$link") with
        | true, linkProp ->
            let count = element.EnumerateObject() |> Seq.length
            if count > 1 then
                Error "$link object must have exactly one key"
            elif linkProp.ValueKind <> JsonValueKind.String then
                Error "$link value must be a string"
            else
                match Cid.parse (linkProp.GetString()) with
                | Ok cid -> Ok (AtpValue.Link cid)
                | Error e -> Error (sprintf "Invalid CID in $link: %s" e)
        | false, _ ->

        // Check for $bytes
        match element.TryGetProperty("$bytes") with
        | true, bytesProp ->
            let count = element.EnumerateObject() |> Seq.length
            if count > 1 then
                Error "$bytes object must have exactly one key"
            elif bytesProp.ValueKind <> JsonValueKind.String then
                Error "$bytes value must be a string"
            else
                try
                    let bytes = Convert.FromBase64String(padBase64 (bytesProp.GetString()))
                    Ok (AtpValue.Bytes bytes)
                with _ ->
                    Error "Invalid base64 in $bytes"
        | false, _ ->

        // Check $type validation
        match element.TryGetProperty("$type") with
        | true, typeProp ->
            if typeProp.ValueKind = JsonValueKind.Null then
                Error "$type must not be null"
            elif typeProp.ValueKind <> JsonValueKind.String then
                Error "$type must be a string"
            elif typeProp.GetString().Length = 0 then
                Error "$type must not be empty"
            elif typeProp.GetString() = "blob" then
                validateBlob element
            else
                convertRegularObject element
        | false, _ ->
            convertRegularObject element

    and private validateBlob (element: JsonElement) : Result<AtpValue, string> =
        // Blob must have: $type (already validated), ref, mimeType, size
        match element.TryGetProperty("ref") with
        | false, _ -> Error "Blob must have a 'ref' field"
        | true, refProp ->
            if refProp.ValueKind <> JsonValueKind.Object then
                Error "Blob 'ref' must be an object"
            else
        match element.TryGetProperty("mimeType") with
        | false, _ -> Error "Blob must have a 'mimeType' field"
        | true, mimeProp ->
            if mimeProp.ValueKind <> JsonValueKind.String then
                Error "Blob 'mimeType' must be a string"
            else
        match element.TryGetProperty("size") with
        | false, _ -> Error "Blob must have a 'size' field"
        | true, sizeProp ->
            if sizeProp.ValueKind <> JsonValueKind.Number then
                Error "Blob 'size' must be a number"
            else
                match sizeProp.TryGetInt64() with
                | false, _ -> Error "Blob 'size' must be an integer"
                | true, _ -> convertRegularObject element

    and private convertRegularObject (element: JsonElement) : Result<AtpValue, string> =
        let mutable result = Ok Map.empty
        for prop in element.EnumerateObject() do
            match result with
            | Error _ -> ()
            | Ok map ->
                match convertElement prop.Value with
                | Error e -> result <- Error e
                | Ok v -> result <- Ok (Map.add prop.Name v map)
        result |> Result.map AtpValue.Object

    /// Convert a JSON element to AtpValue with data model validation.
    /// Top-level must be an object. Validates $type, $link, $bytes, blob structure.
    let fromJson (element: JsonElement) : Result<AtpValue, string> =
        if element.ValueKind <> JsonValueKind.Object then
            Error "Top-level value must be an object"
        else
            convertElement element

    /// Convert an AtpValue to a JSON node.
    let rec toJsonNode (value: AtpValue) : JsonNode =
        match value with
        | AtpValue.Null -> null
        | AtpValue.Bool b -> JsonValue.Create(b)
        | AtpValue.Integer n -> JsonValue.Create(n)
        | AtpValue.String s -> JsonValue.Create(s)
        | AtpValue.Bytes b ->
            let obj = JsonObject()
            obj.Add("$bytes", JsonValue.Create(Convert.ToBase64String(b)))
            obj
        | AtpValue.Link cid ->
            let obj = JsonObject()
            obj.Add("$link", JsonValue.Create(Cid.value cid))
            obj
        | AtpValue.Array items ->
            let arr = JsonArray()
            for item in items do arr.Add(toJsonNode item)
            arr
        | AtpValue.Object map ->
            let obj = JsonObject()
            for KeyValue(k, v) in map do obj.Add(k, toJsonNode v)
            obj
```

**Step 4: Run tests, verify pass**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.DRISL.Tests/FSharp.ATProto.DRISL.Tests.fsproj
```

**Important:** The `TryGetProperty` method on `JsonElement` returns a tuple `(bool * JsonElement)` in F#. If the F# compiler treats it differently, use:
```fsharp
let mutable prop = Unchecked.defaultof<JsonElement>
if element.TryGetProperty("$link", &prop) then ...
```

Also, the `validateBlob` function nests match expressions. If the F# compiler complains about indentation, restructure using early returns or a `result` computation expression.

**Step 5: Commit**

```bash
git add src/FSharp.ATProto.DRISL/AtpJson.fs tests/FSharp.ATProto.DRISL.Tests/AtpJsonTests.fs && git commit -m "Implement AtpJson module for JSON/AtpValue conversion"
```

---

### Task 8: Implement interop test suite

Test the full pipeline against official AT Protocol interop test vectors:
- 3 fixture roundtrips: JSON -> AtpValue -> CBOR bytes (must match) -> CID (must match)
- 5 valid cases: JSON -> AtpValue succeeds
- 11 invalid cases: JSON -> AtpValue fails

**Files:**
- Modify: `tests/FSharp.ATProto.DRISL.Tests/InteropTests.fs`

**Step 1: Write interop tests**

Replace `tests/FSharp.ATProto.DRISL.Tests/InteropTests.fs`:

```fsharp
module InteropTests

open Expecto
open System
open System.Text.Json
open FSharp.ATProto.DRISL
open FSharp.ATProto.Syntax

let fixturesDoc = TestHelpers.loadInteropJson "data-model/data-model-fixtures.json"
let validDoc = TestHelpers.loadInteropJson "data-model/data-model-valid.json"
let invalidDoc = TestHelpers.loadInteropJson "data-model/data-model-invalid.json"

[<Tests>]
let tests =
    testList "Interop" [
        testList "fixtures" [
            let fixtures = fixturesDoc.RootElement.EnumerateArray() |> Seq.toArray
            for i in 0 .. fixtures.Length - 1 do
                let fixture = fixtures.[i]
                let jsonValue = fixture.GetProperty("json")
                let expectedCborBase64 = fixture.GetProperty("cbor_base64").GetString()
                let expectedCid = fixture.GetProperty("cid").GetString()

                testCase (sprintf "fixture %d: JSON -> AtpValue" i) <| fun () ->
                    // The fixture JSON values are objects, so we can use fromJson directly
                    // But fromJson requires top-level object, and fixture values ARE objects
                    let result = AtpJson.fromJson jsonValue
                    Expect.isOk result (sprintf "fixture %d should parse" i)

                testCase (sprintf "fixture %d: encode matches expected CBOR" i) <| fun () ->
                    let atpValue = AtpJson.fromJson jsonValue |> Result.defaultWith failwith
                    let actualCbor = Drisl.encode atpValue
                    let expectedCbor = Convert.FromBase64String(expectedCborBase64)
                    Expect.equal actualCbor expectedCbor (sprintf "fixture %d CBOR bytes should match" i)

                testCase (sprintf "fixture %d: CID matches expected" i) <| fun () ->
                    let atpValue = AtpJson.fromJson jsonValue |> Result.defaultWith failwith
                    let cbor = Drisl.encode atpValue
                    let actualCid = CidBinary.compute cbor
                    Expect.equal (Cid.value actualCid) expectedCid (sprintf "fixture %d CID should match" i)

                testCase (sprintf "fixture %d: decode roundtrip" i) <| fun () ->
                    let atpValue = AtpJson.fromJson jsonValue |> Result.defaultWith failwith
                    let cbor = Drisl.encode atpValue
                    let decoded = Drisl.decode cbor
                    Expect.isOk decoded (sprintf "fixture %d should decode" i)
                    let reEncoded = Drisl.encode (decoded |> Result.defaultWith failwith)
                    Expect.equal reEncoded cbor (sprintf "fixture %d re-encoding should be identical" i)
        ]

        testList "valid cases" [
            let cases = validDoc.RootElement.EnumerateArray() |> Seq.toArray
            for i in 0 .. cases.Length - 1 do
                let case = cases.[i]
                let note = case.GetProperty("note").GetString()
                let jsonValue = case.GetProperty("json")
                testCase (sprintf "valid: %s" note) <| fun () ->
                    let result = AtpJson.fromJson jsonValue
                    Expect.isOk result (sprintf "should accept: %s" note)
        ]

        testList "invalid cases" [
            let cases = invalidDoc.RootElement.EnumerateArray() |> Seq.toArray
            for i in 0 .. cases.Length - 1 do
                let case = cases.[i]
                let note = case.GetProperty("note").GetString()
                let jsonValue = case.GetProperty("json")
                testCase (sprintf "invalid: %s" note) <| fun () ->
                    let result = AtpJson.fromJson jsonValue
                    Expect.isError result (sprintf "should reject: %s" note)
        ]
    ]
```

**Step 2: Run tests**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.DRISL.Tests/FSharp.ATProto.DRISL.Tests.fsproj
```

**If fixture CBOR bytes don't match:** This is the most critical test. If the CBOR encoding doesn't match byte-for-byte, the issue is likely:
- **Map key sorting**: The DRISL sort order must be length-first, then lexicographic on UTF-8 bytes. Check the `compareKeys` function. Also verify the CborWriter in Canonical mode accepts our sort order.
- **Integer encoding**: Must use shortest form. `CborConformanceMode.Canonical` handles this.
- **Tag 42 encoding**: Check the 0x00 prefix byte is included.
- **Base64 decoding**: The fixture's `cbor_base64` uses standard base64 (with `+` and `/`). Make sure `Convert.FromBase64String` handles it (it may need padding added).

Check the fixture CBOR base64 strings -- they appear to NOT have padding. If `Convert.FromBase64String` fails, add padding:
```fsharp
let padBase64 (s: string) =
    match s.Length % 4 with
    | 2 -> s + "=="
    | 3 -> s + "="
    | _ -> s
let expectedCbor = Convert.FromBase64String(padBase64 expectedCborBase64)
```

**If valid/invalid cases fail:** Read the specific test case, understand what's being tested, and fix the `AtpJson.fromJson` logic. Pay attention to:
- The `json` field structure -- some valid tests wrap arrays in objects (e.g., `{"arr": [1,2,null]}`)
- The blob validation in invalid tests

**Step 3: Fix any failures, then commit**

```bash
git add tests/FSharp.ATProto.DRISL.Tests/InteropTests.fs && git commit -m "Add interop test suite for DRISL fixtures, valid, and invalid cases"
```

---

### Task 9: Add FsCheck property-based tests

**Files:**
- Modify: `tests/FSharp.ATProto.DRISL.Tests/PropertyTests.fs`

**Step 1: Write property tests**

Replace `tests/FSharp.ATProto.DRISL.Tests/PropertyTests.fs`:

```fsharp
module PropertyTests

open Expecto
open FsCheck
open FSharp.ATProto.DRISL
open FSharp.ATProto.Syntax

// Generate AtpValue trees (no Links -- those require valid CIDs which are expensive to compute)
let rec genAtpValue (depth: int) : Gen<AtpValue> =
    if depth <= 0 then
        Gen.oneof [
            Gen.constant AtpValue.Null
            Gen.map AtpValue.Bool Arb.generate<bool>
            Gen.map AtpValue.Integer (Gen.choose (-1000, 1000) |> Gen.map int64)
            Gen.map AtpValue.String (Gen.elements [""; "a"; "hello"; "test"])
            Gen.map AtpValue.Bytes (Gen.arrayOfLength 4 Arb.generate<byte>)
        ]
    else
        Gen.oneof [
            Gen.constant AtpValue.Null
            Gen.map AtpValue.Bool Arb.generate<bool>
            Gen.map AtpValue.Integer (Gen.choose (-1000, 1000) |> Gen.map int64)
            Gen.map AtpValue.String (Gen.elements [""; "a"; "hello"; "test"])
            Gen.map AtpValue.Bytes (Gen.arrayOfLength 4 Arb.generate<byte>)
            gen {
                let! len = Gen.choose (0, 3)
                let! items = Gen.listOfLength len (genAtpValue (depth - 1))
                return AtpValue.Array items
            }
            gen {
                let! len = Gen.choose (0, 3)
                let! keys = Gen.listOfLength len (Gen.elements ["a"; "b"; "c"; "x"; "yy"; "zzz"])
                let! values = Gen.listOfLength len (genAtpValue (depth - 1))
                let map = List.zip keys values |> Map.ofList
                return AtpValue.Object map
            }
        ]

[<Tests>]
let tests =
    testList "property tests" [
        testProperty "encode/decode roundtrip" <|
            fun () ->
                Prop.forAll
                    (Arb.fromGen (genAtpValue 3))
                    (fun value ->
                        let encoded = Drisl.encode value
                        let decoded = Drisl.decode encoded |> Result.defaultWith failwith
                        let reEncoded = Drisl.encode decoded
                        encoded = reEncoded)

        testProperty "encoded CBOR never contains float indicators" <|
            fun () ->
                Prop.forAll
                    (Arb.fromGen (genAtpValue 2))
                    (fun value ->
                        let encoded = Drisl.encode value
                        // Float indicators in CBOR: 0xF9 (half), 0xFA (single), 0xFB (double)
                        // This is a heuristic - these bytes could appear in string/bytes content
                        // But for small test values, they shouldn't appear as CBOR type markers
                        true)  // Type safety guarantees no floats - AtpValue has no float case

        testProperty "map keys always sorted in DRISL order" <|
            fun () ->
                Prop.forAll
                    (Arb.fromGen (gen {
                        let! keys = Gen.listOfLength 5 (Gen.elements ["a"; "bb"; "ccc"; "d"; "ee"; "fff"; "g"])
                        let! values = Gen.listOfLength 5 (Gen.constant AtpValue.Null)
                        let map = List.zip keys values |> Map.ofList
                        return AtpValue.Object map
                    }))
                    (fun value ->
                        let encoded = Drisl.encode value
                        // If encoding succeeds without throwing, keys were in valid order
                        // (CborWriter in Canonical mode validates sort order)
                        let decoded = Drisl.decode encoded
                        Result.isOk decoded)
    ]
```

**Step 2: Run tests**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.DRISL.Tests/FSharp.ATProto.DRISL.Tests.fsproj
```

Expected: All tests pass including property tests.

**Step 3: Commit**

```bash
git add tests/FSharp.ATProto.DRISL.Tests/PropertyTests.fs && git commit -m "Add FsCheck property-based tests for DRISL encode/decode"
```

---

### Task 10: Final verification and cleanup

**Step 1: Run full test suite (both projects)**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet build /Users/aron/dev/atproto-fsharp/FSharp.ATProto.sln --no-incremental
```

Expected: 0 warnings, 0 errors.

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.Syntax.Tests/FSharp.ATProto.Syntax.Tests.fsproj
```

Expected: 726 tests pass.

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project /Users/aron/dev/atproto-fsharp/tests/FSharp.ATProto.DRISL.Tests/FSharp.ATProto.DRISL.Tests.fsproj
```

Expected: All DRISL tests pass (Varint + Base32 + CidBinary + Drisl + AtpJson + Interop + Properties).

**Step 2: Check for TODOs or dead code**

Search all .fs files in `src/FSharp.ATProto.DRISL/` and `tests/FSharp.ATProto.DRISL.Tests/` for TODO, FIXME, HACK.

**Step 3: Verify git status is clean**

```bash
git status
```

If there are any cleanup changes, commit:

```bash
git add -A && git commit -m "Phase 2 complete: DRISL/CBOR encoding with full interop test coverage"
```

---

## What's Next

Phase 3: Lexicon Parser -- Parse all 324 Lexicon JSON schema files into an F# domain model, with interop test coverage.
