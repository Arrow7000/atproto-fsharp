(**
---
title: OAuth
category: Server-Side
categoryindex: 4
index: 24
description: OAuth 2.0 client with DPoP and PKCE, plus authorization server
keywords: fsharp, atproto, oauth, dpop, pkce, authorization, token
---

# OAuth

AT Protocol uses OAuth 2.0 with two mandatory security extensions: **DPoP** (Demonstration of Proof-of-Possession, RFC 9449) binds access tokens to a cryptographic key pair so stolen tokens cannot be replayed, and **PKCE** (Proof Key for Code Exchange, RFC 7636) prevents authorization code interception. FSharp.ATProto provides both an OAuth client for applications that authenticate users and an OAuth server for PDS operators.

## OAuth Client

`FSharp.ATProto.OAuth` implements the full client-side OAuth flow. Use this when building an application that needs to authenticate Bluesky users through their browser.
*)

(*** hide ***)
#nowarn "20"
#r "../../src/FSharp.ATProto.Syntax/bin/Release/net10.0/FSharp.ATProto.Syntax.dll"
#r "../../src/FSharp.ATProto.DRISL/bin/Release/net10.0/FSharp.ATProto.DRISL.dll"
#r "../../src/FSharp.ATProto.Core/bin/Release/net10.0/FSharp.ATProto.Core.dll"
#r "../../src/FSharp.ATProto.OAuth/bin/Release/net10.0/FSharp.ATProto.OAuth.dll"
#r "../../src/FSharp.ATProto.OAuthServer/bin/Release/net10.0/FSharp.ATProto.OAuthServer.dll"
open FSharp.ATProto.Syntax
open FSharp.ATProto.Core
open FSharp.ATProto.OAuth
open FSharp.ATProto.OAuthServer
open System
(***)

open FSharp.ATProto.OAuth
open System.Net.Http

(**
### Discovery

Before starting an authorization flow, discover the authorization server for the user's PDS. `Discovery.discover` performs two-step discovery: it fetches the PDS's protected resource metadata (RFC 9728) to find the authorization server, then fetches the authorization server metadata (RFC 8414):
*)

(*** hide ***)
let httpClient = Unchecked.defaultof<HttpClient>
let serverMetadata = Unchecked.defaultof<AuthorizationServerMetadata>
let authState = Unchecked.defaultof<AuthorizationState>
let authorizationCode = Unchecked.defaultof<string>
let session = Unchecked.defaultof<OAuthSession>
let saveSession (_s: OAuthSession) = ()
(***)

(**
```fsharp
let httpClient = new HttpClient()
let! serverMetadata = Discovery.discover httpClient "https://bsky.social"
```

You can also discover each step separately with `Discovery.discoverProtectedResource` and `Discovery.discoverAuthorizationServer`.

### Authorization Flow

The flow has three phases: start, user approval, and completion.

**1. Start the authorization flow.** `OAuthClient.startAuthorization` generates a PKCE challenge and DPoP key pair, submits a Pushed Authorization Request (PAR) if the server supports it, and returns the URL to redirect the user to:
*)

let clientMetadata : ClientMetadata =
    { ClientId = "https://myapp.example.com/client-metadata.json"
      ClientUri = Some "https://myapp.example.com"
      RedirectUris = [ "https://myapp.example.com/callback" ]
      Scope = "atproto transition:generic"
      GrantTypes = [ "authorization_code"; "refresh_token" ]
      ResponseTypes = [ "code" ]
      TokenEndpointAuthMethod = "none"
      ApplicationType = "web"
      DpopBoundAccessTokens = true }

(**
```fsharp
// serverMetadata is the AuthorizationServerMetadata from discovery
let! result = OAuthClient.startAuthorization httpClient clientMetadata serverMetadata "https://myapp.example.com/callback"
let authorizationUrl, authState = result
// Redirect the user's browser to authorizationUrl
// Save authState for the callback
```

The `AuthorizationState` record holds the PKCE verifier, DPoP key pair, and server metadata. You must persist this between the redirect and the callback.

**2. User approves in their browser.** The authorization server redirects back to your `redirect_uri` with a `code` and `state` parameter.

**3. Exchange the code for tokens.** `OAuthClient.completeAuthorization` sends the authorization code with the PKCE verifier and a DPoP proof to get an access token:

```fsharp
let! session = OAuthClient.completeAuthorization httpClient clientMetadata authState authorizationCode
```

This returns an `OAuthSession` containing DPoP-bound tokens:

```fsharp
type OAuthSession = {
    AccessToken: string
    RefreshToken: string option
    ExpiresAt: DateTimeOffset
    Did: Did
    DpopKeyPair: ECDsa
    TokenEndpoint: string
}
```

### Using the Session with AtpAgent

`OAuthBridge.resumeSession` bridges an `OAuthSession` to an `AtpAgent`, so all convenience functions (`Bluesky.post`, `Bluesky.like`, etc.) work with OAuth authentication. It configures DPoP proof generation on every request and automatic token refresh on 401:
*)

