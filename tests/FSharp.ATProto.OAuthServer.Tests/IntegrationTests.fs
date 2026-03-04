module FSharp.ATProto.OAuthServer.Tests.IntegrationTests

open System
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.TestHost
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

/// Parse a JSON string into a JsonDocument.
let private parseJson (s: string) : JsonDocument = JsonDocument.Parse(s)

/// Helper: create a WebApplication with TestServer, mapping all OAuth routes manually,
/// and run a test function with the HttpClient and EndpointDeps for inspection.
let private withTestServer
    (f: HttpClient -> Endpoints.EndpointDeps -> Task<unit>)
    : Task<unit> =
    task {
        let signingKey = TokenSigner.createSigningKey ()

        let config =
            { OAuthServerConfig.defaults with
                Issuer = "https://auth.example.com"
                SigningKey = TokenSigner.makeSigningFunction signingKey
                PublicKeyJwk = TokenSigner.exportPublicJwk signingKey }

        let tokenStore = InMemoryTokenStore() :> ITokenStore
        let requestStore = InMemoryRequestStore() :> IRequestStore
        let replayStore = InMemoryReplayStore() :> IReplayStore
        let accountStore = InMemoryAccountStore()

        accountStore.AddAccount(
            "testuser",
            "password123",
            { Sub = testDid
              Handle = Some testHandle
              DisplayName = Some "Test User" }
        )

        let nonceSecret = RandomNumberGenerator.GetBytes(32)

        let deps: Endpoints.EndpointDeps =
            { Config = config
              TokenStore = tokenStore
              RequestStore = requestStore
              ReplayStore = replayStore
              AccountStore = accountStore :> IAccountStore
              HttpClient = new HttpClient()
              ClientCache = ClientDiscovery.ClientCache(TimeSpan.FromMinutes 5.0)
              NonceSecret = nonceSecret }

        // Build WebApplication with TestServer
        let builder = WebApplication.CreateBuilder()
        builder.WebHost.UseTestServer() |> ignore
        let app = builder.Build()

        // Map all OAuth routes (replicating Server.fs configure)
        app.MapGet(
            "/.well-known/oauth-authorization-server",
            Func<HttpContext, _>(fun ctx -> Endpoints.serverMetadata deps ctx)
        )
        |> ignore

        app.MapGet(
            "/.well-known/oauth-protected-resource",
            Func<HttpContext, _>(fun ctx -> Endpoints.protectedResourceMetadata deps ctx)
        )
        |> ignore

        app.MapGet("/oauth/jwks", Func<HttpContext, _>(fun ctx -> Endpoints.jwks deps ctx))
        |> ignore

        app.MapPost("/oauth/par", Func<HttpContext, _>(fun ctx -> Endpoints.par deps ctx))
        |> ignore

        app.MapGet(
            "/oauth/authorize",
            Func<HttpContext, _>(fun ctx -> Endpoints.authorize deps ctx)
        )
        |> ignore

        app.MapPost("/oauth/token", Func<HttpContext, _>(fun ctx -> Endpoints.token deps ctx))
        |> ignore

        app.MapPost("/oauth/revoke", Func<HttpContext, _>(fun ctx -> Endpoints.revoke deps ctx))
        |> ignore

        app.MapPost("/api/sign-in", Func<HttpContext, _>(fun ctx -> ConsentApi.signIn deps ctx))
        |> ignore

        app.MapPost("/api/consent", Func<HttpContext, _>(fun ctx -> ConsentApi.consent deps ctx))
        |> ignore

        app.MapPost("/api/reject", Func<HttpContext, _>(fun ctx -> ConsentApi.reject deps ctx))
        |> ignore

        do! app.StartAsync()

        let server = app.Services.GetRequiredService<IServer>() :?> TestServer
        let client = server.CreateClient()

        try
            do! f client deps
        finally
            client.Dispose()
            app.StopAsync().Wait()
    }

/// Create a form-encoded POST request.
let private formPost (url: string) (fields: (string * string) list) : HttpRequestMessage =
    let msg = new HttpRequestMessage(HttpMethod.Post, url)
    msg.Content <- new FormUrlEncodedContent(fields |> List.map (fun (k, v) -> System.Collections.Generic.KeyValuePair(k, v)))
    msg

