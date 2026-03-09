(**
---
title: XRPC Server
category: Server-Side
categoryindex: 4
index: 23
description: Host AT Protocol XRPC endpoints with authentication and rate limiting
keywords: fsharp, atproto, xrpc, server, api, rate-limiting, authentication
---

# XRPC Server

`FSharp.ATProto.XrpcServer` provides a framework for building AT Protocol-compliant XRPC servers with built-in authentication and rate limiting. It maps XRPC endpoints to ASP.NET minimal API routes, handling error formatting, bearer token verification, and sliding-window rate limiting automatically.
*)

(*** hide ***)
#nowarn "20"
#I "/usr/local/share/dotnet/shared/Microsoft.AspNetCore.App/10.0.3/"
#r "Microsoft.AspNetCore.Http.Abstractions.dll"
#r "Microsoft.AspNetCore.Http.Results.dll"
#r "Microsoft.Extensions.Primitives.dll"
#r "../../src/FSharp.ATProto.Syntax/bin/Release/net10.0/FSharp.ATProto.Syntax.dll"
#r "../../src/FSharp.ATProto.XrpcServer/bin/Release/net10.0/FSharp.ATProto.XrpcServer.dll"
open FSharp.ATProto.Syntax
open FSharp.ATProto.XrpcServer
open System.Security.Claims
open Microsoft.AspNetCore.Http
(***)

open FSharp.ATProto.XrpcServer
open FSharp.ATProto.Syntax
open Microsoft.AspNetCore.Http

(**
## Key Types

Each endpoint is described by an `XrpcEndpoint` record:

```
type XrpcEndpoint = {
    Nsid: Nsid
    Method: XrpcMethod       // XrpcMethod.Query (GET) or XrpcMethod.Procedure (POST)
    Handler: HttpContext -> Task<IResult>
    RateLimit: RateLimitConfig option
    RequireAuth: bool
}
```

The server configuration holds all registered endpoints, an optional token verifier, and global rate limits:

```
type XrpcServerConfig = {
    Endpoints: XrpcEndpoint list
    VerifyToken: (string -> Task<Result<ClaimsPrincipal, string>>) option
    GlobalRateLimit: RateLimitConfig option
    JsonOptions: JsonSerializerOptions
}
```

## Defining Endpoints

Use `XrpcServer.endpoint` to create an endpoint, then pipe through `withRateLimit` and `withAuth` as needed:
*)

let nsid = Nsid.parse "app.example.getProfile" |> Result.defaultWith failwith

let getProfile =
    XrpcServer.endpoint nsid XrpcMethod.Query (fun ctx ->
        task {
            match Middleware.requireQueryParam "actor" ctx with
            | Ok actor ->
                return Results.Json({| did = actor; handle = "example.com" |})
            | Error msg ->
                return Middleware.invalidRequest msg XrpcServerConfig.defaultJsonOptions
        })
    |> XrpcServer.withRateLimit { MaxRequests = 100; Window = System.TimeSpan.FromMinutes 1.0 }
    |> XrpcServer.withAuth

(**
For POST endpoints, use `XrpcMethod.Procedure` and read the request body with `Middleware.tryReadJsonBody`:
*)

(*** hide ***)
let createItemNsid = Nsid.parse "app.example.createItem" |> Result.defaultWith failwith
(***)

let createItem =
    XrpcServer.endpoint createItemNsid XrpcMethod.Procedure (fun ctx ->
        task {
            let! body = Middleware.tryReadJsonBody<{| name: string |}> XrpcServerConfig.defaultJsonOptions ctx
            match body with
            | Ok item ->
                return Results.Json({| created = true; name = item.name |})
            | Error msg ->
                return Middleware.invalidRequest msg XrpcServerConfig.defaultJsonOptions
        })
    |> XrpcServer.withAuth

(**
## Authentication

The `Auth` module extracts and verifies bearer tokens. You provide a `verifyToken` function that validates the token string and returns a `ClaimsPrincipal` on success:

```fsharp
let myTokenVerifier (token: string) : Task<Result<ClaimsPrincipal, string>> =
    task {
        // Your JWT validation logic here
        // Return Ok principal or Error message
    }
```

Inside an endpoint handler, retrieve the authenticated principal or a specific claim:

```fsharp
let handler (ctx: HttpContext) =
    task {
        match Auth.getPrincipal ctx with
        | Some principal -> // authenticated
            let did = Auth.getClaim "sub" ctx // get the "sub" claim
            return Results.Json({| authed = true |})
        | None -> // not authenticated
            return Middleware.authRequired "Login required" XrpcServerConfig.defaultJsonOptions
    }
```

Endpoints marked with `withAuth` reject unauthenticated requests with a 401 error automatically. For endpoints without `withAuth`, the framework still attempts optional token verification if a bearer token is present, making the principal available but not requiring it.

## Rate Limiting

`RateLimiter` implements a per-client sliding window rate limiter. Clients are identified by IP address. Configure rate limits per-endpoint or globally:

```
type RateLimitConfig = {
    MaxRequests: int
    Window: TimeSpan
}
```

Per-endpoint limits take precedence over the global limit. When a client exceeds their limit, the server returns a 429 response with a `Retry-After` header. Remaining request counts are sent in the `RateLimit-Remaining` header on successful requests.

## Middleware Helpers

The `Middleware` module provides helpers for common request/response patterns:

| Function | Purpose |
|---|---|
| `requireQueryParam name ctx` | Parse a required query param. Returns `Result<string, string>` |
| `optionalQueryParam name ctx` | Parse an optional query param. Returns `string option` |
| `intQueryParam name default min max ctx` | Parse an integer query param with bounds |
| `tryReadJsonBody<'T> jsonOptions ctx` | Deserialize the JSON request body |
| `xrpcError statusCode error message jsonOptions` | Format an AT Protocol error response |
| `invalidRequest message jsonOptions` | Format a 400 InvalidRequest error |
| `authRequired message jsonOptions` | Format a 401 AuthenticationRequired error |
| `rateLimitExceeded retryAfter jsonOptions` | Format a 429 RateLimitExceeded error |

## Building and Running

Compose the server configuration with the builder functions, then call `XrpcServer.configure` or `XrpcServer.configureWithPort`:
*)

let myTokenVerifier (token: string) =
    task { return Ok (System.Security.Claims.ClaimsPrincipal()) }

let config =
    XrpcServerConfig.defaults
    |> XrpcServer.addEndpoint getProfile
    |> XrpcServer.addEndpoint createItem
    |> XrpcServer.withTokenVerifier myTokenVerifier
    |> XrpcServer.withGlobalRateLimit { MaxRequests = 1000; Window = System.TimeSpan.FromMinutes 5.0 }

(*** hide ***)
// XrpcServer.configureWithPort and app.Run() would start the server;
// omitted here to avoid side effects during fsdocs build.
(***)

(**
```fsharp
let app = XrpcServer.configureWithPort 3000 config
app.Run()
```

`configure` returns a `WebApplication` without binding a port (useful when you want to control hosting yourself). `configureWithPort` binds to the specified port.

The server automatically registers a `GET /_health` endpoint that returns `{ "version": "1.0.0" }` for health checks.
*)
