module FSharp.ATProto.OAuthServer.Tests.TokenSignerTests

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Expecto
open FSharp.ATProto.Syntax
open FSharp.ATProto.OAuthServer

let private unwrap =
    function
    | Ok v -> v
    | Error e -> failtest (sprintf "Expected Ok, got Error: %A" e)

let private testDid = Did.parse "did:plc:testuser123" |> unwrap

/// Decode a base64url string to bytes (mirror of TokenSigner.fromBase64Url).
let private fromBase64Url (s: string) : byte[] =
    let s = s.Replace('-', '+').Replace('_', '/')

    let padded =
        match s.Length % 4 with
        | 2 -> s + "=="
        | 3 -> s + "="
        | _ -> s

    Convert.FromBase64String(padded)

/// Parse a JWT part (header or payload) from base64url to JsonDocument.
let private parseJwtPart (base64url: string) : JsonDocument =
    let json = Encoding.UTF8.GetString(fromBase64Url base64url)
    JsonDocument.Parse(json)

/// Create a test configuration with a real ES256 key.
let private makeTestConfig () =
    let key = TokenSigner.createSigningKey ()
    let signingFn = TokenSigner.makeSigningFunction key
    let jwk = TokenSigner.exportPublicJwk key

    let config =
        { OAuthServerConfig.defaults with
            Issuer = "https://auth.example.com"
            SigningKey = signingFn
            PublicKeyJwk = jwk }

    (key, config)

