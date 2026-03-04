namespace FSharp.ATProto.Core

open System
open System.Text
open System.Text.Json
open FSharp.ATProto.Syntax

/// Service-to-service JWT authentication for AT Protocol backend services
/// (labelers, feed generators, etc.).
module ServiceAuth =

    /// JWT signing algorithm.
    [<RequireQualifiedAccess>]
    type Algorithm =
        | ES256
        | ES256K

    /// Claims in a service auth JWT.
    type Claims =
        { Iss : Did
          Aud : Did
          Lxm : Nsid option
          Exp : DateTimeOffset
          Iat : DateTimeOffset }

    let private base64UrlEncode (bytes : byte[]) : string =
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')

    let private base64UrlDecode (s : string) : Result<byte[], string> =
        try
            let s = s.Replace('-', '+').Replace('_', '/')

            let padded =
                match s.Length % 4 with
                | 2 -> s + "=="
                | 3 -> s + "="
                | _ -> s

            Ok (Convert.FromBase64String padded)
        with ex ->
            Error (sprintf "Base64url decode error: %s" ex.Message)

    let private encodeHeader (alg : Algorithm) : string =
        let algName =
            match alg with
            | Algorithm.ES256 -> "ES256"
            | Algorithm.ES256K -> "ES256K"

        sprintf """{"alg":"%s","typ":"JWT"}""" algName

    let private encodeClaims (claims : Claims) : string =
        use ms = new IO.MemoryStream ()
        use writer = new Utf8JsonWriter (ms)
        writer.WriteStartObject ()
        writer.WriteString ("iss", Did.value claims.Iss)
        writer.WriteString ("aud", Did.value claims.Aud)
        writer.WriteNumber ("exp", claims.Exp.ToUnixTimeSeconds ())
        writer.WriteNumber ("iat", claims.Iat.ToUnixTimeSeconds ())

        match claims.Lxm with
        | Some nsid -> writer.WriteString ("lxm", Nsid.value nsid)
        | None -> ()

        writer.WriteEndObject ()
        writer.Flush ()
        Encoding.UTF8.GetString (ms.ToArray ())

    /// Create a service auth JWT token.
    /// The sign function should produce a 64-byte compact ECDSA signature over the input bytes.
    let createToken (alg : Algorithm) (sign : byte[] -> byte[]) (claims : Claims) : string =
        let header = encodeHeader alg |> Encoding.UTF8.GetBytes |> base64UrlEncode
        let payload = encodeClaims claims |> Encoding.UTF8.GetBytes |> base64UrlEncode
        let signingInput = sprintf "%s.%s" header payload
        let signature = sign (Encoding.UTF8.GetBytes signingInput) |> base64UrlEncode
        sprintf "%s.%s.%s" header payload signature

    /// Create a service auth JWT with default timing (issued now, expires in 60 seconds).
    let createTokenNow
        (alg : Algorithm)
        (sign : byte[] -> byte[])
        (iss : Did)
        (aud : Did)
        (lxm : Nsid option)
        : string =
        let now = DateTimeOffset.UtcNow

        createToken
            alg
            sign
            { Iss = iss
              Aud = aud
              Lxm = lxm
              Exp = now.AddSeconds 60.0
              Iat = now }

    /// Parse JWT claims without verifying the signature.
    let parseClaims (token : string) : Result<Claims * Algorithm, string> =
        let parts = token.Split '.'

        if parts.Length <> 3 then
            Error "Invalid JWT: expected 3 parts"
        else
            match base64UrlDecode parts.[0], base64UrlDecode parts.[1] with
            | Error e, _ -> Error (sprintf "Header decode: %s" e)
            | _, Error e -> Error (sprintf "Payload decode: %s" e)
            | Ok headerBytes, Ok payloadBytes ->
                try
                    let header = JsonDocument.Parse (headerBytes)
                    let payload = JsonDocument.Parse (payloadBytes)

                    let alg =
                        match header.RootElement.GetProperty("alg").GetString () with
                        | "ES256" -> Algorithm.ES256
                        | "ES256K" -> Algorithm.ES256K
                        | a -> failwith (sprintf "Unsupported algorithm: %s" a)

                    let iss =
                        match Did.parse (payload.RootElement.GetProperty("iss").GetString ()) with
                        | Ok d -> d
                        | Error e -> failwith (sprintf "Invalid iss: %s" e)

                    let aud =
                        match Did.parse (payload.RootElement.GetProperty("aud").GetString ()) with
                        | Ok d -> d
                        | Error e -> failwith (sprintf "Invalid aud: %s" e)

                    let exp =
                        DateTimeOffset.FromUnixTimeSeconds (payload.RootElement.GetProperty("exp").GetInt64 ())

                    let iat =
                        DateTimeOffset.FromUnixTimeSeconds (payload.RootElement.GetProperty("iat").GetInt64 ())

                    let lxm =
                        match
                            payload.RootElement.TryGetProperty "lxm"
                            |> fun (ok, v) -> if ok then Some (v.GetString ()) else None
                        with
                        | Some s ->
                            match Nsid.parse s with
                            | Ok nsid -> Some nsid
                            | Error e -> failwith (sprintf "Invalid lxm: %s" e)
                        | None -> None

                    Ok (
                        { Iss = iss
                          Aud = aud
                          Lxm = lxm
                          Exp = exp
                          Iat = iat },
                        alg
                    )
                with ex ->
                    Error (sprintf "JWT parse error: %s" ex.Message)

    /// Validate a service auth JWT: verify signature, check expiry.
    /// The verify function should check a 64-byte compact ECDSA signature against data.
    let validateToken
        (verify : byte[] -> byte[] -> bool)
        (token : string)
        : Result<Claims, string> =
        let parts = token.Split '.'

        if parts.Length <> 3 then
            Error "Invalid JWT: expected 3 parts"
        else
            match base64UrlDecode parts.[2] with
            | Error e -> Error (sprintf "Signature decode: %s" e)
            | Ok sigBytes ->
                let signingInput = sprintf "%s.%s" parts.[0] parts.[1]

                if not (verify (Encoding.UTF8.GetBytes signingInput) sigBytes) then
                    Error "Invalid signature"
                else
                    match parseClaims token with
                    | Error e -> Error e
                    | Ok (claims, _) ->
                        if claims.Exp < DateTimeOffset.UtcNow then
                            Error "Token expired"
                        else
                            Ok claims

    /// Configure an AtpAgent to use service auth for requests.
    /// The sign function should produce a 64-byte compact ECDSA signature.
    let withServiceAuth
        (alg : Algorithm)
        (sign : byte[] -> byte[])
        (iss : Did)
        (aud : Did)
        (agent : AtpAgent)
        : AtpAgent =
        { agent with
            AuthenticateRequest =
                Some (fun request ->
                    let lxm =
                        // Extract NSID from the URL path (e.g. /xrpc/com.atproto.sync.getRepo -> com.atproto.sync.getRepo)
                        let path = request.RequestUri.AbsolutePath

                        if path.StartsWith "/xrpc/" then
                            match Nsid.parse (path.Substring 6) with
                            | Ok nsid -> Some nsid
                            | Error _ -> None
                        else
                            None

                    let token = createTokenNow alg sign iss aud lxm

                    request.Headers.TryAddWithoutValidation ("Authorization", sprintf "Bearer %s" token)
                    |> ignore) }
