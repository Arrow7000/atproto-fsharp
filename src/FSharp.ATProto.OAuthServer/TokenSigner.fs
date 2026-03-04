namespace FSharp.ATProto.OAuthServer

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open FSharp.ATProto.Syntax

/// Access token JWT creation and signing.
module TokenSigner =

    // ── Base64url helpers ──────────────────────────────────────────────

    /// Encode bytes as base64url (RFC 4648 section 5), without padding.
    let internal toBase64Url (bytes: byte[]) : string =
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=')

    /// Decode a base64url string to bytes.
    let internal fromBase64Url (s: string) : byte[] =
        let s = s.Replace('-', '+').Replace('_', '/')

        let padded =
            match s.Length % 4 with
            | 2 -> s + "=="
            | 3 -> s + "="
            | _ -> s

        Convert.FromBase64String(padded)

    /// Encode a UTF-8 string as base64url.
    let private stringToBase64Url (s: string) : string =
        toBase64Url (Encoding.UTF8.GetBytes(s))

    /// Generate a cryptographically random base64url string (32 random bytes).
    let internal generateRandomString () : string =
        let bytes = RandomNumberGenerator.GetBytes(32)
        toBase64Url bytes

    // ── Key management ─────────────────────────────────────────────────

    /// Create a new ES256 (P-256) key pair for server signing.
    let createSigningKey () : ECDsa =
        ECDsa.Create(ECCurve.NamedCurves.nistP256)

    /// Compute a JWK thumbprint (RFC 7638) of an EC public key using SHA-256.
    /// The canonical form for EC keys is: {"crv":"P-256","kty":"EC","x":"...","y":"..."}
    let private computeJwkThumbprint (key: ECDsa) : string =
        let parameters = key.ExportParameters(false)
        let x = toBase64Url parameters.Q.X
        let y = toBase64Url parameters.Q.Y
        let canonical = sprintf """{"crv":"P-256","kty":"EC","x":"%s","y":"%s"}""" x y
        let hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical))
        toBase64Url hash

    /// Export the public key of an ECDsa key pair as a JWK JSON string.
    /// Includes kty, crv, x, y, kid (JWK thumbprint), use, and alg fields.
    let exportPublicJwk (key: ECDsa) : string =
        let parameters = key.ExportParameters(false)
        let x = toBase64Url parameters.Q.X
        let y = toBase64Url parameters.Q.Y
        let kid = computeJwkThumbprint key

        let ms = new MemoryStream()
        use writer = new Utf8JsonWriter(ms)
        writer.WriteStartObject()
        writer.WriteString("kty", "EC")
        writer.WriteString("crv", "P-256")
        writer.WriteString("x", x)
        writer.WriteString("y", y)
        writer.WriteString("kid", kid)
        writer.WriteString("use", "sig")
        writer.WriteString("alg", "ES256")
        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(ms.ToArray())

    /// Export public key as JWKS (JWK Set): {"keys":[<jwk>]}.
    let exportJwks (key: ECDsa) : string =
        let jwk = exportPublicJwk key

        let ms = new MemoryStream()
        use writer = new Utf8JsonWriter(ms)
        writer.WriteStartObject()
        writer.WritePropertyName("keys")
        writer.WriteStartArray()
        let jwkDoc = JsonDocument.Parse(jwk)
        jwkDoc.RootElement.WriteTo(writer)
        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(ms.ToArray())

    /// Create a signing function from an ECDsa key.
    /// Signs data with SHA-256 and returns the signature bytes.
    let makeSigningFunction (key: ECDsa) : (byte[] -> byte[]) =
        fun data -> key.SignData(data, HashAlgorithmName.SHA256)

    // ── Token creation ─────────────────────────────────────────────────

    /// Create a signed access token JWT (at+jwt).
    /// Returns a compact JWS string (header.payload.signature).
    let createAccessToken
        (config: OAuthServerConfig)
        (sub: Did)
        (clientId: string)
        (scope: string)
        (dpopJkt: string)
        (issuedAt: DateTimeOffset)
        : string =

        // Extract kid from config's public JWK
        let kid =
            let doc = JsonDocument.Parse(config.PublicKeyJwk)
            doc.RootElement.GetProperty("kid").GetString()

        // Build header
        let header =
            let ms = new MemoryStream()
            use writer = new Utf8JsonWriter(ms)
            writer.WriteStartObject()
            writer.WriteString("typ", "at+jwt")
            writer.WriteString("alg", "ES256")
            writer.WriteString("kid", kid)
            writer.WriteEndObject()
            writer.Flush()
            stringToBase64Url (Encoding.UTF8.GetString(ms.ToArray()))

        // Build payload
        let iat = issuedAt.ToUnixTimeSeconds()
        let exp = issuedAt.Add(config.AccessTokenLifetime).ToUnixTimeSeconds()
        let jti = generateRandomString ()

        let payload =
            let ms = new MemoryStream()
            use writer = new Utf8JsonWriter(ms)
            writer.WriteStartObject()
            writer.WriteString("iss", config.Issuer)
            writer.WriteString("sub", Did.value sub)
            writer.WriteString("aud", config.Issuer)
            writer.WriteNumber("exp", exp)
            writer.WriteNumber("iat", iat)
            writer.WriteString("jti", jti)
            writer.WriteString("scope", scope)
            writer.WriteString("client_id", clientId)
            writer.WritePropertyName("cnf")
            writer.WriteStartObject()
            writer.WriteString("jkt", dpopJkt)
            writer.WriteEndObject()
            writer.WriteEndObject()
            writer.Flush()
            stringToBase64Url (Encoding.UTF8.GetString(ms.ToArray()))

        // Sign
        let signingInput = header + "." + payload
        let signatureBytes = config.SigningKey(Encoding.UTF8.GetBytes(signingInput))
        let signature = toBase64Url signatureBytes

        signingInput + "." + signature

    /// Generate an opaque refresh token (cryptographically random string).
    let createRefreshToken () : string = generateRandomString ()

    /// Extract the kid from an access token JWT header without verifying signature.
    /// Returns None if the token is malformed or the kid field is missing.
    let parseAccessTokenKid (jwt: string) : string option =
        let parts = jwt.Split('.')

        if parts.Length < 2 then
            None
        else
            try
                let headerJson = Encoding.UTF8.GetString(fromBase64Url parts.[0])
                let doc = JsonDocument.Parse(headerJson)
                let mutable kidElem = Unchecked.defaultof<JsonElement>

                if doc.RootElement.TryGetProperty("kid", &kidElem) then
                    Some(kidElem.GetString())
                else
                    None
            with _ ->
                None
