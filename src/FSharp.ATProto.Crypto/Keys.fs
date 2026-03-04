namespace FSharp.ATProto.Crypto

open System
open System.Security.Cryptography
open Org.BouncyCastle.Asn1.X9
open Org.BouncyCastle.Crypto.EC
open Org.BouncyCastle.Crypto.Parameters
open Org.BouncyCastle.Crypto.Generators
open Org.BouncyCastle.Math
open Org.BouncyCastle.Security

/// The elliptic curve algorithm used for a key.
[<RequireQualifiedAccess>]
type Algorithm =
    | P256
    | K256

/// A public key for either P-256 or secp256k1.
type PublicKey =
    { Algorithm : Algorithm
      /// Compressed public key bytes (33 bytes: 0x02/0x03 prefix + 32-byte X).
      CompressedBytes : byte[] }

/// A key pair for either P-256 or secp256k1.
type KeyPair =
    { Algorithm : Algorithm
      /// Private key scalar (32 bytes).
      PrivateKeyBytes : byte[]
      /// Compressed public key bytes (33 bytes).
      CompressedPublicKey : byte[] }

/// Key management for P-256 and secp256k1 curves.
module Keys =

    // BouncyCastle curve parameters for secp256k1
    let internal k256Params =
        let curve = CustomNamedCurves.GetByName "secp256k1"
        ECDomainParameters (curve.Curve, curve.G, curve.N, curve.H)

    let internal p256Params =
        let curve = CustomNamedCurves.GetByName "P-256"
        ECDomainParameters (curve.Curve, curve.G, curve.N, curve.H)

    /// Generate a new key pair for the given algorithm.
    let generate (algorithm : Algorithm) : KeyPair =
        match algorithm with
        | Algorithm.P256 ->
            let ecdsa = ECDsa.Create (ECCurve.NamedCurves.nistP256)
            let parameters = ecdsa.ExportParameters true
            let x = parameters.Q.X
            let y = parameters.Q.Y
            // Compress: 0x02 if Y is even, 0x03 if Y is odd
            let prefix = if y.[y.Length - 1] % 2uy = 0uy then 0x02uy else 0x03uy
            let compressed = Array.concat [ [| prefix |]; x ]
            { Algorithm = Algorithm.P256
              PrivateKeyBytes = parameters.D
              CompressedPublicKey = compressed }

        | Algorithm.K256 ->
            let gen = ECKeyPairGenerator ()
            let random = SecureRandom ()
            gen.Init (ECKeyGenerationParameters (k256Params, random))
            let pair = gen.GenerateKeyPair ()
            let priv = pair.Private :?> ECPrivateKeyParameters
            let pub = pair.Public :?> ECPublicKeyParameters
            let compressed = pub.Q.GetEncoded true
            let dBytes = priv.D.ToByteArrayUnsigned ()
            // Pad to 32 bytes if needed
            let privateKey =
                if dBytes.Length < 32 then
                    Array.append (Array.zeroCreate (32 - dBytes.Length)) dBytes
                else
                    dBytes
            { Algorithm = Algorithm.K256
              PrivateKeyBytes = privateKey
              CompressedPublicKey = compressed }

    /// Import a private key from raw bytes.
    let importPrivateKey (algorithm : Algorithm) (privateKeyBytes : byte[]) : KeyPair =
        match algorithm with
        | Algorithm.P256 ->
            let parameters = ECParameters (Curve = ECCurve.NamedCurves.nistP256, D = privateKeyBytes)
            let ecdsa = ECDsa.Create parameters
            let full = ecdsa.ExportParameters false
            let x = full.Q.X
            let y = full.Q.Y
            let prefix = if y.[y.Length - 1] % 2uy = 0uy then 0x02uy else 0x03uy
            let compressed = Array.concat [ [| prefix |]; x ]
            { Algorithm = Algorithm.P256
              PrivateKeyBytes = privateKeyBytes
              CompressedPublicKey = compressed }

        | Algorithm.K256 ->
            let d = BigInteger (1, privateKeyBytes)
            let pub = k256Params.G.Multiply(d).Normalize ()
            let compressed = pub.GetEncoded true
            { Algorithm = Algorithm.K256
              PrivateKeyBytes = privateKeyBytes
              CompressedPublicKey = compressed }

    /// Import a compressed public key (33 bytes).
    let importPublicKey (algorithm : Algorithm) (compressedBytes : byte[]) : Result<PublicKey, string> =
        if compressedBytes.Length <> 33 then
            Error (sprintf "Expected 33-byte compressed public key, got %d bytes" compressedBytes.Length)
        elif compressedBytes.[0] <> 0x02uy && compressedBytes.[0] <> 0x03uy then
            Error (sprintf "Invalid compressed key prefix: 0x%02x" compressedBytes.[0])
        else
            Ok { Algorithm = algorithm; CompressedBytes = compressedBytes }

    /// Decompress a 33-byte compressed key to 65-byte uncompressed (0x04 + X + Y).
    let decompress (key : PublicKey) : byte[] =
        match key.Algorithm with
        | Algorithm.P256 ->
            let curve = CustomNamedCurves.GetByName "P-256"
            let point = curve.Curve.DecodePoint key.CompressedBytes
            point.GetEncoded false
        | Algorithm.K256 ->
            let point = k256Params.Curve.DecodePoint key.CompressedBytes
            point.GetEncoded false

    /// Get the public key from a key pair.
    let publicKey (keyPair : KeyPair) : PublicKey =
        { Algorithm = keyPair.Algorithm
          CompressedBytes = keyPair.CompressedPublicKey }
