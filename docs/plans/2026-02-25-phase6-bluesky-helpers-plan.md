# Phase 6: Rich Text, Identity & Convenience Methods — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add rich text facet detection, identity resolution, and convenience methods (post, like, follow, etc.) to FSharp.ATProto.Bluesky.

**Architecture:** Three hand-written F# modules (`RichText.fs`, `Identity.fs`, `Bluesky.fs`) added to the existing `FSharp.ATProto.Bluesky` project, compiling after `Generated/Generated.fs`. A new `FSharp.ATProto.Bluesky.Tests` project validates all new code with mocked HTTP.

**Tech Stack:** .NET 9, Expecto 10.2.3, FsCheck 2.16.6, FSharp.SystemTextJson 1.4.36, System.Text.Json, System.Globalization.StringInfo

---

## Task 1: Scaffold Test Project + Source Files

Create the test project and add empty source files to the Bluesky project.

**Files:**
- Create: `tests/FSharp.ATProto.Bluesky.Tests/FSharp.ATProto.Bluesky.Tests.fsproj`
- Create: `tests/FSharp.ATProto.Bluesky.Tests/TestHelpers.fs`
- Create: `tests/FSharp.ATProto.Bluesky.Tests/Main.fs`
- Create: `src/FSharp.ATProto.Bluesky/RichText.fs`
- Create: `src/FSharp.ATProto.Bluesky/Identity.fs`
- Create: `src/FSharp.ATProto.Bluesky/Bluesky.fs`
- Modify: `src/FSharp.ATProto.Bluesky/FSharp.ATProto.Bluesky.fsproj` — add new source files after Generated.fs
- Modify: `FSharp.ATProto.sln` — add new test project

**Step 1: Create test project fsproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="TestHelpers.fs" />
    <Compile Include="RichTextTests.fs" />
    <Compile Include="IdentityTests.fs" />
    <Compile Include="BlueskyTests.fs" />
    <Compile Include="Main.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/FSharp.ATProto.Bluesky/FSharp.ATProto.Bluesky.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Expecto" Version="10.2.3" />
    <PackageReference Include="Expecto.FsCheck" Version="10.2.3" />
    <PackageReference Include="FsCheck" Version="2.16.6" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.15.3" />
  </ItemGroup>
</Project>
```

**Step 2: Create TestHelpers.fs**

Reuse the same MockHandler pattern from Core.Tests (`tests/FSharp.ATProto.Core.Tests/TestHelpers.fs`):

```fsharp
module TestHelpers

open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks

type MockHandler(handler: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()

    override _.SendAsync(request, _cancellationToken) =
        Task.FromResult(handler request)

let jsonResponse (statusCode: HttpStatusCode) (body: obj) =
    let json = JsonSerializer.Serialize(body)
    let response = new HttpResponseMessage(statusCode)
    response.Content <- new StringContent(json, Encoding.UTF8, "application/json")
    response

let emptyResponse (statusCode: HttpStatusCode) =
    new HttpResponseMessage(statusCode)

let createMockAgent (handler: HttpRequestMessage -> HttpResponseMessage) =
    let httpClient = new HttpClient(new MockHandler(handler))
    FSharp.ATProto.Core.AtpAgent.createWithClient httpClient "https://bsky.social"
```

**Step 3: Create Main.fs**

```fsharp
module FSharp.ATProto.Bluesky.Tests.Main

open Expecto

[<EntryPoint>]
let main args =
    runTestsInAssemblyWithCLIArgs [] args
```

**Step 4: Create empty source files**

`src/FSharp.ATProto.Bluesky/RichText.fs`:
```fsharp
namespace FSharp.ATProto.Bluesky

module RichText =
    let placeholder = ()
```

`src/FSharp.ATProto.Bluesky/Identity.fs`:
```fsharp
namespace FSharp.ATProto.Bluesky

module Identity =
    let placeholder = ()
```

`src/FSharp.ATProto.Bluesky/Bluesky.fs`:
```fsharp
namespace FSharp.ATProto.Bluesky

module Bluesky =
    let placeholder = ()
```

**Step 5: Update Bluesky fsproj — add source files after Generated.fs**

In `src/FSharp.ATProto.Bluesky/FSharp.ATProto.Bluesky.fsproj`, the `<Compile>` items should be:
```xml
<Compile Include="Generated/Generated.fs" />
<Compile Include="RichText.fs" />
<Compile Include="Identity.fs" />
<Compile Include="Bluesky.fs" />
```

**Step 6: Add test project to solution**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet sln add tests/FSharp.ATProto.Bluesky.Tests/FSharp.ATProto.Bluesky.Tests.fsproj --solution-folder tests
```

**Step 7: Create empty test files (placeholders)**

`tests/FSharp.ATProto.Bluesky.Tests/RichTextTests.fs`:
```fsharp
module FSharp.ATProto.Bluesky.Tests.RichTextTests

open Expecto

[<Tests>]
let tests = testList "RichText" []
```

`tests/FSharp.ATProto.Bluesky.Tests/IdentityTests.fs`:
```fsharp
module FSharp.ATProto.Bluesky.Tests.IdentityTests

open Expecto

[<Tests>]
let tests = testList "Identity" []
```

`tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs`:
```fsharp
module FSharp.ATProto.Bluesky.Tests.BlueskyTests

open Expecto

[<Tests>]
let tests = testList "Bluesky" []
```

**Step 8: Build and verify everything compiles**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet build
```

**Step 9: Run tests to verify discovery works (0 tests is fine)**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.Bluesky.Tests
```

**Step 10: Commit**

```bash
git add -A && git commit -m "Scaffold Phase 6: Bluesky.Tests project + empty RichText/Identity/Bluesky modules"
```

---

## Task 2: RichText — Facet Detection (Pure)

Implement `RichText.detect` which finds mentions, links, and hashtags in text and returns byte-indexed positions. This is a pure function — no I/O.

**Files:**
- Create: `tests/FSharp.ATProto.Bluesky.Tests/RichTextTests.fs` (replace placeholder)
- Modify: `src/FSharp.ATProto.Bluesky/RichText.fs`

**Step 1: Write failing tests for detect**

Replace `RichTextTests.fs` with:

```fsharp
module FSharp.ATProto.Bluesky.Tests.RichTextTests

open Expecto
open FSharp.ATProto.Bluesky

[<Tests>]
let detectTests =
    testList "RichText.detect" [
        testCase "detects mention in text" <| fun _ ->
            let facets = RichText.detect "Hello @alice.bsky.social!"
            Expect.equal facets.Length 1 "should find one facet"
            match facets.[0] with
            | RichText.DetectedMention (s, e, h) ->
                Expect.equal s 6 "byteStart"
                Expect.equal e 26 "byteEnd"
                Expect.equal h "alice.bsky.social" "handle"
            | _ -> failtest "expected mention"

        testCase "detects link in text" <| fun _ ->
            let facets = RichText.detect "Check https://example.com ok"
            Expect.equal facets.Length 1 "should find one facet"
            match facets.[0] with
            | RichText.DetectedLink (s, e, u) ->
                Expect.equal s 6 "byteStart"
                Expect.equal e 26 "byteEnd"
                Expect.equal u "https://example.com" "uri"
            | _ -> failtest "expected link"

        testCase "detects hashtag in text" <| fun _ ->
            let facets = RichText.detect "Hello #atproto world"
            Expect.equal facets.Length 1 "should find one facet"
            match facets.[0] with
            | RichText.DetectedTag (s, e, t) ->
                Expect.equal s 6 "byteStart"
                Expect.equal e 14 "byteEnd"
                Expect.equal t "atproto" "tag"
            | _ -> failtest "expected tag"

        testCase "detects multiple facets" <| fun _ ->
            let facets = RichText.detect "Hi @alice.bsky.social check #atproto"
            Expect.equal facets.Length 2 "should find two facets"

        testCase "no facets in plain text" <| fun _ ->
            let facets = RichText.detect "Hello world"
            Expect.equal facets.Length 0 "should find no facets"

        testCase "mention must have dot (no bare @word)" <| fun _ ->
            let facets = RichText.detect "Hello @alice"
            Expect.equal facets.Length 0 "bare @word is not a mention"

        testCase "correct byte offsets with emoji" <| fun _ ->
            // 👋 is 4 bytes in UTF-8
            let facets = RichText.detect "👋 @alice.bsky.social"
            Expect.equal facets.Length 1 "should find one facet"
            match facets.[0] with
            | RichText.DetectedMention (s, e, _) ->
                Expect.equal s 5 "byteStart (4 bytes emoji + 1 byte space)"
                Expect.equal e 25 "byteEnd"
            | _ -> failtest "expected mention"

        testCase "correct byte offsets with accented chars" <| fun _ ->
            // ã is 2 bytes in UTF-8, ç is 2 bytes
            let facets = RichText.detect "Posição @alice.bsky.social"
            Expect.equal facets.Length 1 "should find one facet"
            match facets.[0] with
            | RichText.DetectedMention (s, e, _) ->
                // "Posição " = P(1)+o(1)+s(1)+i(1)+ç(2)+ã(2)+o(1)+ (1) = 10 bytes
                Expect.equal s 10 "byteStart"
                Expect.equal e 30 "byteEnd"
            | _ -> failtest "expected mention"

        testCase "strips trailing punctuation from links" <| fun _ ->
            let facets = RichText.detect "See https://example.com."
            match facets.[0] with
            | RichText.DetectedLink (_, _, u) ->
                Expect.equal u "https://example.com" "trailing period stripped"
            | _ -> failtest "expected link"

        testCase "hashtag excludes pure numeric" <| fun _ ->
            let facets = RichText.detect "Test #123"
            Expect.equal facets.Length 0 "pure numeric hashtag excluded"

        testCase "mention at start of text" <| fun _ ->
            let facets = RichText.detect "@alice.bsky.social hello"
            Expect.equal facets.Length 1 "mention at start"
            match facets.[0] with
            | RichText.DetectedMention (s, _, _) ->
                Expect.equal s 0 "byteStart at 0"
            | _ -> failtest "expected mention"

        testCase "link with path and query" <| fun _ ->
            let facets = RichText.detect "Go to https://example.com/path?q=1 now"
            match facets.[0] with
            | RichText.DetectedLink (_, _, u) ->
                Expect.equal u "https://example.com/path?q=1" "full URL preserved"
            | _ -> failtest "expected link"

        testCase "hashtag with fullwidth hash" <| fun _ ->
            // ＃ (U+FF03) is 3 bytes in UTF-8
            let facets = RichText.detect "Hello ＃atproto"
            Expect.equal facets.Length 1 "fullwidth hash detected"
            match facets.[0] with
            | RichText.DetectedTag (_, _, t) ->
                Expect.equal t "atproto" "tag without hash prefix"
            | _ -> failtest "expected tag"
    ]
```

**Step 2: Run tests to verify they fail**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "RichText.detect"
```

Expected: compilation error (DetectedMention not defined, detect not defined).

**Step 3: Implement RichText.detect**

Replace `src/FSharp.ATProto.Bluesky/RichText.fs`:

```fsharp
namespace FSharp.ATProto.Bluesky

open System
open System.Text
open System.Text.RegularExpressions

module RichText =

    type DetectedFacet =
        | DetectedMention of byteStart: int * byteEnd: int * handle: string
        | DetectedLink of byteStart: int * byteEnd: int * uri: string
        | DetectedTag of byteStart: int * byteEnd: int * tag: string

    let private charIndexToByteIndex (text: string) (charIndex: int) =
        Encoding.UTF8.GetByteCount(text, 0, charIndex)

    let private mentionRegex =
        Regex(@"(?:^|[\s(])(@([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)", RegexOptions.Compiled)

    let private linkRegex =
        Regex(@"(?:^|[\s(])(https?://[^\s)\]]*)", RegexOptions.Compiled)

    let private hashtagRegex =
        Regex(@"(?:^|[\s])[#\uFF03](\S*[^\d\s\p{P}]\S*)", RegexOptions.Compiled)

    let private trailingPunctuation = Regex(@"[.,;:!?]+$", RegexOptions.Compiled)

    let private detectMentions (text: string) =
        [ for m in mentionRegex.Matches(text) do
            // Find the @handle part within the match
            let fullMatch = m.Value
            let atIndex = fullMatch.IndexOf('@')
            let handle = fullMatch.Substring(atIndex + 1)
            let charStart = m.Index + atIndex
            let charEnd = charStart + handle.Length + 1 // +1 for @
            let byteStart = charIndexToByteIndex text charStart
            let byteEnd = charIndexToByteIndex text charEnd
            DetectedMention(byteStart, byteEnd, handle) ]

    let private detectLinks (text: string) =
        [ for m in linkRegex.Matches(text) do
            let rawUrl =
                let v = m.Groups.[1].Value
                trailingPunctuation.Replace(v, "")
            let charStart = m.Groups.[1].Index
            let charEnd = charStart + m.Groups.[1].Length
            // Recalculate end based on cleaned URL
            let cleanCharEnd = charStart + rawUrl.Length
            let byteStart = charIndexToByteIndex text charStart
            let byteEnd = charIndexToByteIndex text cleanCharEnd
            DetectedLink(byteStart, byteEnd, rawUrl) ]

    let private detectHashtags (text: string) =
        [ for m in hashtagRegex.Matches(text) do
            let tag = m.Groups.[1].Value |> fun t -> trailingPunctuation.Replace(t, "")
            if tag.Length > 0 then
                // Find the # character position
                let fullMatch = m.Value
                let hashIndex = fullMatch.IndexOfAny([| '#'; '\uFF03' |])
                let charStart = m.Index + hashIndex
                let charEnd = charStart + 1 + tag.Length // +1 for # char
                // Adjust for fullwidth # which is 3 bytes but 1 char
                let byteStart = charIndexToByteIndex text charStart
                let byteEnd = charIndexToByteIndex text charEnd
                DetectedTag(byteStart, byteEnd, tag) ]

    let detect (text: string) : DetectedFacet list =
        let mentions = detectMentions text
        let links = detectLinks text
        let tags = detectHashtags text
        mentions @ links @ tags
        |> List.sortBy (fun f ->
            match f with
            | DetectedMention (s, _, _) -> s
            | DetectedLink (s, _, _) -> s
            | DetectedTag (s, _, _) -> s)
```

**Step 4: Run tests to verify they pass**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "RichText.detect"
```

Expected: all tests pass. Some may need regex tuning — iterate until green.

**Step 5: Add FsCheck property tests**

Add to the bottom of `RichTextTests.fs`:

```fsharp
open FsCheck

[<Tests>]
let propertyTests =
    testList "RichText.detect properties" [
        testProperty "byte ranges within text bounds" <| fun (text: NonNull<string>) ->
            let facets = RichText.detect text.Get
            let totalBytes = Encoding.UTF8.GetByteCount(text.Get)
            facets |> List.iter (fun f ->
                let s, e = match f with
                           | RichText.DetectedMention (s, e, _) -> s, e
                           | RichText.DetectedLink (s, e, _) -> s, e
                           | RichText.DetectedTag (s, e, _) -> s, e
                Expect.isLessThanOrEqual s totalBytes "start within bounds"
                Expect.isLessThanOrEqual e totalBytes "end within bounds"
                Expect.isLessThan s e "start < end")

        testProperty "detected facets are non-overlapping and sorted" <| fun (text: NonNull<string>) ->
            let facets = RichText.detect text.Get
            let ranges = facets |> List.map (fun f ->
                match f with
                | RichText.DetectedMention (s, e, _) -> s, e
                | RichText.DetectedLink (s, e, _) -> s, e
                | RichText.DetectedTag (s, e, _) -> s, e)
            ranges |> List.pairwise |> List.iter (fun ((_, e1), (s2, _)) ->
                Expect.isLessThanOrEqual e1 s2 "non-overlapping")
    ]
```

**Step 6: Run all RichText tests**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "RichText"
```

**Step 7: Commit**

```bash
git add -A && git commit -m "Add RichText.detect: facet detection with UTF-8 byte indexing"
```

---

## Task 3: RichText — Resolve + Parse + Utilities

Add `RichText.resolve` (async handle→DID resolution), `RichText.parse` (detect+resolve), `graphemeLength`, and `byteLength`.

**Files:**
- Modify: `tests/FSharp.ATProto.Bluesky.Tests/RichTextTests.fs`
- Modify: `src/FSharp.ATProto.Bluesky/RichText.fs`

**Step 1: Write failing tests**

Add to `RichTextTests.fs`:

```fsharp
open System.Net
open System.Text.Json
open System.Threading.Tasks
open FSharp.ATProto.Core
open TestHelpers

[<Tests>]
let resolveTests =
    testList "RichText.resolve" [
        testCase "resolves mention handle to DID" <| fun _ ->
            let agent = createMockAgent (fun req ->
                if req.RequestUri.PathAndQuery.Contains("resolveHandle") then
                    jsonResponse HttpStatusCode.OK {| did = "did:plc:abc123" |}
                else
                    emptyResponse HttpStatusCode.NotFound)
            agent.Session <- Some { AccessJwt = "test"; RefreshJwt = "test"; Did = "did:plc:me"; Handle = "me.bsky.social" }
            let detected = [ RichText.DetectedMention(0, 18, "alice.bsky.social") ]
            let facets = RichText.resolve agent detected |> Async.AwaitTask |> Async.RunSynchronously
            Expect.equal facets.Length 1 "one facet"
            // Verify the facet has correct byte slice and mention DID
            Expect.equal facets.[0].Index.ByteStart 0L "byteStart"
            Expect.equal facets.[0].Index.ByteEnd 18L "byteEnd"

        testCase "drops mention when handle resolution fails" <| fun _ ->
            let agent = createMockAgent (fun _ ->
                jsonResponse HttpStatusCode.BadRequest {| error = "HandleNotFound"; message = "not found" |})
            agent.Session <- Some { AccessJwt = "test"; RefreshJwt = "test"; Did = "did:plc:me"; Handle = "me.bsky.social" }
            let detected = [ RichText.DetectedMention(0, 18, "alice.bsky.social") ]
            let facets = RichText.resolve agent detected |> Async.AwaitTask |> Async.RunSynchronously
            Expect.equal facets.Length 0 "mention dropped on failure"

        testCase "passes through links and tags without resolution" <| fun _ ->
            let agent = createMockAgent (fun _ -> emptyResponse HttpStatusCode.NotFound)
            let detected = [
                RichText.DetectedLink(0, 20, "https://example.com")
                RichText.DetectedTag(21, 29, "atproto")
            ]
            let facets = RichText.resolve agent detected |> Async.AwaitTask |> Async.RunSynchronously
            Expect.equal facets.Length 2 "both facets preserved"

        testCase "parse detects and resolves in one step" <| fun _ ->
            let agent = createMockAgent (fun req ->
                if req.RequestUri.PathAndQuery.Contains("resolveHandle") then
                    jsonResponse HttpStatusCode.OK {| did = "did:plc:abc123" |}
                else
                    emptyResponse HttpStatusCode.NotFound)
            agent.Session <- Some { AccessJwt = "test"; RefreshJwt = "test"; Did = "did:plc:me"; Handle = "me.bsky.social" }
            let facets = RichText.parse agent "Hello @alice.bsky.social #atproto" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.equal facets.Length 2 "mention + hashtag"
    ]

[<Tests>]
let utilityTests =
    testList "RichText utilities" [
        testCase "graphemeLength counts grapheme clusters" <| fun _ ->
            Expect.equal (RichText.graphemeLength "Hello") 5 "ASCII"
            Expect.equal (RichText.graphemeLength "👋🏽") 1 "emoji with skin tone = 1 grapheme"
            Expect.equal (RichText.graphemeLength "café") 4 "accented"

        testCase "byteLength counts UTF-8 bytes" <| fun _ ->
            Expect.equal (RichText.byteLength "Hello") 5 "ASCII"
            Expect.equal (RichText.byteLength "👋") 4 "emoji"
            Expect.equal (RichText.byteLength "café") 5 "é is 2 bytes"
    ]
```

**Step 2: Run to verify failures**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "RichText.resolve|RichText utilities"
```

**Step 3: Implement resolve, parse, graphemeLength, byteLength**

Add to `RichText.fs` after the `detect` function:

```fsharp
    open System.Globalization
    open System.Text.Json
    open System.Threading.Tasks
    open FSharp.ATProto.Core

    let private makeFacet (byteStart: int) (byteEnd: int) (feature: JsonElement) : AppBskyRichtext.Facet.Facet =
        { Index = { ByteStart = int64 byteStart; ByteEnd = int64 byteEnd }
          Features = [ feature ] }

    let private serializeFeature (typeName: string) (fields: (string * obj) list) : JsonElement =
        let dict = System.Collections.Generic.Dictionary<string, obj>()
        dict.["$type"] <- typeName
        for (k, v) in fields do dict.[k] <- v
        JsonSerializer.SerializeToElement(dict)

    let resolve (agent: AtpAgent) (detected: DetectedFacet list) : Task<AppBskyRichtext.Facet.Facet list> =
        task {
            let results = System.Collections.Generic.List<AppBskyRichtext.Facet.Facet>()
            for facet in detected do
                match facet with
                | DetectedMention (s, e, handle) ->
                    let! result = ComAtprotoIdentity.ResolveHandle.query agent { Handle = handle }
                    match result with
                    | Ok output ->
                        let feature = serializeFeature "app.bsky.richtext.facet#mention" [ "did", output.Did ]
                        results.Add(makeFacet s e feature)
                    | Error _ -> () // silently drop failed mentions
                | DetectedLink (s, e, uri) ->
                    let feature = serializeFeature "app.bsky.richtext.facet#link" [ "uri", uri ]
                    results.Add(makeFacet s e feature)
                | DetectedTag (s, e, tag) ->
                    let feature = serializeFeature "app.bsky.richtext.facet#tag" [ "tag", tag ]
                    results.Add(makeFacet s e feature)
            return results |> Seq.toList
        }

    let parse (agent: AtpAgent) (text: string) : Task<AppBskyRichtext.Facet.Facet list> =
        task {
            let detected = detect text
            return! resolve agent detected
        }

    let graphemeLength (text: string) : int =
        let info = StringInfo(text)
        info.LengthInTextElements

    let byteLength (text: string) : int =
        Encoding.UTF8.GetByteCount(text)
```

**Step 4: Run tests**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "RichText"
```

**Step 5: Commit**

```bash
git add -A && git commit -m "Add RichText.resolve, parse, graphemeLength, byteLength"
```

---

## Task 4: Identity — DID Document Parsing (Pure)

Implement `Identity.parseDidDocument` which extracts AT Protocol fields from a DID document JSON.

**Files:**
- Modify: `tests/FSharp.ATProto.Bluesky.Tests/IdentityTests.fs`
- Modify: `src/FSharp.ATProto.Bluesky/Identity.fs`

**Step 1: Write failing tests**

Replace `IdentityTests.fs`:

```fsharp
module FSharp.ATProto.Bluesky.Tests.IdentityTests

open Expecto
open System.Text.Json
open FSharp.ATProto.Bluesky

let private parseJson (json: string) =
    JsonSerializer.Deserialize<JsonElement>(json)

[<Tests>]
let parseTests =
    testList "Identity.parseDidDocument" [
        testCase "parses full PLC DID document" <| fun _ ->
            let doc = parseJson """{
                "id": "did:plc:z72i7hdynmk6r22z27h6tvur",
                "alsoKnownAs": ["at://bsky.app"],
                "verificationMethod": [{
                    "id": "did:plc:z72i7hdynmk6r22z27h6tvur#atproto",
                    "type": "Multikey",
                    "controller": "did:plc:z72i7hdynmk6r22z27h6tvur",
                    "publicKeyMultibase": "zQ3shQo6TF2moaqMTrUZEM1jeuYRQXeHEx4evX9751y2qPqRA"
                }],
                "service": [{
                    "id": "#atproto_pds",
                    "type": "AtprotoPersonalDataServer",
                    "serviceEndpoint": "https://puffball.us-east.host.bsky.network"
                }]
            }"""
            let result = Identity.parseDidDocument doc
            let identity = Expect.wantOk result "should parse"
            Expect.equal identity.Did "did:plc:z72i7hdynmk6r22z27h6tvur" "did"
            Expect.equal identity.Handle (Some "bsky.app") "handle"
            Expect.equal identity.PdsEndpoint (Some "https://puffball.us-east.host.bsky.network") "pds"
            Expect.equal identity.SigningKey (Some "zQ3shQo6TF2moaqMTrUZEM1jeuYRQXeHEx4evX9751y2qPqRA") "key"

        testCase "handles missing optional fields" <| fun _ ->
            let doc = parseJson """{"id": "did:plc:test123"}"""
            let result = Identity.parseDidDocument doc
            let identity = Expect.wantOk result "should parse"
            Expect.equal identity.Did "did:plc:test123" "did"
            Expect.isNone identity.Handle "no handle"
            Expect.isNone identity.PdsEndpoint "no pds"
            Expect.isNone identity.SigningKey "no key"

        testCase "extracts handle from at:// URI" <| fun _ ->
            let doc = parseJson """{
                "id": "did:plc:test",
                "alsoKnownAs": ["https://other.example", "at://alice.example.com"]
            }"""
            let identity = Identity.parseDidDocument doc |> Result.defaultWith failwith
            Expect.equal identity.Handle (Some "alice.example.com") "extracts handle from at:// entry"

        testCase "finds atproto service by fragment id" <| fun _ ->
            let doc = parseJson """{
                "id": "did:plc:test",
                "service": [
                    {"id": "#other_service", "type": "Other", "serviceEndpoint": "https://other.com"},
                    {"id": "#atproto_pds", "type": "AtprotoPersonalDataServer", "serviceEndpoint": "https://my.pds.com"}
                ]
            }"""
            let identity = Identity.parseDidDocument doc |> Result.defaultWith failwith
            Expect.equal identity.PdsEndpoint (Some "https://my.pds.com") "finds correct service"

        testCase "finds verification method by #atproto suffix" <| fun _ ->
            let doc = parseJson """{
                "id": "did:plc:test",
                "verificationMethod": [
                    {"id": "#other", "type": "Other", "publicKeyMultibase": "wrong"},
                    {"id": "did:plc:test#atproto", "type": "Multikey", "publicKeyMultibase": "zCorrectKey"}
                ]
            }"""
            let identity = Identity.parseDidDocument doc |> Result.defaultWith failwith
            Expect.equal identity.SigningKey (Some "zCorrectKey") "finds correct key"

        testCase "returns error when id field missing" <| fun _ ->
            let doc = parseJson """{"alsoKnownAs": []}"""
            let result = Identity.parseDidDocument doc
            Expect.isError result "should error without id"
    ]
```

**Step 2: Run to verify failures**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "Identity.parseDidDocument"
```

**Step 3: Implement Identity.parseDidDocument**

Replace `src/FSharp.ATProto.Bluesky/Identity.fs`:

```fsharp
namespace FSharp.ATProto.Bluesky

open System
open System.Text.Json

module Identity =

    type AtprotoIdentity =
        { Did: string
          Handle: string option
          PdsEndpoint: string option
          SigningKey: string option }

    let private tryGetString (element: JsonElement) (prop: string) =
        match element.TryGetProperty(prop) with
        | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
        | _ -> None

    let private tryGetArray (element: JsonElement) (prop: string) =
        match element.TryGetProperty(prop) with
        | true, v when v.ValueKind = JsonValueKind.Array -> Some(v.EnumerateArray() |> Seq.toList)
        | _ -> None

    let private extractHandle (doc: JsonElement) =
        tryGetArray doc "alsoKnownAs"
        |> Option.bind (fun entries ->
            entries
            |> List.tryPick (fun e ->
                if e.ValueKind = JsonValueKind.String then
                    let s = e.GetString()
                    if s.StartsWith("at://") then Some(s.Substring(5))
                    else None
                else None))

    let private extractPdsEndpoint (doc: JsonElement) =
        tryGetArray doc "service"
        |> Option.bind (fun services ->
            services
            |> List.tryPick (fun svc ->
                let id = tryGetString svc "id"
                let typ = tryGetString svc "type"
                let endpoint = tryGetString svc "serviceEndpoint"
                match id, typ, endpoint with
                | Some id, Some "AtprotoPersonalDataServer", Some ep
                    when id.EndsWith("#atproto_pds") -> Some ep
                | _ -> None))

    let private extractSigningKey (doc: JsonElement) =
        tryGetArray doc "verificationMethod"
        |> Option.bind (fun methods ->
            methods
            |> List.tryPick (fun vm ->
                let id = tryGetString vm "id"
                let key = tryGetString vm "publicKeyMultibase"
                match id, key with
                | Some id, Some k when id.EndsWith("#atproto") -> Some k
                | _ -> None))

    let parseDidDocument (doc: JsonElement) : Result<AtprotoIdentity, string> =
        match tryGetString doc "id" with
        | None -> Error "DID document missing 'id' field"
        | Some did ->
            Ok { Did = did
                 Handle = extractHandle doc
                 PdsEndpoint = extractPdsEndpoint doc
                 SigningKey = extractSigningKey doc }
```

**Step 4: Run tests**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "Identity"
```

**Step 5: Commit**

```bash
git add -A && git commit -m "Add Identity.parseDidDocument: extract handle, PDS endpoint, signing key from DID docs"
```

---

## Task 5: Identity — DID + Handle Resolution (Async)

Implement `resolveDid`, `resolveHandle`, and `resolveIdentity` with bidirectional verification.

**Files:**
- Modify: `tests/FSharp.ATProto.Bluesky.Tests/IdentityTests.fs`
- Modify: `src/FSharp.ATProto.Bluesky/Identity.fs`

**Step 1: Write failing tests**

Add to `IdentityTests.fs`:

```fsharp
open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks
open FSharp.ATProto.Core
open TestHelpers

let private plcDidDoc = """{
    "id": "did:plc:abc123",
    "alsoKnownAs": ["at://alice.example.com"],
    "verificationMethod": [{"id": "#atproto", "type": "Multikey", "publicKeyMultibase": "zKey123"}],
    "service": [{"id": "#atproto_pds", "type": "AtprotoPersonalDataServer", "serviceEndpoint": "https://pds.example.com"}]
}"""

[<Tests>]
let resolveTests =
    testList "Identity resolution" [
        testCase "resolveDid resolves did:plc via PLC directory" <| fun _ ->
            let agent = createMockAgent (fun req ->
                if req.RequestUri.Host = "plc.directory" then
                    jsonResponse HttpStatusCode.OK (JsonSerializer.Deserialize<JsonElement>(plcDidDoc))
                else
                    emptyResponse HttpStatusCode.NotFound)
            let result = Identity.resolveDid agent "did:plc:abc123" |> Async.AwaitTask |> Async.RunSynchronously
            let identity = Expect.wantOk result "should resolve"
            Expect.equal identity.Did "did:plc:abc123" "did"
            Expect.equal identity.Handle (Some "alice.example.com") "handle"
            Expect.equal identity.PdsEndpoint (Some "https://pds.example.com") "pds"

        testCase "resolveDid resolves did:web via .well-known" <| fun _ ->
            let webDidDoc = """{"id": "did:web:bob.example.com", "alsoKnownAs": ["at://bob.example.com"]}"""
            let agent = createMockAgent (fun req ->
                if req.RequestUri.PathAndQuery.Contains(".well-known/did.json") then
                    jsonResponse HttpStatusCode.OK (JsonSerializer.Deserialize<JsonElement>(webDidDoc))
                else
                    emptyResponse HttpStatusCode.NotFound)
            let result = Identity.resolveDid agent "did:web:bob.example.com" |> Async.AwaitTask |> Async.RunSynchronously
            let identity = Expect.wantOk result "should resolve"
            Expect.equal identity.Did "did:web:bob.example.com" "did"

        testCase "resolveDid returns error for unsupported method" <| fun _ ->
            let agent = createMockAgent (fun _ -> emptyResponse HttpStatusCode.NotFound)
            let result = Identity.resolveDid agent "did:key:abc" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isError result "unsupported DID method"

        testCase "resolveHandle calls XRPC resolveHandle" <| fun _ ->
            let agent = createMockAgent (fun req ->
                if req.RequestUri.PathAndQuery.Contains("resolveHandle") then
                    jsonResponse HttpStatusCode.OK {| did = "did:plc:abc123" |}
                else
                    emptyResponse HttpStatusCode.NotFound)
            agent.Session <- Some { AccessJwt = "t"; RefreshJwt = "t"; Did = "did:plc:me"; Handle = "me.test" }
            let result = Identity.resolveHandle agent "alice.example.com" |> Async.AwaitTask |> Async.RunSynchronously
            let did = Expect.wantOk result "should resolve"
            Expect.equal did "did:plc:abc123" "resolved DID"

        testCase "resolveIdentity does bidirectional verification from handle" <| fun _ ->
            let mutable callCount = 0
            let agent = createMockAgent (fun req ->
                callCount <- callCount + 1
                if req.RequestUri.PathAndQuery.Contains("resolveHandle") then
                    jsonResponse HttpStatusCode.OK {| did = "did:plc:abc123" |}
                elif req.RequestUri.Host = "plc.directory" then
                    jsonResponse HttpStatusCode.OK (JsonSerializer.Deserialize<JsonElement>(plcDidDoc))
                else
                    emptyResponse HttpStatusCode.NotFound)
            agent.Session <- Some { AccessJwt = "t"; RefreshJwt = "t"; Did = "did:plc:me"; Handle = "me.test" }
            let result = Identity.resolveIdentity agent "alice.example.com" |> Async.AwaitTask |> Async.RunSynchronously
            let identity = Expect.wantOk result "should resolve"
            Expect.equal identity.Did "did:plc:abc123" "did"
            Expect.equal identity.Handle (Some "alice.example.com") "verified handle"

        testCase "resolveIdentity clears handle when bidirectional check fails" <| fun _ ->
            // resolveHandle returns did:plc:abc123, but DID doc says handle is "other.com"
            let mismatchDoc = """{"id": "did:plc:abc123", "alsoKnownAs": ["at://other.com"]}"""
            let agent = createMockAgent (fun req ->
                if req.RequestUri.PathAndQuery.Contains("resolveHandle") then
                    jsonResponse HttpStatusCode.OK {| did = "did:plc:abc123" |}
                elif req.RequestUri.Host = "plc.directory" then
                    jsonResponse HttpStatusCode.OK (JsonSerializer.Deserialize<JsonElement>(mismatchDoc))
                else
                    emptyResponse HttpStatusCode.NotFound)
            agent.Session <- Some { AccessJwt = "t"; RefreshJwt = "t"; Did = "did:plc:me"; Handle = "me.test" }
            let result = Identity.resolveIdentity agent "alice.example.com" |> Async.AwaitTask |> Async.RunSynchronously
            let identity = Expect.wantOk result "should resolve but with no handle"
            Expect.isNone identity.Handle "handle cleared due to mismatch"
    ]
```

**Step 2: Run to verify failures**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "Identity resolution"
```

**Step 3: Implement resolution functions**

Add to `Identity.fs` after `parseDidDocument`:

```fsharp
    open System.Net.Http
    open System.Threading.Tasks
    open FSharp.ATProto.Core

    let private plcDirectoryUrl = "https://plc.directory"

    let resolveDid (agent: AtpAgent) (did: string) : Task<Result<AtprotoIdentity, string>> =
        task {
            if did.StartsWith("did:plc:") then
                let url = $"{plcDirectoryUrl}/{did}"
                let! response = agent.HttpClient.GetAsync(url)
                if response.IsSuccessStatusCode then
                    let! json = response.Content.ReadAsStringAsync()
                    let doc = JsonSerializer.Deserialize<JsonElement>(json)
                    return parseDidDocument doc
                else
                    return Error $"PLC directory returned {int response.StatusCode} for {did}"
            elif did.StartsWith("did:web:") then
                let domain = did.Substring(8)
                let url = $"https://{domain}/.well-known/did.json"
                let! response = agent.HttpClient.GetAsync(url)
                if response.IsSuccessStatusCode then
                    let! json = response.Content.ReadAsStringAsync()
                    let doc = JsonSerializer.Deserialize<JsonElement>(json)
                    return parseDidDocument doc
                else
                    return Error $"did:web resolution returned {int response.StatusCode} for {did}"
            else
                return Error $"Unsupported DID method: {did}"
        }

    let resolveHandle (agent: AtpAgent) (handle: string) : Task<Result<string, XrpcError>> =
        task {
            let! result = ComAtprotoIdentity.ResolveHandle.query agent { Handle = handle }
            return result |> Result.map (fun o -> o.Did)
        }

    let resolveIdentity (agent: AtpAgent) (identifier: string) : Task<Result<AtprotoIdentity, string>> =
        task {
            // Determine if identifier is a DID or a handle
            let isDid = identifier.StartsWith("did:")
            if isDid then
                let! identity = resolveDid agent identifier
                match identity with
                | Error e -> return Error e
                | Ok id ->
                    // Bidirectional: check handle resolves back to this DID
                    match id.Handle with
                    | None -> return Ok id
                    | Some handle ->
                        let! reverseResult = resolveHandle agent handle
                        match reverseResult with
                        | Ok reverseDid when reverseDid = identifier -> return Ok id
                        | _ -> return Ok { id with Handle = None }
            else
                // identifier is a handle
                let! handleResult = resolveHandle agent identifier
                match handleResult with
                | Error e -> return Error $"Handle resolution failed: {e.Error |> Option.defaultValue "unknown"}"
                | Ok did ->
                    let! identity = resolveDid agent did
                    match identity with
                    | Error e -> return Error e
                    | Ok id ->
                        // Bidirectional: check DID doc's handle matches
                        if id.Handle = Some identifier then return Ok id
                        else return Ok { id with Handle = None }
        }
```

**Step 4: Run tests**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "Identity"
```

**Step 5: Commit**

```bash
git add -A && git commit -m "Add Identity resolution: resolveDid, resolveHandle, resolveIdentity with bidirectional verification"
```

---

## Task 6: Bluesky Convenience — Record Creation + Deletion

Implement `post`, `postWith`, `like`, `repost`, `follow`, `block`, and `deleteRecord`.

**Files:**
- Modify: `tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs`
- Modify: `src/FSharp.ATProto.Bluesky/Bluesky.fs`

**Step 1: Write failing tests**

Replace `BlueskyTests.fs`:

```fsharp
module FSharp.ATProto.Bluesky.Tests.BlueskyTests

open Expecto
open System
open System.Net
open System.Text.Json
open System.Threading.Tasks
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open TestHelpers

let private testSession =
    { AccessJwt = "test-jwt"; RefreshJwt = "test-refresh"; Did = "did:plc:testuser"; Handle = "test.bsky.social" }

let private createRecordAgent (captureRequest: HttpRequestMessage -> unit) =
    let agent = createMockAgent (fun req ->
        captureRequest req
        jsonResponse HttpStatusCode.OK {| uri = "at://did:plc:testuser/app.bsky.feed.post/abc123"; cid = "bafyreiabc123" |})
    agent.Session <- Some testSession
    agent

let private deleteRecordAgent (captureRequest: HttpRequestMessage -> unit) =
    let agent = createMockAgent (fun req ->
        captureRequest req
        jsonResponse HttpStatusCode.OK {| |})
    agent.Session <- Some testSession
    agent

[<Tests>]
let postTests =
    testList "Bluesky.post" [
        testCase "postWith creates post with correct collection" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.postWith agent "Hello world" [] |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let req = captured.Value
            let body = req.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.feed.post" "collection in body"
            Expect.stringContains body "did:plc:testuser" "repo = session DID"
            Expect.stringContains body "Hello world" "text in record"

        testCase "postWith includes facets when provided" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let facets : AppBskyRichtext.Facet.Facet list = [
                { Index = { ByteStart = 0L; ByteEnd = 5L }
                  Features = [ JsonSerializer.SerializeToElement({| ``$type`` = "app.bsky.richtext.facet#tag"; tag = "hello" |}) ] }
            ]
            let result = Bluesky.postWith agent "#hello world" facets |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
    ]

[<Tests>]
let likeTests =
    testList "Bluesky.like" [
        testCase "like creates record with correct collection and subject" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.like agent "at://did:plc:other/app.bsky.feed.post/abc" "bafyreiabc" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.feed.like" "like collection"
            Expect.stringContains body "bafyreiabc" "cid in subject"

        testCase "repost creates record with correct collection" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.repost agent "at://did:plc:other/app.bsky.feed.post/abc" "bafyreiabc" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.feed.repost" "repost collection"
    ]

[<Tests>]
let followTests =
    testList "Bluesky.follow" [
        testCase "follow creates record with DID subject" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.follow agent "did:plc:other" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.graph.follow" "follow collection"
            Expect.stringContains body "did:plc:other" "subject DID"

        testCase "block creates record with DID subject" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.block agent "did:plc:other" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.graph.block" "block collection"
    ]

[<Tests>]
let deleteTests =
    testList "Bluesky.deleteRecord" [
        testCase "deleteRecord parses AT-URI and sends correct request" <| fun _ ->
            let mutable captured = None
            let agent = deleteRecordAgent (fun req -> captured <- Some req)
            let result = Bluesky.deleteRecord agent "at://did:plc:testuser/app.bsky.feed.post/abc123" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "app.bsky.feed.post" "collection"
            Expect.stringContains body "abc123" "rkey"
            Expect.stringContains body "did:plc:testuser" "repo"
    ]

[<Tests>]
let replyTests =
    testList "Bluesky.reply" [
        testCase "reply includes root and parent refs" <| fun _ ->
            let mutable captured = None
            let agent = createRecordAgent (fun req -> captured <- Some req)
            let result =
                Bluesky.reply agent "A reply"
                    "at://did:plc:p/app.bsky.feed.post/parent" "bafyparent"
                    "at://did:plc:r/app.bsky.feed.post/root" "bafyroot"
                |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let body = captured.Value.Content.ReadAsStringAsync().Result
            Expect.stringContains body "bafyparent" "parent cid"
            Expect.stringContains body "bafyroot" "root cid"
    ]
```

**Step 2: Run to verify failures**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "Bluesky"
```

**Step 3: Implement convenience methods**

Replace `src/FSharp.ATProto.Bluesky/Bluesky.fs`:

```fsharp
namespace FSharp.ATProto.Bluesky

open System
open System.Text.Json
open System.Threading.Tasks
open FSharp.ATProto.Core

module Bluesky =

    let private nowTimestamp () =
        DateTimeOffset.UtcNow.ToString("o")

    let private sessionDid (agent: AtpAgent) =
        match agent.Session with
        | Some s -> s.Did
        | None -> failwith "Not logged in"

    let private createRecord (agent: AtpAgent) (collection: string) (record: obj) =
        let recordElement = JsonSerializer.SerializeToElement(record, Json.options)
        ComAtprotoRepo.CreateRecord.call agent
            { Repo = sessionDid agent
              Collection = collection
              Record = recordElement
              Rkey = None
              SwapCommit = None
              Validate = None }

    let postWith (agent: AtpAgent) (text: string) (facets: AppBskyRichtext.Facet.Facet list)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        let record =
            {| ``$type`` = AppBskyFeed.Post.TypeId
               text = text
               createdAt = nowTimestamp ()
               facets = if facets.IsEmpty then null else facets |> box |}
        createRecord agent "app.bsky.feed.post" record

    let post (agent: AtpAgent) (text: string)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        task {
            let! facets = RichText.parse agent text
            return! postWith agent text facets
        }

    let reply (agent: AtpAgent) (text: string) (parentUri: string) (parentCid: string) (rootUri: string) (rootCid: string)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        task {
            let! facets = RichText.parse agent text
            let record =
                {| ``$type`` = AppBskyFeed.Post.TypeId
                   text = text
                   createdAt = nowTimestamp ()
                   facets = if facets.IsEmpty then null else facets |> box
                   reply = {| parent = {| uri = parentUri; cid = parentCid |}
                              root = {| uri = rootUri; cid = rootCid |} |} |}
            return! createRecord agent "app.bsky.feed.post" record
        }

    let like (agent: AtpAgent) (uri: string) (cid: string)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        let record =
            {| ``$type`` = AppBskyFeed.Like.TypeId
               createdAt = nowTimestamp ()
               subject = {| uri = uri; cid = cid |} |}
        createRecord agent "app.bsky.feed.like" record

    let repost (agent: AtpAgent) (uri: string) (cid: string)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        let record =
            {| ``$type`` = AppBskyFeed.Repost.TypeId
               createdAt = nowTimestamp ()
               subject = {| uri = uri; cid = cid |} |}
        createRecord agent "app.bsky.feed.repost" record

    let follow (agent: AtpAgent) (did: string)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        let record =
            {| ``$type`` = AppBskyGraph.Follow.TypeId
               createdAt = nowTimestamp ()
               subject = did |}
        createRecord agent "app.bsky.graph.follow" record

    let block (agent: AtpAgent) (did: string)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        let record =
            {| ``$type`` = AppBskyGraph.Block.TypeId
               createdAt = nowTimestamp ()
               subject = did |}
        createRecord agent "app.bsky.graph.block" record

    let deleteRecord (agent: AtpAgent) (atUri: string)
        : Task<Result<unit, XrpcError>> =
        task {
            // Parse AT-URI: at://did/collection/rkey
            let parts = atUri.Replace("at://", "").Split('/')
            let repo = parts.[0]
            let collection = parts.[1]
            let rkey = parts.[2]
            let! result = ComAtprotoRepo.DeleteRecord.call agent
                            { Repo = repo
                              Collection = collection
                              Rkey = rkey
                              SwapCommit = None
                              SwapRecord = None }
            return result |> Result.map ignore
        }
```

**Step 4: Run tests**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "Bluesky"
```

**Step 5: Commit**

```bash
git add -A && git commit -m "Add Bluesky convenience methods: post, reply, like, repost, follow, block, deleteRecord"
```

---

## Task 7: Bluesky Convenience — Blob Upload + Post With Images

Implement `uploadBlob` and `postWithImages`. Blob upload requires raw binary POST (not JSON), so this needs custom HTTP handling.

**Files:**
- Modify: `tests/FSharp.ATProto.Bluesky.Tests/BlueskyTests.fs`
- Modify: `src/FSharp.ATProto.Bluesky/Bluesky.fs`

**Step 1: Write failing tests**

Add to `BlueskyTests.fs`:

```fsharp
[<Tests>]
let blobTests =
    testList "Bluesky.uploadBlob" [
        testCase "uploadBlob sends binary content with correct content type" <| fun _ ->
            let mutable captured = None
            let agent = createMockAgent (fun req ->
                captured <- Some req
                jsonResponse HttpStatusCode.OK {| blob = {| ``$type`` = "blob"; ref = {| ``$link`` = "bafyblob" |}; mimeType = "image/png"; size = 100 |} |})
            agent.Session <- Some testSession
            let data = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy |] // PNG header bytes
            let result = Bluesky.uploadBlob agent data "image/png" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            let req = captured.Value
            Expect.equal (req.Content.Headers.ContentType.MediaType) "image/png" "content type"
            Expect.equal (req.Method) System.Net.Http.HttpMethod.Post "POST method"
    ]