[<Tests>]
let tokenSignerTests =
    testList
        "OAuthServer.TokenSigner"
        [ testCase "createSigningKey creates valid EC P-256 key"
          <| fun _ ->
              let key = TokenSigner.createSigningKey ()
              let parameters = key.ExportParameters(false)
              // OID friendly name is platform-dependent: "ECDSA_P256" on Windows, "nistP256" on macOS
              let curveName = parameters.Curve.Oid.FriendlyName
              Expect.isTrue (curveName = "ECDSA_P256" || curveName = "nistP256") (sprintf "Curve should be P-256 but got '%s'" curveName)
              Expect.equal parameters.Q.X.Length 32 "X coordinate should be 32 bytes"
              Expect.equal parameters.Q.Y.Length 32 "Y coordinate should be 32 bytes"

          testCase "exportPublicJwk produces valid JWK JSON with required fields"
          <| fun _ ->
              let key = TokenSigner.createSigningKey ()
              let jwk = TokenSigner.exportPublicJwk key
              let doc = JsonDocument.Parse(jwk)
              let root = doc.RootElement
              Expect.equal (root.GetProperty("kty").GetString()) "EC" "kty should be EC"
              Expect.equal (root.GetProperty("crv").GetString()) "P-256" "crv should be P-256"
              Expect.isTrue (root.TryGetProperty("x", ref Unchecked.defaultof<_>)) "should have x"
              Expect.isTrue (root.TryGetProperty("y", ref Unchecked.defaultof<_>)) "should have y"
              Expect.equal (root.GetProperty("use").GetString()) "sig" "use should be sig"
              Expect.equal (root.GetProperty("alg").GetString()) "ES256" "alg should be ES256"
              // kid should be non-empty
              let kid = root.GetProperty("kid").GetString()
              Expect.isNotEmpty kid "kid should be non-empty"

          testCase "exportPublicJwk kid is JWK thumbprint"
          <| fun _ ->
              let key = TokenSigner.createSigningKey ()
              let jwk = TokenSigner.exportPublicJwk key
              let doc = JsonDocument.Parse(jwk)
              let x = doc.RootElement.GetProperty("x").GetString()
              let y = doc.RootElement.GetProperty("y").GetString()
              // Compute expected thumbprint manually
              let canonical = sprintf """{"crv":"P-256","kty":"EC","x":"%s","y":"%s"}""" x y
              let hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical))
              let expectedKid = TokenSigner.toBase64Url hash
              let actualKid = doc.RootElement.GetProperty("kid").GetString()
              Expect.equal actualKid expectedKid "kid should be JWK thumbprint"

          testCase "exportJwks produces valid JWKS JSON with keys array"
          <| fun _ ->
              let key = TokenSigner.createSigningKey ()
              let jwks = TokenSigner.exportJwks key
              let doc = JsonDocument.Parse(jwks)
              let keys = doc.RootElement.GetProperty("keys")
              Expect.equal (keys.GetArrayLength()) 1 "JWKS should have one key"
              let jwkInSet = keys[0]
              Expect.equal (jwkInSet.GetProperty("kty").GetString()) "EC" "key kty should be EC"
              Expect.equal (jwkInSet.GetProperty("alg").GetString()) "ES256" "key alg should be ES256"

          testCase "makeSigningFunction produces valid ES256 signature"
          <| fun _ ->
              let key = TokenSigner.createSigningKey ()
              let sign = TokenSigner.makeSigningFunction key
              let data = Encoding.UTF8.GetBytes("test data to sign")
              let signature = sign data
              Expect.isGreaterThan signature.Length 0 "signature should be non-empty"
              // Verify with the same key
              let verified = key.VerifyData(data, signature, HashAlgorithmName.SHA256)
              Expect.isTrue verified "signature should verify with the public key"

          testCase "createAccessToken produces 3-part JWT"
          <| fun _ ->
              let (_key, config) = makeTestConfig ()
              let now = DateTimeOffset.UtcNow

              let token =
                  TokenSigner.createAccessToken config testDid "https://app.example.com" "atproto" "dpop-thumbprint" now

              let parts = token.Split('.')
              Expect.equal parts.Length 3 "JWT should have 3 parts"
              // Each part should be non-empty
              for part in parts do
                  Expect.isNotEmpty part "each JWT part should be non-empty"

          testCase "createAccessToken JWT header has correct typ, alg, kid"
          <| fun _ ->
              let (_key, config) = makeTestConfig ()
              let now = DateTimeOffset.UtcNow

              let token =
                  TokenSigner.createAccessToken config testDid "https://app.example.com" "atproto" "dpop-thumbprint" now

              let parts = token.Split('.')
              let header = parseJwtPart parts.[0]
              Expect.equal (header.RootElement.GetProperty("typ").GetString()) "at+jwt" "typ should be at+jwt"
              Expect.equal (header.RootElement.GetProperty("alg").GetString()) "ES256" "alg should be ES256"
              // kid should match the config's JWK kid
              let configDoc = JsonDocument.Parse(config.PublicKeyJwk)
              let expectedKid = configDoc.RootElement.GetProperty("kid").GetString()
              Expect.equal (header.RootElement.GetProperty("kid").GetString()) expectedKid "kid should match config JWK"

          testCase "createAccessToken JWT payload has correct claims"
          <| fun _ ->
              let (_key, config) = makeTestConfig ()
              let now = DateTimeOffset.UtcNow

              let token =
                  TokenSigner.createAccessToken
                      config
                      testDid
                      "https://app.example.com"
                      "atproto transition:generic"
                      "dpop-thumbprint-xyz"
                      now

              let parts = token.Split('.')
              let payload = parseJwtPart parts.[1]
              let root = payload.RootElement
              Expect.equal (root.GetProperty("iss").GetString()) "https://auth.example.com" "iss"
              Expect.equal (root.GetProperty("sub").GetString()) "did:plc:testuser123" "sub"
              Expect.equal (root.GetProperty("aud").GetString()) "https://auth.example.com" "aud"
              Expect.equal (root.GetProperty("scope").GetString()) "atproto transition:generic" "scope"
              Expect.equal (root.GetProperty("client_id").GetString()) "https://app.example.com" "client_id"
              // cnf.jkt
              let cnf = root.GetProperty("cnf")
              Expect.equal (cnf.GetProperty("jkt").GetString()) "dpop-thumbprint-xyz" "cnf.jkt"
              // jti should be non-empty
              Expect.isNotEmpty (root.GetProperty("jti").GetString()) "jti should be non-empty"

          testCase "createAccessToken exp = iat + AccessTokenLifetime"
          <| fun _ ->
              let (_key, config) = makeTestConfig ()
              let now = DateTimeOffset.UtcNow

              let token =
                  TokenSigner.createAccessToken config testDid "https://app.example.com" "atproto" "thumbprint" now

              let parts = token.Split('.')
              let payload = parseJwtPart parts.[1]
              let iat = payload.RootElement.GetProperty("iat").GetInt64()
              let exp = payload.RootElement.GetProperty("exp").GetInt64()
              let expectedExp = iat + int64 config.AccessTokenLifetime.TotalSeconds
              Expect.equal exp expectedExp "exp should be iat + AccessTokenLifetime"

          testCase "createAccessToken signature is verifiable with the public key"
          <| fun _ ->
              let (key, config) = makeTestConfig ()
              let now = DateTimeOffset.UtcNow

              let token =
                  TokenSigner.createAccessToken config testDid "https://app.example.com" "atproto" "thumbprint" now

              let parts = token.Split('.')
              let signingInput = parts.[0] + "." + parts.[1]
              let signatureBytes = fromBase64Url parts.[2]
              let verified = key.VerifyData(Encoding.UTF8.GetBytes(signingInput), signatureBytes, HashAlgorithmName.SHA256)
              Expect.isTrue verified "access token signature should verify with the signing key"

          testCase "createRefreshToken produces non-empty unique strings"
          <| fun _ ->
              let t1 = TokenSigner.createRefreshToken ()
              let t2 = TokenSigner.createRefreshToken ()
              Expect.isNotEmpty t1 "refresh token 1 should be non-empty"
              Expect.isNotEmpty t2 "refresh token 2 should be non-empty"
              Expect.notEqual t1 t2 "refresh tokens should be unique"

          testCase "parseAccessTokenKid extracts kid from JWT header"
          <| fun _ ->
              let (_key, config) = makeTestConfig ()
              let now = DateTimeOffset.UtcNow

              let token =
                  TokenSigner.createAccessToken config testDid "https://app.example.com" "atproto" "thumbprint" now

              let kid = TokenSigner.parseAccessTokenKid token
              Expect.isSome kid "should extract kid"
              let configDoc = JsonDocument.Parse(config.PublicKeyJwk)
              let expectedKid = configDoc.RootElement.GetProperty("kid").GetString()
              Expect.equal kid.Value expectedKid "extracted kid should match config JWK kid"

          testCase "parseAccessTokenKid returns None for malformed input"
          <| fun _ ->
              Expect.isNone (TokenSigner.parseAccessTokenKid "not-a-jwt") "no dots"
              Expect.isNone (TokenSigner.parseAccessTokenKid "a.b") "only 2 parts but invalid base64"
              Expect.isNone (TokenSigner.parseAccessTokenKid "") "empty string"

          testCase "generateRandomString produces base64url strings of expected length"
          <| fun _ ->
              let s = TokenSigner.generateRandomString ()
              // 32 bytes -> ceil(32*4/3) = 43 base64url chars (no padding)
              Expect.equal s.Length 43 "32 random bytes should produce 43 base64url chars"
              // Should only contain base64url characters
              let isBase64Url c =
                  (c >= 'A' && c <= 'Z')
                  || (c >= 'a' && c <= 'z')
                  || (c >= '0' && c <= '9')
                  || c = '-'
                  || c = '_'

              for c in s do
                  Expect.isTrue (isBase64Url c) (sprintf "char '%c' should be base64url" c)

          testCase "round-trip: sign and verify with makeSigningFunction"
          <| fun _ ->
              let key = TokenSigner.createSigningKey ()
              let sign = TokenSigner.makeSigningFunction key
              let message = "header.payload"
              let data = Encoding.UTF8.GetBytes(message)
              let signature = sign data
              let verified = key.VerifyData(data, signature, HashAlgorithmName.SHA256)
              Expect.isTrue verified "round-trip sign/verify should succeed" ]
