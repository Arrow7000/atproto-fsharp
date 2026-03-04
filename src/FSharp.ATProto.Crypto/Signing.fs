namespace FSharp.ATProto.Crypto

open System
open System.Security.Cryptography
open Org.BouncyCastle.Crypto.Parameters
open Org.BouncyCastle.Crypto.Signers
open Org.BouncyCastle.Math

/// ECDSA signing and verification with low-S normalization as required by AT Protocol.
/// All signatures use compact format (64 bytes: r || s, 32 bytes each).
module Signing =

    /// P-256 curve order
    let private p256Order =
        BigInteger ("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551", 16)

    /// secp256k1 curve order
    let private k256Order =
        BigInteger ("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141", 16)

    let private halfOrder (order : BigInteger) = order.ShiftRight 1

    /// Check if the S component of a signature is low (s <= order/2).
    let internal isLowS (algorithm : Algorithm) (s : byte[]) : bool =
        let order =
            match algorithm with
            | Algorithm.P256 -> p256Order
            | Algorithm.K256 -> k256Order

        let sVal = BigInteger (1, s)
        sVal.CompareTo (halfOrder order) <= 0

    /// Normalize S to low-S form if needed. Returns the normalized S bytes (32 bytes).
    let internal normalizeLowS (algorithm : Algorithm) (s : byte[]) : byte[] =
        let order =
            match algorithm with
            | Algorithm.P256 -> p256Order
            | Algorithm.K256 -> k256Order

        let sVal = BigInteger (1, s)

        if sVal.CompareTo (halfOrder order) > 0 then
            let normalized = order.Subtract(sVal).ToByteArrayUnsigned ()

            if normalized.Length < 32 then
                Array.append (Array.zeroCreate (32 - normalized.Length)) normalized
            else
                normalized
        else
            s

    /// Parse a 64-byte compact signature into (r, s) each 32 bytes.
    let internal parseCompact (signature : byte[]) : Result<byte[] * byte[], string> =
        if signature.Length <> 64 then
            Error (sprintf "Expected 64-byte compact signature, got %d bytes" signature.Length)
        else
            Ok (signature.[..31], signature.[32..])

    /// Pad a BigInteger byte array to exactly 32 bytes.
    let private padTo32 (bytes : byte[]) : byte[] =
        if bytes.Length < 32 then
            Array.append (Array.zeroCreate (32 - bytes.Length)) bytes
        else
            bytes

    /// Sign data with a key pair. Returns a 64-byte compact signature with low-S.
    /// The data is hashed with SHA-256 internally.
    let sign (keyPair : KeyPair) (data : byte[]) : byte[] =
        match keyPair.Algorithm with
        | Algorithm.P256 ->
            let uncompressed = Keys.decompress (Keys.publicKey keyPair)
            let x = uncompressed.[1..32]
            let y = uncompressed.[33..64]

            let parameters =
                ECParameters (
                    Curve = ECCurve.NamedCurves.nistP256,
                    D = keyPair.PrivateKeyBytes,
                    Q = ECPoint (X = x, Y = y)
                )

            let ecdsa = ECDsa.Create parameters

            let rawSig =
                ecdsa.SignData (data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)

            let r = rawSig.[..31]
            let s = rawSig.[32..]
            let normalizedS = normalizeLowS Algorithm.P256 s
            Array.append r normalizedS

        | Algorithm.K256 ->
            let d = BigInteger (1, keyPair.PrivateKeyBytes)
            let privParams = ECPrivateKeyParameters (d, Keys.k256Params)

            let signer =
                ECDsaSigner (HMacDsaKCalculator (Org.BouncyCastle.Crypto.Digests.Sha256Digest ()))

            signer.Init (true, privParams)
            let hash = SHA256.HashData data
            let components = signer.GenerateSignature hash
            let rBig = components.[0]
            let sBig = components.[1]
            // Normalize S
            let half = halfOrder k256Order

            let normalizedS =
                if sBig.CompareTo half > 0 then
                    k256Order.Subtract sBig
                else
                    sBig

            let rBytes = padTo32 (rBig.ToByteArrayUnsigned ())
            let sBytes = padTo32 (normalizedS.ToByteArrayUnsigned ())
            Array.append rBytes sBytes

    /// Verify a compact signature against data and a public key.
    /// Enforces low-S requirement. Returns false for high-S or DER-encoded signatures.
    let verify (publicKey : PublicKey) (data : byte[]) (signature : byte[]) : bool =
        match parseCompact signature with
        | Error _ -> false
        | Ok (r, s) ->
            // Reject high-S signatures
            if not (isLowS publicKey.Algorithm s) then
                false
            else
                match publicKey.Algorithm with
                | Algorithm.P256 ->
                    try
                        let uncompressed = Keys.decompress publicKey
                        let x = uncompressed.[1..32]
                        let y = uncompressed.[33..64]

                        let parameters =
                            ECParameters (Curve = ECCurve.NamedCurves.nistP256, Q = ECPoint (X = x, Y = y))

                        let ecdsa = ECDsa.Create parameters

                        ecdsa.VerifyData (
                            data,
                            signature,
                            HashAlgorithmName.SHA256,
                            DSASignatureFormat.IeeeP1363FixedFieldConcatenation
                        )
                    with _ ->
                        false

                | Algorithm.K256 ->
                    try
                        let point = Keys.k256Params.Curve.DecodePoint publicKey.CompressedBytes
                        let pubParams = ECPublicKeyParameters (point, Keys.k256Params)
                        let signer = ECDsaSigner ()
                        signer.Init (false, pubParams)
                        let hash = SHA256.HashData data
                        let rBig = BigInteger (1, r)
                        let sBig = BigInteger (1, s)
                        signer.VerifySignature (hash, rBig, sBig)
                    with _ ->
                        false
