module FSharp.ATProto.OAuthServer.Tests.EndpointTests

open System
open System.IO
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Expecto
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open FSharp.ATProto.Syntax
open FSharp.ATProto.OAuth
open FSharp.ATProto.OAuthServer

let private unwrap =
    function
    | Ok v -> v
    | Error e -> failtest (sprintf "Expected Ok: %A" e)

let private testDid = Did.parse "did:plc:testuser123" |> unwrap
let private testHandle = Handle.parse "test.bsky.social" |> unwrap

/// Create an EndpointDeps with in-memory stores and a real signing key.
let private makeDeps () =
    let key = TokenSigner.createSigningKey ()

    let config =
        { OAuthServerConfig.defaults with
            Issuer = "https://auth.example.com"
            SigningKey = TokenSigner.makeSigningFunction key
            PublicKeyJwk = TokenSigner.exportPublicJwk key }

    let accountStore = InMemoryAccountStore()

    accountStore.AddAccount(
        "testuser",
        "password123",
        { Sub = testDid
          Handle = Some testHandle
          DisplayName = Some "Test User" }
    )

    { Config = config
      TokenStore = InMemoryTokenStore() :> ITokenStore
      RequestStore = InMemoryRequestStore() :> IRequestStore
      ReplayStore = InMemoryReplayStore() :> IReplayStore
      AccountStore = accountStore :> IAccountStore
      HttpClient = new HttpClient()
      ClientCache = ClientDiscovery.ClientCache(TimeSpan.FromMinutes 5.0)
      NonceSecret = RandomNumberGenerator.GetBytes(32) }
    : Endpoints.EndpointDeps

/// Read the response body from a DefaultHttpContext whose Response.Body is a MemoryStream.
let private readResponseBody (ctx: HttpContext) : string =
    ctx.Response.Body.Seek(0L, SeekOrigin.Begin) |> ignore
    use reader = new StreamReader(ctx.Response.Body)
    reader.ReadToEnd()

/// Execute an IResult against a DefaultHttpContext and return the response body as string.
let private executeResult (result: IResult) (ctx: HttpContext) : System.Threading.Tasks.Task<string> =
    task {
        do! result.ExecuteAsync(ctx)
        return readResponseBody ctx
    }

/// Parse a JSON string into a JsonDocument.
let private parseJson (s: string) : JsonDocument = JsonDocument.Parse(s)

/// Create a DefaultHttpContext with a writable response body stream and service provider.
let private makeContext () =
    let services = ServiceCollection()
    services.AddLogging() |> ignore
    let ctx = DefaultHttpContext()
    ctx.RequestServices <- services.BuildServiceProvider()
    ctx.Response.Body <- new MemoryStream()
    ctx

/// Set up a JSON body on the context's request.
let private setJsonBody (ctx: HttpContext) (json: string) =
    ctx.Request.Body <- new MemoryStream(Encoding.UTF8.GetBytes(json))
    ctx.Request.ContentType <- "application/json"
    ctx.Request.ContentLength <- Nullable(int64 (Encoding.UTF8.GetByteCount(json)))

// ── Metadata Endpoint Tests ──────────────────────────────────────────

