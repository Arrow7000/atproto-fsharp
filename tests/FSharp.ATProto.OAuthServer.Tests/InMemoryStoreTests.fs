module FSharp.ATProto.OAuthServer.Tests.InMemoryStoreTests

open System
open Expecto
open FSharp.ATProto.Syntax
open FSharp.ATProto.OAuthServer

let private unwrap =
    function
    | Ok v -> v
    | Error e -> failtest (sprintf "Expected Ok, got Error: %A" e)

let private testDid = Did.parse "did:plc:testuser123" |> unwrap
let private testDid2 = Did.parse "did:plc:otheruser456" |> unwrap
let private testHandle = Handle.parse "test.bsky.social" |> unwrap

let private now = DateTimeOffset.UtcNow

let private makeTokenData sub =
    { Sub = sub
      ClientId = "https://app.example.com/client-metadata.json"
      Scope = "atproto transition:generic"
      DpopJkt = "thumbprint123"
      AccessToken = "access-token-" + Guid.NewGuid().ToString("N").[..7]
      RefreshToken = "refresh-token-" + Guid.NewGuid().ToString("N").[..7]
      ExpiresAt = now.AddMinutes 5.0
      CreatedAt = now }

let private makeRequestData () =
    { ClientId = "https://app.example.com/client-metadata.json"
      RedirectUri = "https://app.example.com/callback"
      Scope = "atproto transition:generic"
      State = Some "random-state-value"
      CodeChallenge = "challenge123"
      CodeChallengeMethod = "S256"
      DpopJkt = "thumbprint123"
      Code = None
      AuthorizedSub = None
      ExpiresAt = now.AddMinutes 10.0
      CreatedAt = now }

[<Tests>]
let tokenStoreTests =
    testList "OAuthServer.InMemoryTokenStore" [
        testTask "CreateToken and ReadToken round-trip" {
            let store = InMemoryTokenStore() :> ITokenStore
            let data = makeTokenData testDid
            do! store.CreateToken("tok1", data)
            let! result = store.ReadToken("tok1")
            Expect.isSome result "Should find created token"
            Expect.equal result.Value.Sub testDid "Sub should match"
            Expect.equal result.Value.Scope "atproto transition:generic" "Scope should match"
        }

        testTask "ReadToken returns None for missing token" {
            let store = InMemoryTokenStore() :> ITokenStore
            let! result = store.ReadToken("nonexistent")
            Expect.isNone result "Should return None for missing token"
        }

        testTask "DeleteToken removes token" {
            let store = InMemoryTokenStore() :> ITokenStore
            let data = makeTokenData testDid
            do! store.CreateToken("tok1", data)
            do! store.DeleteToken("tok1")
            let! result = store.ReadToken("tok1")
            Expect.isNone result "Token should be deleted"
        }

        testTask "FindByRefreshToken finds correct token" {
            let store = InMemoryTokenStore() :> ITokenStore
            let data1 = makeTokenData testDid
            let data2 = makeTokenData testDid2
            do! store.CreateToken("tok1", data1)
            do! store.CreateToken("tok2", data2)
            let! result = store.FindByRefreshToken(data2.RefreshToken)
            Expect.isSome result "Should find token by refresh token"
            let (id, found) = result.Value
            Expect.equal id "tok2" "Token ID should match"
            Expect.equal found.Sub testDid2 "Sub should match"
        }

        testTask "FindByRefreshToken returns None for unknown refresh token" {
            let store = InMemoryTokenStore() :> ITokenStore
            let data = makeTokenData testDid
            do! store.CreateToken("tok1", data)
            let! result = store.FindByRefreshToken("nonexistent-refresh")
            Expect.isNone result "Should return None for unknown refresh token"
        }

        testTask "RotateToken replaces old token with new" {
            let store = InMemoryTokenStore() :> ITokenStore
            let data = makeTokenData testDid
            do! store.CreateToken("tok1", data)

            let newData =
                { data with
                    AccessToken = "new-access"
                    ExpiresAt = now.AddMinutes 10.0 }

            do! store.RotateToken("tok1", "tok2", "new-refresh", newData)

            let! oldResult = store.ReadToken("tok1")
            Expect.isNone oldResult "Old token should be removed"

            let! newResult = store.ReadToken("tok2")
            Expect.isSome newResult "New token should exist"
            Expect.equal newResult.Value.RefreshToken "new-refresh" "Refresh token should be updated"
            Expect.equal newResult.Value.AccessToken "new-access" "Access token should be updated"
        }

        testTask "CreateToken overwrites existing token with same ID" {
            let store = InMemoryTokenStore() :> ITokenStore
            let data1 = makeTokenData testDid
            let data2 = makeTokenData testDid2
            do! store.CreateToken("tok1", data1)
            do! store.CreateToken("tok1", data2)
            let! result = store.ReadToken("tok1")
            Expect.isSome result "Token should exist"
            Expect.equal result.Value.Sub testDid2 "Should have overwritten data"
        }
    ]

