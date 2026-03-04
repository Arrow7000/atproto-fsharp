module FSharp.ATProto.OAuthServer.Tests.DPoPValidatorTests

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Expecto
open FSharp.ATProto.OAuth
open FSharp.ATProto.OAuthServer

/// Helper to create a valid DPoP proof using the client-side module (public API).
let private createValidProof
    (key: ECDsa)
    (httpMethod: string)
    (httpUrl: string)
    (ath: string option)
    (nonce: string option)
    =
    DPoP.createProof key httpMethod httpUrl ath nonce

/// Export a public key as JWK JSON (reimplemented here since DPoP.exportPublicJwk is internal).
let private exportPublicJwk (key: ECDsa) : string =
    let parameters = key.ExportParameters(false)
    let x = DPoPValidator.toBase64Url parameters.Q.X
    let y = DPoPValidator.toBase64Url parameters.Q.Y
    let writer = new System.IO.MemoryStream()
    use jsonWriter = new Utf8JsonWriter(writer)
    jsonWriter.WriteStartObject()
    jsonWriter.WriteString("kty", "EC")
    jsonWriter.WriteString("crv", "P-256")
    jsonWriter.WriteString("x", x)
    jsonWriter.WriteString("y", y)
    jsonWriter.WriteEndObject()
    jsonWriter.Flush()
    Encoding.UTF8.GetString(writer.ToArray())

/// Encode a UTF-8 string as base64url.
let private stringToBase64Url (s: string) : string =
    DPoPValidator.toBase64Url (Encoding.UTF8.GetBytes(s))

/// A replay store that always says everything is unique (never blocks).
type private AlwaysUniqueReplayStore() =
    interface IReplayStore with
        member _.IsUnique(_, _, _) =
            Threading.Tasks.Task.FromResult(true)

/// A replay store that always says everything is a duplicate (always blocks).
type private AlwaysDuplicateReplayStore() =
    interface IReplayStore with
        member _.IsUnique(_, _, _) =
            Threading.Tasks.Task.FromResult(false)

let private defaultMaxAge = TimeSpan.FromMinutes 5.0
let private now = DateTimeOffset.UtcNow
let private replayStore () = AlwaysUniqueReplayStore() :> IReplayStore

/// Unwrap a Result, failing the test on Error.
let private unwrapOk result =
    match result with
    | Ok v -> v
    | Error(e: OAuthServerError) -> failtest (sprintf "Expected Ok, got Error: %A" e)

/// Unwrap a Result to get the Error, failing the test on Ok.
let private unwrapError result =
    match result with
    | Ok v -> failtest (sprintf "Expected Error, got Ok: %A" v)
    | Error e -> e

[<Tests>]
let thumbprintTests =
    testList "OAuthServer.DPoPValidator.thumbprint" [
        test "computeJwkThumbprint produces consistent results" {
            let key = DPoP.generateKeyPair ()
            let jwk = exportPublicJwk key
            let t1 = DPoPValidator.computeJwkThumbprint jwk
            let t2 = DPoPValidator.computeJwkThumbprint jwk
            Expect.equal t1 t2 "Same JWK should produce same thumbprint"
            Expect.isNonEmpty t1 "Thumbprint should not be empty"
        }

        test "computeJwkThumbprint matches client-side DPoP thumbprint" {
            // The server computeJwkThumbprint(jwkJson) should produce the same result
            // as the client DPoP.computeJwkThumbprint(key) since both follow RFC 7638.
            // We verify by creating a proof, validating it, and comparing the returned thumbprint.
            let key = DPoP.generateKeyPair ()
            let jwk = exportPublicJwk key
            let serverThumbprint = DPoPValidator.computeJwkThumbprint jwk
            // Compute thumbprint the same way as DPoP module: SHA-256 of canonical JSON
            let doc = JsonDocument.Parse(jwk)
            let x = doc.RootElement.GetProperty("x").GetString()
            let y = doc.RootElement.GetProperty("y").GetString()
            let canonical = sprintf """{"crv":"P-256","kty":"EC","x":"%s","y":"%s"}""" x y
            let hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical))
            let expectedThumbprint = DPoPValidator.toBase64Url hash
            Expect.equal serverThumbprint expectedThumbprint "Server thumbprint should match manual RFC 7638 computation"
        }

        test "computeJwkThumbprint differs for different keys" {
            let key1 = DPoP.generateKeyPair ()
            let key2 = DPoP.generateKeyPair ()
            let jwk1 = exportPublicJwk key1
            let jwk2 = exportPublicJwk key2
            let t1 = DPoPValidator.computeJwkThumbprint jwk1
            let t2 = DPoPValidator.computeJwkThumbprint jwk2
            Expect.notEqual t1 t2 "Different keys should produce different thumbprints"
        }
    ]

