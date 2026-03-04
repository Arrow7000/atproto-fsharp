module ServiceAuthTests

open System
open Expecto
open FSharp.ATProto.Syntax
open FSharp.ATProto.Core
open FSharp.ATProto.Crypto

let private testDid =
    match Did.parse "did:plc:testservice123" with
    | Ok d -> d
    | Error e -> failwith e

let private audienceDid =
    match Did.parse "did:plc:targetpds456" with
    | Ok d -> d
    | Error e -> failwith e

let private testNsid =
    match Nsid.parse "com.atproto.sync.getRepo" with
    | Ok n -> n
    | Error e -> failwith e

[<Tests>]
let serviceAuthTests =
    testList
        "ServiceAuth"
        [ testCase "create and parse P-256 JWT"
          <| fun () ->
              let kp = Keys.generate Algorithm.P256
              let now = DateTimeOffset.UtcNow

              let claims : ServiceAuth.Claims =
                  { Iss = testDid
                    Aud = audienceDid
                    Lxm = Some testNsid
                    Exp = now.AddSeconds 60.0
                    Iat = now }

              let token =
                  ServiceAuth.createToken ServiceAuth.Algorithm.ES256 (Signing.sign kp) claims

              let parts = token.Split '.'
              Expect.equal parts.Length 3 "JWT has 3 parts"

              match ServiceAuth.parseClaims token with
              | Ok (parsed, alg) ->
                  Expect.equal parsed.Iss testDid "iss"
                  Expect.equal parsed.Aud audienceDid "aud"
                  Expect.equal alg ServiceAuth.Algorithm.ES256 "algorithm"

                  match parsed.Lxm with
                  | Some nsid -> Expect.equal (Nsid.value nsid) "com.atproto.sync.getRepo" "lxm"
                  | None -> failtest "Expected lxm"
              | Error e -> failtest e

          testCase "create and parse K-256 JWT"
          <| fun () ->
              let kp = Keys.generate Algorithm.K256
              let now = DateTimeOffset.UtcNow

              let claims : ServiceAuth.Claims =
                  { Iss = testDid
                    Aud = audienceDid
                    Lxm = None
                    Exp = now.AddSeconds 60.0
                    Iat = now }

              let token =
                  ServiceAuth.createToken ServiceAuth.Algorithm.ES256K (Signing.sign kp) claims

              match ServiceAuth.parseClaims token with
              | Ok (parsed, alg) ->
                  Expect.equal parsed.Iss testDid "iss"
                  Expect.equal parsed.Lxm None "no lxm"
                  Expect.equal alg ServiceAuth.Algorithm.ES256K "algorithm"
              | Error e -> failtest e

          testCase "validate P-256 JWT signature"
          <| fun () ->
              let kp = Keys.generate Algorithm.P256
              let pub = Keys.publicKey kp

              let token =
                  ServiceAuth.createTokenNow
                      ServiceAuth.Algorithm.ES256
                      (Signing.sign kp)
                      testDid
                      audienceDid
                      (Some testNsid)

              match ServiceAuth.validateToken (Signing.verify pub) token with
              | Ok claims ->
                  Expect.equal claims.Iss testDid "iss"
                  Expect.equal claims.Aud audienceDid "aud"
              | Error e -> failtest e

          testCase "validate K-256 JWT signature"
          <| fun () ->
              let kp = Keys.generate Algorithm.K256
              let pub = Keys.publicKey kp

              let token =
                  ServiceAuth.createTokenNow
                      ServiceAuth.Algorithm.ES256K
                      (Signing.sign kp)
                      testDid
                      audienceDid
                      None

              match ServiceAuth.validateToken (Signing.verify pub) token with
              | Ok claims -> Expect.equal claims.Iss testDid "iss"
              | Error e -> failtest e

          testCase "wrong key rejects JWT"
          <| fun () ->
              let kp1 = Keys.generate Algorithm.P256
              let kp2 = Keys.generate Algorithm.P256

              let token =
                  ServiceAuth.createTokenNow
                      ServiceAuth.Algorithm.ES256
                      (Signing.sign kp1)
                      testDid
                      audienceDid
                      None

              match ServiceAuth.validateToken (Signing.verify (Keys.publicKey kp2)) token with
              | Error msg -> Expect.stringContains msg "Invalid signature" "reject wrong key"
              | Ok _ -> failtest "Expected rejection"

          testCase "expired JWT rejected"
          <| fun () ->
              let kp = Keys.generate Algorithm.P256
              let pub = Keys.publicKey kp

              let claims : ServiceAuth.Claims =
                  { Iss = testDid
                    Aud = audienceDid
                    Lxm = None
                    Exp = DateTimeOffset.UtcNow.AddSeconds -10.0
                    Iat = DateTimeOffset.UtcNow.AddSeconds -70.0 }

              let token =
                  ServiceAuth.createToken ServiceAuth.Algorithm.ES256 (Signing.sign kp) claims

              match ServiceAuth.validateToken (Signing.verify pub) token with
              | Error msg -> Expect.stringContains msg "expired" "reject expired"
              | Ok _ -> failtest "Expected rejection"

          testCase "malformed JWT rejected"
          <| fun () ->
              match ServiceAuth.parseClaims "not.a.valid.jwt" with
              | Error _ -> ()
              | Ok _ -> failtest "Expected error"

              match ServiceAuth.parseClaims "onlyonepart" with
              | Error msg -> Expect.stringContains msg "3 parts" "reject malformed"
              | Ok _ -> failtest "Expected error"

          testCase "createTokenNow sets reasonable expiry"
          <| fun () ->
              let kp = Keys.generate Algorithm.P256

              let token =
                  ServiceAuth.createTokenNow
                      ServiceAuth.Algorithm.ES256
                      (Signing.sign kp)
                      testDid
                      audienceDid
                      None

              match ServiceAuth.parseClaims token with
              | Ok (claims, _) ->
                  let diff = (claims.Exp - claims.Iat).TotalSeconds
                  Expect.floatClose Accuracy.medium diff 60.0 "60 second expiry"
              | Error e -> failtest e

          testCase "withServiceAuth configures agent"
          <| fun () ->
              let kp = Keys.generate Algorithm.P256
              let agent = AtpAgent.create "https://bsky.social"

              let authed =
                  ServiceAuth.withServiceAuth
                      ServiceAuth.Algorithm.ES256
                      (Signing.sign kp)
                      testDid
                      audienceDid
                      agent

              Expect.isSome authed.AuthenticateRequest "auth handler set" ]