[<Tests>]
let requestStoreTests =
    testList "OAuthServer.InMemoryRequestStore" [
        testTask "CreateRequest and ReadRequest round-trip" {
            let store = InMemoryRequestStore() :> IRequestStore
            let data = makeRequestData ()
            do! store.CreateRequest("req1", data)
            let! result : RequestData option = store.ReadRequest("req1")
            Expect.isSome result "Should find created request"
            Expect.equal result.Value.ClientId data.ClientId "ClientId should match"
            Expect.equal result.Value.RedirectUri data.RedirectUri "RedirectUri should match"
        }

        testTask "ReadRequest returns None for missing request" {
            let store = InMemoryRequestStore() :> IRequestStore
            let! result = store.ReadRequest("nonexistent")
            Expect.isNone result "Should return None for missing request"
        }

        testTask "DeleteRequest removes request" {
            let store = InMemoryRequestStore() :> IRequestStore
            let data = makeRequestData ()
            do! store.CreateRequest("req1", data)
            do! store.DeleteRequest("req1")
            let! result = store.ReadRequest("req1")
            Expect.isNone result "Request should be deleted"
        }

        testTask "ConsumeCode atomically returns and removes request" {
            let store = InMemoryRequestStore() :> IRequestStore

            let data =
                { makeRequestData () with
                    Code = Some "auth-code-123"
                    AuthorizedSub = Some testDid }

            do! store.CreateRequest("req1", data)

            // First consumption should succeed
            let! result1 = store.ConsumeCode("auth-code-123")
            Expect.isSome result1 "First consumption should succeed"
            Expect.equal result1.Value.Code (Some "auth-code-123") "Code should match"

            // Second consumption should fail (atomicity)
            let! result2 = store.ConsumeCode("auth-code-123")
            Expect.isNone result2 "Second consumption should fail (code already consumed)"
        }

        testTask "ConsumeCode returns None for unknown code" {
            let store = InMemoryRequestStore() :> IRequestStore
            let! result = store.ConsumeCode("nonexistent-code")
            Expect.isNone result "Should return None for unknown code"
        }

        testTask "ConsumeCode does not consume request without code" {
            let store = InMemoryRequestStore() :> IRequestStore
            let data = makeRequestData () // Code = None
            do! store.CreateRequest("req1", data)
            let! result = store.ConsumeCode("any-code")
            Expect.isNone result "Should not match request without code"
        }
    ]

[<Tests>]
let replayStoreTests =
    testList "OAuthServer.InMemoryReplayStore" [
        testTask "IsUnique returns true for first occurrence" {
            let store = InMemoryReplayStore() :> IReplayStore
            let! result = store.IsUnique("dpop", "jti-1", now.AddMinutes 5.0)
            Expect.isTrue result "First occurrence should be unique"
        }

        testTask "IsUnique returns false for duplicate" {
            let store = InMemoryReplayStore() :> IReplayStore
            let! _ = store.IsUnique("dpop", "jti-1", now.AddMinutes 5.0)
            let! result = store.IsUnique("dpop", "jti-1", now.AddMinutes 5.0)
            Expect.isFalse result "Duplicate should not be unique"
        }

        testTask "IsUnique separates by namespace" {
            let store = InMemoryReplayStore() :> IReplayStore
            let! result1 = store.IsUnique("dpop", "key-1", now.AddMinutes 5.0)
            let! result2 = store.IsUnique("nonce", "key-1", now.AddMinutes 5.0)
            Expect.isTrue result1 "First namespace should be unique"
            Expect.isTrue result2 "Different namespace same key should be unique"
        }

        testTask "IsUnique allows same key after different namespace" {
            let store = InMemoryReplayStore() :> IReplayStore
            let! _ = store.IsUnique("ns1", "key", now.AddMinutes 5.0)
            let! result = store.IsUnique("ns2", "key", now.AddMinutes 5.0)
            Expect.isTrue result "Same key in different namespace should be unique"
        }
    ]