[<Tests>]
let serverMetadataTests =
    testList
        "OAuthServer.Endpoints.serverMetadata"
        [ testTask "returns correct JSON structure with all required fields" {
              let deps = makeDeps ()
              let ctx = makeContext ()

              let! result = Endpoints.serverMetadata deps ctx
              let! body = executeResult result ctx
              let doc = parseJson body
              let root = doc.RootElement

              Expect.equal (root.GetProperty("issuer").GetString()) "https://auth.example.com" "issuer"

              Expect.equal
                  (root.GetProperty("authorization_endpoint").GetString())
                  "https://auth.example.com/oauth/authorize"
                  "authorization_endpoint"

              Expect.equal
                  (root.GetProperty("token_endpoint").GetString())
                  "https://auth.example.com/oauth/token"
                  "token_endpoint"

              Expect.equal
                  (root.GetProperty("pushed_authorization_request_endpoint").GetString())
                  "https://auth.example.com/oauth/par"
                  "par_endpoint"

              Expect.equal
                  (root.GetProperty("revocation_endpoint").GetString())
                  "https://auth.example.com/oauth/revoke"
                  "revocation_endpoint"

              Expect.equal
                  (root.GetProperty("jwks_uri").GetString())
                  "https://auth.example.com/oauth/jwks"
                  "jwks_uri"

              Expect.isTrue (root.GetProperty("require_pushed_authorization_requests").GetBoolean()) "require_par"

              Expect.isTrue
                  (root.GetProperty("authorization_response_iss_parameter_supported").GetBoolean())
                  "iss_param_supported"

              // Check arrays
              let scopes =
                  [ for s in root.GetProperty("scopes_supported").EnumerateArray() do
                        yield s.GetString() ]

              Expect.contains scopes "atproto" "scopes should contain atproto"

              let responseTypes =
                  [ for s in root.GetProperty("response_types_supported").EnumerateArray() do
                        yield s.GetString() ]

              Expect.equal responseTypes [ "code" ] "response_types should be [code]"

              let grantTypes =
                  [ for s in root.GetProperty("grant_types_supported").EnumerateArray() do
                        yield s.GetString() ]

              Expect.contains grantTypes "authorization_code" "grant_types should contain authorization_code"
              Expect.contains grantTypes "refresh_token" "grant_types should contain refresh_token"

              let codeMethods =
                  [ for s in root.GetProperty("code_challenge_methods_supported").EnumerateArray() do
                        yield s.GetString() ]

              Expect.equal codeMethods [ "S256" ] "code_challenge_methods should be [S256]"

              let dpopAlgs =
                  [ for s in root.GetProperty("dpop_signing_alg_values_supported").EnumerateArray() do
                        yield s.GetString() ]

              Expect.equal dpopAlgs [ "ES256" ] "dpop_signing_alg_values should be [ES256]"
          }

          testTask "sets CORS header" {
              let deps = makeDeps ()
              let ctx = makeContext ()

              let! _result = Endpoints.serverMetadata deps ctx

              let corsHeader = ctx.Response.Headers.["Access-Control-Allow-Origin"].ToString()
              Expect.equal corsHeader "*" "CORS should allow all origins"
          } ]

[<Tests>]
let protectedResourceMetadataTests =
    testList
        "OAuthServer.Endpoints.protectedResourceMetadata"
        [ testTask "returns correct JSON structure" {
              let deps = makeDeps ()
              let ctx = makeContext ()

              let! result = Endpoints.protectedResourceMetadata deps ctx
              let! body = executeResult result ctx
              let doc = parseJson body
              let root = doc.RootElement

              Expect.equal (root.GetProperty("resource").GetString()) "https://auth.example.com" "resource"

              let authServers =
                  [ for s in root.GetProperty("authorization_servers").EnumerateArray() do
                        yield s.GetString() ]

              Expect.equal authServers [ "https://auth.example.com" ] "authorization_servers"

              let scopes =
                  [ for s in root.GetProperty("scopes_supported").EnumerateArray() do
                        yield s.GetString() ]

              Expect.contains scopes "atproto" "scopes should contain atproto"
          } ]

[<Tests>]
let jwksTests =
    testList
        "OAuthServer.Endpoints.jwks"
        [ testTask "returns valid JWKS with one key" {
              let deps = makeDeps ()
              let ctx = makeContext ()

              let! result = Endpoints.jwks deps ctx
              let! body = executeResult result ctx
              let doc = parseJson body
              let root = doc.RootElement
              let keys = root.GetProperty("keys")
              Expect.equal (keys.GetArrayLength()) 1 "Should have exactly one key"

              let key = keys.[0]
              Expect.equal (key.GetProperty("kty").GetString()) "EC" "kty"
              Expect.equal (key.GetProperty("crv").GetString()) "P-256" "crv"
              Expect.equal (key.GetProperty("alg").GetString()) "ES256" "alg"
              Expect.equal (key.GetProperty("use").GetString()) "sig" "use"
              Expect.isTrue (key.TryGetProperty("kid") |> fst) "should have kid"
              Expect.isTrue (key.TryGetProperty("x") |> fst) "should have x"
              Expect.isTrue (key.TryGetProperty("y") |> fst) "should have y"
          } ]

// ── Authorize Endpoint Tests ─────────────────────────────────────────

