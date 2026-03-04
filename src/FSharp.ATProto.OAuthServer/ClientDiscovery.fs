namespace FSharp.ATProto.OAuthServer

open System
open System.Collections.Concurrent
open System.IO
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading.Tasks
open FSharp.ATProto.OAuth

/// Client metadata discovery, PKCE validation, and client assertion validation.
module ClientDiscovery =

    /// Encode bytes as base64url (RFC 4648 section 5), without padding.
    let private toBase64Url (bytes: byte array) : string =
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=')

    // ── PKCE Validation ──

    /// Verify an S256 PKCE code challenge against the given verifier.
    /// Hashes the verifier with SHA-256, base64url encodes it, and compares with the challenge.
    let validatePkceS256 (verifier: string) (challenge: string) : bool =
        let hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier))
        let computed = toBase64Url hash
        String.Equals(computed, challenge, StringComparison.Ordinal)

    // ── Loopback Detection ──

    /// Check if a client_id URL is a loopback client (http://localhost or http://127.0.0.1).
    /// Loopback clients get special treatment per the AT Protocol OAuth spec.
    let isLoopbackClient (clientId: string) : bool =
        try
            let uri = Uri(clientId, UriKind.Absolute)

            uri.Scheme = "http"
            && (uri.Host = "localhost" || uri.Host = "127.0.0.1")
        with :? UriFormatException ->
            false

    // ── Client Metadata Validation ──

    /// Validate that client metadata meets AT Protocol OAuth requirements.
    let validateClientMetadata
        (metadata: ClientMetadata)
        : Result<unit, OAuthServerError> =
        if not metadata.DpopBoundAccessTokens then
            Error(OAuthServerError.InvalidClient "Client must set dpop_bound_access_tokens to true")
        elif not (List.contains "code" metadata.ResponseTypes) then
            Error(OAuthServerError.InvalidClient "Client must include 'code' in response_types")
        elif not (List.contains "authorization_code" metadata.GrantTypes) then
            Error(OAuthServerError.InvalidClient "Client must include 'authorization_code' in grant_types")
        elif
            not (
                metadata.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                |> Array.contains "atproto"
            )
        then
            Error(OAuthServerError.InvalidClient "Client must include 'atproto' in scope")
        elif metadata.RedirectUris.IsEmpty then
            Error(OAuthServerError.InvalidClient "Client must have at least one redirect_uri")
        else
            Ok()

    // ── JSON Parsing for ClientMetadata ──

    let private parseStringList (element: JsonElement) (property: string) : string list =
        match element.TryGetProperty(property) with
        | true, prop when prop.ValueKind = JsonValueKind.Array ->
            [ for item in prop.EnumerateArray() do
                  if item.ValueKind = JsonValueKind.String then
                      yield item.GetString() ]
        | _ -> []

    let private parseOptionalString (element: JsonElement) (property: string) : string option =
        match element.TryGetProperty(property) with
        | true, prop when prop.ValueKind = JsonValueKind.String -> Some(prop.GetString())
        | _ -> None

    let private parseBool (element: JsonElement) (property: string) (defaultValue: bool) : bool =
        match element.TryGetProperty(property) with
        | true, prop ->
            match prop.ValueKind with
            | JsonValueKind.True -> true
            | JsonValueKind.False -> false
            | _ -> defaultValue
        | _ -> defaultValue

    let private parseClientMetadataJson (json: string) : Result<ClientMetadata, OAuthServerError> =
        try
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            match root.TryGetProperty("client_id") with
            | true, prop when prop.ValueKind = JsonValueKind.String ->
                Ok
                    { ClientId = prop.GetString()
                      ClientUri = parseOptionalString root "client_uri"
                      RedirectUris = parseStringList root "redirect_uris"
                      Scope =
                        parseOptionalString root "scope"
                        |> Option.defaultValue ""
                      GrantTypes = parseStringList root "grant_types"
                      ResponseTypes = parseStringList root "response_types"
                      TokenEndpointAuthMethod =
                        parseOptionalString root "token_endpoint_auth_method"
                        |> Option.defaultValue "none"
                      ApplicationType =
                        parseOptionalString root "application_type"
                        |> Option.defaultValue "web"
                      DpopBoundAccessTokens = parseBool root "dpop_bound_access_tokens" false }
            | _ -> Error(OAuthServerError.InvalidClient "Client metadata missing required field: client_id")
        with ex ->
            Error(OAuthServerError.InvalidClient(sprintf "Failed to parse client metadata JSON: %s" ex.Message))

    // ── Default Loopback Metadata ──

    let private loopbackMetadata (clientId: string) : ClientMetadata =
        { ClientId = clientId
          ClientUri = None
          RedirectUris = [ "http://127.0.0.1/callback"; "http://localhost/callback" ]
          Scope = "atproto"
          GrantTypes = [ "authorization_code" ]
          ResponseTypes = [ "code" ]
          TokenEndpointAuthMethod = "none"
          ApplicationType = "native"
          DpopBoundAccessTokens = true }

    // ── Client Discovery ──

    /// Maximum response size for client metadata (1 MB).
    let private maxResponseSize = 1_048_576L

    /// Fetch client metadata from the client_id URL.
    /// Loopback clients return default metadata without an HTTP fetch.
    /// Non-loopback clients must use HTTPS.
    let fetchClientMetadata
        (httpClient: HttpClient)
        (clientId: string)
        : Task<Result<ClientMetadata, OAuthServerError>> =
        task {
            if isLoopbackClient clientId then
                return Ok(loopbackMetadata clientId)
            else
                try
                    let uri = Uri(clientId, UriKind.Absolute)

                    if uri.Scheme <> "https" then
                        return
                            Error(
                                OAuthServerError.InvalidClient
                                    "Non-loopback client_id must use HTTPS"
                            )
                    else
                        use cts =
                            new Threading.CancellationTokenSource(TimeSpan.FromSeconds 10.0)

                        let! response = httpClient.GetAsync(clientId, cts.Token)

                        if not response.IsSuccessStatusCode then
                            return
                                Error(
                                    OAuthServerError.InvalidClient(
                                        sprintf
                                            "Failed to fetch client metadata: HTTP %d"
                                            (int response.StatusCode)
                                    )
                                )
                        else
                            // Check content length if available
                            match response.Content.Headers.ContentLength |> Option.ofNullable with
                            | Some len when len > maxResponseSize ->
                                return
                                    Error(
                                        OAuthServerError.InvalidClient "Client metadata response too large"
                                    )
                            | _ ->
                                let! body = response.Content.ReadAsStringAsync(cts.Token)

                                if int64 body.Length > maxResponseSize then
                                    return
                                        Error(
                                            OAuthServerError.InvalidClient
                                                "Client metadata response too large"
                                        )
                                else
                                    match parseClientMetadataJson body with
                                    | Error e -> return Error e
                                    | Ok metadata ->
                                        match validateClientMetadata metadata with
                                        | Error e -> return Error e
                                        | Ok() -> return Ok metadata
                with
                | :? UriFormatException ->
                    return Error(OAuthServerError.InvalidClient "Invalid client_id URL format")
                | :? TaskCanceledException ->
                    return Error(OAuthServerError.InvalidClient "Client metadata fetch timed out")
                | ex ->
                    return
                        Error(
                            OAuthServerError.InvalidClient(
                                sprintf "Failed to fetch client metadata: %s" ex.Message
                            )
                        )
        }

    // ── Client Cache ──

    /// A simple cache for client metadata with configurable TTL.
    type ClientCache(cacheTtl: TimeSpan) =
        let cache = ConcurrentDictionary<string, DateTimeOffset * ClientMetadata>()

        /// Get cached client metadata or fetch it from the client_id URL.
        member _.GetOrFetch
            (httpClient: HttpClient)
            (clientId: string)
            : Task<Result<ClientMetadata, OAuthServerError>> =
            task {
                let now = DateTimeOffset.UtcNow

                match cache.TryGetValue(clientId) with
                | true, (cachedAt, metadata) when now - cachedAt < cacheTtl -> return Ok metadata
                | _ ->
                    let! result = fetchClientMetadata httpClient clientId

                    match result with
                    | Ok metadata ->
                        cache.[clientId] <- (now, metadata)
                        return Ok metadata
                    | Error e -> return Error e
            }

    // ── Client Assertion Validation ──

    /// Validate a client assertion for confidential clients.
    /// Checks that the assertion type is correct and the assertion is non-empty.
    /// Full JWT validation is a stretch goal.
    let validateClientAssertion
        (assertionType: string)
        (assertion: string)
        (_expectedClientId: string)
        : Result<unit, OAuthServerError> =
        if assertionType <> "urn:ietf:params:oauth:client-assertion-type:jwt-bearer" then
            Error(
                OAuthServerError.InvalidClient(
                    sprintf "Unsupported client assertion type: %s" assertionType
                )
            )
        elif String.IsNullOrWhiteSpace(assertion) then
            Error(OAuthServerError.InvalidClient "Client assertion must not be empty")
        else
            Ok()