(*** hide ***)
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
(***)

(**
```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

let agent = AtpAgent.create "https://bsky.social"

let authedAgent =
    agent
    |> OAuthBridge.resumeSession clientMetadata session (Some (fun newSession ->
        // Persist the refreshed session to disk or database
        saveSession newSession))

// Now use any convenience function
taskResult {
    let! postRef = Bluesky.post authedAgent "Hello from OAuth!"
    return postRef
}
```

The optional `onSessionUpdate` callback fires whenever the token is refreshed, so you can persist the new session.

### Refreshing Tokens

If you manage token lifecycle yourself instead of using `OAuthBridge`, call `OAuthClient.refreshToken` directly:

```fsharp
let! newSession = OAuthClient.refreshToken httpClient clientMetadata session
```

Check expiration with `OAuthBridge.isExpired`:
*)

if OAuthBridge.isExpired session then
    // refresh the token
    ()

(**
### DPoP Utilities

The `DPoP` module provides low-level utilities if you need to work with DPoP proofs directly:

| Function | Purpose |
|---|---|
| `DPoP.generateKeyPair ()` | Generate an ES256 (P-256) key pair |
| `DPoP.generatePkce ()` | Generate a PKCE verifier and S256 challenge |
| `DPoP.createProof key method uri ath nonce` | Create a DPoP proof JWT |
| `DPoP.hashAccessToken token` | SHA-256 hash for the `ath` claim |

### Error Handling

All OAuth operations return `Result<'T, OAuthError>`:

```fsharp
type OAuthError =
    | DiscoveryFailed of message: string
    | TokenRequestFailed of error: string * description: string option
    | DPoPError of message: string
    | InvalidState of message: string
    | NetworkError of message: string
```

---

## OAuth Server

`FSharp.ATProto.OAuthServer` implements an AT Protocol-compliant authorization server. Use this if you are running your own PDS and need to issue tokens to client applications.

The server enforces the AT Protocol's mandatory security requirements: Pushed Authorization Requests (PAR), DPoP-bound tokens, and PKCE with S256.
*)

open FSharp.ATProto.OAuthServer

(**
### Endpoints

The server exposes these routes:

| Route | Method | Purpose |
|---|---|---|
| `/.well-known/oauth-authorization-server` | GET | Server metadata (RFC 8414) |
| `/.well-known/oauth-protected-resource` | GET | Protected resource metadata (RFC 9728) |
| `/oauth/jwks` | GET | Public signing keys |
| `/oauth/par` | POST | Pushed Authorization Requests |
| `/oauth/authorize` | GET | Authorization endpoint |
| `/oauth/token` | POST | Token exchange and refresh |
| `/oauth/revoke` | POST | Token revocation |
| `/api/sign-in` | POST | Consent UI: user authentication |
| `/api/consent` | POST | Consent UI: approve authorization |
| `/api/reject` | POST | Consent UI: deny authorization |

### Pluggable Storage

The server defines four store interfaces for persistence. All have in-memory implementations for development; swap them out for your production database:

| Interface | Purpose |
|---|---|
| `ITokenStore` | Issued access and refresh tokens |
| `IRequestStore` | Pending authorization requests |
| `IReplayStore` | DPoP nonce replay detection |
| `IAccountStore` | User authentication and account lookup |

### Configuration

Use the builder pattern to configure and launch the server:
*)

(*** hide ***)
let myAccountStore = Unchecked.defaultof<IAccountStore>
let myTokenStore = Unchecked.defaultof<ITokenStore>
(***)

(**
```fsharp
let app =
    OAuthServer.defaults
    |> OAuthServer.withIssuer "https://auth.example.com"
    |> OAuthServer.withPort 4000
    |> OAuthServer.withAccountStore myAccountStore
    |> OAuthServer.withTokenStore myTokenStore
    |> OAuthServer.configure

app.Run()
```

All builder functions:

| Function | Purpose |
|---|---|
| `withIssuer url` | Set the issuer URL (required) |
| `withPort port` | Set the listening port |
| `withSigningKey key` | ES256 key for signing tokens (auto-generated if omitted) |
| `withServiceDid did` | Set the service DID |
| `withTokenStore store` | Custom token persistence |
| `withRequestStore store` | Custom request persistence |
| `withReplayStore store` | Custom replay detection |
| `withAccountStore store` | Custom account authentication |
| `withAccessTokenLifetime span` | Token expiry (default: 5 minutes) |
| `withRefreshTokenLifetime span` | Refresh token expiry (default: 90 days) |
| `withScopesSupported scopes` | Supported scopes (default: `atproto`, `transition:generic`) |
| `withConsentPath path` | Path for the consent UI (default: `/consent`) |

If you omit the signing key, token store, request store, replay store, or account store, the server uses in-memory defaults. This is convenient for development but not suitable for production -- tokens and sessions are lost on restart.
*)