[<Tests>]
let authorizeTests =
    testList
        "OAuthServer.Endpoints.authorize"
        [ testTask "rejects missing request_uri" {
              let deps = makeDeps ()
              let ctx = makeContext ()

              let! result = Endpoints.authorize deps ctx
              let! body = executeResult result ctx
              let doc = parseJson body

              Expect.equal (doc.RootElement.GetProperty("error").GetString()) "invalid_request" "error code"
              Expect.stringContains (doc.RootElement.GetProperty("error_description").GetString()) "request_uri" "error message"
          }

          testTask "rejects invalid request_uri format" {
              let deps = makeDeps ()
              let ctx = makeContext ()
              ctx.Request.QueryString <- QueryString("?request_uri=bad-format")

              let! result = Endpoints.authorize deps ctx
              let! body = executeResult result ctx
              let doc = parseJson body

              Expect.equal (doc.RootElement.GetProperty("error").GetString()) "invalid_request" "error code"
              Expect.stringContains (doc.RootElement.GetProperty("error_description").GetString()) "format" "error description"
          }

          testTask "rejects unknown request_uri" {
              let deps = makeDeps ()
              let ctx = makeContext ()
              ctx.Request.QueryString <- QueryString("?request_uri=urn:ietf:params:oauth:request_uri:nonexistent")

              let! result = Endpoints.authorize deps ctx
              let! body = executeResult result ctx
              let doc = parseJson body

              Expect.equal (doc.RootElement.GetProperty("error").GetString()) "invalid_request" "error code"
              Expect.stringContains (doc.RootElement.GetProperty("error_description").GetString()) "Unknown" "error description"
          }

          testTask "redirects to consent URL for valid request" {
              let deps = makeDeps ()
              let requestId = "test-request-id-123"
              let now = DateTimeOffset.UtcNow

              let requestData =
                  { ClientId = "http://localhost/client-metadata.json"
                    RedirectUri = "http://127.0.0.1/callback"
                    Scope = "atproto"
                    State = Some "state123"
                    CodeChallenge = "challenge"
                    CodeChallengeMethod = "S256"
                    DpopJkt = "thumbprint"
                    Code = None
                    AuthorizedSub = None
                    ExpiresAt = now.AddMinutes(10.0)
                    CreatedAt = now }

              do! deps.RequestStore.CreateRequest(requestId, requestData)

              let ctx = makeContext ()
              ctx.Request.QueryString <- QueryString(sprintf "?request_uri=urn:ietf:params:oauth:request_uri:%s" requestId)

              let! (result: IResult) = Endpoints.authorize deps ctx
              do! result.ExecuteAsync(ctx)

              Expect.equal ctx.Response.StatusCode 302 "Should redirect"
              let location = ctx.Response.Headers.Location.ToString()
              Expect.stringContains location "consent" "Should redirect to consent"
              Expect.stringContains location requestId "Should include request_id"
          } ]

// ── ConsentApi Tests ─────────────────────────────────────────────────

[<Tests>]
let signInTests =
    testList
        "OAuthServer.ConsentApi.signIn"
        [ testTask "succeeds with valid credentials" {
              let deps = makeDeps ()
              let ctx = makeContext ()
              setJsonBody ctx """{"identifier":"testuser","password":"password123"}"""

              let! result = ConsentApi.signIn deps ctx
              let! body = executeResult result ctx
              let doc = parseJson body

              Expect.equal (doc.RootElement.GetProperty("sub").GetString()) (Did.value testDid) "sub should match"
              Expect.equal (doc.RootElement.GetProperty("handle").GetString()) (Handle.value testHandle) "handle should match"
          }

          testTask "fails with wrong password" {
              let deps = makeDeps ()
              let ctx = makeContext ()
              setJsonBody ctx """{"identifier":"testuser","password":"wrongpassword"}"""

              let! (result: IResult) = ConsentApi.signIn deps ctx
              do! result.ExecuteAsync(ctx)

              Expect.equal ctx.Response.StatusCode 401 "Should return 401"
              let body = readResponseBody ctx
              let doc = parseJson body
              Expect.isTrue (doc.RootElement.TryGetProperty("error") |> fst) "Should have error field"
          }

          testTask "fails with missing credentials" {
              let deps = makeDeps ()
              let ctx = makeContext ()
              setJsonBody ctx """{}"""

              let! (result: IResult) = ConsentApi.signIn deps ctx
              do! result.ExecuteAsync(ctx)

              Expect.equal ctx.Response.StatusCode 400 "Should return 400"
          } ]

