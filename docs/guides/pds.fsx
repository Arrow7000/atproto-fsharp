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

```
// One-liner: starts a PDS on port 3000 (blocking)
Pds.run "localhost" 3000
```

For more control, use the builder pipeline:
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
#r "../../src/FSharp.ATProto.DRISL/bin/Release/net10.0/FSharp.ATProto.DRISL.dll"
#r "../../src/FSharp.ATProto.Core/bin/Release/net10.0/FSharp.ATProto.Core.dll"
#r "../../src/FSharp.ATProto.Crypto/bin/Release/net10.0/FSharp.ATProto.Crypto.dll"
#r "../../src/FSharp.ATProto.Pds/bin/Release/net10.0/FSharp.ATProto.Pds.dll"
open FSharp.ATProto.Pds
open FSharp.ATProto.Crypto
open FSharp.ATProto.Syntax
open FSharp.ATProto.Core
(***)

open FSharp.ATProto.Pds

let app =
    Pds.create "my-pds.example.com"
    |> Pds.withPort 3000
    |> Pds.configure

(**
```
app.Run()
```

## Creating Users with `Pds.createUser`

`Pds.start` returns a `RunningPds` that you can interact with programmatically. The star of the show is `Pds.createUser` -- it creates an account on the PDS **and** returns a ready-to-use `AtpAgent`, exactly like `Bluesky.login`:

```
task {
    // Start the PDS (non-blocking)
    let! pds = Pds.create "localhost" |> Pds.withPort 3000 |> Pds.start

    // One call: account is created and agent is authenticated
    match! Pds.createUser pds "alice.localhost" "password123" with
    | Ok agent ->
        // Use the agent exactly like a Bluesky bot
        let! _ = Bluesky.post agent "Hello from my own PDS!"
        ()
    | Error e ->
        printfn "Failed: %A" e

    do! Pds.stop pds
}
```

`Pds.createUser` handles the full lifecycle: it calls `com.atproto.server.createAccount` on the PDS, sets up the session on the returned `AtpAgent`, and gives you back an agent that is ready for any Bluesky operation -- posting, following, liking, everything.

## Configuration

| Function | Default | Description |
|----------|---------|-------------|
| `Pds.create hostname` | -- | Create a builder with the given hostname |
| `Pds.withPort port` | `2583` | Set the listening port |
| `Pds.withSigningKey key` | auto-generated P-256 | Use a specific signing key |
| `Pds.withAdminPassword pw` | `None` | Set the admin password |
| `Pds.withInviteCode code` | disabled | Require an invite code for account creation |
| `Pds.withAccessTokenLifetime ts` | 2 hours | Set access token expiry |
| `Pds.withRefreshTokenLifetime ts` | 90 days | Set refresh token expiry |

### Invite Codes
*)

let restrictedApp =
    Pds.create "my-pds.example.com"
    |> Pds.withPort 3000
    |> Pds.withInviteCode "my-secret-invite"
    |> Pds.configure

(**
### Custom Signing Key
*)

let key = Keys.generate Algorithm.P256

let appWithKey =
    Pds.create "my-pds.example.com"
    |> Pds.withSigningKey key
    |> Pds.configure

(**
## Event Hooks

Register handlers that fire when accounts are created, records are written, or records are deleted. Each hook receives a typed event with exactly the relevant data:
*)

let appWithHooks =
    Pds.create "my-pds.example.com"
    |> Pds.withPort 3000
    |> Pds.onAccountCreated (fun e ->
        printfn "Welcome @%s (%s)!" (Handle.value e.Handle) (Did.value e.Did))
    |> Pds.onRecordCreated (fun e ->
        if e.Collection = "app.bsky.feed.post" then
            printfn "New post: %s" (AtUri.value e.Uri))
    |> Pds.onRecordDeleted (fun e ->
        printfn "Deleted %s/%s" e.Collection e.Rkey)
    |> Pds.configure

(**
| Hook | Event Type | Fields |
|------|-----------|--------|
| `Pds.onAccountCreated` | `AccountCreatedEvent` | `Did`, `Handle` |
| `Pds.onRecordCreated` | `RecordCreatedEvent` | `Did`, `Collection`, `Rkey`, `Uri` |
| `Pds.onRecordDeleted` | `RecordDeletedEvent` | `Did`, `Collection`, `Rkey` |

## Running Modes

| Function | Returns | Use Case |
|----------|---------|----------|
| `Pds.run hostname port` | `unit` (blocks) | Simplest one-liner |
| `Pds.configure builder` | `WebApplication` | ASP.NET Core integration |
| `Pds.start builder` | `Task<RunningPds>` | Non-blocking, programmatic interaction |
| `Pds.mapEndpoints builder app` | `WebApplication` | Compose onto an existing app / TestServer |

### `RunningPds` Functions

| Function | Description |
|----------|-------------|
| `Pds.url pds` | Get the base URL |
| `Pds.createUser pds handle password` | Create account + return authenticated `AtpAgent` |
| `Pds.stop pds` | Graceful shutdown |

## Endpoints

The PDS implements the following AT Protocol endpoints:

### Server

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `com.atproto.server.describeServer` | GET | -- | Server metadata |
| `com.atproto.server.createAccount` | POST | -- | Register a new account |
| `com.atproto.server.createSession` | POST | -- | Log in with handle/DID + password |
| `com.atproto.server.refreshSession` | POST | Bearer (refresh) | Exchange refresh token for new tokens |
| `com.atproto.server.deleteSession` | POST | Bearer | Revoke the current session |
| `com.atproto.server.getSession` | GET | Bearer | Get current session info |

### Repository

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `com.atproto.repo.createRecord` | POST | Bearer | Create a record |
| `com.atproto.repo.getRecord` | GET | -- | Retrieve a record |
| `com.atproto.repo.deleteRecord` | POST | Bearer | Delete a record |
| `com.atproto.repo.listRecords` | GET | -- | List records in a collection |

### Identity and Health

| Path | Method | Description |
|------|--------|-------------|
| `com.atproto.identity.resolveHandle` | GET | Resolve handle to DID |
| `/_health` | GET | Health check |
| `/.well-known/atproto-did` | GET | Handle verification |

## Composing with Other Servers
*)

(*** hide ***)
open Microsoft.AspNetCore.Builder
(***)

let combined =
    let builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder()
    let webApp = builder.Build()

    let pdsConfig = Pds.create "my-pds.example.com"
    Pds.mapEndpoints pdsConfig webApp |> ignore

    webApp.MapGet("/my-custom-route", System.Func<string>(fun () -> "hello"))
    |> ignore

    webApp

(**
## Storage

All state is held in-memory using concurrent dictionaries. Data is lost on restart. This makes the PDS ideal for:

- **Development**: test your AT Protocol client code against a local server
- **Integration testing**: spin up a PDS in `TestServer` for automated tests
- **Prototyping**: quickly explore the AT Protocol without deploying infrastructure

For production use, you would extend the PDS with persistent storage backends.
*)
