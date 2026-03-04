module SigningTests

open System
open Expecto
open FSharp.ATProto.Crypto

/// Decode base64 with optional padding (test vectors omit padding).
let private fromBase64 (s : string) =
    let padded =
        match s.Length % 4 with
        | 2 -> s + "=="
        | 3 -> s + "="
        | _ -> s
    Convert.FromBase64String padded

// Interop test data from extern/atproto-interop-tests/crypto/signature-fixtures.json
let private messageBytes = fromBase64 "oWVoZWxsb2V3b3JsZA"

// Valid P-256 low-S signature
let private p256DidKey = "did:key:zDnaembgSGUhZULN2Caob4HLJPaxBh92N7rtH21TErzqf8HQo"
let private p256ValidSig = fromBase64 "2vZNsG3UKvvO/CDlrdvyZRISOFylinBh0Jupc6KcWoJWExHptCfduPleDbG3rko3YZnn9Lw0IjpixVmexJDegg"

// Valid K-256 low-S signature
let private k256DidKey = "did:key:zQ3shqwJEJyMBsBXCWyCBpUBMqxcon9oHB7mCvx4sSpMdLJwc"
let private k256ValidSig = fromBase64 "5WpdIuEUUfVUYaozsi8G0B3cWO09cgZbIIwg1t2YKdUn/FEznOndsz/qgiYb89zwxYCbB71f7yQK5Lr7NasfoA"

// P-256 high-S (invalid)
let private p256HighSSig = fromBase64 "2vZNsG3UKvvO/CDlrdvyZRISOFylinBh0Jupc6KcWoKp7O4VS9giSAah8k5IUbXIW00SuOrjfEqQ9HEkN9JGzw"

// K-256 high-S (invalid)
let private k256HighSSig = fromBase64 "5WpdIuEUUfVUYaozsi8G0B3cWO09cgZbIIwg1t2YKdXYA67MYxYiTMAVfdnkDCMN9S5B3vHosRe07aORmoshoQ"

// P-256 DER-encoded (invalid - wrong length)
let private p256DerSig = fromBase64 "MEQCIFxYelWJ9lNcAVt+jK0y/T+DC/X4ohFZ+m8f9SEItkY1AiACX7eXz5sgtaRrz/SdPR8kprnbHMQVde0T2R8yOTBweA"

// K-256 DER-encoded (invalid - wrong length)
let private k256DerSig = fromBase64 "MEUCIQCWumUqJqOCqInXF7AzhIRg2MhwRz2rWZcOEsOjPmNItgIgXJH7RnqfYY6M0eg33wU0sFYDlprwdOcpRn78Sz5ePgk"

[<Tests>]
let interopVerifyTests =
    testList
        "Signing.verify (interop)"
        [ testCase "valid P-256 low-S signature"
          <| fun () ->
              match Multikey.decodeDid p256DidKey with
              | Ok key -> Expect.isTrue (Signing.verify key messageBytes p256ValidSig) "valid P-256"
              | Error e -> failtest e

          testCase "valid K-256 low-S signature"
          <| fun () ->
              match Multikey.decodeDid k256DidKey with
              | Ok key -> Expect.isTrue (Signing.verify key messageBytes k256ValidSig) "valid K-256"
              | Error e -> failtest e

          testCase "reject P-256 high-S signature"
          <| fun () ->
              match Multikey.decodeDid p256DidKey with
              | Ok key -> Expect.isFalse (Signing.verify key messageBytes p256HighSSig) "reject high-S P-256"
              | Error e -> failtest e

          testCase "reject K-256 high-S signature"
          <| fun () ->
              match Multikey.decodeDid k256DidKey with
              | Ok key -> Expect.isFalse (Signing.verify key messageBytes k256HighSSig) "reject high-S K-256"
              | Error e -> failtest e

          testCase "reject P-256 DER-encoded signature"
          <| fun () ->
              match Multikey.decodeDid "did:key:zDnaeT6hL2RnTdUhAPLij1QBkhYZnmuKyM7puQLW1tkF4Zkt8" with
              | Ok key -> Expect.isFalse (Signing.verify key messageBytes p256DerSig) "reject DER P-256"
              | Error e -> failtest e

          testCase "reject K-256 DER-encoded signature"
          <| fun () ->
              match Multikey.decodeDid "did:key:zQ3shnriYMXc8wvkbJqfNWh5GXn2bVAeqTC92YuNbek4npqGF" with
              | Ok key -> Expect.isFalse (Signing.verify key messageBytes k256DerSig) "reject DER K-256"
              | Error e -> failtest e ]

[<Tests>]
let signVerifyRoundTripTests =
    testList
        "Signing sign+verify"
        [ testCase "P-256 sign then verify"
          <| fun () ->
              let kp = Keys.generate Algorithm.P256
              let data = Text.Encoding.UTF8.GetBytes "test message"
              let signature = Signing.sign kp data
              Expect.equal signature.Length 64 "compact signature is 64 bytes"
              let pub = Keys.publicKey kp
              Expect.isTrue (Signing.verify pub data signature) "verify own signature"

          testCase "K-256 sign then verify"
          <| fun () ->
              let kp = Keys.generate Algorithm.K256
              let data = Text.Encoding.UTF8.GetBytes "test message"
              let signature = Signing.sign kp data
              Expect.equal signature.Length 64 "compact signature is 64 bytes"
              let pub = Keys.publicKey kp
              Expect.isTrue (Signing.verify pub data signature) "verify own signature"

          testCase "P-256 signature has low-S"
          <| fun () ->
              let kp = Keys.generate Algorithm.P256
              for i in 1..20 do
                  let data = Text.Encoding.UTF8.GetBytes (sprintf "test %d" i)
                  let signature = Signing.sign kp data
                  let s = signature.[32..]
                  Expect.isTrue (Signing.isLowS Algorithm.P256 s) (sprintf "low-S on iteration %d" i)

          testCase "K-256 signature has low-S"
          <| fun () ->
              let kp = Keys.generate Algorithm.K256
              for i in 1..20 do
                  let data = Text.Encoding.UTF8.GetBytes (sprintf "test %d" i)
                  let signature = Signing.sign kp data
                  let s = signature.[32..]
                  Expect.isTrue (Signing.isLowS Algorithm.K256 s) (sprintf "low-S on iteration %d" i)

          testCase "wrong key rejects signature"
          <| fun () ->
              let kp1 = Keys.generate Algorithm.P256
              let kp2 = Keys.generate Algorithm.P256
              let data = Text.Encoding.UTF8.GetBytes "test"
              let signature = Signing.sign kp1 data
              Expect.isFalse (Signing.verify (Keys.publicKey kp2) data signature) "wrong key rejects"

          testCase "tampered data rejects signature"
          <| fun () ->
              let kp = Keys.generate Algorithm.P256
              let data = Text.Encoding.UTF8.GetBytes "original"
              let signature = Signing.sign kp data
              let tampered = Text.Encoding.UTF8.GetBytes "tampered"
              Expect.isFalse (Signing.verify (Keys.publicKey kp) tampered signature) "tampered data rejects" ]
