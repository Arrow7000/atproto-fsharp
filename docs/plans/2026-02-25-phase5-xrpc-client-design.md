# Phase 5: XRPC Client + Generated API — Design

Date: 2026-02-25

## Overview

Build the HTTP transport layer for the AT Protocol and extend code generation to emit typed XRPC method wrappers. After this phase, consumers can authenticate and call any Bluesky API endpoint with full type safety.

## New Projects

| Project | Purpose | Dependencies |
|---------|---------|-------------|
| `FSharp.ATProto.Core` | XRPC transport, auth, session management, error types | Syntax, DRISL, FSharp.SystemTextJson |
| `FSharp.ATProto.Bluesky` (updated) | Generated types + generated XRPC wrappers | Core |
| `FSharp.ATProto.Core.Tests` | Unit tests with mocked HttpClient | Core, Expecto, FsCheck |

Dependency chain: `Syntax → DRISL → Core → Bluesky`

## Core Types

### AtpSession

```fsharp
type AtpSession = {
    AccessJwt: string
    RefreshJwt: string
    Did: string
    Handle: string
}
```

### AtpAgent

```fsharp
type AtpAgent = {
    HttpClient: HttpClient
    BaseUrl: Uri
    mutable Session: AtpSession option
}

module AtpAgent =
    /// Create a new agent pointing at a PDS
    let create (baseUrl: string) : AtpAgent

    /// Create a new agent with a provided HttpClient (for testing)
    let createWithClient (httpClient: HttpClient) (baseUrl: string) : AtpAgent

    /// Log in with identifier (handle or DID) + app password
    let login (identifier: string) (password: string) (agent: AtpAgent) : Task<Result<AtpSession, XrpcError>>

    /// Resume a session from stored tokens
    let resumeSession (session: AtpSession) (agent: AtpAgent) : Task<Result<AtpSession, XrpcError>>
```

The `AtpAgent` uses a mutable `Session` field. Rationale: HTTP clients are inherently stateful (connection pooling, cookies), and automatic token refresh needs to update session state transparently. Making this immutable would force every XRPC call to return `(Result * AtpAgent)` tuples, which is ergonomically painful and doesn't match the reality that `HttpClient` itself is mutable shared state.

### XrpcError

```fsharp
type XrpcError = {
    StatusCode: int
    Error: string option
    Message: string option
}
```

All XRPC calls return `Task<Result<'Output, XrpcError>>`.

## XRPC Transport

### Xrpc Module

```fsharp
module Xrpc =
    /// Execute an XRPC query (HTTP GET)
    let query<'P, 'O> (nsid: string) (params: 'P) (agent: AtpAgent) : Task<Result<'O, XrpcError>>

    /// Execute an XRPC procedure (HTTP POST with JSON body)
    let procedure<'I, 'O> (nsid: string) (input: 'I) (agent: AtpAgent) : Task<Result<'O, XrpcError>>

    /// Execute an XRPC procedure with no response body
    let procedureVoid<'I> (nsid: string) (input: 'I) (agent: AtpAgent) : Task<Result<unit, XrpcError>>

    /// Execute an XRPC query with no parameters
    let queryNoParams<'O> (nsid: string) (agent: AtpAgent) : Task<Result<'O, XrpcError>>
```

### Query Parameter Serialization

Record fields serialized to URL query string via reflection:
- `string` → as-is
- `int` / `int64` → `ToString()`
- `bool` → `"true"` / `"false"`
- `option` → omitted when `None`, unwrapped when `Some`
- `string list` → repeated query params (`?tag=a&tag=b`)

### Response Handling

1. Check HTTP status code
2. On 2xx: deserialize JSON body to `'O` via `System.Text.Json` + `FSharp.SystemTextJson`
3. On 4xx/5xx: deserialize error body `{ "error": "...", "message": "..." }` into `XrpcError`
4. On network/deserialization failure: wrap in `XrpcError` with status 0

### Automatic Session Refresh

On 401 `ExpiredToken`:
1. Call `com.atproto.server.refreshSession` with the refresh JWT
2. If refresh succeeds, update `agent.Session` and retry the original request (once)
3. If refresh fails, return the refresh error

