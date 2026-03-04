namespace FSharp.ATProto.OAuthServer

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading.Tasks

/// Server-side DPoP (Demonstration of Proof-of-Possession) proof validation.
/// Validates JWT proofs per RFC 9449, ensuring the client possesses the private key
/// corresponding to the public key in the proof header.
module DPoPValidator =

    /// Encode bytes as base64url (RFC 4648 section 5), without padding.
    let internal toBase64Url (bytes: byte array) : string =
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=')

    /// Decode a base64url string to bytes.
    let internal fromBase64Url (s: string) : byte array =
        let s = s.Replace('-', '+').Replace('_', '/')

        let padded =
            match s.Length % 4 with
            | 2 -> s + "=="
            | 3 -> s + "="
            | _ -> s

        Convert.FromBase64String(padded)

    /// Compute a JWK thumbprint (RFC 7638) from a JWK JSON string.
    /// The canonical form for EC keys uses alphabetically sorted members:
    /// {"crv":"...","kty":"EC","x":"...","y":"..."}
    let internal computeJwkThumbprint (jwkJson: string) : string =
        let doc = JsonDocument.Parse(jwkJson)
        let root = doc.RootElement
        let crv = root.GetProperty("crv").GetString()
        let kty = root.GetProperty("kty").GetString()
        let x = root.GetProperty("x").GetString()
        let y = root.GetProperty("y").GetString()
        // RFC 7638: members in lexicographic order for EC: crv, kty, x, y
        let canonical =
            sprintf """{"crv":"%s","kty":"%s","x":"%s","y":"%s"}""" crv kty x y

        let hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical))
        toBase64Url hash

    /// Generate a DPoP nonce using HMAC-SHA256 of a time bucket.
    /// The nonce rotates based on the given lifetime bucket.
    let internal generateNonce (secret: byte array) (now: DateTimeOffset) (lifetime: TimeSpan) : string =
        let bucket = now.ToUnixTimeSeconds() / int64 lifetime.TotalSeconds
        let bucketBytes = BitConverter.GetBytes(bucket)
        use hmac = new HMACSHA256(secret)
        let hash = hmac.ComputeHash(bucketBytes)
        toBase64Url hash

    /// Validate a DPoP nonce, accepting both the current and previous time bucket
    /// for smooth rotation.
    let internal validateNonce (secret: byte array) (lifetime: TimeSpan) (now: DateTimeOffset) (nonce: string) : bool =
        let currentNonce = generateNonce secret now lifetime
        let previousBucketTime = now - lifetime
        let previousNonce = generateNonce secret previousBucketTime lifetime
        nonce = currentNonce || nonce = previousNonce

    /// Try to get a string property from a JsonElement, returning None if missing or not a string.
    let private tryGetString (name: string) (element: JsonElement) : string option =
        match element.TryGetProperty(name) with
        | true, prop when prop.ValueKind = JsonValueKind.String -> Some(prop.GetString())
        | _ -> None

    /// Try to get an int64 property from a JsonElement, returning None if missing or not a number.
    let private tryGetInt64 (name: string) (element: JsonElement) : int64 option =
        match element.TryGetProperty(name) with
        | true, prop when prop.ValueKind = JsonValueKind.Number ->
            match prop.TryGetInt64() with
            | true, v -> Some v
            | _ -> None
        | _ -> None

    /// Normalize a URL to scheme+authority+path for comparison (ignoring query and fragment).
    let private normalizeUrl (url: string) =
        try
            let uri = Uri(url, UriKind.Absolute)
            sprintf "%s://%s%s" (uri.Scheme.ToLowerInvariant()) (uri.Authority.ToLowerInvariant()) uri.AbsolutePath
        with _ ->
            url.ToLowerInvariant()

    /// Verify the ES256 signature on a JWT.
    let private verifySignature (headerB64: string) (payloadB64: string) (signatureB64: string) (jwk: JsonElement) : Result<unit, string> =
        match tryGetString "x" jwk, tryGetString "y" jwk with
        | Some x, Some y ->
            try
                let xBytes = fromBase64Url x
                let yBytes = fromBase64Url y

                let mutable ecParams = ECParameters()
                ecParams.Curve <- ECCurve.NamedCurves.nistP256
                let mutable q = ECPoint()
                q.X <- xBytes
                q.Y <- yBytes
                ecParams.Q <- q

                use ecdsa = ECDsa.Create(ecParams)

                let signingInput = headerB64 + "." + payloadB64
                let signingInputBytes = Encoding.UTF8.GetBytes(signingInput)

                let signatureBytes =
                    try
                        Some(fromBase64Url signatureB64)
                    with _ ->
                        None

                match signatureBytes with
                | None -> Error "Invalid base64url in JWT signature"
                | Some sigBytes ->
                    let isValid =
                        try
                            ecdsa.VerifyData(signingInputBytes, sigBytes, HashAlgorithmName.SHA256)
                        with _ ->
                            false

                    if isValid then Ok()
                    else Error "Invalid signature"
            with ex ->
                Error(sprintf "Signature verification failed: %s" ex.Message)
        | _ ->
            Error "Missing 'x' or 'y' in JWK"

    /// Parse and verify a DPoP proof JWT.
    /// Returns Ok(jwkThumbprint) on success, or Error with a descriptive OAuthServerError.
    ///
    /// Parameters:
    /// - dpopHeader: The DPoP JWT string from the DPoP HTTP header
    /// - httpMethod: The HTTP method of the request (e.g., "POST")
    /// - httpUrl: The HTTP URL of the request
    /// - replayStore: Store for replay detection of jti values
    /// - expectedAccessTokenHash: If present, the ath claim must match
    /// - expectedNonce: If present, the nonce claim must match
    /// - now: Current time for freshness checks
    /// - maxAge: Maximum acceptable age for the proof
    let parseAndVerifyProof
        (dpopHeader: string)
        (httpMethod: string)
        (httpUrl: string)
        (replayStore: IReplayStore)
        (expectedAccessTokenHash: string option)
        (expectedNonce: string option)
        (now: DateTimeOffset)
        (maxAge: TimeSpan)
        : Task<Result<string, OAuthServerError>> =
        task {
            try
                // Step 1: Split JWT into 3 parts
                let parts = dpopHeader.Split('.')

                if parts.Length <> 3 then
                    return Error(OAuthServerError.InvalidDpopProof "Invalid JWT format: expected 3 dot-separated parts")
                else

                let headerB64 = parts.[0]
                let payloadB64 = parts.[1]
                let signatureB64 = parts.[2]

                // Step 2: Parse header
                let headerBytes =
                    try
                        Some(fromBase64Url headerB64)
                    with _ ->
                        None

                match headerBytes with
                | None ->
                    return Error(OAuthServerError.InvalidDpopProof "Invalid base64url in JWT header")
                | Some headerBytes ->

                let headerJson = Encoding.UTF8.GetString(headerBytes)
                let headerDoc = JsonDocument.Parse(headerJson)
                let header = headerDoc.RootElement

                // Verify typ
                let typError =
                    match tryGetString "typ" header with
                    | Some "dpop+jwt" -> None
                    | Some typ ->
                        Some(OAuthServerError.InvalidDpopProof(sprintf "Invalid typ: expected 'dpop+jwt', got '%s'" typ))
                    | None ->
                        Some(OAuthServerError.InvalidDpopProof "Missing 'typ' in JWT header")

                match typError with
                | Some e -> return Error e
                | None ->

                // Verify alg
                let algError =
                    match tryGetString "alg" header with
                    | Some "ES256" -> None
                    | Some alg ->
                        Some(OAuthServerError.InvalidDpopProof(sprintf "Unsupported algorithm: expected 'ES256', got '%s'" alg))
                    | None ->
                        Some(OAuthServerError.InvalidDpopProof "Missing 'alg' in JWT header")

                match algError with
                | Some e -> return Error e
                | None ->

                // Extract JWK
                let jwkElement =
                    match header.TryGetProperty("jwk") with
                    | true, jwk when jwk.ValueKind = JsonValueKind.Object -> Some jwk
                    | _ -> None

                match jwkElement with
                | None ->
                    return Error(OAuthServerError.InvalidDpopProof "Missing or invalid 'jwk' in JWT header")
                | Some jwk ->

                let jwkJson = jwk.GetRawText()

                // Step 3: Parse payload
                let payloadBytes =
                    try
                        Some(fromBase64Url payloadB64)
                    with _ ->
                        None

                match payloadBytes with
                | None ->
                    return Error(OAuthServerError.InvalidDpopProof "Invalid base64url in JWT payload")
                | Some payloadBytes ->

                let payloadJson = Encoding.UTF8.GetString(payloadBytes)
                let payloadDoc = JsonDocument.Parse(payloadJson)
                let payload = payloadDoc.RootElement

                let jti = tryGetString "jti" payload
                let htm = tryGetString "htm" payload
                let htu = tryGetString "htu" payload
                let iat = tryGetInt64 "iat" payload
                let ath = tryGetString "ath" payload
                let nonce = tryGetString "nonce" payload

                // Validate required claims
                match jti with
                | None ->
                    return Error(OAuthServerError.InvalidDpopProof "Missing 'jti' claim")
                | Some jti ->

                match htm with
                | None ->
                    return Error(OAuthServerError.InvalidDpopProof "Missing 'htm' claim")
                | Some htm ->

                match htu with
                | None ->
                    return Error(OAuthServerError.InvalidDpopProof "Missing 'htu' claim")
                | Some htu ->

                match iat with
                | None ->
                    return Error(OAuthServerError.InvalidDpopProof "Missing 'iat' claim")
                | Some iat ->

                // Step 4: Verify htm matches httpMethod (case-insensitive)
                if not (String.Equals(htm, httpMethod, StringComparison.OrdinalIgnoreCase)) then
                    return Error(OAuthServerError.InvalidDpopProof(sprintf "Method mismatch: expected '%s', got '%s'" httpMethod htm))
                else

                // Step 5: Verify htu matches httpUrl (scheme+authority+path, ignore query/fragment)
                if normalizeUrl htu <> normalizeUrl httpUrl then
                    return Error(OAuthServerError.InvalidDpopProof(sprintf "URL mismatch: expected '%s', got '%s'" httpUrl htu))
                else

                // Step 6: Verify iat freshness
                let issuedAt = DateTimeOffset.FromUnixTimeSeconds(iat)
                let age = now - issuedAt

                if age > maxAge then
                    return Error(OAuthServerError.InvalidDpopProof(sprintf "Proof too old: issued %d seconds ago, max age is %d seconds" (int64 age.TotalSeconds) (int64 maxAge.TotalSeconds)))
                elif age < TimeSpan.FromSeconds(-30.0) then
                    return Error(OAuthServerError.InvalidDpopProof "Proof issued in the future")
                else

                // Step 7: Verify jti uniqueness via replay store
                let jtiExpiry = now + maxAge
                let! isUnique = replayStore.IsUnique("dpop-jti", jti, jtiExpiry)

                if not isUnique then
                    return Error(OAuthServerError.InvalidDpopProof "Replay detected: duplicate jti")
                else

                // Step 8: Verify access token hash if expected
                let athError =
                    match expectedAccessTokenHash with
                    | Some expectedAth ->
                        match ath with
                        | Some actualAth when actualAth = expectedAth -> None
                        | Some _ ->
                            Some(OAuthServerError.InvalidDpopProof "Access token hash mismatch")
                        | None ->
                            Some(OAuthServerError.InvalidDpopProof "Missing 'ath' claim but access token hash was expected")
                    | None -> None

                match athError with
                | Some e -> return Error e
                | None ->

                // Step 9: Verify nonce if expected
                let nonceError =
                    match expectedNonce with
                    | Some expectedN ->
                        match nonce with
                        | Some actualN when actualN = expectedN -> None
                        | Some _ ->
                            Some(OAuthServerError.InvalidDpopProof "Nonce mismatch")
                        | None ->
                            Some(OAuthServerError.InvalidDpopProof "Missing 'nonce' claim but nonce was expected")
                    | None -> None

                match nonceError with
                | Some e -> return Error e
                | None ->

                // Step 10: Verify ES256 signature
                match verifySignature headerB64 payloadB64 signatureB64 jwk with
                | Error msg ->
                    return Error(OAuthServerError.InvalidDpopProof msg)
                | Ok () ->

                // Step 11: Compute and return JWK thumbprint
                let thumbprint = computeJwkThumbprint jwkJson
                return Ok thumbprint

            with ex ->
                return Error(OAuthServerError.InvalidDpopProof(sprintf "Failed to parse DPoP proof: %s" ex.Message))
        }