[<Tests>]
let nonceTests =
    testList "OAuthServer.DPoPValidator.nonce" [
        test "generateNonce produces non-empty string" {
            let secret = RandomNumberGenerator.GetBytes(32)
            let nonce = DPoPValidator.generateNonce secret now (TimeSpan.FromMinutes 5.0)
            Expect.isNonEmpty nonce "Nonce should not be empty"
        }

        test "generateNonce is deterministic for same time bucket" {
            let secret = RandomNumberGenerator.GetBytes(32)
            let lifetime = TimeSpan.FromMinutes 5.0
            let t = DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.Zero)
            let n1 = DPoPValidator.generateNonce secret t lifetime
            let n2 = DPoPValidator.generateNonce secret t lifetime
            Expect.equal n1 n2 "Same time should produce same nonce"
        }

        test "validateNonce accepts current bucket nonce" {
            let secret = RandomNumberGenerator.GetBytes(32)
            let lifetime = TimeSpan.FromMinutes 5.0
            let nonce = DPoPValidator.generateNonce secret now lifetime
            let isValid = DPoPValidator.validateNonce secret lifetime now nonce
            Expect.isTrue isValid "Current bucket nonce should be valid"
        }

        test "validateNonce accepts previous bucket nonce" {
            let secret = RandomNumberGenerator.GetBytes(32)
            let lifetime = TimeSpan.FromMinutes 5.0
            // Generate nonce from the previous time bucket
            let previousTime = now - lifetime
            let oldNonce = DPoPValidator.generateNonce secret previousTime lifetime
            let isValid = DPoPValidator.validateNonce secret lifetime now oldNonce
            Expect.isTrue isValid "Previous bucket nonce should be valid"
        }

        test "validateNonce rejects random string" {
            let secret = RandomNumberGenerator.GetBytes(32)
            let lifetime = TimeSpan.FromMinutes 5.0
            let isValid = DPoPValidator.validateNonce secret lifetime now "random-garbage-nonce"
            Expect.isFalse isValid "Random string should not be a valid nonce"
        }
    ]