### Rate Limiting

On 429:
1. Read `Retry-After` header (seconds) or `RateLimit-Reset` (Unix timestamp)
2. Wait the specified duration
3. Retry the request (once)

## Generated Typed Wrappers

The code generator (Phase 4) is extended to emit XRPC method functions alongside existing types.

### Query Example

```fsharp
module AppBskyFeed =
    module GetTimeline =
        [<Literal>]
        let TypeId = "app.bsky.feed.getTimeline"

        type Params = { ... }   // already generated
        type Output = { ... }   // already generated

        /// Execute app.bsky.feed.getTimeline query
        let query (agent: AtpAgent) (params: Params) : Task<Result<Output, XrpcError>> =
            Xrpc.query<Params, Output> TypeId params agent
```

### Procedure Example

```fsharp
module ComAtprotoRepo =
    module CreateRecord =
        [<Literal>]
        let TypeId = "com.atproto.repo.createRecord"

        type Input = { ... }    // already generated
        type Output = { ... }   // already generated

        /// Execute com.atproto.repo.createRecord procedure
        let call (agent: AtpAgent) (input: Input) : Task<Result<Output, XrpcError>> =
            Xrpc.procedure<Input, Output> TypeId input agent
```

### Naming Convention

- Queries get a `query` function
- Procedures get a `call` function
- Procedures with no output get a `call` function returning `Task<Result<unit, XrpcError>>`
- Procedures/queries with no params/input omit that argument

### Generated Code Dependencies

The generated `Bluesky` project gains a dependency on `Core` (for `AtpAgent`, `Xrpc`, `XrpcError`). The generated file adds `open FSharp.ATProto.Core`.

## Pagination

```fsharp
module Xrpc =
    /// Paginate through a cursor-based query, yielding pages
    let paginate<'P, 'O>
        (nsid: string)
        (initialParams: 'P)
        (getCursor: 'O -> string option)
        (setCursor: string option -> 'P -> 'P)
        (agent: AtpAgent)
        : IAsyncEnumerable<Result<'O, XrpcError>>
```

Consumers use `IAsyncEnumerable` (via `taskSeq` CE from `FSharp.Control.TaskSeq` or manual `IAsyncEnumerator`). The helper calls the query, extracts the cursor from the output, sets it on params, and repeats until cursor is `None` or an error occurs.

## JSON Configuration

Shared `JsonSerializerOptions` configured once:
- `FSharp.SystemTextJson.JsonFSharpConverter` for DU/record handling
- `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`
- `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull` for option fields

## Testing Strategy

### Unit Tests (FSharp.ATProto.Core.Tests)

All tests use mocked `HttpClient` via custom `HttpMessageHandler`:

1. **XRPC query serialization** — verify params become correct query string
2. **XRPC procedure serialization** — verify input becomes correct JSON body
3. **Response deserialization** — verify JSON response maps to output type
4. **Error handling** — 400, 401, 404, 500 responses map to correct `XrpcError`
5. **Session refresh** — 401 triggers refresh + retry
6. **Rate limiting** — 429 with Retry-After triggers wait + retry
7. **Auth flow** — login sends correct request, stores session
8. **Pagination** — cursor iteration produces correct sequence of pages

### Generated Wrapper Tests (FSharp.ATProto.CodeGen.Tests — extended)

1. **Compilation** — generated code with new wrappers still compiles
2. **Spot-check** — verify specific generated functions have correct signatures

### Integration Tests (deferred to Phase 6)

- Against local PDS Docker container
- End-to-end: create account → create post → read back → verify

## Scope Boundaries

**In scope:**
- `FSharp.ATProto.Core` project with AtpAgent, Xrpc, auth, error types
- Code gen extension for typed XRPC wrappers
- Unit tests with mocked HTTP
- Pagination helper

**Out of scope (deferred):**
- OAuth 2.1 + DPoP
- Typed identifier wrappers in generated code (Did/Handle stay `string`)
- Docker PDS integration tests
- WebSocket subscriptions (`com.atproto.sync.subscribeRepos`)
- Blob upload/download
- Rich text / facets / identity resolution
