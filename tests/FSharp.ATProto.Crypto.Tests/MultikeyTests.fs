module MultikeyTests

open System
open Expecto
open FSharp.ATProto.Crypto

// Interop test data from extern/atproto-interop-tests/crypto/w3c_didkey_*.json

// P-256: privateKeyBytesBase58 -> publicDidKey
let private p256PrivKeyBase58 = "9p4VRzdmhsnq869vQjVCTrRry7u4TtfRxhvBFJTGU2Cp"
let private p256ExpectedDid = "did:key:zDnaeTiq1PdzvZXUaMdezchcMJQpBdH2VN4pgrrEhMCCbmwSb"

// K-256: privateKeyBytesHex -> publicDidKey
let private k256TestVectors =
    [ "9085d2bef69286a6cbb51623c8fa258629945cd55ca705cc4e66700396894e0c",
      "did:key:zQ3shokFTS3brHcDQrn82RUDfCZESWL1ZdCEJwekUDPQiYBme"
      "f0f4df55a2b3ff13051ea814a8f24ad00f2e469af73c363ac7e9fb999a9072ed",
      "did:key:zQ3shtxV1FrJfhqE1dvxYRcCknWNjHc3c5X1y3ZSoPDi2aur2"
      "6b0b91287ae3348f8c2f2552d766f30e3604867e34adc37ccbb74a8e6b893e02",
      "did:key:zQ3shZc2QzApp2oymGvQbzP8eKheVshBHbU4ZYjeXqwSKEn6N"
      "c0a6a7c560d37d7ba81ecee9543721ff48fea3e0fb827d42c1868226540fac15",
      "did:key:zQ3shadCps5JLAHcZiuX5YUtWHHL8ysBJqFLWvjZDKAWUBGzy"
      "175a232d440be1e0788f25488a73d9416c04b6f924bea6354bf05dd2f1a75133",
      "did:key:zQ3shptjE6JwdkeKN4fcpnYQY3m9Cet3NiHdAfpvSUZBFoKBj" ]

[<Tests>]
let interopK256Tests =
    testList
        "Multikey did:key K-256 (interop)"
        (k256TestVectors
         |> List.mapi (fun i (hex, expectedDid) ->
             testCase (sprintf "K-256 vector %d" (i + 1))
             <| fun () ->
                 let privBytes = Convert.FromHexString hex
                 let kp = Keys.importPrivateKey Algorithm.K256 privBytes
                 let did = Multikey.keyPairToDid kp
                 Expect.equal did expectedDid (sprintf "did:key matches vector %d" (i + 1))))

[<Tests>]
let interopP256Tests =
    testList
        "Multikey did:key P-256 (interop)"
        [ testCase "P-256 vector 1"
          <| fun () ->
              match Base58.decode p256PrivKeyBase58 with
              | Ok privBytes ->
                  let kp = Keys.importPrivateKey Algorithm.P256 privBytes
                  let did = Multikey.keyPairToDid kp
                  Expect.equal did p256ExpectedDid "did:key matches P-256 vector"
              | Error e -> failtest (sprintf "Base58 decode failed: %s" e) ]

[<Tests>]
let encodingTests =
    testList
        "Multikey encoding"
        [ testCase "P-256 round-trip did:key"
          <| fun () ->
              let kp = Keys.generate Algorithm.P256
              let did = Multikey.keyPairToDid kp
              Expect.stringStarts did "did:key:zDnae" "P-256 did:key prefix"

              match Multikey.decodeDid did with
              | Ok key ->
                  Expect.equal key.Algorithm Algorithm.P256 "algorithm preserved"
                  Expect.equal key.CompressedBytes kp.CompressedPublicKey "key bytes preserved"
              | Error e -> failtest e

          testCase "K-256 round-trip did:key"
          <| fun () ->
              let kp = Keys.generate Algorithm.K256
              let did = Multikey.keyPairToDid kp
              Expect.stringStarts did "did:key:zQ3sh" "K-256 did:key prefix"

              match Multikey.decodeDid did with
              | Ok key ->
                  Expect.equal key.Algorithm Algorithm.K256 "algorithm preserved"
                  Expect.equal key.CompressedBytes kp.CompressedPublicKey "key bytes preserved"
              | Error e -> failtest e

          testCase "P-256 round-trip multibase"
          <| fun () ->
              let kp = Keys.generate Algorithm.P256
              let pub = Keys.publicKey kp
              let multibase = Multikey.encodeMultibase pub

              match Multikey.decodeMultibase multibase with
              | Ok decoded ->
                  Expect.equal decoded.Algorithm Algorithm.P256 "algorithm"
                  Expect.equal decoded.CompressedBytes pub.CompressedBytes "key bytes"
              | Error e -> failtest e

          testCase "K-256 round-trip multibase"
          <| fun () ->
              let kp = Keys.generate Algorithm.K256
              let pub = Keys.publicKey kp
              let multibase = Multikey.encodeMultibase pub

              match Multikey.decodeMultibase multibase with
              | Ok decoded ->
                  Expect.equal decoded.Algorithm Algorithm.K256 "algorithm"
                  Expect.equal decoded.CompressedBytes pub.CompressedBytes "key bytes"
              | Error e -> failtest e

          testCase "decode invalid multibase prefix"
          <| fun () ->
              match Multikey.decodeMultibase "m123" with
              | Error _ -> ()
              | Ok _ -> failtest "Expected Error for non-z prefix"

          testCase "decode invalid did:key"
          <| fun () ->
              match Multikey.decodeDid "did:web:example.com" with
              | Error _ -> ()
              | Ok _ -> failtest "Expected Error for non-did:key"

          testCase "interop did:key encodes same key as raw multibase"
          <| fun () ->
              // From signature fixtures: publicKeyMultibase is raw compressed key (no multicodec)
              // while publicKeyDid uses did:key format (with multicodec prefix)
              let didKey = "did:key:zDnaembgSGUhZULN2Caob4HLJPaxBh92N7rtH21TErzqf8HQo"
              let rawMultibase = "zxdM8dSstjrpZaRUwBmDvjGXweKuEMVN95A9oJBFjkWMh"

              // Decode did:key (has multicodec prefix)
              match Multikey.decodeDid didKey with
              | Ok fromDid ->
                  Expect.equal fromDid.Algorithm Algorithm.P256 "P-256 algorithm"
                  // Decode raw multibase (no multicodec prefix, just base58btc of compressed key)
                  match Base58.decode (rawMultibase.Substring 1) with
                  | Ok rawBytes ->
                      Expect.equal fromDid.CompressedBytes rawBytes "same key bytes"
                  | Error e -> failtest (sprintf "base58 decode: %s" e)
              | Error e -> failtest (sprintf "did:key decode: %s" e) ]
