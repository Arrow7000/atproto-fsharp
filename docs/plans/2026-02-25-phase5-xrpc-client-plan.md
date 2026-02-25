# Phase 5: XRPC Client + Generated API — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build the HTTP transport layer for AT Protocol and extend code generation to emit typed XRPC method wrappers, so consumers can authenticate and call any Bluesky API with full type safety.

**Architecture:** Two new projects: `FSharp.ATProto.Core` (XRPC transport, auth, session management) and updated `FSharp.ATProto.Bluesky` (gains dependency on Core, generated typed wrappers). Code gen extended to emit `query`/`call` functions into each NSID module.

**Tech Stack:** System.Net.Http, System.Text.Json, FSharp.SystemTextJson 1.4.36, Expecto 10.2.3, FsCheck 2.16.6, Fabulous.AST 1.10.0

**Design doc:** `docs/plans/2026-02-25-phase5-xrpc-client-design.md`

**IMPORTANT:** All `dotnet` commands must be prefixed with `export PATH="$HOME/.dotnet:$PATH" &&`

---

### Task 1: Scaffold Core Project + Test Project

Create `FSharp.ATProto.Core` library and `FSharp.ATProto.Core.Tests` test project. Register both in the solution.

**Files:**
- Create: `src/FSharp.ATProto.Core/FSharp.ATProto.Core.fsproj`
- Create: `src/FSharp.ATProto.Core/Types.fs`
- Create: `tests/FSharp.ATProto.Core.Tests/FSharp.ATProto.Core.Tests.fsproj`
- Create: `tests/FSharp.ATProto.Core.Tests/TestHelpers.fs`
- Create: `tests/FSharp.ATProto.Core.Tests/Main.fs`
- Modify: `FSharp.ATProto.sln`

**Step 1: Create the Core library .fsproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Types.fs" />
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

**Step 2: Create the placeholder Types.fs**

```fsharp
namespace FSharp.ATProto.Core

/// XRPC error returned by AT Protocol endpoints.
type XrpcError =
    { StatusCode: int
      Error: string option
      Message: string option }

/// Authenticated session with a PDS.
type AtpSession =
    { AccessJwt: string
      RefreshJwt: string
      Did: string
      Handle: string }
```

**Step 3: Create the Core.Tests .fsproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="TestHelpers.fs" />
    <Compile Include="Main.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\FSharp.ATProto.Core\FSharp.ATProto.Core.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Expecto" Version="10.2.3" />
    <PackageReference Include="Expecto.FsCheck" Version="10.2.3" />
    <PackageReference Include="FsCheck" Version="2.16.6" />
  </ItemGroup>
</Project>
```

**Step 4: Create TestHelpers.fs**

```fsharp
module TestHelpers

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks

/// Create a mock HttpMessageHandler that calls the given function for each request.
type MockHandler(handler: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()

    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        Task.FromResult(handler request)

/// Create an HttpResponseMessage with a JSON body.
let jsonResponse (statusCode: HttpStatusCode) (body: obj) =
    let json = JsonSerializer.Serialize(body)
    let response = new HttpResponseMessage(statusCode)
    response.Content <- new StringContent(json, Encoding.UTF8, "application/json")
    response

/// Create an HttpResponseMessage with no body.
let emptyResponse (statusCode: HttpStatusCode) =
    new HttpResponseMessage(statusCode)
```

**Step 5: Create Main.fs**

```fsharp
module Main

open Expecto

[<EntryPoint>]
let main args =
    runTestsInAssemblyWithCLIArgs [] args
```

**Step 6: Add both projects to the solution**

Run:
```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet sln add src/FSharp.ATProto.Core/FSharp.ATProto.Core.fsproj --solution-folder src
export PATH="$HOME/.dotnet:$PATH" && dotnet sln add tests/FSharp.ATProto.Core.Tests/FSharp.ATProto.Core.Tests.fsproj --solution-folder tests
```

**Step 7: Build to verify scaffold**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet build`
Expected: Build succeeded with 0 errors.

**Step 8: Run the (empty) test project**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.Core.Tests`
Expected: 0 tests passed.

**Step 9: Commit**

```bash
git add src/FSharp.ATProto.Core/ tests/FSharp.ATProto.Core.Tests/ FSharp.ATProto.sln
git commit -m "Scaffold FSharp.ATProto.Core and Core.Tests projects"
```

---

### Task 2: JSON Configuration + Query Parameter Serialization

Create the shared JSON serializer options and a reflection-based query parameter serializer that converts F# records to URL query strings.

**Files:**
- Create: `src/FSharp.ATProto.Core/Json.fs`
- Create: `src/FSharp.ATProto.Core/QueryParams.fs`
- Create: `tests/FSharp.ATProto.Core.Tests/QueryParamsTests.fs`
- Modify: `src/FSharp.ATProto.Core/FSharp.ATProto.Core.fsproj` (add Compile entries)
- Modify: `tests/FSharp.ATProto.Core.Tests/FSharp.ATProto.Core.Tests.fsproj` (add Compile entry)

**Step 1: Write the failing test for query param serialization**

Create `tests/FSharp.ATProto.Core.Tests/QueryParamsTests.fs`:

```fsharp
module QueryParamsTests

open Expecto
open FSharp.ATProto.Core

type SimpleParams = { Actor: string }
type OptionalParams = { Limit: int64 option; Cursor: string option }
type ListParams = { Tag: string list }
type BoolParams = { IncludeReplies: bool }
type AllOptionalNone = { A: string option; B: int64 option }

[<Tests>]
let tests =
    testList "QueryParams" [
        testCase "serializes string field" <| fun () ->
            let result = QueryParams.toQueryString { Actor = "did:plc:abc" }
            Expect.equal result "?actor=did%3Aplc%3Aabc" "string field"

        testCase "serializes int64 field" <| fun () ->
            let result = QueryParams.toQueryString { Limit = Some 50L; Cursor = None }
            Expect.equal result "?limit=50" "int64 option Some"

        testCase "omits None option fields" <| fun () ->
            let result = QueryParams.toQueryString { A = None; B = None }
            Expect.equal result "" "all None = empty string"

        testCase "serializes bool field" <| fun () ->
            let result = QueryParams.toQueryString { IncludeReplies = true }
            Expect.equal result "?includeReplies=true" "bool true"

        testCase "serializes string list as repeated params" <| fun () ->
            let result = QueryParams.toQueryString { Tag = [ "cat"; "dog" ] }
            Expect.equal result "?tag=cat&tag=dog" "repeated params"

        testCase "serializes empty list as no params" <| fun () ->
            let result = QueryParams.toQueryString { Tag = [] }
            Expect.equal result "" "empty list = nothing"

        testCase "combines multiple fields" <| fun () ->
            let result = QueryParams.toQueryString { Limit = Some 25L; Cursor = Some "abc123" }
            Expect.equal result "?limit=25&cursor=abc123" "multiple fields"
    ]
```

**Step 2: Add QueryParamsTests.fs to test .fsproj**

Add `<Compile Include="QueryParamsTests.fs" />` before `Main.fs` in the test project.

**Step 3: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.Core.Tests`
Expected: Build error — `QueryParams` module does not exist.

**Step 4: Create Json.fs with shared serializer options**

```fsharp
namespace FSharp.ATProto.Core

open System.Text.Json
open System.Text.Json.Serialization

/// Shared JSON configuration for AT Protocol serialization.
module Json =
    /// JsonSerializerOptions configured for AT Protocol.
    let options =
        let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        opts.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
        opts.Converters.Add(JsonFSharpConverter(JsonFSharpOptions.Default().WithUnionInternalTag().WithUnionNamedFields()))
        opts
```

**Step 5: Create QueryParams.fs**

```fsharp
namespace FSharp.ATProto.Core

open System
open System.Reflection
open Microsoft.FSharp.Reflection

/// Serializes F# records to URL query strings for XRPC queries.
module QueryParams =

    let private isOptionType (t: Type) =
        t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

    let private isListType (t: Type) =
        t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<list<_>>

    let private formatValue (value: obj) : string =
        match value with
        | :? string as s -> s
        | :? int64 as i -> string i
        | :? int as i -> string i
        | :? bool as b -> if b then "true" else "false"
        | _ -> string value

    let private toCamelCase (name: string) =
        if String.IsNullOrEmpty(name) then name
        else string (Char.ToLowerInvariant(name.[0])) + name.[1..]

    /// Convert an F# record to a URL query string.
    /// Option fields are omitted when None.
    /// List fields are emitted as repeated parameters.
    let toQueryString<'T> (record: 'T) : string =
        let fields = FSharpType.GetRecordFields(typeof<'T>)
        let pairs =
            fields
            |> Array.collect (fun (prop: PropertyInfo) ->
                let value = prop.GetValue(record)
                let name = toCamelCase prop.Name

                if isOptionType prop.PropertyType then
                    let cases = FSharpType.GetUnionCases(prop.PropertyType)
                    let tag = FSharpValue.PreComputeUnionTagReader(prop.PropertyType)
                    if tag value = 0 then // None
                        [||]
                    else // Some
                        let caseInfo = cases.[1] // Some case
                        let fields = FSharpValue.PreComputeUnionReader(caseInfo)
                        let inner = (fields value).[0]
                        [| (name, Uri.EscapeDataString(formatValue inner)) |]
                elif isListType prop.PropertyType then
                    let items = value :?> System.Collections.IEnumerable
                    [| for item in items -> (name, Uri.EscapeDataString(formatValue item)) |]
                else
                    [| (name, Uri.EscapeDataString(formatValue value)) |])

        if Array.isEmpty pairs then ""
        else "?" + (pairs |> Array.map (fun (k, v) -> $"{k}={v}") |> String.concat "&")
```

**Step 6: Add new files to Core .fsproj** (compile order: Types.fs, Json.fs, QueryParams.fs)

Update `src/FSharp.ATProto.Core/FSharp.ATProto.Core.fsproj` `<Compile>` items to:
```xml
    <Compile Include="Types.fs" />
    <Compile Include="Json.fs" />
    <Compile Include="QueryParams.fs" />
```

**Step 7: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.Core.Tests`
Expected: 7 tests passed.

**Step 8: Commit**

```bash
git add src/FSharp.ATProto.Core/ tests/FSharp.ATProto.Core.Tests/
git commit -m "Add JSON config and query parameter serialization"
```

---

### Task 3: XRPC Transport — query and procedure

Implement `Xrpc.query` (HTTP GET) and `Xrpc.procedure` (HTTP POST) with JSON response deserialization and error handling.

**Files:**
- Create: `src/FSharp.ATProto.Core/Xrpc.fs`
- Create: `tests/FSharp.ATProto.Core.Tests/XrpcTests.fs`
- Modify: `src/FSharp.ATProto.Core/FSharp.ATProto.Core.fsproj` (add Compile)
- Modify: `tests/FSharp.ATProto.Core.Tests/FSharp.ATProto.Core.Tests.fsproj` (add Compile)

**Step 1: Write failing tests for Xrpc.query and Xrpc.procedure**

Create `tests/FSharp.ATProto.Core.Tests/XrpcTests.fs`:

```fsharp
module XrpcTests

open System.Net
open System.Net.Http
open System.Text.Json
open System.Text.Json.Serialization
open Expecto
open FSharp.ATProto.Core

// Test types
type TestParams = { Actor: string }

type TestOutput =
    { [<JsonPropertyName("displayName")>]
      DisplayName: string
      [<JsonPropertyName("followersCount")>]
      FollowersCount: int64 }

type TestInput =
    { [<JsonPropertyName("repo")>]
      Repo: string
      [<JsonPropertyName("collection")>]
      Collection: string }

type TestProcOutput =
    { [<JsonPropertyName("uri")>]
      Uri: string }

let makeAgent (handler: HttpRequestMessage -> HttpResponseMessage) =
    let client = new HttpClient(new TestHelpers.MockHandler(handler))
    { HttpClient = client
      BaseUrl = System.Uri("https://bsky.social")
      Session = None }

[<Tests>]
let queryTests =
    testList "Xrpc.query" [
        testCase "sends GET with query params and deserializes response" <| fun () ->
            let mutable capturedRequest: HttpRequestMessage option = None
            let agent = makeAgent (fun req ->
                capturedRequest <- Some req
                TestHelpers.jsonResponse HttpStatusCode.OK
                    {| displayName = "Alice"; followersCount = 42 |})

            let result =
                Xrpc.query<TestParams, TestOutput> "app.bsky.actor.getProfile"
                    { Actor = "alice.bsky.social" } agent
                |> Async.AwaitTask |> Async.RunSynchronously

            let req = capturedRequest.Value
            Expect.equal req.Method HttpMethod.Get "should be GET"
            Expect.stringContains (string req.RequestUri) "xrpc/app.bsky.actor.getProfile" "correct path"
            Expect.stringContains (string req.RequestUri) "actor=alice.bsky.social" "has query param"

            match result with
            | Ok output ->
                Expect.equal output.DisplayName "Alice" "display name"
                Expect.equal output.FollowersCount 42L "followers count"
            | Error e -> failtest $"Expected Ok, got Error: {e}"

        testCase "returns XrpcError on 400" <| fun () ->
            let agent = makeAgent (fun _ ->
                TestHelpers.jsonResponse HttpStatusCode.BadRequest
                    {| error = "InvalidRequest"; message = "Bad param" |})

            let result =
                Xrpc.query<TestParams, TestOutput> "app.bsky.actor.getProfile"
                    { Actor = "bad" } agent
                |> Async.AwaitTask |> Async.RunSynchronously

            match result with
            | Error e ->
                Expect.equal e.StatusCode 400 "status code"
                Expect.equal e.Error (Some "InvalidRequest") "error name"
                Expect.equal e.Message (Some "Bad param") "error message"
            | Ok _ -> failtest "Expected Error, got Ok"

        testCase "returns XrpcError on 500 with no body" <| fun () ->
            let agent = makeAgent (fun _ ->
                TestHelpers.emptyResponse HttpStatusCode.InternalServerError)

            let result =
                Xrpc.query<TestParams, TestOutput> "app.bsky.actor.getProfile"
                    { Actor = "x" } agent
                |> Async.AwaitTask |> Async.RunSynchronously

            match result with
            | Error e ->
                Expect.equal e.StatusCode 500 "status code"
                Expect.equal e.Error None "no error name"
            | Ok _ -> failtest "Expected Error, got Ok"

        testCase "includes auth header when session exists" <| fun () ->
            let mutable capturedRequest: HttpRequestMessage option = None
            let agent =
                { makeAgent (fun req ->
                    capturedRequest <- Some req
                    TestHelpers.jsonResponse HttpStatusCode.OK
                        {| displayName = "A"; followersCount = 0 |})
                  with Session = Some { AccessJwt = "tok123"; RefreshJwt = "ref"; Did = "did:plc:x"; Handle = "a.bsky.social" } }

            Xrpc.query<TestParams, TestOutput> "app.bsky.actor.getProfile"
                { Actor = "a" } agent
            |> Async.AwaitTask |> Async.RunSynchronously |> ignore

            let authHeader = capturedRequest.Value.Headers.Authorization
            Expect.isNotNull authHeader "auth header present"
            Expect.equal authHeader.Scheme "Bearer" "Bearer scheme"
            Expect.equal authHeader.Parameter "tok123" "token value"
    ]

[<Tests>]
let procedureTests =
    testList "Xrpc.procedure" [
        testCase "sends POST with JSON body and deserializes response" <| fun () ->
            let mutable capturedRequest: HttpRequestMessage option = None
            let mutable capturedBody: string option = None
            let agent = makeAgent (fun req ->
                capturedRequest <- Some req
                capturedBody <- Some (req.Content.ReadAsStringAsync().Result)
                TestHelpers.jsonResponse HttpStatusCode.OK {| uri = "at://did:plc:x/app.bsky.feed.post/abc" |})

            let result =
                Xrpc.procedure<TestInput, TestProcOutput> "com.atproto.repo.createRecord"
                    { Repo = "did:plc:x"; Collection = "app.bsky.feed.post" } agent
                |> Async.AwaitTask |> Async.RunSynchronously

            let req = capturedRequest.Value
            Expect.equal req.Method HttpMethod.Post "should be POST"
            Expect.stringContains (string req.RequestUri) "xrpc/com.atproto.repo.createRecord" "correct path"

            let bodyJson = JsonDocument.Parse(capturedBody.Value)
            Expect.equal (bodyJson.RootElement.GetProperty("repo").GetString()) "did:plc:x" "repo in body"
            Expect.equal (bodyJson.RootElement.GetProperty("collection").GetString()) "app.bsky.feed.post" "collection in body"

            match result with
            | Ok output ->
                Expect.equal output.Uri "at://did:plc:x/app.bsky.feed.post/abc" "uri"
            | Error e -> failtest $"Expected Ok, got Error: {e}"

        testCase "returns XrpcError on 401" <| fun () ->
            let agent = makeAgent (fun _ ->
                TestHelpers.jsonResponse HttpStatusCode.Unauthorized
                    {| error = "AuthenticationRequired"; message = "Not logged in" |})

            let result =
                Xrpc.procedure<TestInput, TestProcOutput> "com.atproto.repo.createRecord"
                    { Repo = "x"; Collection = "y" } agent
                |> Async.AwaitTask |> Async.RunSynchronously

            match result with
            | Error e ->
                Expect.equal e.StatusCode 401 "status code"
                Expect.equal e.Error (Some "AuthenticationRequired") "error name"
            | Ok _ -> failtest "Expected Error, got Ok"
    ]
```

**Step 2: Add XrpcTests.fs to test .fsproj before Main.fs**

**Step 3: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.Core.Tests`
Expected: Build error — `Xrpc` module does not exist, `HttpClient`/`Session` fields missing from agent type.

**Step 4: Update Types.fs to add AtpAgent**

```fsharp
namespace FSharp.ATProto.Core

open System
open System.Net.Http

/// XRPC error returned by AT Protocol endpoints.
type XrpcError =
    { StatusCode: int
      Error: string option
      Message: string option }

/// Authenticated session with a PDS.
type AtpSession =
    { AccessJwt: string
      RefreshJwt: string
      Did: string
      Handle: string }

/// Client agent for communicating with an AT Protocol PDS.
type AtpAgent =
    { HttpClient: HttpClient
      BaseUrl: Uri
      mutable Session: AtpSession option }
```

**Step 5: Create Xrpc.fs**

```fsharp
namespace FSharp.ATProto.Core

open System
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Threading.Tasks

/// XRPC transport for AT Protocol API calls.
module Xrpc =

    let private addAuth (agent: AtpAgent) (request: HttpRequestMessage) =
        match agent.Session with
        | Some session ->
            request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", session.AccessJwt)
        | None -> ()

    let private tryDeserializeError (response: HttpResponseMessage) : Task<XrpcError> =
        task {
            try
                let! body = response.Content.ReadAsStringAsync()
                let doc = JsonDocument.Parse(body)
                let root = doc.RootElement
                let error =
                    match root.TryGetProperty("error") with
                    | true, v -> Some (v.GetString())
                    | false, _ -> None
                let message =
                    match root.TryGetProperty("message") with
                    | true, v -> Some (v.GetString())
                    | false, _ -> None
                return { StatusCode = int response.StatusCode; Error = error; Message = message }
            with _ ->
                return { StatusCode = int response.StatusCode; Error = None; Message = None }
        }

    /// Execute an XRPC query (HTTP GET).
    let query<'P, 'O> (nsid: string) (params: 'P) (agent: AtpAgent) : Task<Result<'O, XrpcError>> =
        task {
            let queryString = QueryParams.toQueryString params
            let url = $"{agent.BaseUrl}xrpc/{nsid}{queryString}"
            let request = new HttpRequestMessage(HttpMethod.Get, url)
            addAuth agent request

            let! response = agent.HttpClient.SendAsync(request)

            if response.IsSuccessStatusCode then
                let! body = response.Content.ReadAsStringAsync()
                let output = JsonSerializer.Deserialize<'O>(body, Json.options)
                return Ok output
            else
                let! error = tryDeserializeError response
                return Error error
        }

    /// Execute an XRPC procedure (HTTP POST with JSON body).
    let procedure<'I, 'O> (nsid: string) (input: 'I) (agent: AtpAgent) : Task<Result<'O, XrpcError>> =
        task {
            let url = $"{agent.BaseUrl}xrpc/{nsid}"
            let json = JsonSerializer.Serialize(input, Json.options)
            let request = new HttpRequestMessage(HttpMethod.Post, url)
            request.Content <- new StringContent(json, Encoding.UTF8, "application/json")
            addAuth agent request

            let! response = agent.HttpClient.SendAsync(request)

            if response.IsSuccessStatusCode then
                let! body = response.Content.ReadAsStringAsync()
                let output = JsonSerializer.Deserialize<'O>(body, Json.options)
                return Ok output
            else
                let! error = tryDeserializeError response
                return Error error
        }

    /// Execute an XRPC procedure with no response body.
    let procedureVoid<'I> (nsid: string) (input: 'I) (agent: AtpAgent) : Task<Result<unit, XrpcError>> =
        task {
            let url = $"{agent.BaseUrl}xrpc/{nsid}"
            let json = JsonSerializer.Serialize(input, Json.options)
            let request = new HttpRequestMessage(HttpMethod.Post, url)
            request.Content <- new StringContent(json, Encoding.UTF8, "application/json")
            addAuth agent request

            let! response = agent.HttpClient.SendAsync(request)

            if response.IsSuccessStatusCode then
                return Ok ()
            else
                let! error = tryDeserializeError response
                return Error error
        }
```

**Step 6: Update Core .fsproj compile order**

```xml
    <Compile Include="Types.fs" />
    <Compile Include="Json.fs" />
    <Compile Include="QueryParams.fs" />
    <Compile Include="Xrpc.fs" />
```

**Step 7: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.Core.Tests`
Expected: All tests pass (7 QueryParams + 5 Xrpc = 12 tests).

**Step 8: Commit**

```bash
git add src/FSharp.ATProto.Core/ tests/FSharp.ATProto.Core.Tests/
git commit -m "Add XRPC query and procedure transport with mocked HTTP tests"
```

---

### Task 4: Session Auth — Login, Refresh, Auto-Refresh

Implement `AtpAgent.login`, session refresh on 401, and the `AtpAgent` module.

**Files:**
- Create: `src/FSharp.ATProto.Core/AtpAgent.fs`
- Create: `tests/FSharp.ATProto.Core.Tests/AtpAgentTests.fs`
- Modify: `src/FSharp.ATProto.Core/Xrpc.fs` (add auto-refresh to query/procedure)
- Modify: `src/FSharp.ATProto.Core/FSharp.ATProto.Core.fsproj` (add Compile)
- Modify: `tests/FSharp.ATProto.Core.Tests/FSharp.ATProto.Core.Tests.fsproj` (add Compile)

**Step 1: Write failing tests for login and auto-refresh**

Create `tests/FSharp.ATProto.Core.Tests/AtpAgentTests.fs`:

```fsharp
module AtpAgentTests

open System.Net
open System.Net.Http
open System.Text.Json
open System.Text.Json.Serialization
open Expecto
open FSharp.ATProto.Core

let makeAgent (handler: HttpRequestMessage -> HttpResponseMessage) =
    let client = new HttpClient(new TestHelpers.MockHandler(handler))
    AtpAgent.createWithClient client "https://bsky.social"

[<Tests>]
let loginTests =
    testList "AtpAgent.login" [
        testCase "successful login stores session" <| fun () ->
            let agent = makeAgent (fun req ->
                Expect.stringContains (string req.RequestUri) "com.atproto.server.createSession" "calls createSession"
                Expect.equal req.Method HttpMethod.Post "is POST"
                TestHelpers.jsonResponse HttpStatusCode.OK
                    {| accessJwt = "access1"; refreshJwt = "refresh1"; did = "did:plc:alice"; handle = "alice.bsky.social" |})

            let result =
                AtpAgent.login "alice.bsky.social" "app-password" agent
                |> Async.AwaitTask |> Async.RunSynchronously

            match result with
            | Ok session ->
                Expect.equal session.Did "did:plc:alice" "did"
                Expect.equal session.Handle "alice.bsky.social" "handle"
                Expect.equal session.AccessJwt "access1" "access jwt"
                Expect.isSome agent.Session "session stored on agent"
            | Error e -> failtest $"Expected Ok, got Error: {e}"

        testCase "failed login returns error" <| fun () ->
            let agent = makeAgent (fun _ ->
                TestHelpers.jsonResponse HttpStatusCode.Unauthorized
                    {| error = "AuthenticationRequired"; message = "Invalid identifier or password" |})

            let result =
                AtpAgent.login "bad" "bad" agent
                |> Async.AwaitTask |> Async.RunSynchronously

            match result with
            | Error e ->
                Expect.equal e.StatusCode 401 "status code"
                Expect.equal e.Error (Some "AuthenticationRequired") "error"
            | Ok _ -> failtest "Expected Error, got Ok"
    ]

type TestOutput =
    { [<JsonPropertyName("displayName")>]
      DisplayName: string }

type TestParams = { Actor: string }

[<Tests>]
let refreshTests =
    testList "Xrpc auto-refresh" [
        testCase "retries with new token on 401 ExpiredToken" <| fun () ->
            let mutable callCount = 0
            let agent = makeAgent (fun req ->
                callCount <- callCount + 1
                let uri = string req.RequestUri
                if uri.Contains("refreshSession") then
                    TestHelpers.jsonResponse HttpStatusCode.OK
                        {| accessJwt = "access2"; refreshJwt = "refresh2"; did = "did:plc:alice"; handle = "alice.bsky.social" |}
                elif callCount = 1 then
                    // First call: return expired token error
                    TestHelpers.jsonResponse HttpStatusCode.Unauthorized
                        {| error = "ExpiredToken"; message = "Token expired" |}
                else
                    // Retry after refresh: succeed
                    TestHelpers.jsonResponse HttpStatusCode.OK
                        {| displayName = "Alice" |})

            agent.Session <- Some { AccessJwt = "old"; RefreshJwt = "refresh1"; Did = "did:plc:alice"; Handle = "alice.bsky.social" }

            let result =
                Xrpc.query<TestParams, TestOutput> "app.bsky.actor.getProfile"
                    { Actor = "a" } agent
                |> Async.AwaitTask |> Async.RunSynchronously

            match result with
            | Ok output ->
                Expect.equal output.DisplayName "Alice" "got result after refresh"
                Expect.equal agent.Session.Value.AccessJwt "access2" "session updated"
            | Error e -> failtest $"Expected Ok after refresh, got Error: {e}"

        testCase "does not refresh when no session exists" <| fun () ->
            let agent = makeAgent (fun _ ->
                TestHelpers.jsonResponse HttpStatusCode.Unauthorized
                    {| error = "AuthenticationRequired"; message = "Not logged in" |})

            let result =
                Xrpc.query<TestParams, TestOutput> "app.bsky.actor.getProfile"
                    { Actor = "a" } agent
                |> Async.AwaitTask |> Async.RunSynchronously

            match result with
            | Error e -> Expect.equal e.StatusCode 401 "401 returned without refresh attempt"
            | Ok _ -> failtest "Expected Error, got Ok"

        testCase "returns refresh error if refresh itself fails" <| fun () ->
            let agent = makeAgent (fun req ->
                let uri = string req.RequestUri
                if uri.Contains("refreshSession") then
                    TestHelpers.jsonResponse HttpStatusCode.Unauthorized
                        {| error = "ExpiredToken"; message = "Refresh token expired" |}
                else
                    TestHelpers.jsonResponse HttpStatusCode.Unauthorized
                        {| error = "ExpiredToken"; message = "Token expired" |})

            agent.Session <- Some { AccessJwt = "old"; RefreshJwt = "oldref"; Did = "did:plc:x"; Handle = "x" }

            let result =
                Xrpc.query<TestParams, TestOutput> "app.bsky.actor.getProfile"
                    { Actor = "a" } agent
                |> Async.AwaitTask |> Async.RunSynchronously

            match result with
            | Error e -> Expect.equal e.Error (Some "ExpiredToken") "returns refresh error"
            | Ok _ -> failtest "Expected Error, got Ok"
    ]
```

**Step 2: Add AtpAgentTests.fs to test .fsproj before Main.fs**

**Step 3: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.Core.Tests`
Expected: Build error — `AtpAgent` module does not exist.

**Step 4: Create AtpAgent.fs**

```fsharp
namespace FSharp.ATProto.Core

open System
open System.Net.Http
open System.Threading.Tasks

/// Agent for communicating with an AT Protocol PDS.
module AtpAgent =

    /// Create a new agent pointing at a PDS.
    let create (baseUrl: string) : AtpAgent =
        let uri = if baseUrl.EndsWith("/") then Uri(baseUrl) else Uri(baseUrl + "/")
        { HttpClient = new HttpClient()
          BaseUrl = uri
          Session = None }

    /// Create a new agent with a provided HttpClient (for testing).
    let createWithClient (httpClient: HttpClient) (baseUrl: string) : AtpAgent =
        let uri = if baseUrl.EndsWith("/") then Uri(baseUrl) else Uri(baseUrl + "/")
        { HttpClient = httpClient
          BaseUrl = uri
          Session = None }

    /// Log in with identifier (handle or DID) + app password.
    let login (identifier: string) (password: string) (agent: AtpAgent) : Task<Result<AtpSession, XrpcError>> =
        task {
            let input = {| identifier = identifier; password = password |}
            let! result = Xrpc.procedure<{| identifier: string; password: string |}, AtpSession>
                            "com.atproto.server.createSession" input agent
            match result with
            | Ok session ->
                agent.Session <- Some session
                return Ok session
            | Error e ->
                return Error e
        }

    /// Refresh the current session using the refresh token.
    let refreshSession (agent: AtpAgent) : Task<Result<AtpSession, XrpcError>> =
        task {
            match agent.Session with
            | None ->
                return Error { StatusCode = 401; Error = Some "NoSession"; Message = Some "No session to refresh" }
            | Some session ->
                // refreshSession uses the refresh JWT, not the access JWT
                let originalSession = agent.Session
                agent.Session <- Some { session with AccessJwt = session.RefreshJwt }
                let! result = Xrpc.procedure<{||}, AtpSession>
                                "com.atproto.server.refreshSession" {||} agent
                match result with
                | Ok newSession ->
                    agent.Session <- Some newSession
                    return Ok newSession
                | Error e ->
                    agent.Session <- originalSession
                    return Error e
        }
```

**Step 5: Update Xrpc.fs to add auto-refresh on 401 ExpiredToken**

Add a private `tryRefreshAndRetry` helper and modify `query` and `procedure` to call it.

Replace the `query` function body with:

```fsharp
    let private tryRefreshAndRetry (agent: AtpAgent) (error: XrpcError) (retry: unit -> Task<Result<'O, XrpcError>>) : Task<Result<'O, XrpcError>> =
        task {
            if error.StatusCode = 401 && error.Error = Some "ExpiredToken" && agent.Session.IsSome then
                // Try to refresh the session
                let! refreshResult = AtpAgent.refreshSession agent
                match refreshResult with
                | Ok _ -> return! retry ()
                | Error refreshError -> return Error refreshError
            else
                return Error error
        }
```

This creates a circular dependency: `Xrpc` references `AtpAgent` and `AtpAgent` references `Xrpc`. To solve this, restructure: move the refresh logic into `Xrpc.fs` directly and have `AtpAgent` be a simpler module that calls `Xrpc`.

Alternative: put the refresh call directly inline in the query/procedure functions. Here's the updated `Xrpc.fs` approach:

After `tryDeserializeError`, add:

```fsharp
    let private refreshSession (agent: AtpAgent) : Task<Result<AtpSession, XrpcError>> =
        task {
            match agent.Session with
            | None ->
                return Error { StatusCode = 401; Error = Some "NoSession"; Message = Some "No session to refresh" }
            | Some session ->
                let url = $"{agent.BaseUrl}xrpc/com.atproto.server.refreshSession"
                let request = new HttpRequestMessage(HttpMethod.Post, url)
                request.Headers.Authorization <-
                    System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.RefreshJwt)

                let! response = agent.HttpClient.SendAsync(request)

                if response.IsSuccessStatusCode then
                    let! body = response.Content.ReadAsStringAsync()
                    let newSession = JsonSerializer.Deserialize<AtpSession>(body, Json.options)
                    agent.Session <- Some newSession
                    return Ok newSession
                else
                    let! error = tryDeserializeError response
                    return Error error
        }
```

Then update `query` to:

```fsharp
    let query<'P, 'O> (nsid: string) (params: 'P) (agent: AtpAgent) : Task<Result<'O, XrpcError>> =
        task {
            let queryString = QueryParams.toQueryString params
            let url = $"{agent.BaseUrl}xrpc/{nsid}{queryString}"
            let request = new HttpRequestMessage(HttpMethod.Get, url)
            addAuth agent request

            let! response = agent.HttpClient.SendAsync(request)

            if response.IsSuccessStatusCode then
                let! body = response.Content.ReadAsStringAsync()
                let output = JsonSerializer.Deserialize<'O>(body, Json.options)
                return Ok output
            else
                let! error = tryDeserializeError response
                // Auto-refresh on ExpiredToken
                if error.StatusCode = 401 && error.Error = Some "ExpiredToken" && agent.Session.IsSome then
                    let! refreshResult = refreshSession agent
                    match refreshResult with
                    | Ok _ ->
                        // Retry the original request with new token
                        let retryRequest = new HttpRequestMessage(HttpMethod.Get, url)
                        addAuth agent retryRequest
                        let! retryResponse = agent.HttpClient.SendAsync(retryRequest)
                        if retryResponse.IsSuccessStatusCode then
                            let! retryBody = retryResponse.Content.ReadAsStringAsync()
                            return Ok (JsonSerializer.Deserialize<'O>(retryBody, Json.options))
                        else
                            let! retryError = tryDeserializeError retryResponse
                            return Error retryError
                    | Error refreshError ->
                        return Error refreshError
                else
                    return Error error
        }
```

Apply the same auto-refresh pattern to `procedure` and `procedureVoid`.

And `AtpAgent.fs` becomes simpler (just `create`, `createWithClient`, `login` that calls `Xrpc.procedure`).

**Step 6: Update Core .fsproj compile order**

```xml
    <Compile Include="Types.fs" />
    <Compile Include="Json.fs" />
    <Compile Include="QueryParams.fs" />
    <Compile Include="Xrpc.fs" />
    <Compile Include="AtpAgent.fs" />
```

**Step 7: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.Core.Tests`
Expected: All tests pass (7 QueryParams + 5 Xrpc + 5 AtpAgent = 17 tests).

**Step 8: Commit**

```bash
git add src/FSharp.ATProto.Core/ tests/FSharp.ATProto.Core.Tests/
git commit -m "Add session auth with login and auto-refresh on 401 ExpiredToken"
```

---

### Task 5: Rate Limiting + Pagination

Add 429 rate-limit retry and cursor-based pagination helper.

**Files:**
- Modify: `src/FSharp.ATProto.Core/Xrpc.fs` (add rate limiting + pagination)
- Create: `tests/FSharp.ATProto.Core.Tests/RateLimitTests.fs`
- Create: `tests/FSharp.ATProto.Core.Tests/PaginationTests.fs`
- Modify: `tests/FSharp.ATProto.Core.Tests/FSharp.ATProto.Core.Tests.fsproj` (add Compile entries)

**Step 1: Write failing tests for rate limiting**

Create `tests/FSharp.ATProto.Core.Tests/RateLimitTests.fs`:

```fsharp
module RateLimitTests

open System.Net
open System.Net.Http
open System.Text.Json.Serialization
open Expecto
open FSharp.ATProto.Core

type SimpleOutput =
    { [<JsonPropertyName("ok")>]
      Ok: bool }

type SimpleParams = { X: string }

[<Tests>]
let tests =
    testList "rate limiting" [
        testCase "retries on 429 with Retry-After" <| fun () ->
            let mutable callCount = 0
            let handler = TestHelpers.MockHandler(fun _ ->
                callCount <- callCount + 1
                if callCount = 1 then
                    let resp = TestHelpers.emptyResponse (enum<HttpStatusCode> 429)
                    resp.Headers.Add("Retry-After", "1")
                    resp
                else
                    TestHelpers.jsonResponse HttpStatusCode.OK {| ok = true |})
            let agent =
                { HttpClient = new HttpClient(handler)
                  BaseUrl = System.Uri("https://bsky.social/")
                  Session = None }

            let result =
                Xrpc.query<SimpleParams, SimpleOutput> "test.method" { X = "a" } agent
                |> Async.AwaitTask |> Async.RunSynchronously

            match result with
            | Ok output -> Expect.isTrue output.Ok "succeeded on retry"
            | Error e -> failtest $"Expected Ok, got Error: {e}"
            Expect.equal callCount 2 "called twice (original + retry)"

        testCase "returns error if retry also fails" <| fun () ->
            let handler = TestHelpers.MockHandler(fun _ ->
                let resp = TestHelpers.emptyResponse (enum<HttpStatusCode> 429)
                resp.Headers.Add("Retry-After", "1")
                resp)
            let agent =
                { HttpClient = new HttpClient(handler)
                  BaseUrl = System.Uri("https://bsky.social/")
                  Session = None }

            let result =
                Xrpc.query<SimpleParams, SimpleOutput> "test.method" { X = "a" } agent
                |> Async.AwaitTask |> Async.RunSynchronously

            match result with
            | Error e -> Expect.equal e.StatusCode 429 "429 returned"
            | Ok _ -> failtest "Expected Error, got Ok"
    ]
```

**Step 2: Write failing tests for pagination**

Create `tests/FSharp.ATProto.Core.Tests/PaginationTests.fs`:

```fsharp
module PaginationTests

open System.Net
open System.Net.Http
open System.Text.Json.Serialization
open Expecto
open FSharp.ATProto.Core

type PageParams =
    { Limit: int64
      Cursor: string option }

type PageOutput =
    { [<JsonPropertyName("items")>]
      Items: string list
      [<JsonPropertyName("cursor")>]
      Cursor: string option }

[<Tests>]
let tests =
    testList "pagination" [
        testCase "iterates through pages until no cursor" <| fun () ->
            let handler = TestHelpers.MockHandler(fun req ->
                let uri = string req.RequestUri
                if uri.Contains("cursor=page2") then
                    TestHelpers.jsonResponse HttpStatusCode.OK
                        {| items = [| "c"; "d" |]; cursor = (null: string) |}
                else
                    TestHelpers.jsonResponse HttpStatusCode.OK
                        {| items = [| "a"; "b" |]; cursor = "page2" |})
            let agent =
                { HttpClient = new HttpClient(handler)
                  BaseUrl = System.Uri("https://bsky.social/")
                  Session = None }

            let pages =
                Xrpc.paginate<PageParams, PageOutput>
                    "test.list"
                    { Limit = 2L; Cursor = None }
                    (fun output -> output.Cursor)
                    (fun cursor ps -> { ps with Cursor = cursor })
                    agent
                |> AsyncSeq.toList

            Expect.equal pages.Length 2 "two pages"
            match pages.[0] with
            | Ok p -> Expect.equal p.Items [ "a"; "b" ] "first page"
            | Error e -> failtest $"page 1 error: {e}"
            match pages.[1] with
            | Ok p -> Expect.equal p.Items [ "c"; "d" ] "second page"
            | Error e -> failtest $"page 2 error: {e}"

        testCase "stops on error" <| fun () ->
            let mutable callCount = 0
            let handler = TestHelpers.MockHandler(fun _ ->
                callCount <- callCount + 1
                if callCount = 1 then
                    TestHelpers.jsonResponse HttpStatusCode.OK
                        {| items = [| "a" |]; cursor = "page2" |}
                else
                    TestHelpers.jsonResponse HttpStatusCode.InternalServerError
                        {| error = "ServerError"; message = "oops" |})
            let agent =
                { HttpClient = new HttpClient(handler)
                  BaseUrl = System.Uri("https://bsky.social/")
                  Session = None }

            let pages =
                Xrpc.paginate<PageParams, PageOutput>
                    "test.list"
                    { Limit = 2L; Cursor = None }
                    (fun output -> output.Cursor)
                    (fun cursor ps -> { ps with Cursor = cursor })
                    agent
                |> AsyncSeq.toList

            Expect.equal pages.Length 2 "two results (one ok, one error)"
            match pages.[1] with
            | Error e -> Expect.equal e.StatusCode 500 "error on page 2"
            | Ok _ -> failtest "Expected Error on page 2"
    ]

/// Helper to collect IAsyncEnumerable into a list.
module AsyncSeq =
    let toList (source: System.Collections.Generic.IAsyncEnumerable<'T>) : 'T list =
        task {
            let results = System.Collections.Generic.List<'T>()
            let enumerator = source.GetAsyncEnumerator()
            try
                let mutable hasMore = true
                while hasMore do
                    let! moved = enumerator.MoveNextAsync()
                    if moved then
                        results.Add(enumerator.Current)
                    else
                        hasMore <- false
                return results |> Seq.toList
            finally
                enumerator.DisposeAsync().AsTask().Wait()
        }
        |> Async.AwaitTask |> Async.RunSynchronously
```

**Step 3: Add test files to .fsproj before Main.fs**

**Step 4: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.Core.Tests`
Expected: Build errors — `paginate` does not exist, rate limit retry not implemented.

**Step 5: Add rate limiting to Xrpc.fs**

Add after the `refreshSession` function:

```fsharp
    let private waitForRateLimit (response: HttpResponseMessage) : Task<bool> =
        task {
            if int response.StatusCode = 429 then
                let retryAfter =
                    match response.Headers.RetryAfter with
                    | null -> 1.0
                    | ra when ra.Delta.HasValue -> ra.Delta.Value.TotalSeconds
                    | ra when ra.Date.HasValue ->
                        let diff = ra.Date.Value - DateTimeOffset.UtcNow
                        max 0.0 diff.TotalSeconds
                    | _ -> 1.0
                do! Task.Delay(int (retryAfter * 1000.0))
                return true
            else
                return false
        }
```

Then in `query`, after the 401 refresh block, before the final `return Error error`, add:

```fsharp
                elif error.StatusCode = 429 then
                    let! shouldRetry = waitForRateLimit response
                    if shouldRetry then
                        let retryRequest = new HttpRequestMessage(HttpMethod.Get, url)
                        addAuth agent retryRequest
                        let! retryResponse = agent.HttpClient.SendAsync(retryRequest)
                        if retryResponse.IsSuccessStatusCode then
                            let! retryBody = retryResponse.Content.ReadAsStringAsync()
                            return Ok (JsonSerializer.Deserialize<'O>(retryBody, Json.options))
                        else
                            let! retryError = tryDeserializeError retryResponse
                            return Error retryError
                    else
                        return Error error
```

Apply the same pattern to `procedure` and `procedureVoid`.

**Step 6: Add pagination to Xrpc.fs**

```fsharp
    /// Paginate through a cursor-based XRPC query.
    let paginate<'P, 'O>
        (nsid: string)
        (initialParams: 'P)
        (getCursor: 'O -> string option)
        (setCursor: string option -> 'P -> 'P)
        (agent: AtpAgent)
        : Collections.Generic.IAsyncEnumerable<Result<'O, XrpcError>> =
        { new Collections.Generic.IAsyncEnumerable<Result<'O, XrpcError>> with
            member _.GetAsyncEnumerator(ct) =
                let mutable currentParams = initialParams
                let mutable finished = false
                let mutable current = Unchecked.defaultof<Result<'O, XrpcError>>
                { new Collections.Generic.IAsyncEnumerator<Result<'O, XrpcError>> with
                    member _.Current = current
                    member _.MoveNextAsync() =
                        if finished then
                            ValueTask<bool>(false)
                        else
                            ValueTask<bool>(task {
                                let! result = query<'P, 'O> nsid currentParams agent
                                current <- result
                                match result with
                                | Ok output ->
                                    match getCursor output with
                                    | Some cursor ->
                                        currentParams <- setCursor (Some cursor) currentParams
                                    | None ->
                                        finished <- true
                                | Error _ ->
                                    finished <- true
                                return true
                            })
                    member _.DisposeAsync() = ValueTask()
                }
        }
```

**Step 7: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.Core.Tests`
Expected: All tests pass (7 + 5 + 5 + 2 + 2 = 21 tests).

**Step 8: Commit**

```bash
git add src/FSharp.ATProto.Core/ tests/FSharp.ATProto.Core.Tests/
git commit -m "Add rate limit retry and cursor-based pagination"
```

---

### Task 6: Extend Code Generator — Emit XRPC Wrapper Functions

Modify the code generator to emit `query` or `call` functions in each NSID module that wraps `Xrpc.query`/`Xrpc.procedure`.

**Files:**
- Modify: `src/FSharp.ATProto.CodeGen/NamespaceGen.fs` (add wrapper generation)
- Create: `tests/FSharp.ATProto.CodeGen.Tests/WrapperGenTests.fs`
- Modify: `tests/FSharp.ATProto.CodeGen.Tests/FSharp.ATProto.CodeGen.Tests.fsproj` (add Compile)
- Modify: `src/FSharp.ATProto.CodeGen/FSharp.ATProto.CodeGen.fsproj` (add Core project reference)

**Context:** The generated code lives in `namespace rec FSharp.ATProto.Bluesky`. The `Xrpc`, `AtpAgent`, and `XrpcError` types are in `FSharp.ATProto.Core`. The generated wrappers need to reference Core types by their fully-qualified names (or `open FSharp.ATProto.Core` at the top of the generated file).

**Step 1: Write tests for wrapper generation**

Create `tests/FSharp.ATProto.CodeGen.Tests/WrapperGenTests.fs`:

```fsharp
module WrapperGenTests

open Expecto
open FSharp.ATProto.Lexicon
open FSharp.ATProto.Syntax

/// Minimal query lexicon doc
let queryDoc =
    { Lexicon = 1
      Id = Nsid.parse "app.bsky.feed.getTimeline" |> Result.defaultWith failwith
      Revision = None
      Description = None
      Defs = Map.ofList [
        "main", LexDef.Query {
            Description = None
            Parameters = Some { Description = None; Properties = Map.ofList [ "limit", LexType.Integer { Description = None; Minimum = None; Maximum = None; Default = None; Enum = [] } ]; Required = [] }
            Output = Some { Description = None; Encoding = "application/json"; Schema = Some (LexType.Object { Description = None; Properties = Map.ofList [ "feed", LexType.Array { Description = None; Items = LexType.Ref "app.bsky.feed.defs#feedViewPost"; MinLength = None; MaxLength = None } ]; Required = [ "feed" ]; Nullable = [] }) }
            Errors = []
        }
      ] }

/// Minimal procedure lexicon doc
let procedureDoc =
    { Lexicon = 1
      Id = Nsid.parse "com.atproto.repo.createRecord" |> Result.defaultWith failwith
      Revision = None
      Description = None
      Defs = Map.ofList [
        "main", LexDef.Procedure {
            Description = None
            Parameters = None
            Input = Some { Description = None; Encoding = "application/json"; Schema = Some (LexType.Object { Description = None; Properties = Map.ofList [ "repo", LexType.String { Description = None; Format = None; MinLength = None; MaxLength = None; MinGraphemes = None; MaxGraphemes = None; Enum = []; Default = None; Const = None; KnownValues = [] } ]; Required = [ "repo" ]; Nullable = [] }) }
            Output = Some { Description = None; Encoding = "application/json"; Schema = Some (LexType.Object { Description = None; Properties = Map.ofList [ "uri", LexType.String { Description = None; Format = None; MinLength = None; MaxLength = None; MinGraphemes = None; MaxGraphemes = None; Enum = []; Default = None; Const = None; KnownValues = [] } ]; Required = [ "uri" ]; Nullable = [] }) }
            Errors = []
        }
      ] }

[<Tests>]
let tests =
    testList "wrapper generation" [
        testCase "query module contains query function" <| fun () ->
            let result = FSharp.ATProto.CodeGen.NamespaceGen.generateAll [ queryDoc ]
            let (_, content) = result.[0]
            Expect.stringContains content "let query" "has query function"
            Expect.stringContains content "Xrpc.query" "calls Xrpc.query"
            Expect.stringContains content "AtpAgent" "references AtpAgent"
            Expect.stringContains content "TypeId" "uses TypeId constant"

        testCase "procedure module contains call function" <| fun () ->
            let result = FSharp.ATProto.CodeGen.NamespaceGen.generateAll [ procedureDoc ]
            let (_, content) = result.[0]
            Expect.stringContains content "let call" "has call function"
            Expect.stringContains content "Xrpc.procedure" "calls Xrpc.procedure"
    ]
```

**Step 2: Add WrapperGenTests.fs to test .fsproj before Main.fs, and add Core project reference to CodeGen .fsproj** (not strictly needed at runtime, but for tests to reference both)

Actually, **CodeGen does NOT need to reference Core** — it just generates string code that references Core types. The test just checks the generated string contains the right patterns.

**Step 3: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.CodeGen.Tests`
Expected: Tests fail because generated code doesn't contain `query`/`call` functions.

**Step 4: Modify NamespaceGen.fs — add wrapper generation**

In `generateAll`, add `Open("FSharp.ATProto.Core")` to the generated namespace (after the existing `Open` statements):

Find the section in `generateAll` that builds the namespace widget:
```fsharp
    let namespaceWidget =
        (Namespace("FSharp.ATProto.Bluesky") {
            Open("System.Text.Json")
            Open("System.Text.Json.Serialization")
```

Add after `Open("System.Text.Json.Serialization")`:
```fsharp
            Open("System.Threading.Tasks")
            Open("FSharp.ATProto.Core")
```

Then modify `generateQueryModule` to add a wrapper function after the types. After the existing type generation in the query module, add:

```fsharp
        // Generate query wrapper function
        // The exact Fabulous.AST API for function definitions needs to be explored.
        // If Fabulous.AST supports typed parameters, use that.
        // Otherwise, generate the function as a raw string constant.
```

The implementation approach depends on Fabulous.AST's API for function definitions. Two strategies:

**Strategy A: Use Fabulous.AST `Value` with `ConstantExpr` containing the function body as a raw F# expression.**

In the query module builder, after generating Output type, add something like:

```fsharp
        // Generate the query wrapper
        let hasParams = query.Parameters.IsSome && query.Parameters.Value.Properties.Count > 0
        let hasOutput = query.Output.IsSome && (hasJsonObjectSchema query.Output.Value).IsSome

        if hasOutput then
            if hasParams then
                // let query (agent: AtpAgent) (params: Params) : Task<Result<Output, XrpcError>> =
                //     Xrpc.query<Params, Output> TypeId params agent
                ()  // Emit the function widget
            else
                // let query (agent: AtpAgent) : Task<Result<Output, XrpcError>> =
                //     Xrpc.queryNoParams<Output> TypeId agent
                ()  // Emit the function widget
```

**Strategy B: Post-process the generated string to inject wrapper functions.** After Fantomas formats the code, use string manipulation to insert wrapper functions at the right locations.

**Recommended: Strategy A** — the implementer should explore the Fabulous.AST API. If `Value(name, body).parameters(...)` or `Function(name, params, body)` exists, use it. If not, look into using `ConstantExpr(Constant(rawCode))` or `AppExpr` compositions. The Fabulous.AST source on GitHub/NuGet docs should be consulted.

Key patterns the wrapper function needs to produce in the final output:

For a query with params + output:
```fsharp
        let query (agent: AtpAgent) (params: Params) : Task<Result<Output, XrpcError>> =
            Xrpc.query<Params, Output> TypeId params agent
```

For a query with output but no params:
```fsharp
        let query (agent: AtpAgent) : Task<Result<Output, XrpcError>> =
            Xrpc.queryNoParams<Output> TypeId agent
```

For a procedure with input + output:
```fsharp
        let call (agent: AtpAgent) (input: Input) : Task<Result<Output, XrpcError>> =
            Xrpc.procedure<Input, Output> TypeId input agent
```

For a procedure with input but no output:
```fsharp
        let call (agent: AtpAgent) (input: Input) : Task<Result<unit, XrpcError>> =
            Xrpc.procedureVoid<Input> TypeId input agent
```

**Step 5: Also modify `generateProcedureModule` with equivalent wrapper generation.**

**Step 6: Run CodeGen tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.CodeGen.Tests`
Expected: All tests pass including the new wrapper tests.

**Step 7: Commit**

```bash
git add src/FSharp.ATProto.CodeGen/ tests/FSharp.ATProto.CodeGen.Tests/
git commit -m "Extend code generator to emit typed XRPC wrapper functions"
```

---

### Task 7: Update Bluesky Project + Regenerate + Verify Compilation

Add Core dependency to Bluesky, regenerate Generated.fs with the new wrappers, and verify the whole solution compiles.

**Files:**
- Modify: `src/FSharp.ATProto.Bluesky/FSharp.ATProto.Bluesky.fsproj` (add Core dependency)
- Regenerate: `src/FSharp.ATProto.Bluesky/Generated/Generated.fs`

**Step 1: Add Core project reference to Bluesky .fsproj**

Add to the `<ProjectReference>` ItemGroup in `src/FSharp.ATProto.Bluesky/FSharp.ATProto.Bluesky.fsproj`:
```xml
    <ProjectReference Include="..\FSharp.ATProto.Core\FSharp.ATProto.Core.fsproj" />
```

**Step 2: Regenerate Generated.fs**

Run:
```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project src/FSharp.ATProto.CodeGen -- --lexdir extern/atproto/lexicons --outdir src/FSharp.ATProto.Bluesky/Generated
```

**Step 3: Verify the generated file contains wrapper functions**

Check that `src/FSharp.ATProto.Bluesky/Generated/Generated.fs` contains:
- `open FSharp.ATProto.Core`
- `open System.Threading.Tasks`
- `let query` in query modules
- `let call` in procedure modules
- References to `Xrpc.query`, `Xrpc.procedure`, `AtpAgent`, `XrpcError`

**Step 4: Build the entire solution**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet build`
Expected: Build succeeded with 0 errors.

**Step 5: Run ALL tests**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test`
Expected: All tests pass (726 Syntax + 112 DRISL + 387 Lexicon + ~165 CodeGen + ~21 Core = ~1400+ tests).

Note: `dotnet test` may not discover Expecto tests. If so, run each test project individually:
```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.Syntax.Tests
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.DRISL.Tests
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.Lexicon.Tests
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.CodeGen.Tests
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.Core.Tests
```

**Step 6: Commit**

```bash
git add src/FSharp.ATProto.Bluesky/ src/FSharp.ATProto.CodeGen/
git commit -m "Regenerate Bluesky types with XRPC wrappers, add Core dependency"
```

---

### Task 8: Final Verification + Documentation

Run full test suite, verify test counts, update memory file.

**Step 1: Run all test projects and record counts**

```bash
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.Syntax.Tests -- --summary
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.DRISL.Tests -- --summary
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.Lexicon.Tests -- --summary
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.CodeGen.Tests -- --summary
export PATH="$HOME/.dotnet:$PATH" && dotnet run --project tests/FSharp.ATProto.Core.Tests -- --summary
```

**Step 2: Update the project memory file at `/Users/aron/.claude/projects/-Users-aron-dev-atproto-fsharp/memory/MEMORY.md`**

Update the "Current Status" section:
- Phase 5 status
- New test counts
- New project added (FSharp.ATProto.Core)

**Step 3: Final commit if any docs changed**
