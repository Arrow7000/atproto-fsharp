namespace FSharp.ATProto.OAuth

open System
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks

/// Authorization server and protected resource metadata discovery
/// for the AT Protocol OAuth flow.
/// Implements RFC 8414 (Authorization Server Metadata) and
/// RFC 9728 (Protected Resource Metadata).
module Discovery =

    /// Parse a JSON element as a string list, returning an empty list if the property is missing.
    let private parseStringList (element: JsonElement) (property: string) : string list =
        match element.TryGetProperty(property) with
        | true, prop when prop.ValueKind = JsonValueKind.Array ->
            [ for item in prop.EnumerateArray() do
                  if item.ValueKind = JsonValueKind.String then
                      yield item.GetString() ]
        | _ -> []

    /// Parse a JSON element as an optional string.
    let private parseOptionalString (element: JsonElement) (property: string) : string option =
        match element.TryGetProperty(property) with
        | true, prop when prop.ValueKind = JsonValueKind.String -> Some(prop.GetString())
        | _ -> None

    /// Parse a JSON element as a required string.
    let private parseRequiredString
        (element: JsonElement)
        (property: string)
        : Result<string, OAuthError> =
        match element.TryGetProperty(property) with
        | true, prop when prop.ValueKind = JsonValueKind.String -> Ok(prop.GetString())
        | _ -> Error(OAuthError.DiscoveryFailed(sprintf "Missing required field: %s" property))

    /// Parse a JSON element as a bool, defaulting to false if missing.
    let private parseBool (element: JsonElement) (property: string) : bool =
        match element.TryGetProperty(property) with
        | true, prop ->
            match prop.ValueKind with
            | JsonValueKind.True -> true
            | JsonValueKind.False -> false
            | _ -> false
        | _ -> false

    /// Parse protected resource metadata from a JSON document.
    let internal parseProtectedResourceMetadata
        (json: string)
        : Result<ProtectedResourceMetadata, OAuthError> =
        try
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            match parseRequiredString root "resource" with
            | Error e -> Error e
            | Ok resource ->
                Ok
                    { Resource = resource
                      AuthorizationServers = parseStringList root "authorization_servers"
                      ScopesSupported = parseStringList root "scopes_supported" }
        with ex ->
            Error(OAuthError.DiscoveryFailed(sprintf "Failed to parse protected resource metadata: %s" ex.Message))

    /// Parse authorization server metadata from a JSON document.
    let internal parseAuthorizationServerMetadata
        (json: string)
        : Result<AuthorizationServerMetadata, OAuthError> =
        try
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            let issuer = parseRequiredString root "issuer"
            let authEndpoint = parseRequiredString root "authorization_endpoint"
            let tokenEndpoint = parseRequiredString root "token_endpoint"

            match issuer, authEndpoint, tokenEndpoint with
            | Ok issuer, Ok authEndpoint, Ok tokenEndpoint ->
                Ok
                    { Issuer = issuer
                      AuthorizationEndpoint = authEndpoint
                      TokenEndpoint = tokenEndpoint
                      PushedAuthorizationRequestEndpoint =
                        parseOptionalString root "pushed_authorization_request_endpoint"
                      ScopesSupported = parseStringList root "scopes_supported"
                      ResponseTypesSupported = parseStringList root "response_types_supported"
                      GrantTypesSupported = parseStringList root "grant_types_supported"
                      TokenEndpointAuthMethodsSupported =
                        parseStringList root "token_endpoint_auth_methods_supported"
                      DpopSigningAlgValuesSupported =
                        parseStringList root "dpop_signing_alg_values_supported"
                      RequirePushedAuthorizationRequests =
                        parseBool root "require_pushed_authorization_requests" }
            | Error e, _, _ -> Error e
            | _, Error e, _ -> Error e
            | _, _, Error e -> Error e
        with ex ->
            Error(
                OAuthError.DiscoveryFailed(
                    sprintf "Failed to parse authorization server metadata: %s" ex.Message
                )
            )

    /// Parse a token response from a JSON document.
    let internal parseTokenResponse (json: string) : Result<TokenResponse, OAuthError> =
        try
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            // Check for error response
            match parseOptionalString root "error" with
            | Some errorCode ->
                let description = parseOptionalString root "error_description"
                Error(OAuthError.TokenRequestFailed(errorCode, description))
            | None ->
                let accessToken = parseRequiredString root "access_token"
                let tokenType = parseRequiredString root "token_type"
                let sub = parseRequiredString root "sub"

                match accessToken, tokenType, sub with
                | Ok accessToken, Ok tokenType, Ok sub ->
                    let expiresIn =
                        match root.TryGetProperty("expires_in") with
                        | true, prop ->
                            match prop.TryGetInt32() with
                            | true, v -> v
                            | false, _ -> 0
                        | _ -> 0

                    Ok
                        { AccessToken = accessToken
                          TokenType = tokenType
                          ExpiresIn = expiresIn
                          RefreshToken = parseOptionalString root "refresh_token"
                          Scope = parseOptionalString root "scope"
                          Sub = sub }
                | Error e, _, _ -> Error e
                | _, Error e, _ -> Error e
                | _, _, Error e -> Error e
        with ex ->
            Error(OAuthError.TokenRequestFailed("parse_error", Some ex.Message))

    /// Construct the well-known URL for protected resource metadata (RFC 9728).
    let internal protectedResourceUrl (pdsUrl: string) : string =
        let uri = Uri(pdsUrl)
        sprintf "%s://%s/.well-known/oauth-protected-resource" uri.Scheme uri.Authority

    /// Construct the well-known URL for authorization server metadata (RFC 8414).
    let internal authorizationServerUrl (issuer: string) : string =
        let uri = Uri(issuer)
        sprintf "%s://%s/.well-known/oauth-authorization-server" uri.Scheme uri.Authority

    /// Discover protected resource metadata for a PDS.
    /// Fetches GET https://<pds>/.well-known/oauth-protected-resource
    let discoverProtectedResource
        (httpClient: HttpClient)
        (pdsUrl: string)
        : Task<Result<ProtectedResourceMetadata, OAuthError>> =
        task {
            try
                let url = protectedResourceUrl pdsUrl
                let! response = httpClient.GetAsync(url)

                if response.IsSuccessStatusCode then
                    let! body = response.Content.ReadAsStringAsync()
                    return parseProtectedResourceMetadata body
                else
                    return
                        Error(
                            OAuthError.DiscoveryFailed(
                                sprintf
                                    "Protected resource metadata request failed with status %d"
                                    (int response.StatusCode)
                            )
                        )
            with ex ->
                return Error(OAuthError.NetworkError(sprintf "Failed to fetch protected resource metadata: %s" ex.Message))
        }

    /// Discover authorization server metadata.
    /// Fetches GET <issuer>/.well-known/oauth-authorization-server
    let discoverAuthorizationServer
        (httpClient: HttpClient)
        (issuer: string)
        : Task<Result<AuthorizationServerMetadata, OAuthError>> =
        task {
            try
                let url = authorizationServerUrl issuer
                let! response = httpClient.GetAsync(url)

                if response.IsSuccessStatusCode then
                    let! body = response.Content.ReadAsStringAsync()
                    return parseAuthorizationServerMetadata body
                else
                    return
                        Error(
                            OAuthError.DiscoveryFailed(
                                sprintf
                                    "Authorization server metadata request failed with status %d"
                                    (int response.StatusCode)
                            )
                        )
            with ex ->
                return Error(OAuthError.NetworkError(sprintf "Failed to fetch authorization server metadata: %s" ex.Message))
        }

    /// Full discovery: PDS URL -> protected resource -> authorization server metadata.
    /// First discovers the protected resource to find the authorization server,
    /// then fetches the authorization server metadata.
    let discover
        (httpClient: HttpClient)
        (pdsUrl: string)
        : Task<Result<AuthorizationServerMetadata, OAuthError>> =
        task {
            let! protectedResource = discoverProtectedResource httpClient pdsUrl

            match protectedResource with
            | Error e -> return Error e
            | Ok prm ->
                match prm.AuthorizationServers with
                | [] ->
                    return
                        Error(OAuthError.DiscoveryFailed "No authorization servers found in protected resource metadata")
                | issuer :: _ -> return! discoverAuthorizationServer httpClient issuer
        }