[<Tests>]
let accountStoreTests =
    testList "OAuthServer.InMemoryAccountStore" [
        testTask "Authenticate succeeds with correct credentials" {
            let store = InMemoryAccountStore()

            let info =
                { Sub = testDid
                  Handle = Some testHandle
                  DisplayName = Some "Test User" }

            store.AddAccount("testuser", "password123", info)
            let istore = store :> IAccountStore

            let! result =
                istore.Authenticate(
                    { Identifier = "testuser"
                      Password = "password123" }
                )

            match result with
            | Ok (account : AccountInfo) ->
                Expect.equal account.Sub testDid "Sub should match"
                Expect.equal account.Handle (Some testHandle) "Handle should match"
                Expect.equal account.DisplayName (Some "Test User") "DisplayName should match"
            | Error e -> failtest (sprintf "Expected Ok, got Error: %s" e)
        }

        testTask "Authenticate fails with wrong password" {
            let store = InMemoryAccountStore()

            let info =
                { Sub = testDid
                  Handle = Some testHandle
                  DisplayName = None }

            store.AddAccount("testuser", "correct", info)
            let istore = store :> IAccountStore

            let! result =
                istore.Authenticate(
                    { Identifier = "testuser"
                      Password = "wrong" }
                )

            Expect.isError result "Should fail with wrong password"
        }

        testTask "Authenticate fails with unknown account" {
            let store = InMemoryAccountStore() :> IAccountStore

            let! result =
                store.Authenticate(
                    { Identifier = "nobody"
                      Password = "anything" }
                )

            Expect.isError result "Should fail with unknown account"
        }

        testTask "GetAccount finds account by DID" {
            let store = InMemoryAccountStore()

            let info =
                { Sub = testDid
                  Handle = Some testHandle
                  DisplayName = Some "Test" }

            store.AddAccount("testuser", "pass", info)
            let istore = store :> IAccountStore

            let! result : AccountInfo option = istore.GetAccount(testDid)
            Expect.isSome result "Should find account by DID"
            Expect.equal result.Value.Sub testDid "Sub should match"
        }

        testTask "GetAccount returns None for unknown DID" {
            let store = InMemoryAccountStore() :> IAccountStore
            let! result = store.GetAccount(testDid2)
            Expect.isNone result "Should return None for unknown DID"
        }
    ]

[<Tests>]
let scopeTests =
    testList "OAuthServer.OAuthScope" [
        test "parse splits space-separated scopes" {
            let scopes = OAuthScope.parse "atproto transition:generic"
            Expect.equal scopes [ "atproto"; "transition:generic" ] "Should parse two scopes"
        }

        test "parse handles single scope" {
            let scopes = OAuthScope.parse "atproto"
            Expect.equal scopes [ "atproto" ] "Should parse single scope"
        }

        test "parse handles empty string" {
            let scopes = OAuthScope.parse ""
            Expect.isEmpty scopes "Should return empty list"
        }

        test "format joins scopes with spaces" {
            let result = OAuthScope.format [ "atproto"; "transition:generic" ]
            Expect.equal result "atproto transition:generic" "Should join with space"
        }

        test "isValid accepts all supported scopes" {
            let supported = [ "atproto"; "transition:generic" ]
            let result = OAuthScope.isValid supported [ "atproto"; "transition:generic" ]
            Expect.isTrue result "All requested scopes are supported"
        }

        test "isValid rejects unsupported scope" {
            let supported = [ "atproto" ]
            let result = OAuthScope.isValid supported [ "atproto"; "unsupported" ]
            Expect.isFalse result "Should reject unsupported scope"
        }

        test "hasAtproto detects atproto scope" {
            Expect.isTrue (OAuthScope.hasAtproto [ "atproto"; "other" ]) "Should detect atproto"
            Expect.isFalse (OAuthScope.hasAtproto [ "other" ]) "Should not detect when absent"
        }
    ]

[<Tests>]
let errorTests =
    testList "OAuthServer.OAuthServerError" [
        test "ErrorCode returns correct OAuth error codes" {
            Expect.equal (OAuthServerError.InvalidRequest "x").ErrorCode "invalid_request" "InvalidRequest code"
            Expect.equal (OAuthServerError.InvalidClient "x").ErrorCode "invalid_client" "InvalidClient code"
            Expect.equal (OAuthServerError.InvalidGrant "x").ErrorCode "invalid_grant" "InvalidGrant code"
            Expect.equal (OAuthServerError.UnauthorizedClient "x").ErrorCode "unauthorized_client" "UnauthorizedClient code"
            Expect.equal (OAuthServerError.UnsupportedGrantType "x").ErrorCode "unsupported_grant_type" "UnsupportedGrantType code"
            Expect.equal (OAuthServerError.InvalidScope "x").ErrorCode "invalid_scope" "InvalidScope code"
            Expect.equal (OAuthServerError.AccessDenied "x").ErrorCode "access_denied" "AccessDenied code"
            Expect.equal (OAuthServerError.ServerError "x").ErrorCode "server_error" "ServerError code"
            Expect.equal (OAuthServerError.InvalidDpopProof "x").ErrorCode "invalid_dpop_proof" "InvalidDpopProof code"
            Expect.equal (OAuthServerError.UseDpopNonce "x").ErrorCode "use_dpop_nonce" "UseDpopNonce code"
        }
    ]