/// Create a JSON POST request.
let private jsonPost (url: string) (json: string) : HttpRequestMessage =
    let msg = new HttpRequestMessage(HttpMethod.Post, url)
    msg.Content <- new StringContent(json, Encoding.UTF8, "application/json")
    msg

[<Tests>]
let metadataIntegrationTests =
    testList
        "OAuthServer.Integration.metadata"
        [ testTask "GET server metadata returns valid JSON" {
              do!
                  withTestServer (fun client _deps ->
                      task {
                          let! response = client.GetAsync("/.well-known/oauth-authorization-server")
                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          let! bodyStr = response.Content.ReadAsStringAsync()
                          let doc = parseJson bodyStr
                          let root = doc.RootElement

                          Expect.equal (root.GetProperty("issuer").GetString()) "https://auth.example.com" "issuer"
                          Expect.isTrue (root.TryGetProperty("authorization_endpoint") |> fst) "has authorization_endpoint"
                          Expect.isTrue (root.TryGetProperty("token_endpoint") |> fst) "has token_endpoint"
                          Expect.isTrue (root.TryGetProperty("jwks_uri") |> fst) "has jwks_uri"

                          // Check CORS header
                          let hasCorHeader = response.Headers.Contains("Access-Control-Allow-Origin")
                          Expect.isTrue hasCorHeader "Should have CORS header"
                      })
          }

          testTask "GET protected resource metadata returns valid JSON" {
              do!
                  withTestServer (fun client _deps ->
                      task {
                          let! response = client.GetAsync("/.well-known/oauth-protected-resource")
                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          let! bodyStr = response.Content.ReadAsStringAsync()
                          let doc = parseJson bodyStr
                          Expect.equal (doc.RootElement.GetProperty("resource").GetString()) "https://auth.example.com" "resource"
                      })
          }

          testTask "GET /oauth/jwks returns keys array" {
              do!
                  withTestServer (fun client _deps ->
                      task {
                          let! response = client.GetAsync("/oauth/jwks")
                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          let! bodyStr = response.Content.ReadAsStringAsync()
                          let doc = parseJson bodyStr
                          let keys = doc.RootElement.GetProperty("keys")
                          Expect.equal (keys.GetArrayLength()) 1 "Should have one key"
                      })
          } ]

[<Tests>]
let parIntegrationTests =
    testList
        "OAuthServer.Integration.par"
        [ testTask "POST /oauth/par rejects request without DPoP header" {
              do!
                  withTestServer (fun client _deps ->
                      task {
                          let fields =
                              [ "client_id", "http://localhost/client-metadata.json"
                                "redirect_uri", "http://127.0.0.1/callback"
                                "response_type", "code"
                                "scope", "atproto"
                                "code_challenge", "challenge123"
                                "code_challenge_method", "S256" ]

                          let! response = client.SendAsync(formPost "/oauth/par" fields)

                          Expect.equal response.StatusCode HttpStatusCode.BadRequest "Should return 400"

                          let! bodyStr = response.Content.ReadAsStringAsync()
                          let doc = parseJson bodyStr
                          Expect.equal (doc.RootElement.GetProperty("error").GetString()) "invalid_dpop_proof" "error"
                      })
          }

          testTask "POST /oauth/par with DPoP but missing client_id returns error" {
              do!
                  withTestServer (fun client _deps ->
                      task {
                          let dpopKey = DPoP.generateKeyPair()
                          // Use http://localhost since that's where TestServer runs
                          let dpopProof = DPoP.createProof dpopKey "POST" "http://localhost/oauth/par" None None

                          let fields =
                              [ "redirect_uri", "http://127.0.0.1/callback"
                                "response_type", "code"
                                "scope", "atproto"
                                "code_challenge", "challenge123"
                                "code_challenge_method", "S256" ]

                          let msg = formPost "/oauth/par" fields
                          msg.Headers.Add("DPoP", dpopProof)

                          let! response = client.SendAsync(msg)

                          Expect.equal response.StatusCode HttpStatusCode.BadRequest "Should return 400"

                          let! bodyStr = response.Content.ReadAsStringAsync()
                          let doc = parseJson bodyStr
                          Expect.equal (doc.RootElement.GetProperty("error").GetString()) "invalid_request" "error"
                          Expect.stringContains (doc.RootElement.GetProperty("error_description").GetString()) "client_id" "description"
                      })
          }

          testTask "POST /oauth/par returns DPoP-Nonce header" {
              do!
                  withTestServer (fun client _deps ->
                      task {
                          // Even on failure, the endpoint should set a DPoP-Nonce header
                          let fields = [ "client_id", "http://localhost/client-metadata.json" ]
                          let! response = client.SendAsync(formPost "/oauth/par" fields)

                          let hasNonce = response.Headers.Contains("DPoP-Nonce")
                          Expect.isTrue hasNonce "Should always set DPoP-Nonce header"
                      })
          } ]

