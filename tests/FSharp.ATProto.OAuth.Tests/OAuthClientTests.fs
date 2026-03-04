module FSharp.ATProto.OAuth.Tests.OAuthClientTests

open System
open Expecto
open FSharp.ATProto.OAuth

let private sampleServerMetadata =
    { Issuer = "https://bsky.social"
      AuthorizationEndpoint = "https://bsky.social/oauth/authorize"
      TokenEndpoint = "https://bsky.social/oauth/token"
      PushedAuthorizationRequestEndpoint = Some "https://bsky.social/oauth/par"
      ScopesSupported = [ "atproto"; "transition:generic" ]
      ResponseTypesSupported = [ "code" ]
      GrantTypesSupported = [ "authorization_code"; "refresh_token" ]
      TokenEndpointAuthMethodsSupported = [ "none" ]
      DpopSigningAlgValuesSupported = [ "ES256" ]
      RequirePushedAuthorizationRequests = true }

let private sampleClientMetadata =
    { ClientId = "https://myapp.example.com/client-metadata.json"
      ClientUri = Some "https://myapp.example.com"
      RedirectUris = [ "https://myapp.example.com/callback" ]
      Scope = "atproto transition:generic"
      GrantTypes = [ "authorization_code"; "refresh_token" ]
      ResponseTypes = [ "code" ]
      TokenEndpointAuthMethod = "none"
      ApplicationType = "web"
      DpopBoundAccessTokens = true }

[<Tests>]
let buildAuthorizationUrlTests =
    testList
        "OAuthClient.buildAuthorizationUrl"
        [ test "builds correct authorization URL with all parameters" {
              let url =
                  OAuthClient.buildAuthorizationUrl
                      sampleServerMetadata
                      "https://myapp.example.com/client-metadata.json"
                      "https://myapp.example.com/callback"
                      "atproto"
                      "random-state-123"
                      "challenge-abc"

              Expect.stringStarts url "https://bsky.social/oauth/authorize?" "Should start with auth endpoint"
              Expect.stringContains url "response_type=code" "Should contain response_type"
              Expect.stringContains url "code_challenge_method=S256" "Should contain challenge method"
              Expect.stringContains url "state=random-state-123" "Should contain state"
              Expect.stringContains url "code_challenge=challenge-abc" "Should contain code_challenge"
          }

          test "URL-encodes parameter values" {
              let url =
                  OAuthClient.buildAuthorizationUrl
                      sampleServerMetadata
                      "https://myapp.example.com/client-metadata.json"
                      "https://myapp.example.com/callback"
                      "atproto transition:generic"
                      "state"
                      "challenge"

              // Space should be encoded as %20
              Expect.stringContains url "atproto%20transition%3Ageneric" "Should URL-encode scope"
          } ]

[<Tests>]
let buildParAuthorizationUrlTests =
    testList
        "OAuthClient.buildParAuthorizationUrl"
        [ test "builds correct PAR authorization URL" {
              let url =
                  OAuthClient.buildParAuthorizationUrl
                      sampleServerMetadata
                      "https://myapp.example.com/client-metadata.json"
                      "urn:ietf:params:oauth:request_uri:abc123"

              Expect.stringStarts url "https://bsky.social/oauth/authorize?" "Should start with auth endpoint"
              Expect.stringContains url "request_uri=" "Should contain request_uri"
          } ]

