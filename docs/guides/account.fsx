(**
---
title: Account
category: Advanced Guides
categoryindex: 3
index: 21
description: Account creation, deletion, session management, and labeler configuration
keywords: fsharp, atproto, bluesky, account, session, login, logout, labeler
---

# Account

FSharp.ATProto provides functions for authentication, session management, account lifecycle, and agent configuration. These are the entry points for all interaction with the AT Protocol.

All examples use `taskResult {}`. See the [Error Handling guide](error-handling.html) for details.
*)

(*** hide ***)
#nowarn "20"
#r "../../src/FSharp.ATProto.Syntax/bin/Release/net10.0/FSharp.ATProto.Syntax.dll"
#r "../../src/FSharp.ATProto.Core/bin/Release/net10.0/FSharp.ATProto.Core.dll"
#r "../../src/FSharp.ATProto.Bluesky/bin/Release/net10.0/FSharp.ATProto.Bluesky.dll"

open FSharp.ATProto.Syntax
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

let agent = Unchecked.defaultof<AtpAgent>
(***)

open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax

(**
## Authentication

| Function | Returns | Description |
|---|---|---|
| `Bluesky.login` | `Task<Result<AtpAgent, XrpcError>>` | Authenticate with base URL, identifier, and app password |
| `Bluesky.loginWithClient` | `Task<Result<AtpAgent, XrpcError>>` | Login with a custom `HttpClient` |
| `Bluesky.resumeSession` | `AtpAgent` | Resume from saved session data (no network call) |
| `Bluesky.resumeSessionWithClient` | `AtpAgent` | Resume with a custom `HttpClient` |
| `Bluesky.logout` | `Task<Result<unit, XrpcError>>` | Terminate the session on the server |

### Basic Login
*)

taskResult {
    let! agent = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"
    printfn "Logged in as %s" (Did.value agent.Session.Value.Did)
}

(**
### Session Persistence

The `AtpSession` record contains the JWTs and identity needed to restore a session without re-authenticating:

```fsharp
type AtpSession =
    { AccessJwt : string
      RefreshJwt : string
      Did : Did
      Handle : Handle }
```

Save the session after login, then restore it later:
*)

// Save after login
taskResult {
    let! agent = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"
    let session = agent.Session.Value
    // Persist session.AccessJwt, session.RefreshJwt, session.Did, session.Handle
    return agent
}

(*** hide ***)
let savedSession = Unchecked.defaultof<AtpSession>
(***)

// Restore later (no network call)
let agent = Bluesky.resumeSession "https://bsky.social" savedSession

(**
The access JWT expires after a short period. The library automatically refreshes it using the refresh JWT on the first 401 response, so a restored session remains functional as long as the refresh JWT is valid.

### Custom HttpClient

Use `loginWithClient` or `resumeSessionWithClient` when you need a custom HTTP handler (e.g., for testing with a mock handler or custom timeouts):
*)

let client = new System.Net.Http.HttpClient()
client.Timeout <- System.TimeSpan.FromSeconds(30.0)

taskResult {
    let! agent = Bluesky.loginWithClient client "https://bsky.social" "handle.bsky.social" "app-password"
    return agent
}

(**
## Account Lifecycle

| Function | Description |
|---|---|
| `Bluesky.createAccount` | Create a new account on a PDS |
| `Bluesky.requestAccountDelete` | Request deletion (sends confirmation email) |
| `Bluesky.deleteAccount` | Confirm deletion with token from email |

### Creating an Account
*)

(*** hide ***)
let agent = Unchecked.defaultof<AtpAgent>
(***)

taskResult {
    let handle = Handle.parse "new-user.my-pds.example" |> Result.defaultWith failwith

    let! agent =
        Bluesky.createAccount
            "https://my-pds.example"
            handle
            (Some "user@example.com")
            (Some "strong-password")
            None // invite code, if required by the PDS

    printfn "Account created: %s" (Did.value agent.Session.Value.Did)
}

(**
### Deleting an Account

Account deletion is a two-step process. First, request deletion (which sends a confirmation email), then confirm with the token from the email:
*)

taskResult {
    // Step 1: Request deletion -- sends confirmation email
    do! Bluesky.requestAccountDelete agent

    // Step 2: After receiving the email, confirm with the token
    do! Bluesky.deleteAccount agent "account-password" "token-from-email"
    // Agent session is cleared after successful deletion
}

(**
## Agent Configuration

### Labeler Subscriptions

`AtpAgent.configureLabelers` returns a new agent configured with the `atproto-accept-labelers` header. This tells the server which labeler services to include labels from in responses:
*)

taskResult {
    let! agent = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"

    // Subscribe to a custom labeler (redact=false means labels are informational)
    let agent = AtpAgent.configureLabelers [ "did:plc:my-labeler", false ] agent

    // Subscribe with redact=true (labeler's labels can hide content entirely)
    let agent = AtpAgent.configureLabelers [ "did:plc:my-labeler", true ] agent

    // Multiple labelers
    let agent =
        AtpAgent.configureLabelers
            [ "did:plc:labeler-one", false
              "did:plc:labeler-two", true ]
            agent

    return agent
}

(**
### Chat Proxy

`AtpAgent.withChatProxy` returns a new agent with the proxy header needed for Bluesky DM operations:
*)

let chatAgent = AtpAgent.withChatProxy agent

(**
The chat proxy header (`atproto-proxy: did:web:api.bsky.chat#bsky_chat`) is required for all `chat.bsky.*` endpoints. See the [Chat guide](chat.html) for usage.
*)
