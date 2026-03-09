(**
---
title: Cryptography
category: Infrastructure
categoryindex: 5
index: 25
description: P-256 and K-256 key management, ECDSA signing, and did:key encoding
keywords: fsharp, atproto, crypto, ecdsa, p256, k256, signing, did-key, multikey
---

# Cryptography

The AT Protocol uses ECDSA with two elliptic curves: **P-256** (NIST) and **K-256** (secp256k1). The `FSharp.ATProto.Crypto` package handles key generation, signing, verification, and encoding to the `did:key` format used in DID documents.

P-256 uses the native .NET `ECDsa` API. K-256 uses BouncyCastle, since .NET does not support secp256k1 natively.

## Algorithm

The `Algorithm` discriminated union selects a curve:
*)

(*** hide ***)
#nowarn "20"
#r "../../src/FSharp.ATProto.Syntax/bin/Release/net10.0/FSharp.ATProto.Syntax.dll"
#r "../../src/FSharp.ATProto.DRISL/bin/Release/net10.0/FSharp.ATProto.DRISL.dll"
#r "../../src/FSharp.ATProto.Crypto/bin/Release/net10.0/FSharp.ATProto.Crypto.dll"
open FSharp.ATProto.Syntax
open FSharp.ATProto.Crypto
(***)

type Algorithm = P256 | K256

(**
All functions that deal with keys or signatures take an `Algorithm` to identify the curve. Use `Algorithm.P256` for P-256 and `Algorithm.K256` for secp256k1.

## Key Types
*)

type PublicKey =
    { Algorithm : Algorithm
      CompressedBytes : byte[] }  // 33 bytes: 0x02/0x03 prefix + 32-byte X

type KeyPair =
    { Algorithm : Algorithm
      PrivateKeyBytes : byte[]        // 32 bytes
      CompressedPublicKey : byte[] }  // 33 bytes

(**
## Key Management

The `Keys` module provides generation, import, and decompression:
*)

(*** hide ***)
let somePrivateBytes = Unchecked.defaultof<byte[]>
let compressedBytes = Unchecked.defaultof<byte[]>
(***)

open FSharp.ATProto.Crypto

// Generate a new key pair
let keyPair = Keys.generate Algorithm.P256

// Import from raw private key bytes (32 bytes)
let imported = Keys.importPrivateKey Algorithm.K256 somePrivateBytes

// Import a compressed public key (33 bytes) -- returns Result
let pubKey = Keys.importPublicKey Algorithm.P256 compressedBytes

// Decompress to 65-byte uncompressed form (0x04 + X + Y)
let uncompressed = Keys.decompress pubKey

// Extract the public key from a key pair
let pub = Keys.publicKey keyPair

(**
## Signing and Verification

The `Signing` module produces 64-byte compact ECDSA signatures (r || s, 32 bytes each) with **low-S normalization** as required by the AT Protocol. Data is hashed with SHA-256 internally.
*)

(*** hide ***)
let keyPair2 = Keys.generate Algorithm.P256
(***)

let data = System.Text.Encoding.UTF8.GetBytes "hello"

// Sign -- returns 64-byte compact signature
let signature = Signing.sign keyPair2 data

// Verify -- rejects high-S signatures
let isValid = Signing.verify (Keys.publicKey keyPair2) data signature
// true

(**
`Signing.verify` returns `false` for invalid, high-S, or DER-encoded signatures. It never throws.

## Multikey and did:key Encoding

The `Multikey` module encodes and decodes public keys in the `did:key` format used by AT Protocol DID documents. Keys are serialized as base58btc with a multicodec prefix (P-256: `0x1200`, K-256: `0xe7`).
*)

(*** hide ***)
let keyPair3 = Keys.generate Algorithm.P256
(***)

// Encode as did:key (e.g. "did:key:zDnae...")
let didKey = Multikey.keyPairToDid keyPair3

// Encode just the multibase portion (e.g. "zDnae...")
let multibase = Multikey.encodeMultibase (Keys.publicKey keyPair3)

// Decode a did:key string back to a PublicKey
match Multikey.decodeDid didKey with
| Ok pubKey -> printfn "Algorithm: %A" pubKey.Algorithm
| Error msg -> printfn "Invalid did:key: %s" msg

// Decode a multibase string
match Multikey.decodeMultibase multibase with
| Ok pubKey -> printfn "Got key with %d bytes" pubKey.CompressedBytes.Length
| Error msg -> printfn "Decode failed: %s" msg

(**
## Base58

The `Base58` module provides standalone base58btc encoding and decoding using the Bitcoin alphabet. This is used internally by `Multikey` but is available for direct use if needed.
*)

let encoded = Base58.encode [| 0uy; 1uy; 2uy |]
// "12c"

match Base58.decode encoded with
| Ok bytes -> printfn "Decoded %d bytes" bytes.Length
| Error msg -> printfn "Invalid base58: %s" msg