[<Tests>]
let consentTests =
    testList
        "OAuthServer.ConsentApi.consent"
        [ testTask "returns redirect URL with code for valid request" {
              let deps = makeDeps ()
              let requestId = "consent-test-req"
              let now = DateTimeOffset.UtcNow

              let requestData =
                  { ClientId = "http://localhost/client-metadata.json"
                    RedirectUri = "http://127.0.0.1/callback"
                    Scope = "atproto"
                    State = Some "mystate"
                    CodeChallenge = "challenge"
                    CodeChallengeMethod = "S256"
                    DpopJkt = "thumbprint"
                    Code = None
                    AuthorizedSub = None
                    ExpiresAt = now.AddMinutes(10.0)
                    CreatedAt = now }

              do! deps.RequestStore.CreateRequest(requestId, requestData)

              let ctx = makeContext ()
              let jsonBody = sprintf """{"request_id":"%s","sub":"%s"}""" requestId (Did.value testDid)
              setJsonBody ctx jsonBody

              let! result = ConsentApi.consent deps ctx
              let! body = executeResult result ctx
              let doc = parseJson body
              let redirectUri = doc.RootElement.GetProperty("redirect_uri").GetString()

              Expect.stringContains redirectUri "http://127.0.0.1/callback" "Should redirect to client callback"
              Expect.stringContains redirectUri "code=" "Should include authorization code"
              Expect.stringContains redirectUri "iss=" "Should include issuer"
              Expect.stringContains redirectUri "state=mystate" "Should include state"
          }

          testTask "returns 404 for unknown request" {
              let deps = makeDeps ()
              let ctx = makeContext ()
              let jsonBody = sprintf """{"request_id":"nonexistent","sub":"%s"}""" (Did.value testDid)
              setJsonBody ctx jsonBody

              let! (result: IResult) = ConsentApi.consent deps ctx
              do! result.ExecuteAsync(ctx)

              Expect.equal ctx.Response.StatusCode 404 "Should return 404"
          } ]

[<Tests>]
let rejectTests =
    testList
        "OAuthServer.ConsentApi.reject"
        [ testTask "returns redirect URL with error" {
              let deps = makeDeps ()
              let requestId = "reject-test-req"
              let now = DateTimeOffset.UtcNow

              let requestData =
                  { ClientId = "http://localhost/client-metadata.json"
                    RedirectUri = "http://127.0.0.1/callback"
                    Scope = "atproto"
                    State = Some "mystate"
                    CodeChallenge = "challenge"
                    CodeChallengeMethod = "S256"
                    DpopJkt = "thumbprint"
                    Code = None
                    AuthorizedSub = None
                    ExpiresAt = now.AddMinutes(10.0)
                    CreatedAt = now }

              do! deps.RequestStore.CreateRequest(requestId, requestData)

              let ctx = makeContext ()
              setJsonBody ctx (sprintf """{"request_id":"%s"}""" requestId)

              let! result = ConsentApi.reject deps ctx
              let! body = executeResult result ctx
              let doc = parseJson body
              let redirectUri = doc.RootElement.GetProperty("redirect_uri").GetString()

              Expect.stringContains redirectUri "error=access_denied" "Should include access_denied error"
              Expect.stringContains redirectUri "state=mystate" "Should include state"
          }

          testTask "returns 404 for unknown request" {
              let deps = makeDeps ()
              let ctx = makeContext ()
              setJsonBody ctx """{"request_id":"nonexistent"}"""

              let! (result: IResult) = ConsentApi.reject deps ctx
              do! result.ExecuteAsync(ctx)

              Expect.equal ctx.Response.StatusCode 404 "Should return 404"
          } ]

// ── Token Signer Sanity in Endpoint Context ──────────────────────────

[<Tests>]
let tokenSignerInEndpointContextTests =
    testList
        "OAuthServer.Endpoints.tokenSigner"
        [ test "createAccessToken produces valid 3-part JWT" {
              let key = TokenSigner.createSigningKey ()

              let config =
                  { OAuthServerConfig.defaults with
                      Issuer = "https://auth.example.com"
                      SigningKey = TokenSigner.makeSigningFunction key
                      PublicKeyJwk = TokenSigner.exportPublicJwk key }

              let token =
                  TokenSigner.createAccessToken config testDid "https://client.example.com" "atproto" "jkt123" DateTimeOffset.UtcNow

              let parts = token.Split('.')
              Expect.equal parts.Length 3 "JWT should have 3 parts"

              // Parse header
              let headerJson = Encoding.UTF8.GetString(TokenSigner.fromBase64Url parts.[0])
              let headerDoc = parseJson headerJson
              Expect.equal (headerDoc.RootElement.GetProperty("typ").GetString()) "at+jwt" "typ should be at+jwt"
              Expect.equal (headerDoc.RootElement.GetProperty("alg").GetString()) "ES256" "alg should be ES256"
          } ]