[<Tests>]
let imagePostTests =
    testList "Bluesky.postWithImages" [
        testCase "postWithImages uploads blob and creates post with embed" <| fun _ ->
            let mutable requestCount = 0
            let agent = createMockAgent (fun req ->
                requestCount <- requestCount + 1
                if req.RequestUri.PathAndQuery.Contains("uploadBlob") then
                    jsonResponse HttpStatusCode.OK
                        {| blob = {| ``$type`` = "blob"; ref = {| ``$link`` = "bafyblob" |}; mimeType = "image/png"; size = 100 |} |}
                else
                    jsonResponse HttpStatusCode.OK {| uri = "at://did:plc:testuser/app.bsky.feed.post/abc"; cid = "bafypost" |})
            agent.Session <- Some testSession
            let images = [ ([| 0x89uy; 0x50uy |], "image/png", "A test image") ]
            let result = Bluesky.postWithImages agent "Check this out" images |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed"
            Expect.isGreaterThanOrEqual requestCount 2 "at least 2 requests (upload + create)"
    ]
```

**Step 2: Run to verify failures**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "uploadBlob|postWithImages"
```

**Step 3: Implement uploadBlob and postWithImages**

Add to `Bluesky.fs` (before or after existing functions):

```fsharp
    let uploadBlob (agent: AtpAgent) (data: byte[]) (mimeType: string)
        : Task<Result<JsonElement, XrpcError>> =
        task {
            let url = Uri(agent.BaseUrl, $"/xrpc/{ComAtprotoRepo.UploadBlob.TypeId}")
            let request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url)
            request.Content <- new System.Net.Http.ByteArrayContent(data)
            request.Content.Headers.ContentType <- System.Net.Http.Headers.MediaTypeHeaderValue(mimeType)
            match agent.Session with
            | Some session ->
                request.Headers.Authorization <-
                    System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessJwt)
            | None -> ()
            let! response = agent.HttpClient.SendAsync(request)
            if response.IsSuccessStatusCode then
                let! json = response.Content.ReadAsStringAsync()
                let doc = JsonSerializer.Deserialize<JsonElement>(json)
                return Ok(doc.GetProperty("blob"))
            else
                let! errorJson = response.Content.ReadAsStringAsync()
                try
                    let err = JsonSerializer.Deserialize<XrpcError>(errorJson, Json.options)
                    return Error { err with StatusCode = int response.StatusCode }
                with _ ->
                    return Error { StatusCode = int response.StatusCode; Error = None; Message = Some errorJson }
        }

    let postWithImages (agent: AtpAgent) (text: string) (images: (byte[] * string * string) list)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        task {
            // Upload all blobs
            let mutable blobRefs = []
            for (data, mimeType, altText) in images do
                let! result = uploadBlob agent data mimeType
                match result with
                | Ok blob -> blobRefs <- blobRefs @ [ (blob, altText) ]
                | Error e -> return! Task.FromResult(Error e)

            let! facets = RichText.parse agent text
            let embed =
                {| ``$type`` = "app.bsky.embed.images"
                   images = blobRefs |> List.map (fun (blob, alt) ->
                    {| alt = alt; image = blob |}) |}
            let record =
                {| ``$type`` = AppBskyFeed.Post.TypeId
                   text = text
                   createdAt = nowTimestamp ()
                   facets = if facets.IsEmpty then null else facets |> box
                   embed = embed |}
            return! createRecord agent "app.bsky.feed.post" record
        }
```

