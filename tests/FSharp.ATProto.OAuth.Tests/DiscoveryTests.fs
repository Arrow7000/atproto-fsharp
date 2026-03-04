module FSharp.ATProto.OAuth.Tests.DiscoveryTests

open Expecto
open FSharp.ATProto.OAuth

[<Tests>]
let protectedResourceUrlTests =
    testList
        "Discovery.protectedResourceUrl"
        [ test "constructs well-known URL from PDS URL" {
              let url = Discovery.protectedResourceUrl "https://bsky.social"
              Expect.equal url "https://bsky.social/.well-known/oauth-protected-resource" "URL"
          }

          test "constructs well-known URL from PDS URL with path" {
              let url = Discovery.protectedResourceUrl "https://bsky.social/xrpc/foo"
              Expect.equal url "https://bsky.social/.well-known/oauth-protected-resource" "URL should strip path"
          }

          test "constructs well-known URL from PDS URL with port" {
              let url = Discovery.protectedResourceUrl "https://pds.example.com:8443"
              Expect.equal url "https://pds.example.com:8443/.well-known/oauth-protected-resource" "URL should include port"
          } ]

[<Tests>]
let authorizationServerUrlTests =
    testList
        "Discovery.authorizationServerUrl"
        [ test "constructs well-known URL from issuer" {
              let url = Discovery.authorizationServerUrl "https://bsky.social"
              Expect.equal url "https://bsky.social/.well-known/oauth-authorization-server" "URL"
          } ]

[<Tests>]
let parseProtectedResourceTests =
    testList
        "Discovery.parseProtectedResourceMetadata"
        [ test "parses valid protected resource metadata" {
              let json =
                  """{
                      "resource": "https://bsky.social",
                      "authorization_servers": ["https://bsky.social"],
                      "scopes_supported": ["atproto", "transition:generic"]
                  }"""

              match Discovery.parseProtectedResourceMetadata json with
              | Ok prm ->
                  Expect.equal prm.Resource "https://bsky.social" "resource"
                  Expect.equal prm.AuthorizationServers [ "https://bsky.social" ] "authorization_servers"
                  Expect.equal prm.ScopesSupported [ "atproto"; "transition:generic" ] "scopes_supported"
              | Error e -> failtest (sprintf "Expected Ok, got Error: %A" e)
          }

          test "parses metadata with missing optional fields" {
              let json = """{"resource": "https://pds.example.com"}"""

              match Discovery.parseProtectedResourceMetadata json with
              | Ok prm ->
                  Expect.equal prm.Resource "https://pds.example.com" "resource"
                  Expect.isEmpty prm.AuthorizationServers "authorization_servers should be empty"
                  Expect.isEmpty prm.ScopesSupported "scopes_supported should be empty"
              | Error e -> failtest (sprintf "Expected Ok, got Error: %A" e)
          }

          test "fails on missing resource field" {
              let json = """{"authorization_servers": ["https://bsky.social"]}"""

              match Discovery.parseProtectedResourceMetadata json with
              | Error (OAuthError.DiscoveryFailed msg) ->
                  Expect.stringContains msg "resource" "Error should mention missing field"
              | other -> failtest (sprintf "Expected DiscoveryFailed, got %A" other)
          }

          test "fails on invalid JSON" {
              match Discovery.parseProtectedResourceMetadata "not json" with
              | Error (OAuthError.DiscoveryFailed _) -> ()
              | other -> failtest (sprintf "Expected DiscoveryFailed, got %A" other)
          } ]