[<Tests>]
let parseParResponseTests =
    testList
        "OAuthClient.parseParResponse"
        [ test "parses valid PAR response" {
              let json =
                  """{
                      "request_uri": "urn:ietf:params:oauth:request_uri:abc123",
                      "expires_in": 60
                  }"""

              match OAuthClient.parseParResponse json with
              | Ok requestUri ->
                  Expect.equal requestUri "urn:ietf:params:oauth:request_uri:abc123" "request_uri"
              | Error e -> failtest (sprintf "Expected Ok, got Error: %A" e)
          }

          test "returns error for OAuth error response" {
              let json =
                  """{
                      "error": "invalid_request",
                      "error_description": "Invalid redirect_uri"
                  }"""

              match OAuthClient.parseParResponse json with
              | Error (OAuthError.TokenRequestFailed (code, desc)) ->
                  Expect.equal code "invalid_request" "error code"
                  Expect.equal desc (Some "Invalid redirect_uri") "error description"
              | other -> failtest (sprintf "Expected TokenRequestFailed, got %A" other)
          }

          test "returns error when request_uri is missing" {
              let json = """{"expires_in": 60}"""

              match OAuthClient.parseParResponse json with
              | Error (OAuthError.InvalidState _) -> ()
              | other -> failtest (sprintf "Expected InvalidState, got %A" other)
          }

          test "returns error for invalid JSON" {
              match OAuthClient.parseParResponse "not json" with
              | Error (OAuthError.InvalidState _) -> ()
              | other -> failtest (sprintf "Expected InvalidState, got %A" other)
          } ]

[<Tests>]
let createAuthenticatedRequestTests =
    testList
        "OAuthClient.createAuthenticatedRequest"
        [ test "adds Authorization header with DPoP scheme" {
              use key = DPoP.generateKeyPair ()

              let did =
                  match FSharp.ATProto.Syntax.Did.parse "did:plc:testuser123" with
                  | Ok d -> d
                  | Error e -> failtest e

              let session =
                  { AccessToken = "test-access-token"
                    RefreshToken = Some "test-refresh-token"
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1.0)
                    Did = did
                    DpopKeyPair = key
                    TokenEndpoint = "https://bsky.social/oauth/token" }

              use request =
                  OAuthClient.createAuthenticatedRequest
                      session
                      System.Net.Http.HttpMethod.Get
                      "https://bsky.social/xrpc/app.bsky.feed.getTimeline"
                      None

              let authHeader =
                  request.Headers.GetValues("Authorization") |> Seq.head

              Expect.stringStarts authHeader "DPoP " "Should use DPoP scheme"
              Expect.stringContains authHeader "test-access-token" "Should contain access token"
          }

          test "adds DPoP proof header" {
              use key = DPoP.generateKeyPair ()

              let did =
                  match FSharp.ATProto.Syntax.Did.parse "did:plc:testuser123" with
                  | Ok d -> d
                  | Error e -> failtest e

              let session =
                  { AccessToken = "test-access-token"
                    RefreshToken = None
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1.0)
                    Did = did
                    DpopKeyPair = key
                    TokenEndpoint = "https://bsky.social/oauth/token" }

              use request =
                  OAuthClient.createAuthenticatedRequest
                      session
                      System.Net.Http.HttpMethod.Post
                      "https://bsky.social/xrpc/com.atproto.repo.createRecord"
                      None

              let dpopHeader =
                  request.Headers.GetValues("DPoP") |> Seq.head

              // DPoP proof should be a 3-part JWT
              let parts = dpopHeader.Split('.')
              Expect.equal parts.Length 3 "DPoP proof should be a JWT"
          }

          test "sets correct request method and URL" {
              use key = DPoP.generateKeyPair ()

              let did =
                  match FSharp.ATProto.Syntax.Did.parse "did:plc:testuser123" with
                  | Ok d -> d
                  | Error e -> failtest e

              let session =
                  { AccessToken = "tok"
                    RefreshToken = None
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1.0)
                    Did = did
                    DpopKeyPair = key
                    TokenEndpoint = "https://bsky.social/oauth/token" }

              let url = "https://bsky.social/xrpc/app.bsky.feed.getTimeline"

              use request =
                  OAuthClient.createAuthenticatedRequest session System.Net.Http.HttpMethod.Get url None

              Expect.equal request.Method System.Net.Http.HttpMethod.Get "Method should be GET"
              Expect.equal (request.RequestUri.ToString()) url "URL should match"
          } ]
