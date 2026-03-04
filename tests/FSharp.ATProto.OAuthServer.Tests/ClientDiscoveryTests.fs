module FSharp.ATProto.OAuthServer.Tests.ClientDiscoveryTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open Expecto
open FSharp.ATProto.OAuth
open FSharp.ATProto.OAuthServer

/// Build a minimal valid client metadata JSON string.
let private validClientMetadataJson clientId =
    sprintf
        """{
  "client_id": "%s",
  "client_uri": "https://app.example.com",
  "redirect_uris": ["https://app.example.com/callback"],
  "scope": "atproto transition:generic",
  "grant_types": ["authorization_code", "refresh_token"],
  "response_types": ["code"],
  "token_endpoint_auth_method": "none",
  "application_type": "web",
  "dpop_bound_access_tokens": true
}"""
        clientId

/// Build a valid ClientMetadata record for testing.
let private validMetadata : ClientMetadata =
    { ClientId = "https://app.example.com/client-metadata.json"
      ClientUri = Some "https://app.example.com"
      RedirectUris = [ "https://app.example.com/callback" ]
      Scope = "atproto transition:generic"
      GrantTypes = [ "authorization_code"; "refresh_token" ]
      ResponseTypes = [ "code" ]
      TokenEndpointAuthMethod = "none"
      ApplicationType = "web"
      DpopBoundAccessTokens = true }

/// A mock HTTP message handler that returns a predefined response.
type private MockHandler(responseBody: string, statusCode: HttpStatusCode) =
    inherit HttpMessageHandler()

    override _.SendAsync(_, _) =
        let response = new HttpResponseMessage(statusCode)
        response.Content <- new StringContent(responseBody, Encoding.UTF8, "application/json")
        Task.FromResult(response)

[<Tests>]
let pkceTests =
    testList
        "OAuthServer.ClientDiscovery.PKCE"
        [
          test "validatePkceS256 accepts valid verifier/challenge pair" {
              // Use the OAuth client's generatePkce to produce a known-valid pair
              let pkce = DPoP.generatePkce ()

              let result =
                  ClientDiscovery.validatePkceS256 pkce.Verifier pkce.Challenge

              Expect.isTrue result "Valid PKCE pair should validate"
          }

          test "validatePkceS256 rejects incorrect verifier" {
              let pkce = DPoP.generatePkce ()

              let result =
                  ClientDiscovery.validatePkceS256 "wrong-verifier-value" pkce.Challenge

              Expect.isFalse result "Wrong verifier should fail validation"
          }

          test "validatePkceS256 rejects incorrect challenge" {
              let pkce = DPoP.generatePkce ()

              let result =
                  ClientDiscovery.validatePkceS256 pkce.Verifier "wrong-challenge-value"

              Expect.isFalse result "Wrong challenge should fail validation"
          } ]

[<Tests>]
let loopbackTests =
    testList
        "OAuthServer.ClientDiscovery.Loopback"
        [
          test "isLoopbackClient recognizes http://localhost" {
              Expect.isTrue
                  (ClientDiscovery.isLoopbackClient "http://localhost")
                  "localhost should be loopback"
          }

          test "isLoopbackClient recognizes http://localhost with path" {
              Expect.isTrue
                  (ClientDiscovery.isLoopbackClient "http://localhost/client-metadata.json")
                  "localhost with path should be loopback"
          }

          test "isLoopbackClient recognizes http://127.0.0.1" {
              Expect.isTrue
                  (ClientDiscovery.isLoopbackClient "http://127.0.0.1")
                  "127.0.0.1 should be loopback"
          }

          test "isLoopbackClient rejects https://localhost" {
              Expect.isFalse
                  (ClientDiscovery.isLoopbackClient "https://localhost")
                  "HTTPS localhost is not a loopback client"
          }

          test "isLoopbackClient rejects non-loopback URLs" {
              Expect.isFalse
                  (ClientDiscovery.isLoopbackClient "https://app.example.com")
                  "External URL should not be loopback"
          } ]

[<Tests>]
let validateMetadataTests =
    testList
        "OAuthServer.ClientDiscovery.ValidateMetadata"
        [
          test "validateClientMetadata accepts valid metadata" {
              let result = ClientDiscovery.validateClientMetadata validMetadata
              Expect.isOk result "Valid metadata should pass validation"
          }

          test "validateClientMetadata rejects when DPoP binding is false" {
              let metadata =
                  { validMetadata with
                      DpopBoundAccessTokens = false }

              let result = ClientDiscovery.validateClientMetadata metadata
              Expect.isError result "Should reject non-DPoP clients"
          }

          test "validateClientMetadata rejects missing 'code' response type" {
              let metadata =
                  { validMetadata with
                      ResponseTypes = [ "token" ] }

              let result = ClientDiscovery.validateClientMetadata metadata
              Expect.isError result "Should reject missing 'code' response type"
          }

          test "validateClientMetadata rejects missing 'authorization_code' grant type" {
              let metadata =
                  { validMetadata with
                      GrantTypes = [ "refresh_token" ] }

              let result = ClientDiscovery.validateClientMetadata metadata
              Expect.isError result "Should reject missing 'authorization_code' grant type"
          }

          test "validateClientMetadata rejects missing 'atproto' scope" {
              let metadata =
                  { validMetadata with
                      Scope = "openid profile" }

              let result = ClientDiscovery.validateClientMetadata metadata
              Expect.isError result "Should reject missing 'atproto' scope"
          }

          test "validateClientMetadata rejects empty redirect URIs" {
              let metadata =
                  { validMetadata with
                      RedirectUris = [] }

              let result = ClientDiscovery.validateClientMetadata metadata
              Expect.isError result "Should reject empty redirect_uris"
          } ]