[<Tests>]
let parseAuthorizationServerTests =
    testList
        "Discovery.parseAuthorizationServerMetadata"
        [ test "parses valid authorization server metadata" {
              let json =
                  """{
                      "issuer": "https://bsky.social",
                      "authorization_endpoint": "https://bsky.social/oauth/authorize",
                      "token_endpoint": "https://bsky.social/oauth/token",
                      "pushed_authorization_request_endpoint": "https://bsky.social/oauth/par",
                      "scopes_supported": ["atproto", "transition:generic"],
                      "response_types_supported": ["code"],
                      "grant_types_supported": ["authorization_code", "refresh_token"],
                      "token_endpoint_auth_methods_supported": ["none"],
                      "dpop_signing_alg_values_supported": ["ES256"],
                      "require_pushed_authorization_requests": true
                  }"""

              match Discovery.parseAuthorizationServerMetadata json with
              | Ok asm ->
                  Expect.equal asm.Issuer "https://bsky.social" "issuer"
                  Expect.equal asm.AuthorizationEndpoint "https://bsky.social/oauth/authorize" "authorization_endpoint"
                  Expect.equal asm.TokenEndpoint "https://bsky.social/oauth/token" "token_endpoint"

                  Expect.equal
                      asm.PushedAuthorizationRequestEndpoint
                      (Some "https://bsky.social/oauth/par")
                      "pushed_authorization_request_endpoint"

                  Expect.equal asm.ScopesSupported [ "atproto"; "transition:generic" ] "scopes_supported"
                  Expect.equal asm.ResponseTypesSupported [ "code" ] "response_types_supported"

                  Expect.equal
                      asm.GrantTypesSupported
                      [ "authorization_code"; "refresh_token" ]
                      "grant_types_supported"

                  Expect.equal asm.TokenEndpointAuthMethodsSupported [ "none" ] "token_endpoint_auth_methods_supported"
                  Expect.equal asm.DpopSigningAlgValuesSupported [ "ES256" ] "dpop_signing_alg_values_supported"
                  Expect.isTrue asm.RequirePushedAuthorizationRequests "require_pushed_authorization_requests"
              | Error e -> failtest (sprintf "Expected Ok, got Error: %A" e)
          }

          test "parses metadata with only required fields" {
              let json =
                  """{
                      "issuer": "https://auth.example.com",
                      "authorization_endpoint": "https://auth.example.com/authorize",
                      "token_endpoint": "https://auth.example.com/token"
                  }"""

              match Discovery.parseAuthorizationServerMetadata json with
              | Ok asm ->
                  Expect.equal asm.Issuer "https://auth.example.com" "issuer"
                  Expect.isNone asm.PushedAuthorizationRequestEndpoint "PAR should be None"
                  Expect.isEmpty asm.ScopesSupported "scopes_supported should be empty"
                  Expect.isFalse asm.RequirePushedAuthorizationRequests "require_par should default to false"
              | Error e -> failtest (sprintf "Expected Ok, got Error: %A" e)
          }

          test "fails on missing issuer" {
              let json =
                  """{
                      "authorization_endpoint": "https://example.com/authorize",
                      "token_endpoint": "https://example.com/token"
                  }"""

              match Discovery.parseAuthorizationServerMetadata json with
              | Error (OAuthError.DiscoveryFailed msg) ->
                  Expect.stringContains msg "issuer" "Error should mention missing field"
              | other -> failtest (sprintf "Expected DiscoveryFailed, got %A" other)
          }

          test "fails on missing authorization_endpoint" {
              let json =
                  """{
                      "issuer": "https://example.com",
                      "token_endpoint": "https://example.com/token"
                  }"""

              match Discovery.parseAuthorizationServerMetadata json with
              | Error (OAuthError.DiscoveryFailed msg) ->
                  Expect.stringContains msg "authorization_endpoint" "Error should mention missing field"
              | other -> failtest (sprintf "Expected DiscoveryFailed, got %A" other)
          }

          test "fails on missing token_endpoint" {
              let json =
                  """{
                      "issuer": "https://example.com",
                      "authorization_endpoint": "https://example.com/authorize"
                  }"""

              match Discovery.parseAuthorizationServerMetadata json with
              | Error (OAuthError.DiscoveryFailed msg) ->
                  Expect.stringContains msg "token_endpoint" "Error should mention missing field"
              | other -> failtest (sprintf "Expected DiscoveryFailed, got %A" other)
          } ]

[<Tests>]
let parseTokenResponseTests =
    testList
        "Discovery.parseTokenResponse"
        [ test "parses valid token response" {
              let json =
                  """{
                      "access_token": "eyJ0eXAiOiJhdCtqd3QiLCJhbGciOiJFUzI1NiJ9.test",
                      "token_type": "DPoP",
                      "expires_in": 3600,
                      "refresh_token": "rt-abc123",
                      "scope": "atproto transition:generic",
                      "sub": "did:plc:z72i7hdynmk6r22z27h6tvur"
                  }"""

              match Discovery.parseTokenResponse json with
              | Ok tr ->
                  Expect.equal tr.AccessToken "eyJ0eXAiOiJhdCtqd3QiLCJhbGciOiJFUzI1NiJ9.test" "access_token"
                  Expect.equal tr.TokenType "DPoP" "token_type"
                  Expect.equal tr.ExpiresIn 3600 "expires_in"
                  Expect.equal tr.RefreshToken (Some "rt-abc123") "refresh_token"
                  Expect.equal tr.Scope (Some "atproto transition:generic") "scope"
                  Expect.equal tr.Sub "did:plc:z72i7hdynmk6r22z27h6tvur" "sub"
              | Error e -> failtest (sprintf "Expected Ok, got Error: %A" e)
          }

          test "parses token response without optional fields" {
              let json =
                  """{
                      "access_token": "tok",
                      "token_type": "DPoP",
                      "expires_in": 1800,
                      "sub": "did:plc:abc123"
                  }"""

              match Discovery.parseTokenResponse json with
              | Ok tr ->
                  Expect.isNone tr.RefreshToken "refresh_token should be None"
                  Expect.isNone tr.Scope "scope should be None"
              | Error e -> failtest (sprintf "Expected Ok, got Error: %A" e)
          }

          test "returns error for OAuth error response" {
              let json =
                  """{
                      "error": "invalid_grant",
                      "error_description": "The authorization code has expired"
                  }"""

              match Discovery.parseTokenResponse json with
              | Error (OAuthError.TokenRequestFailed (code, desc)) ->
                  Expect.equal code "invalid_grant" "error code"
                  Expect.equal desc (Some "The authorization code has expired") "error description"
              | other -> failtest (sprintf "Expected TokenRequestFailed, got %A" other)
          }

          test "returns error for OAuth error response without description" {
              let json = """{"error": "server_error"}"""

              match Discovery.parseTokenResponse json with
              | Error (OAuthError.TokenRequestFailed (code, desc)) ->
                  Expect.equal code "server_error" "error code"
                  Expect.isNone desc "description should be None"
              | other -> failtest (sprintf "Expected TokenRequestFailed, got %A" other)
          }

          test "fails on invalid JSON" {
              match Discovery.parseTokenResponse "{{broken" with
              | Error (OAuthError.TokenRequestFailed _) -> ()
              | other -> failtest (sprintf "Expected TokenRequestFailed, got %A" other)
          } ]
