module FSharp.ATProto.OAuth.Tests.DPoPTests

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Expecto
open FSharp.ATProto.OAuth

/// Parse a JWT into its three parts (header, payload, signature) as raw strings.
let private splitJwt (jwt: string) =
    let parts = jwt.Split('.')

    if parts.Length <> 3 then
        failtest (sprintf "JWT should have 3 parts, got %d" parts.Length)

    parts.[0], parts.[1], parts.[2]

/// Decode a base64url string to a UTF-8 string.
let private decodeBase64Url (s: string) =
    let s = s.Replace('-', '+').Replace('_', '/')

    let padded =
        match s.Length % 4 with
        | 2 -> s + "=="
        | 3 -> s + "="
        | _ -> s

    Convert.FromBase64String(padded) |> Encoding.UTF8.GetString

/// Parse a base64url-encoded JWT part as a JSON document.
let private parseJwtPart (part: string) =
    let json = decodeBase64Url part
    JsonDocument.Parse(json)

[<Tests>]
let dpopKeyPairTests =
    testList
        "DPoP.generateKeyPair"
        [ test "generates an ES256 key pair" {
              use key = DPoP.generateKeyPair ()
              let parameters = key.ExportParameters(false)
              // P-256 keys have 32-byte coordinates
              Expect.equal parameters.Q.X.Length 32 "X coordinate should be 32 bytes"
              Expect.equal parameters.Q.Y.Length 32 "Y coordinate should be 32 bytes"
          }

          test "generates distinct key pairs" {
              use key1 = DPoP.generateKeyPair ()
              use key2 = DPoP.generateKeyPair ()
              let p1 = key1.ExportParameters(false)
              let p2 = key2.ExportParameters(false)
              Expect.notEqual p1.Q.X p2.Q.X "Keys should be distinct"
          }

          test "generated key can sign and verify" {
              use key = DPoP.generateKeyPair ()
              let data = Encoding.UTF8.GetBytes("test data")
              let signature = key.SignData(data, HashAlgorithmName.SHA256)
              let valid = key.VerifyData(data, signature, HashAlgorithmName.SHA256)
              Expect.isTrue valid "Signature should verify"
          } ]