[<Tests>]
let tokenIntegrationTests =
    testList
        "OAuthServer.Integration.token"
        [ testTask "POST /oauth/token rejects missing grant_type" {
              do!
                  withTestServer (fun client _deps ->
                      task {
                          let dpopKey = DPoP.generateKeyPair()
                          let dpopProof = DPoP.createProof dpopKey "POST" "http://localhost/oauth/token" None None
                          let msg = formPost "/oauth/token" [ "client_id", "test" ]
                          msg.Headers.Add("DPoP", dpopProof)

                          let! response = client.SendAsync(msg)

                          Expect.equal response.StatusCode HttpStatusCode.BadRequest "Should return 400"

                          let! bodyStr = response.Content.ReadAsStringAsync()
                          let doc = parseJson bodyStr
                          Expect.equal (doc.RootElement.GetProperty("error").GetString()) "invalid_request" "error"
                          Expect.stringContains (doc.RootElement.GetProperty("error_description").GetString()) "grant_type" "description"
                      })
          }

          testTask "POST /oauth/token rejects unsupported grant_type" {
              do!
                  withTestServer (fun client _deps ->
                      task {
                          let dpopKey = DPoP.generateKeyPair()
                          let dpopProof = DPoP.createProof dpopKey "POST" "http://localhost/oauth/token" None None
                          let msg = formPost "/oauth/token" [ "grant_type", "client_credentials" ]
                          msg.Headers.Add("DPoP", dpopProof)

                          let! response = client.SendAsync(msg)

                          Expect.equal response.StatusCode HttpStatusCode.BadRequest "Should return 400"

                          let! bodyStr = response.Content.ReadAsStringAsync()
                          let doc = parseJson bodyStr
                          Expect.equal (doc.RootElement.GetProperty("error").GetString()) "unsupported_grant_type" "error"
                      })
          }

          testTask "POST /oauth/token returns DPoP-Nonce header even on error" {
              do!
                  withTestServer (fun client _deps ->
                      task {
                          let msg = formPost "/oauth/token" [ "grant_type", "authorization_code" ]
                          let! response = client.SendAsync(msg)

                          let hasNonce = response.Headers.Contains("DPoP-Nonce")
                          Expect.isTrue hasNonce "Should always set DPoP-Nonce header"
                      })
          } ]

[<Tests>]
let revokeIntegrationTests =
    testList
        "OAuthServer.Integration.revoke"
        [ testTask "POST /oauth/revoke returns 200 even with missing token" {
              do!
                  withTestServer (fun client _deps ->
                      task {
                          let! response = client.SendAsync(formPost "/oauth/revoke" [])
                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200 per RFC 7009"
                      })
          }

          testTask "POST /oauth/revoke returns 200 for unknown token" {
              do!
                  withTestServer (fun client _deps ->
                      task {
                          let! response = client.SendAsync(formPost "/oauth/revoke" [ "token", "unknown-token-value" ])
                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200 per RFC 7009"
                      })
          } ]

