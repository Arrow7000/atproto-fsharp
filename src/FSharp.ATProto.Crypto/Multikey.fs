namespace FSharp.ATProto.Crypto

open System

/// Multicodec and did:key encoding/decoding for AT Protocol public keys.
/// Supports P-256 (multicodec 0x1200) and secp256k1 (multicodec 0xe7).
module Multikey =

    // Multicodec varint prefixes (unsigned LEB128)
    // P-256: 0x1200 -> varint bytes [0x80, 0x24]
    // secp256k1: 0xe7 -> varint bytes [0xe7, 0x01]
    let private p256Prefix = [| 0x80uy; 0x24uy |]
    let private k256Prefix = [| 0xe7uy; 0x01uy |]

    /// Encode a public key to multibase (base58btc, 'z' prefix) format.
    /// This is the format used in DID document verificationMethod publicKeyMultibase fields.
    let encodeMultibase (key : PublicKey) : string =
        let prefix =
            match key.Algorithm with
            | Algorithm.P256 -> p256Prefix
            | Algorithm.K256 -> k256Prefix
        let payload = Array.append prefix key.CompressedBytes
        "z" + Base58.encode payload

    /// Decode a multibase-encoded public key string.
    /// Expects 'z' prefix (base58btc) followed by multicodec prefix + compressed key.
    let decodeMultibase (multibase : string) : Result<PublicKey, string> =
        if String.IsNullOrEmpty multibase || multibase.[0] <> 'z' then
            Error "Expected multibase string with 'z' (base58btc) prefix"
        else
            match Base58.decode (multibase.Substring 1) with
            | Error e -> Error (sprintf "Base58 decode failed: %s" e)
            | Ok bytes ->
                if bytes.Length < 2 then
                    Error "Multikey payload too short"
                elif bytes.[0] = p256Prefix.[0] && bytes.[1] = p256Prefix.[1] then
                    let keyBytes = bytes.[2..]
                    Keys.importPublicKey Algorithm.P256 keyBytes
                elif bytes.[0] = k256Prefix.[0] && bytes.[1] = k256Prefix.[1] then
                    let keyBytes = bytes.[2..]
                    Keys.importPublicKey Algorithm.K256 keyBytes
                else
                    Error (sprintf "Unknown multicodec prefix: 0x%02x 0x%02x" bytes.[0] bytes.[1])

    /// Encode a public key as a did:key string.
    /// Format: "did:key:" + "z" + base58btc(multicodec_prefix + compressed_public_key)
    let encodeDid (key : PublicKey) : string =
        "did:key:" + encodeMultibase key

    /// Decode a did:key string to a public key.
    let decodeDid (didKey : string) : Result<PublicKey, string> =
        if not (didKey.StartsWith "did:key:z") then
            Error "Expected did:key: with z-prefixed multibase"
        else
            let multibase = didKey.Substring 8 // skip "did:key:"
            decodeMultibase multibase

    /// Format a key pair's public key as a did:key string.
    let keyPairToDid (keyPair : KeyPair) : string =
        encodeDid (Keys.publicKey keyPair)
