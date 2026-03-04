# Phase 19: OAuth Authorization Server Implementation Plan

## Goal

Implement a full AT Protocol OAuth 2.0 authorization server, making FSharp.ATProto the first community SDK with 10/10 completeness on the [AT Protocol SDKs page](https://atproto.com/sdks).

## New Project

`src/FSharp.ATProto.OAuthServer/` with test project `tests/FSharp.ATProto.OAuthServer.Tests/`

## Dependencies

- `FSharp.ATProto.Syntax` (Did, Handle, Nsid)
- `FSharp.ATProto.OAuth` (reuse ClientMetadata, TokenResponse, DPoP utilities, PKCE types)
- `FSharp.ATProto.XrpcServer` (ASP.NET minimal API patterns, rate limiter, middleware helpers)
- `Microsoft.AspNetCore.App` framework reference

Does NOT depend on Crypto directly -- signing functions are injected as `byte[] -> byte[]` (same pattern as ServiceAuth).

## Architecture

### Storage Interfaces (Pluggable)

All state is behind F# interfaces so consumers can use any backend (in-memory for dev, PostgreSQL/Redis for production):

```fsharp
type ITokenStore =
    abstract CreateToken : tokenId:string * data:TokenData -> Task<unit>
    abstract ReadToken : tokenId:string -> Task<TokenData option>
    abstract DeleteToken : tokenId:string -> Task<unit>
    abstract RotateToken : tokenId:string * newId:string * newRefreshToken:string * newData:TokenData -> Task<unit>
    abstract FindByRefreshToken : refreshToken:string -> Task<TokenData option>
    abstract FindByCode : code:string -> Task<TokenData option>

type IRequestStore =
    abstract CreateRequest : requestId:string * data:RequestData -> Task<unit>
    abstract ReadRequest : requestId:string -> Task<RequestData option>
    abstract ConsumeCode : code:string -> Task<RequestData option>  // atomic
    abstract DeleteRequest : requestId:string -> Task<unit>

type IReplayStore =
    abstract IsUnique : ns:string * key:string * expiresAt:DateTimeOffset -> Task<bool>

type IAccountStore =
    abstract Authenticate : credentials:LoginCredentials -> Task<Result<AccountInfo, string>>
    abstract GetAccount : sub:Did -> Task<AccountInfo option>
```

### Source Files (8 files)

1. **Types.fs** -- Core types: `TokenData`, `RequestData`, `AccountInfo`, `LoginCredentials`, `OAuthServerConfig`, storage interfaces, `GrantType` DU, `OAuthScope` module
2. **DPoPValidator.fs** -- Server-side DPoP proof verification: parse JWT, verify ES256 signature, validate claims (htm, htu, iat, jti, ath, nonce), JWK thumbprint extraction, nonce generation/rotation
3. **ClientDiscovery.fs** -- Fetch and validate client metadata from `client_id` URL: HTTPS enforcement, loopback detection, metadata validation (dpop_bound_access_tokens, redirect_uris, scopes, jwks), caching
4. **TokenSigner.fs** -- Access token JWT creation: ES256 signing with injected key, claims (iss, sub, aud, exp, iat, jti, scope, cnf.jkt, client_id), JWKS endpoint key set generation
5. **Endpoints.fs** -- All OAuth HTTP endpoints:
   - `GET /.well-known/oauth-authorization-server` -- server metadata
   - `GET /.well-known/oauth-protected-resource` -- protected resource metadata
   - `GET /oauth/jwks` -- public key set
   - `POST /oauth/par` -- pushed authorization request
   - `GET /oauth/authorize` -- authorization/consent redirect
   - `POST /oauth/token` -- token exchange (authorization_code + refresh_token)
   - `POST /oauth/revoke` -- token revocation
6. **ConsentApi.fs** -- JSON API for consent UI frontend:
   - `POST /api/sign-in` -- authenticate user
   - `POST /api/consent` -- approve authorization
   - `POST /api/reject` -- reject authorization
7. **InMemoryStores.fs** -- In-memory implementations of all storage interfaces (for dev/testing)
8. **Server.fs** -- Builder API: `OAuthServer.configure`, `OAuthServer.withTokenStore`, etc. Composes all middleware into a `WebApplication`

### Implementation Tasks (6 steps)

#### Step 1: Types + Storage Interfaces + In-Memory Stores
- Define all types (TokenData, RequestData, AccountInfo, scopes, config)
- Define storage interfaces (ITokenStore, IRequestStore, IReplayStore, IAccountStore)
- Implement InMemoryStores (ConcurrentDictionary-based, suitable for dev/tests)
- Tests: store CRUD operations, atomic code consumption, replay detection

#### Step 2: DPoP Validator
- Parse DPoP JWT header (typ, alg, jwk)
- Verify ES256 signature using .NET ECDsa (extract key from embedded JWK)
- Validate claims: htm (HTTP method), htu (URL), iat (freshness), jti (uniqueness via IReplayStore), ath (access token hash), nonce
- Nonce generation: HMAC-SHA256(secret, timestamp) truncated, with configurable rotation interval
- Nonce validation: accept current and previous nonce (smooth rotation)
- Compute JWK thumbprint (SHA-256 of canonical JWK, base64url-encoded)
- Tests: valid proof acceptance, expired proof rejection, replay rejection, method mismatch, URL mismatch, nonce validation, thumbprint computation

#### Step 3: Client Discovery + PKCE Validation
- Fetch client_id URL with hardened HTTP client (timeout, size limit, no redirects)
- Parse client metadata JSON into ClientMetadata
- Validate: dpop_bound_access_tokens=true, response_types includes "code", grant_types includes "authorization_code", scope includes "atproto", redirect_uri validation, HTTPS enforcement
- Loopback client handling (http://localhost with virtual metadata)
- PKCE: validate S256 challenge/verifier, reject plain method
- Client assertion validation for confidential clients (private_key_jwt)
- Caching with configurable TTL
- Tests: valid metadata acceptance, missing fields rejection, loopback detection, PKCE round-trip, client assertion verification

#### Step 4: Token Signer + Access Token JWT
- Generate ES256 key pair for server signing
- Sign access token JWT with claims: iss, sub, aud, exp, iat, jti, scope, cnf.jkt, client_id
- Configurable token lifetime (default 5 minutes, max 30 minutes)
- JWKS endpoint: export public key as JWK Set
- Refresh token: generate opaque random string, store in ITokenStore
- Tests: JWT structure validation, claim presence, signature verification, JWKS format

#### Step 5: OAuth Endpoints
- **Metadata endpoints**: server metadata at well-known URL, protected resource metadata, JWKS
- **PAR endpoint**: validate client, DPoP, PKCE, scopes, redirect_uri; store request; return request_uri
- **Authorize endpoint**: look up request, redirect to consent UI or directly authorize
- **Token endpoint (auth_code)**: consume code atomically, validate PKCE verifier, validate DPoP, generate tokens, bind to DPoP key
- **Token endpoint (refresh)**: validate refresh token, check DPoP key binding, rotate tokens
- **Revoke endpoint**: revoke token, suppress errors per RFC 7009
- CORS headers on metadata, PAR, token endpoints
- DPoP-Nonce header on all token responses
- Authorization response with `iss` parameter (RFC 9207)
- Tests: full flow tests with in-memory stores, error cases, CORS, nonce handling

#### Step 6: Consent API + Server Builder
- Sign-in API endpoint (delegates to IAccountStore.Authenticate)
- Consent approval endpoint (generates auth code, redirects)
- Rejection endpoint (redirects with error=access_denied)
- Server builder: `OAuthServer.create`, `withTokenStore`, `withAccountStore`, `withSigningKey`, `withIssuer`, `configure`
- Integration tests: full PAR -> authorize -> token -> refresh -> revoke flow using the OAuth CLIENT from FSharp.ATProto.OAuth as the test client
- Tests: end-to-end flows with TestServer, error handling, rate limiting

### Test Strategy

- **Unit tests**: Each module tested independently with mock stores
- **Integration tests**: Full OAuth flow using ASP.NET `TestServer` + the existing OAuth client from `FSharp.ATProto.OAuth` as the test client (client talks to our server)
- **Interop validation**: Our OAuth client (`OAuthClient.startAuthorization` / `completeAuthorization` / `refreshToken`) should work against our server
- **Target**: ~80-100 tests

### Key Design Decisions

1. **Pluggable storage**: All 4 stores are interfaces, not concrete implementations. Ship in-memory stores for dev; consumers bring their own for production.
2. **No Crypto dependency**: ES256 signing uses native .NET `ECDsa`. DPoP verification uses native .NET. No BouncyCastle needed.
3. **No embedded UI**: The consent UI is consumer-provided. We provide the API endpoints (`/api/sign-in`, `/api/consent`, `/api/reject`) and a `configureConsentRedirect` hook. Consumers can use Razor Pages, Bolero, Giraffe, or a static SPA.
4. **Functional composition**: Server configuration uses the `|>` pipeline pattern from XrpcServer, not mutable builders.
5. **AT Protocol specific**: Enforces all AT Protocol OAuth requirements (PAR mandatory, PKCE mandatory, DPoP mandatory, S256 only, atproto scope required).

### Estimated Effort

6 steps, each 2-4 days = ~2-3 weeks total.

### File Structure

```
src/FSharp.ATProto.OAuthServer/
  FSharp.ATProto.OAuthServer.fsproj
  Types.fs
  DPoPValidator.fs
  ClientDiscovery.fs
  TokenSigner.fs
  Endpoints.fs
  ConsentApi.fs
  InMemoryStores.fs
  Server.fs

tests/FSharp.ATProto.OAuthServer.Tests/
  FSharp.ATProto.OAuthServer.Tests.fsproj
  InMemoryStoreTests.fs
  DPoPValidatorTests.fs
  ClientDiscoveryTests.fs
  TokenSignerTests.fs
  EndpointTests.fs
  IntegrationTests.fs
  Main.fs
```