[<Tests>]
let consentFlowIntegrationTests =
    testList
        "OAuthServer.Integration.consentFlow"
        [ testTask "sign-in succeeds via TestServer" {
              do!
                  withTestServer (fun client _deps ->
                      task {
                          let! response =
                              client.SendAsync(
                                  jsonPost "/api/sign-in" """{"identifier":"testuser","password":"password123"}"""
                              )

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          let! bodyStr = response.Content.ReadAsStringAsync()
                          let doc = parseJson bodyStr
                          Expect.equal (doc.RootElement.GetProperty("sub").GetString()) (Did.value testDid) "sub"
                      })
          }

          testTask "sign-in fails with wrong password" {
              do!
                  withTestServer (fun client _deps ->
                      task {
                          let! response =
                              client.SendAsync(
                                  jsonPost "/api/sign-in" """{"identifier":"testuser","password":"wrong"}"""
                              )

                          Expect.equal response.StatusCode HttpStatusCode.Unauthorized "Should return 401"
                      })
          }

          testTask "consent flow: create request, consent, get redirect URL" {
              do!
                  withTestServer (fun client deps ->
                      task {
                          // Pre-create a request directly in the store (bypassing PAR which needs DPoP)
                          let requestId = "flow-test-req"
                          let now = DateTimeOffset.UtcNow

                          let requestData =
                              { ClientId = "http://localhost/client-metadata.json"
                                RedirectUri = "http://127.0.0.1/callback"
                                Scope = "atproto"
                                State = Some "flowstate"
                                CodeChallenge = "challenge"
                                CodeChallengeMethod = "S256"
                                DpopJkt = "thumbprint"
                                Code = None
                                AuthorizedSub = None
                                ExpiresAt = now.AddMinutes(10.0)
                                CreatedAt = now }

                          do! deps.RequestStore.CreateRequest(requestId, requestData)

                          // Step 1: Sign in
                          let! signInResponse =
                              client.SendAsync(
                                  jsonPost "/api/sign-in" """{"identifier":"testuser","password":"password123"}"""
                              )

                          Expect.equal signInResponse.StatusCode HttpStatusCode.OK "Sign-in should succeed"

                          // Step 2: Consent
                          let consentJson =
                              sprintf """{"request_id":"%s","sub":"%s"}""" requestId (Did.value testDid)

                          let! consentResponse = client.SendAsync(jsonPost "/api/consent" consentJson)
                          Expect.equal consentResponse.StatusCode HttpStatusCode.OK "Consent should succeed"

                          let! consentBody = consentResponse.Content.ReadAsStringAsync()
                          let consentDoc = parseJson consentBody
                          let redirectUri = consentDoc.RootElement.GetProperty("redirect_uri").GetString()

                          Expect.stringContains redirectUri "http://127.0.0.1/callback" "Should redirect to callback"
                          Expect.stringContains redirectUri "code=" "Should have auth code"
                          Expect.stringContains redirectUri "state=flowstate" "Should include state"
                      })
          }

          testTask "reject flow: create request, reject, get error redirect" {
              do!
                  withTestServer (fun client deps ->
                      task {
                          let requestId = "reject-flow-req"
                          let now = DateTimeOffset.UtcNow

                          let requestData =
                              { ClientId = "http://localhost/client-metadata.json"
                                RedirectUri = "http://127.0.0.1/callback"
                                Scope = "atproto"
                                State = Some "rejectstate"
                                CodeChallenge = "challenge"
                                CodeChallengeMethod = "S256"
                                DpopJkt = "thumbprint"
                                Code = None
                                AuthorizedSub = None
                                ExpiresAt = now.AddMinutes(10.0)
                                CreatedAt = now }

                          do! deps.RequestStore.CreateRequest(requestId, requestData)

                          let rejectJson = sprintf """{"request_id":"%s"}""" requestId
                          let! rejectResponse = client.SendAsync(jsonPost "/api/reject" rejectJson)
                          Expect.equal rejectResponse.StatusCode HttpStatusCode.OK "Reject should succeed"

                          let! rejectBody = rejectResponse.Content.ReadAsStringAsync()
                          let rejectDoc = parseJson rejectBody
                          let redirectUri = rejectDoc.RootElement.GetProperty("redirect_uri").GetString()

                          Expect.stringContains redirectUri "error=access_denied" "Should have access_denied error"
                          Expect.stringContains redirectUri "state=rejectstate" "Should include state"
                      })
          } ]