[<Tests>]
let fetchMetadataTests =
    testList
        "OAuthServer.ClientDiscovery.FetchMetadata"
        [
          testTask "fetchClientMetadata returns default metadata for loopback client" {
              let httpClient = new HttpClient()

              let! result =
                  ClientDiscovery.fetchClientMetadata httpClient "http://localhost/client-metadata.json"

              match result with
              | Ok (metadata: ClientMetadata) ->
                  Expect.equal metadata.ClientId "http://localhost/client-metadata.json" "ClientId should match"
                  Expect.isTrue metadata.DpopBoundAccessTokens "DPoP should be enabled"
                  Expect.contains metadata.GrantTypes "authorization_code" "Should have authorization_code"
                  Expect.contains metadata.ResponseTypes "code" "Should have code response type"
              | Error e -> failtest (sprintf "Expected Ok, got Error: %A" e)
          }

          testTask "fetchClientMetadata rejects non-HTTPS client_id" {
              let httpClient = new HttpClient()

              let! result =
                  ClientDiscovery.fetchClientMetadata httpClient "http://app.example.com/metadata.json"

              Expect.isError result "Should reject HTTP (non-loopback) client_id"
          }

          testTask "fetchClientMetadata fetches and parses valid HTTPS metadata" {
              let clientId = "https://app.example.com/client-metadata.json"
              let json = validClientMetadataJson clientId
              let handler = new MockHandler(json, HttpStatusCode.OK)
              let httpClient = new HttpClient(handler)

              let! result =
                  ClientDiscovery.fetchClientMetadata httpClient clientId

              match result with
              | Ok (metadata: ClientMetadata) ->
                  Expect.equal metadata.ClientId clientId "ClientId should match"
                  Expect.isTrue metadata.DpopBoundAccessTokens "DPoP should be enabled"
                  Expect.equal metadata.Scope "atproto transition:generic" "Scope should match"
              | Error e -> failtest (sprintf "Expected Ok, got Error: %A" e)
          } ]

[<Tests>]
let cacheTests =
    testList
        "OAuthServer.ClientDiscovery.Cache"
        [
          testTask "ClientCache caches metadata and returns cached value" {
              let clientId = "http://localhost/metadata.json"

              let cache = ClientDiscovery.ClientCache(TimeSpan.FromMinutes 5.0)
              let httpClient = new HttpClient()

              // First fetch
              let! result1 = cache.GetOrFetch httpClient clientId
              Expect.isOk result1 "First fetch should succeed"

              // Second fetch should use cache (loopback doesn't hit network anyway,
              // but we verify the cache stores and returns the same value)
              let! result2 = cache.GetOrFetch httpClient clientId
              Expect.isOk result2 "Second fetch should succeed"

              match result1, result2 with
              | Ok (m1: ClientMetadata), Ok (m2: ClientMetadata) ->
                  Expect.equal m1.ClientId m2.ClientId "Cached metadata should match"
              | _ -> failtest "Both fetches should succeed"
          } ]

[<Tests>]
let assertionTests =
    testList
        "OAuthServer.ClientDiscovery.ClientAssertion"
        [
          test "validateClientAssertion accepts valid jwt-bearer type" {
              let result =
                  ClientDiscovery.validateClientAssertion
                      "urn:ietf:params:oauth:client-assertion-type:jwt-bearer"
                      "eyJhbGciOiJFUzI1NiJ9.eyJzdWIiOiJ0ZXN0In0.sig"
                      "https://app.example.com/client-metadata.json"

              Expect.isOk result "Valid assertion type should be accepted"
          }

          test "validateClientAssertion rejects invalid assertion type" {
              let result =
                  ClientDiscovery.validateClientAssertion
                      "urn:ietf:params:oauth:client-assertion-type:saml2-bearer"
                      "some-assertion"
                      "https://app.example.com/client-metadata.json"

              Expect.isError result "Invalid assertion type should be rejected"
          }

          test "validateClientAssertion rejects empty assertion" {
              let result =
                  ClientDiscovery.validateClientAssertion
                      "urn:ietf:params:oauth:client-assertion-type:jwt-bearer"
                      ""
                      "https://app.example.com/client-metadata.json"

              Expect.isError result "Empty assertion should be rejected"
          } ]