[<Tests>]
let dpopProofTests =
    testList
        "DPoP.createProof"
        [ test "proof is a valid three-part JWT" {
              use key = DPoP.generateKeyPair ()
              let proof = DPoP.createProof key "GET" "https://example.com/resource" None None
              let parts = proof.Split('.')
              Expect.equal parts.Length 3 "JWT should have 3 parts"
          }

          test "proof header has correct typ" {
              use key = DPoP.generateKeyPair ()
              let proof = DPoP.createProof key "GET" "https://example.com/resource" None None
              let header, _, _ = splitJwt proof
              let doc = parseJwtPart header
              let typ = doc.RootElement.GetProperty("typ").GetString()
              Expect.equal typ "dpop+jwt" "typ should be dpop+jwt"
          }

          test "proof header has ES256 algorithm" {
              use key = DPoP.generateKeyPair ()
              let proof = DPoP.createProof key "POST" "https://example.com/token" None None
              let header, _, _ = splitJwt proof
              let doc = parseJwtPart header
              let alg = doc.RootElement.GetProperty("alg").GetString()
              Expect.equal alg "ES256" "alg should be ES256"
          }

          test "proof header contains JWK with EC public key" {
              use key = DPoP.generateKeyPair ()
              let proof = DPoP.createProof key "GET" "https://example.com/resource" None None
              let header, _, _ = splitJwt proof
              let doc = parseJwtPart header
              let jwk = doc.RootElement.GetProperty("jwk")
              Expect.equal (jwk.GetProperty("kty").GetString()) "EC" "kty should be EC"
              Expect.equal (jwk.GetProperty("crv").GetString()) "P-256" "crv should be P-256"
              Expect.isTrue (jwk.TryGetProperty("x") |> fst) "JWK should have x"
              Expect.isTrue (jwk.TryGetProperty("y") |> fst) "JWK should have y"
          }

          test "proof header JWK does not contain private key" {
              use key = DPoP.generateKeyPair ()
              let proof = DPoP.createProof key "GET" "https://example.com/resource" None None
              let header, _, _ = splitJwt proof
              let doc = parseJwtPart header
              let jwk = doc.RootElement.GetProperty("jwk")
              Expect.isFalse (jwk.TryGetProperty("d") |> fst) "JWK should not contain private key (d)"
          }

          test "proof payload has correct htm" {
              use key = DPoP.generateKeyPair ()
              let proof = DPoP.createProof key "POST" "https://example.com/token" None None
              let _, payload, _ = splitJwt proof
              let doc = parseJwtPart payload
              let htm = doc.RootElement.GetProperty("htm").GetString()
              Expect.equal htm "POST" "htm should be POST"
          }

          test "proof payload has correct htu" {
              use key = DPoP.generateKeyPair ()
              let url = "https://auth.example.com/token"
              let proof = DPoP.createProof key "POST" url None None
              let _, payload, _ = splitJwt proof
              let doc = parseJwtPart payload
              let htu = doc.RootElement.GetProperty("htu").GetString()
              Expect.equal htu url "htu should match target URL"
          }

          test "proof payload has jti claim" {
              use key = DPoP.generateKeyPair ()
              let proof = DPoP.createProof key "GET" "https://example.com/resource" None None
              let _, payload, _ = splitJwt proof
              let doc = parseJwtPart payload
              let jti = doc.RootElement.GetProperty("jti").GetString()
              Expect.isNotEmpty jti "jti should not be empty"
          }

          test "proof payload has iat claim with recent timestamp" {
              use key = DPoP.generateKeyPair ()
              let proof = DPoP.createProof key "GET" "https://example.com/resource" None None
              let _, payload, _ = splitJwt proof
              let doc = parseJwtPart payload
              let iat = doc.RootElement.GetProperty("iat").GetInt64()
              let now = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
              Expect.isLessThanOrEqual (abs (now - iat)) 5L "iat should be within 5 seconds of now"
          }

          test "proof includes ath when access token hash provided" {
              use key = DPoP.generateKeyPair ()
              let ath = DPoP.hashAccessToken "test-token"

              let proof =
                  DPoP.createProof key "GET" "https://example.com/resource" (Some ath) None

              let _, payload, _ = splitJwt proof
              let doc = parseJwtPart payload
              let athClaim = doc.RootElement.GetProperty("ath").GetString()
              Expect.equal athClaim ath "ath should match provided hash"
          }

          test "proof omits ath when no access token hash" {
              use key = DPoP.generateKeyPair ()
              let proof = DPoP.createProof key "GET" "https://example.com/resource" None None
              let _, payload, _ = splitJwt proof
              let doc = parseJwtPart payload
              Expect.isFalse (doc.RootElement.TryGetProperty("ath") |> fst) "ath should be absent"
          }

          test "proof includes nonce when provided" {
              use key = DPoP.generateKeyPair ()

              let proof =
                  DPoP.createProof key "POST" "https://example.com/token" None (Some "server-nonce-123")

              let _, payload, _ = splitJwt proof
              let doc = parseJwtPart payload
              let nonce = doc.RootElement.GetProperty("nonce").GetString()
              Expect.equal nonce "server-nonce-123" "nonce should match"
          }

          test "proof omits nonce when not provided" {
              use key = DPoP.generateKeyPair ()
              let proof = DPoP.createProof key "GET" "https://example.com/resource" None None
              let _, payload, _ = splitJwt proof
              let doc = parseJwtPart payload
              Expect.isFalse (doc.RootElement.TryGetProperty("nonce") |> fst) "nonce should be absent"
          }

          test "proof signature is verifiable with the key" {
              use key = DPoP.generateKeyPair ()
              let proof = DPoP.createProof key "GET" "https://example.com/resource" None None
              let parts = proof.Split('.')
              let signingInput = parts.[0] + "." + parts.[1]
              let signatureBytes = DPoP.fromBase64Url parts.[2]
              let dataBytes = Encoding.UTF8.GetBytes(signingInput)
              let valid = key.VerifyData(dataBytes, signatureBytes, HashAlgorithmName.SHA256)
              Expect.isTrue valid "Signature should verify with the signing key"
          }

          test "each proof has a unique jti" {
              use key = DPoP.generateKeyPair ()
              let proof1 = DPoP.createProof key "GET" "https://example.com/resource" None None
              let proof2 = DPoP.createProof key "GET" "https://example.com/resource" None None
              let _, payload1, _ = splitJwt proof1
              let _, payload2, _ = splitJwt proof2
              let doc1 = parseJwtPart payload1
              let doc2 = parseJwtPart payload2
              let jti1 = doc1.RootElement.GetProperty("jti").GetString()
              let jti2 = doc2.RootElement.GetProperty("jti").GetString()
              Expect.notEqual jti1 jti2 "Each proof should have a unique jti"
          } ]