**Step 4: Run tests**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test tests/FSharp.ATProto.Bluesky.Tests --filter "uploadBlob|postWithImages"
```

**Step 5: Run ALL tests across the entire solution**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test
```

Expected: all 1,414 existing tests + new Bluesky.Tests all pass.

**Step 6: Commit**

```bash
git add -A && git commit -m "Add Bluesky.uploadBlob and postWithImages convenience methods"
```

---

## Task 8: Final Polish + Run Full Suite

Update CLAUDE.md project structure, update memory, and verify everything.

**Files:**
- Modify: `CLAUDE.md` — update project structure and test counts
- Modify: memory file

**Step 1: Run full test suite and count**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet test --verbosity normal 2>&1 | tail -20
```

**Step 2: Update CLAUDE.md project structure**

Add the new project to the structure section and update test counts.

**Step 3: Commit**

```bash
git add -A && git commit -m "Update project docs for Phase 6"
```

---

## Summary

| Task | What | Approx Tests Added |
|------|------|-------------------|
| 1 | Scaffold test project + empty modules | 0 |
| 2 | RichText.detect (pure facet detection) | ~15 |
| 3 | RichText.resolve + parse + utilities | ~7 |
| 4 | Identity.parseDidDocument (pure) | ~6 |
| 5 | Identity.resolveDid/resolveHandle/resolveIdentity | ~6 |
| 6 | Bluesky convenience (post, like, follow, etc.) | ~8 |
| 7 | Blob upload + postWithImages | ~2 |
| 8 | Polish + full suite verification | 0 |
| **Total** | | **~44 new tests** |
