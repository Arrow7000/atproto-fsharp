(**
---
title: Personal Data Server
category: Server-Side
categoryindex: 4
index: 21
description: Run a fully functional AT Protocol PDS with a single function call
keywords: fsharp, atproto, pds, personal data server, server, hosting, accounts, repository
---

# Personal Data Server

`FSharp.ATProto.Pds` provides a turnkey Personal Data Server (PDS) that can be launched with a single function call. It handles account creation, session authentication, repository record storage, and identity resolution out of the box -- all with in-memory storage, suitable for development, testing, and prototyping.

## Quick Start

The simplest possible PDS:
*)

(*** hide ***)
#nowarn "20"
#I "/usr/local/share/dotnet/shared/Microsoft.AspNetCore.App/10.0.3/"
#r "Microsoft.AspNetCore.dll"
#r "Microsoft.AspNetCore.Hosting.Abstractions.dll"
#r "Microsoft.AspNetCore.Http.Abstractions.dll"
#r "Microsoft.AspNetCore.Http.Results.dll"
#r "Microsoft.AspNetCore.Routing.dll"
#r "Microsoft.Extensions.Hosting.Abstractions.dll"
#r "Microsoft.Extensions.Primitives.dll"
#r "../../src/FSharp.ATProto.Syntax/bin/Release/net10.0/FSharp.ATProto.Syntax.dll"
#r "../../src/FSharp.ATProto.Crypto/bin/Release/net10.0/FSharp.ATProto.Crypto.dll"
#r "../../src/FSharp.ATProto.Pds/bin/Release/net10.0/FSharp.ATProto.Pds.dll"
open FSharp.ATProto.Pds
open FSharp.ATProto.Crypto
(***)

open FSharp.ATProto.Pds

(**
```fsharp
// One-liner: starts a PDS on port 3000 (blocking)
Pds.run "localhost" 3000
```

For more control, use the builder pipeline:
*)

let app =
    Pds.defaults "my-pds.example.com"
    |> Pds.withPort 3000
    |> Pds.configure

(**
```fsharp
app.Run()
```

## Configuration

All configuration is done through pipeline functions on the `PdsBuilder`:

| Function | Default | Description |
|----------|---------|-------------|
| `Pds.defaults hostname` | -- | Create a builder with the given hostname |
| `Pds.withPort port` | `2583` | Set the listening port |
| `Pds.withSigningKey key` | auto-generated P-256 | Use a specific signing key |
| `Pds.withAdminPassword pw` | `None` | Set the admin password |
| `Pds.withInviteCode code` | disabled | Require an invite code for account creation |
| `Pds.withAccessTokenLifetime ts` | 2 hours | Set access token expiry |
| `Pds.withRefreshTokenLifetime ts` | 90 days | Set refresh token expiry |

### Invite Codes

To require invite codes for account creation:
*)

let restrictedApp =
    Pds.defaults "my-pds.example.com"
    |> Pds.withPort 3000
    |> Pds.withInviteCode "my-secret-invite"
    |> Pds.configure

(**
Accounts created without the correct code will receive a 400 `InvalidInviteCode` error.

### Custom Signing Key

By default, a P-256 key is generated on startup. To use a persistent key:
*)

let key = Keys.generate Algorithm.P256

let appWithKey =
    Pds.defaults "my-pds.example.com"
    |> Pds.withSigningKey key
    |> Pds.configure

(**
## Endpoints

The PDS implements the following AT Protocol endpoints:

### Server

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `com.atproto.server.describeServer` | GET | -- | Server metadata (DID, available domains, invite requirements) |
| `com.atproto.server.createAccount` | POST | -- | Register a new account (handle + password) |
| `com.atproto.server.createSession` | POST | -- | Authenticate with handle/DID + password |
| `com.atproto.server.refreshSession` | POST | Bearer (refresh) | Exchange a refresh token for new tokens |
| `com.atproto.server.deleteSession` | POST | Bearer | Revoke the current session |
| `com.atproto.server.getSession` | GET | Bearer | Get the current session (DID + handle) |

### Repository

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `com.atproto.repo.createRecord` | POST | Bearer | Create a record in the authenticated user's repo |
| `com.atproto.repo.getRecord` | GET | -- | Retrieve a record by repo + collection + rkey |
| `com.atproto.repo.deleteRecord` | POST | Bearer | Delete a record from the authenticated user's repo |
| `com.atproto.repo.listRecords` | GET | -- | List records in a collection |

### Identity

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `com.atproto.identity.resolveHandle` | GET | -- | Resolve a handle to a DID |

### Other

| Path | Method | Description |
|------|--------|-------------|
| `/_health` | GET | Health check (returns `{ "version": "1.0.0" }`) |
| `/.well-known/atproto-did` | GET | Handle verification (returns the server's DID) |

## Composing with Other Servers

`Pds.mapEndpoints` maps all PDS endpoints onto an existing `WebApplication`, so you can compose PDS functionality with your own routes or other AT Protocol server components:
*)

(*** hide ***)
open Microsoft.AspNetCore.Builder
(***)

let combined =
    let builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder()
    let webApp = builder.Build()

    let pdsConfig = Pds.defaults "my-pds.example.com"
    Pds.mapEndpoints pdsConfig webApp |> ignore

    webApp.MapGet("/my-custom-route", System.Func<string>(fun () -> "hello"))
    |> ignore

    webApp

(**
```fsharp
combined.Run()
```

This is particularly useful for testing with ASP.NET Core's `TestServer`:

```fsharp
let builder = WebApplication.CreateBuilder()
builder.WebHost.UseTestServer() |> ignore
let app = builder.Build()

Pds.mapEndpoints (Pds.defaults "test.example.com") app |> ignore

do! app.StartAsync()
let server = app.Services.GetRequiredService<IServer>() :?> TestServer
let client = server.CreateClient()

// Now use client to make requests against the PDS
let! response = client.PostAsync("/xrpc/com.atproto.server.createAccount", ...)
```

## Connecting with the Client Library

A PDS created with `FSharp.ATProto.Pds` speaks the standard AT Protocol, so the client library (`FSharp.ATProto.Core` / `FSharp.ATProto.Bluesky`) connects to it like any other PDS:

```fsharp
open FSharp.ATProto.Core

// Point the client at your local PDS
let! loginResult = Bluesky.login "http://localhost:3000" "alice.my-pds.com" "password123"
```

## Storage

All state is held in-memory using concurrent dictionaries. Data is lost on restart. This makes the PDS ideal for:

- **Development**: test your AT Protocol client code against a local server
- **Integration testing**: spin up a PDS in `TestServer` for automated tests
- **Prototyping**: quickly explore the AT Protocol without deploying infrastructure

For production use, you would extend the PDS with persistent storage backends.
*)
