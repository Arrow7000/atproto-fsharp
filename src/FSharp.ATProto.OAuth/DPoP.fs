namespace FSharp.ATProto.OAuth

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json

/// DPoP (Demonstration of Proof-of-Possession) proof generation.
/// Creates JWT proofs that bind access tokens to a specific key pair,
/// as specified in RFC 9449.
module DPoP =

    /// Encode bytes as base64url (RFC 4648 section 5), without padding.
    let internal toBase64Url (bytes: byte array) : string =
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=')

    /// Encode a UTF-8 string as base64url.
    let internal stringToBase64Url (s: string) : string =
        toBase64Url (Encoding.UTF8.GetBytes(s))

    /// Decode a base64url string to bytes.
    let internal fromBase64Url (s: string) : byte array =
        let s = s.Replace('-', '+').Replace('_', '/')

        let padded =
            match s.Length % 4 with
            | 2 -> s + "=="
            | 3 -> s + "="
            | _ -> s

        Convert.FromBase64String(padded)

    /// Export the public key of an ECDsa key pair as a JWK JSON object string.
    let internal exportPublicJwk (key: ECDsa) : string =
        let parameters = key.ExportParameters(false)
        let x = toBase64Url parameters.Q.X
        let y = toBase64Url parameters.Q.Y

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

    /// Compute a JWK thumbprint (RFC 7638) of an EC public key using SHA-256.
    /// The canonical form for EC keys is: {"crv":"P-256","kty":"EC","x":"...","y":"..."}
    let internal computeJwkThumbprint (key: ECDsa) : string =
        let parameters = key.ExportParameters(false)
        let x = toBase64Url parameters.Q.X
        let y = toBase64Url parameters.Q.Y
        // RFC 7638: members in lexicographic order for EC: crv, kty, x, y
        let canonical =
            sprintf """{"crv":"P-256","kty":"EC","x":"%s","y":"%s"}""" x y

        let hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical))
        toBase64Url hash

    /// Generate a new ES256 key pair for DPoP.
    let generateKeyPair () : ECDsa =
        let key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
        key

    /// Generate PKCE code verifier and S256 challenge (RFC 7636).
    let generatePkce () : PkceChallenge =
        let bytes = RandomNumberGenerator.GetBytes(32)
        let verifier = toBase64Url bytes
        let challengeHash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier))
        let challenge = toBase64Url challengeHash

        { Verifier = verifier
          Challenge = challenge
          Method = "S256" }

    /// Generate a random string suitable for use as a JWT ID (jti) or OAuth state parameter.
    let internal generateRandomString () : string =
        let bytes = RandomNumberGenerator.GetBytes(32)
        toBase64Url bytes

    /// Create a DPoP proof JWT for a request.
    /// The proof binds the request to the key pair and optionally to an access token.
    let createProof
        (key: ECDsa)
        (httpMethod: string)
        (targetUri: string)
        (accessTokenHash: string option)
        (nonce: string option)
        : string =

        // Build header
        let jwk = exportPublicJwk key

        let header =
            let writer = new System.IO.MemoryStream()
            use jsonWriter = new Utf8JsonWriter(writer)
            jsonWriter.WriteStartObject()
            jsonWriter.WriteString("typ", "dpop+jwt")
            jsonWriter.WriteString("alg", "ES256")
            jsonWriter.WritePropertyName("jwk")
            // Write the JWK as a raw JSON value
            let jwkDoc = JsonDocument.Parse(jwk)
            jwkDoc.RootElement.WriteTo(jsonWriter)
            jsonWriter.WriteEndObject()
            jsonWriter.Flush()
            stringToBase64Url (Encoding.UTF8.GetString(writer.ToArray()))

        // Build payload
        let jti = generateRandomString ()
        let iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds()

        let payload =
            let writer = new System.IO.MemoryStream()
            use jsonWriter = new Utf8JsonWriter(writer)
            jsonWriter.WriteStartObject()
            jsonWriter.WriteString("jti", jti)
            jsonWriter.WriteString("htm", httpMethod)
            jsonWriter.WriteString("htu", targetUri)
            jsonWriter.WriteNumber("iat", iat)

            match accessTokenHash with
            | Some ath -> jsonWriter.WriteString("ath", ath)
            | None -> ()

            match nonce with
            | Some n -> jsonWriter.WriteString("nonce", n)
            | None -> ()

            jsonWriter.WriteEndObject()
            jsonWriter.Flush()
            stringToBase64Url (Encoding.UTF8.GetString(writer.ToArray()))

        // Sign
        let signingInput = header + "." + payload
        let signature = key.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256)
        let sig64 = toBase64Url signature

        signingInput + "." + sig64

    /// Compute the base64url-encoded SHA-256 hash of an access token,
    /// for use in the "ath" claim of a DPoP proof.
    let hashAccessToken (accessToken: string) : string =
        let hash = SHA256.HashData(Encoding.ASCII.GetBytes(accessToken))
        toBase64Url hash