[<Tests>]
let pkceTests =
    testList
        "DPoP.generatePkce"
        [ test "generates a PKCE challenge with S256 method" {
              let pkce = DPoP.generatePkce ()
              Expect.equal pkce.Method "S256" "Method should be S256"
          }

          test "verifier is non-empty" {
              let pkce = DPoP.generatePkce ()
              Expect.isNotEmpty pkce.Verifier "Verifier should not be empty"
          }

          test "challenge is base64url-encoded SHA-256 of verifier" {
              let pkce = DPoP.generatePkce ()
              // Recompute the challenge from the verifier
              let expectedHash = SHA256.HashData(Encoding.ASCII.GetBytes(pkce.Verifier))
              let expectedChallenge = DPoP.toBase64Url expectedHash
              Expect.equal pkce.Challenge expectedChallenge "Challenge should be S256 hash of verifier"
          }

          test "each call generates distinct verifiers" {
              let pkce1 = DPoP.generatePkce ()
              let pkce2 = DPoP.generatePkce ()
              Expect.notEqual pkce1.Verifier pkce2.Verifier "Verifiers should be distinct"
          } ]

[<Tests>]
let hashAccessTokenTests =
    testList
        "DPoP.hashAccessToken"
        [ test "produces base64url-encoded SHA-256 hash" {
              let token = "test-access-token"
              let hash = DPoP.hashAccessToken token
              // Verify by recomputing
              let expectedHash = SHA256.HashData(Encoding.ASCII.GetBytes(token))
              let expected = DPoP.toBase64Url expectedHash
              Expect.equal hash expected "Hash should be base64url SHA-256"
          }

          test "different tokens produce different hashes" {
              let hash1 = DPoP.hashAccessToken "token-1"
              let hash2 = DPoP.hashAccessToken "token-2"
              Expect.notEqual hash1 hash2 "Different tokens should have different hashes"
          } ]

[<Tests>]
let base64UrlTests =
    testList
        "DPoP base64url encoding"
        [ test "toBase64Url produces URL-safe characters" {
              // Use bytes that would produce + and / in standard base64
              let bytes = [| 0xFBuy; 0xFFuy; 0xFEuy |]
              let encoded = DPoP.toBase64Url bytes
              Expect.isFalse (encoded.Contains('+')) "Should not contain +"
              Expect.isFalse (encoded.Contains('/')) "Should not contain /"
              Expect.isFalse (encoded.Contains('=')) "Should not contain padding"
          }

          test "fromBase64Url roundtrips with toBase64Url" {
              let original = [| 1uy; 2uy; 3uy; 4uy; 5uy; 6uy; 7uy; 8uy |]
              let encoded = DPoP.toBase64Url original
              let decoded = DPoP.fromBase64Url encoded
              Expect.equal decoded original "Roundtrip should preserve bytes"
          } ]

[<Tests>]
let exportPublicJwkTests =
    testList
        "DPoP.exportPublicJwk"
        [ test "exports valid JSON with required EC fields" {
              use key = DPoP.generateKeyPair ()
              let jwk = DPoP.exportPublicJwk key
              let doc = JsonDocument.Parse(jwk)
              Expect.equal (doc.RootElement.GetProperty("kty").GetString()) "EC" "kty"
              Expect.equal (doc.RootElement.GetProperty("crv").GetString()) "P-256" "crv"
              Expect.isTrue (doc.RootElement.TryGetProperty("x") |> fst) "has x"
              Expect.isTrue (doc.RootElement.TryGetProperty("y") |> fst) "has y"
          } ]