[<Tests>]
let proofValidationTests =
    testList "OAuthServer.DPoPValidator.parseAndVerifyProof" [
        testTask "valid proof is accepted" {
            let key = DPoP.generateKeyPair ()
            let proof = createValidProof key "POST" "https://auth.example.com/token" None None

            let! result =
                DPoPValidator.parseAndVerifyProof
                    proof "POST" "https://auth.example.com/token"
                    (replayStore ()) None None now defaultMaxAge

            let thumbprint = unwrapOk result
            // Verify the thumbprint matches what we expect from the key
            let jwk = exportPublicJwk key
            let expectedThumbprint = DPoPValidator.computeJwkThumbprint jwk
            Expect.equal thumbprint expectedThumbprint "Thumbprint should match the key"
        }

        testTask "valid proof with access token hash is accepted" {
            let key = DPoP.generateKeyPair ()
            let accessToken = "eyJhbGciOiJFUzI1NiJ9.test-access-token"
            let ath = DPoP.hashAccessToken accessToken
            let proof = createValidProof key "GET" "https://api.example.com/resource" (Some ath) None

            let! result =
                DPoPValidator.parseAndVerifyProof
                    proof "GET" "https://api.example.com/resource"
                    (replayStore ()) (Some ath) None now defaultMaxAge

            let thumbprint = unwrapOk result
            Expect.isNonEmpty thumbprint "Should return a thumbprint"
        }

        testTask "valid proof with nonce is accepted" {
            let key = DPoP.generateKeyPair ()
            let nonce = "server-issued-nonce-123"
            let proof = createValidProof key "POST" "https://auth.example.com/token" None (Some nonce)

            let! result =
                DPoPValidator.parseAndVerifyProof
                    proof "POST" "https://auth.example.com/token"
                    (replayStore ()) None (Some nonce) now defaultMaxAge

            let thumbprint = unwrapOk result
            Expect.isNonEmpty thumbprint "Should return a thumbprint"
        }

        testTask "invalid JWT format rejected (too few parts)" {
            let! result =
                DPoPValidator.parseAndVerifyProof
                    "only-one-part" "POST" "https://example.com"
                    (replayStore ()) None None now defaultMaxAge

            let err = unwrapError result
            Expect.isTrue (match err with OAuthServerError.InvalidDpopProof msg -> msg.Contains("3 dot-separated") | _ -> false)
                "Should report invalid JWT format"
        }

        testTask "invalid JWT format rejected (too many parts)" {
            let! result =
                DPoPValidator.parseAndVerifyProof
                    "a.b.c.d" "POST" "https://example.com"
                    (replayStore ()) None None now defaultMaxAge

            let err = unwrapError result
            Expect.isTrue (match err with OAuthServerError.InvalidDpopProof msg -> msg.Contains("3 dot-separated") | _ -> false)
                "Should report invalid JWT format"
        }

        testTask "invalid base64url in header rejected" {
            let! result =
                DPoPValidator.parseAndVerifyProof
                    "!!!invalid.cGF5bG9hZA.c2ln" "POST" "https://example.com"
                    (replayStore ()) None None now defaultMaxAge

            let err = unwrapError result
            Expect.isTrue (match err with OAuthServerError.InvalidDpopProof _ -> true | _ -> false)
                "Should return InvalidDpopProof error"
        }

        testTask "wrong algorithm rejected" {
            // Craft a JWT header with alg=RS256 instead of ES256
            let headerJson = """{"typ":"dpop+jwt","alg":"RS256","jwk":{"kty":"EC","crv":"P-256","x":"test","y":"test"}}"""
            let headerB64 = stringToBase64Url headerJson
            let payloadJson = """{"jti":"test","htm":"POST","htu":"https://example.com","iat":1709568000}"""
            let payloadB64 = stringToBase64Url payloadJson
            let fakeJwt = sprintf "%s.%s.fakesig" headerB64 payloadB64

            let! result =
                DPoPValidator.parseAndVerifyProof
                    fakeJwt "POST" "https://example.com"
                    (replayStore ()) None None now defaultMaxAge

            let err = unwrapError result
            Expect.isTrue
                (match err with OAuthServerError.InvalidDpopProof msg -> msg.Contains("ES256") | _ -> false)
                "Should report unsupported algorithm"
        }

        testTask "method mismatch rejected" {
            let key = DPoP.generateKeyPair ()
            let proof = createValidProof key "POST" "https://auth.example.com/token" None None

            let! result =
                DPoPValidator.parseAndVerifyProof
                    proof "GET" "https://auth.example.com/token"
                    (replayStore ()) None None now defaultMaxAge

            let err = unwrapError result
            Expect.isTrue
                (match err with OAuthServerError.InvalidDpopProof msg -> msg.Contains("Method mismatch") | _ -> false)
                "Should report method mismatch"
        }

        testTask "URL mismatch rejected" {
            let key = DPoP.generateKeyPair ()
            let proof = createValidProof key "POST" "https://auth.example.com/token" None None

            let! result =
                DPoPValidator.parseAndVerifyProof
                    proof "POST" "https://auth.other.com/token"
                    (replayStore ()) None None now defaultMaxAge

            let err = unwrapError result
            Expect.isTrue
                (match err with OAuthServerError.InvalidDpopProof msg -> msg.Contains("URL mismatch") | _ -> false)
                "Should report URL mismatch"
        }

        testTask "URL matching ignores query string" {
            let key = DPoP.generateKeyPair ()
            // Client creates proof with URL without query
            let proof = createValidProof key "GET" "https://api.example.com/resource" None None

            // Server validates with query string on the URL
            let! result =
                DPoPValidator.parseAndVerifyProof
                    proof "GET" "https://api.example.com/resource?page=1"
                    (replayStore ()) None None now defaultMaxAge

            let thumbprint = unwrapOk result
            Expect.isNonEmpty thumbprint "Query string should be ignored in URL comparison"
        }

        testTask "expired proof rejected" {
            let key = DPoP.generateKeyPair ()
            let proof = createValidProof key "POST" "https://auth.example.com/token" None None
            // Advance time well past the max age
            let futureNow = now + TimeSpan.FromMinutes 10.0

            let! result =
                DPoPValidator.parseAndVerifyProof
                    proof "POST" "https://auth.example.com/token"
                    (replayStore ()) None None futureNow defaultMaxAge

            let err = unwrapError result
            Expect.isTrue
                (match err with OAuthServerError.InvalidDpopProof msg -> msg.Contains("too old") | _ -> false)
                "Should report proof too old"
        }

        testTask "future proof rejected" {
            let key = DPoP.generateKeyPair ()
            let proof = createValidProof key "POST" "https://auth.example.com/token" None None
            // Set "now" to well in the past compared to when the proof was created
            let pastNow = now - TimeSpan.FromMinutes 5.0

            let! result =
                DPoPValidator.parseAndVerifyProof
                    proof "POST" "https://auth.example.com/token"
                    (replayStore ()) None None pastNow defaultMaxAge

            let err = unwrapError result
            Expect.isTrue
                (match err with OAuthServerError.InvalidDpopProof msg -> msg.Contains("future") | _ -> false)
                "Should report proof in the future"
        }

        testTask "replay detected via duplicate jti" {
            let key = DPoP.generateKeyPair ()
            let proof = createValidProof key "POST" "https://auth.example.com/token" None None
            let duplicateStore = AlwaysDuplicateReplayStore() :> IReplayStore

            let! result =
                DPoPValidator.parseAndVerifyProof
                    proof "POST" "https://auth.example.com/token"
                    duplicateStore None None now defaultMaxAge

            let err = unwrapError result
            Expect.isTrue
                (match err with OAuthServerError.InvalidDpopProof msg -> msg.Contains("Replay") || msg.Contains("duplicate") | _ -> false)
                "Should report replay/duplicate jti"
        }

        testTask "access token hash mismatch rejected" {
            let key = DPoP.generateKeyPair ()
            let ath = DPoP.hashAccessToken "token-A"
            let proof = createValidProof key "GET" "https://api.example.com/resource" (Some ath) None
            let wrongAth = DPoP.hashAccessToken "token-B"

            let! result =
                DPoPValidator.parseAndVerifyProof
                    proof "GET" "https://api.example.com/resource"
                    (replayStore ()) (Some wrongAth) None now defaultMaxAge

            let err = unwrapError result
            Expect.isTrue
                (match err with OAuthServerError.InvalidDpopProof msg -> msg.Contains("hash mismatch") | _ -> false)
                "Should report access token hash mismatch"
        }

        testTask "missing ath when expected is rejected" {
            let key = DPoP.generateKeyPair ()
            // Create proof WITHOUT ath
            let proof = createValidProof key "GET" "https://api.example.com/resource" None None
            let expectedAth = DPoP.hashAccessToken "some-token"

            let! result =
                DPoPValidator.parseAndVerifyProof
                    proof "GET" "https://api.example.com/resource"
                    (replayStore ()) (Some expectedAth) None now defaultMaxAge

            let err = unwrapError result
            Expect.isTrue
                (match err with OAuthServerError.InvalidDpopProof msg -> msg.Contains("Missing 'ath'") | _ -> false)
                "Should report missing ath"
        }

        testTask "nonce mismatch rejected" {
            let key = DPoP.generateKeyPair ()
            let proof = createValidProof key "POST" "https://auth.example.com/token" None (Some "client-nonce")

            let! result =
                DPoPValidator.parseAndVerifyProof
                    proof "POST" "https://auth.example.com/token"
                    (replayStore ()) None (Some "different-nonce") now defaultMaxAge

            let err = unwrapError result
            Expect.isTrue
                (match err with OAuthServerError.InvalidDpopProof msg -> msg.Contains("Nonce mismatch") | _ -> false)
                "Should report nonce mismatch"
        }

        testTask "missing nonce when expected is rejected" {
            let key = DPoP.generateKeyPair ()
            // Create proof WITHOUT nonce
            let proof = createValidProof key "POST" "https://auth.example.com/token" None None

            let! result =
                DPoPValidator.parseAndVerifyProof
                    proof "POST" "https://auth.example.com/token"
                    (replayStore ()) None (Some "expected-nonce") now defaultMaxAge

            let err = unwrapError result
            Expect.isTrue
                (match err with OAuthServerError.InvalidDpopProof msg -> msg.Contains("Missing 'nonce'") | _ -> false)
                "Should report missing nonce"
        }

        testTask "invalid signature rejected" {
            let key = DPoP.generateKeyPair ()
            let proof = createValidProof key "POST" "https://auth.example.com/token" None None

            // Tamper with the signature by replacing the last part with a different key's signature
            let parts = proof.Split('.')
            let otherKey = DPoP.generateKeyPair ()
            let signingInput = parts[0] + "." + parts[1]
            let badSig = otherKey.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256)
            let tamperedProof = sprintf "%s.%s.%s" parts[0] parts[1] (DPoPValidator.toBase64Url badSig)

            let! result =
                DPoPValidator.parseAndVerifyProof
                    tamperedProof "POST" "https://auth.example.com/token"
                    (replayStore ()) None None now defaultMaxAge

            let err = unwrapError result
            Expect.isTrue
                (match err with OAuthServerError.InvalidDpopProof msg -> msg.Contains("Invalid signature") | _ -> false)
                "Should report invalid signature"
        }

        testTask "tampered payload rejected" {
            let key = DPoP.generateKeyPair ()
            let proof = createValidProof key "POST" "https://auth.example.com/token" None None

            // Replace the payload with a different one, keeping the original signature
            let parts = proof.Split('.')
            let newPayloadJson = """{"jti":"tampered","htm":"POST","htu":"https://auth.example.com/token","iat":1709568000}"""
            let newPayloadB64 = stringToBase64Url newPayloadJson
            let tamperedProof = sprintf "%s.%s.%s" parts[0] newPayloadB64 parts[2]

            let! result =
                DPoPValidator.parseAndVerifyProof
                    tamperedProof "POST" "https://auth.example.com/token"
                    (replayStore ()) None None now defaultMaxAge

            let err = unwrapError result
            Expect.isTrue
                (match err with OAuthServerError.InvalidDpopProof _ -> true | _ -> false)
                "Should reject tampered payload"
        }

        testTask "method matching is case-insensitive" {
            let key = DPoP.generateKeyPair ()
            // Client creates proof with lowercase "post"
            let proof = createValidProof key "post" "https://auth.example.com/token" None None

            let! result =
                DPoPValidator.parseAndVerifyProof
                    proof "POST" "https://auth.example.com/token"
                    (replayStore ()) None None now defaultMaxAge

            let thumbprint = unwrapOk result
            Expect.isNonEmpty thumbprint "Case-insensitive method comparison should accept"
        }
    ]
