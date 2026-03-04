namespace FSharp.ATProto.OAuth

open System
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks
open FSharp.ATProto.Syntax

/// OAuth client for the AT Protocol authorization flow.
/// Implements PKCE (RFC 7636), PAR (RFC 9126), and DPoP (RFC 9449).
module OAuthClient =

    /// Build form-encoded content from key-value pairs.
    let private formContent (pairs: (string * string) list) : FormUrlEncodedContent =
        let dict =
            pairs
            |> List.map (fun (k, v) -> System.Collections.Generic.KeyValuePair(k, v))

        new FormUrlEncodedContent(dict)

    /// Build the authorization URL with query parameters (non-PAR fallback).
    let internal buildAuthorizationUrl
        (serverMetadata: AuthorizationServerMetadata)
        (clientId: string)
        (redirectUri: string)
        (scope: string)
        (state: string)
        (codeChallenge: string)
        : string =
        let parameters =
            [ "response_type", "code"
              "client_id", clientId
              "redirect_uri", redirectUri
              "scope", scope
              "state", state
              "code_challenge", codeChallenge
              "code_challenge_method", "S256" ]

        let queryString =
            parameters
            |> List.map (fun (k, v) -> sprintf "%s=%s" k (Uri.EscapeDataString(v)))
            |> String.concat "&"

        sprintf "%s?%s" serverMetadata.AuthorizationEndpoint queryString

    /// Build the authorization URL from a PAR request_uri.
    let internal buildParAuthorizationUrl
        (serverMetadata: AuthorizationServerMetadata)
        (clientId: string)
        (requestUri: string)
        : string =
        let queryString =
            sprintf
                "client_id=%s&request_uri=%s"
                (Uri.EscapeDataString(clientId))
                (Uri.EscapeDataString(requestUri))

        sprintf "%s?%s" serverMetadata.AuthorizationEndpoint queryString

    /// Parse a PAR response to extract the request_uri.
    let internal parseParResponse (json: string) : Result<string, OAuthError> =
        try
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            match root.TryGetProperty("error") with
            | true, errorProp when errorProp.ValueKind = JsonValueKind.String ->
                let errorDesc =
                    match root.TryGetProperty("error_description") with
                    | true, desc when desc.ValueKind = JsonValueKind.String -> Some(desc.GetString())
                    | _ -> None

                Error(OAuthError.TokenRequestFailed(errorProp.GetString(), errorDesc))
            | _ ->
                match root.TryGetProperty("request_uri") with
                | true, prop when prop.ValueKind = JsonValueKind.String -> Ok(prop.GetString())
                | _ -> Error(OAuthError.InvalidState "PAR response missing request_uri")
        with ex ->
            Error(OAuthError.InvalidState(sprintf "Failed to parse PAR response: %s" ex.Message))

    /// Start an authorization flow.
    /// Returns the authorization URL to redirect the user to and the state to save
    /// for completing the flow after the callback.
    let startAuthorization
        (httpClient: HttpClient)
        (clientMetadata: ClientMetadata)
        (serverMetadata: AuthorizationServerMetadata)
        (redirectUri: string)
        : Task<Result<string * AuthorizationState, OAuthError>> =
        task {
            try
                let pkce = DPoP.generatePkce ()
                let dpopKey = DPoP.generateKeyPair ()
                let state = DPoP.generateRandomString ()

                let authState =
                    { State = state
                      Pkce = pkce
                      DpopKeyPair = dpopKey
                      RedirectUri = redirectUri
                      AuthorizationServer = serverMetadata }

                match serverMetadata.PushedAuthorizationRequestEndpoint with
                | Some parEndpoint ->
                    // Use PAR (Pushed Authorization Request)
                    let dpopProof =
                        DPoP.createProof dpopKey "POST" parEndpoint None None

                    let content =
                        formContent
                            [ "response_type", "code"
                              "client_id", clientMetadata.ClientId
                              "redirect_uri", redirectUri
                              "scope", clientMetadata.Scope
                              "state", state
                              "code_challenge", pkce.Challenge
                              "code_challenge_method", "S256" ]

                    use request = new HttpRequestMessage(HttpMethod.Post, parEndpoint)
                    request.Content <- content
                    request.Headers.TryAddWithoutValidation("DPoP", dpopProof) |> ignore

                    let! response = httpClient.SendAsync(request)
                    let! body = response.Content.ReadAsStringAsync()

                    match parseParResponse body with
                    | Ok requestUri ->
                        let authUrl =
                            buildParAuthorizationUrl serverMetadata clientMetadata.ClientId requestUri

                        return Ok(authUrl, authState)
                    | Error e -> return Error e

                | None ->
                    // Fallback: encode parameters in authorization URL directly
                    let authUrl =
                        buildAuthorizationUrl
                            serverMetadata
                            clientMetadata.ClientId
                            redirectUri
                            clientMetadata.Scope
                            state
                            pkce.Challenge

                    return Ok(authUrl, authState)
            with ex ->
                return Error(OAuthError.NetworkError(sprintf "Failed to start authorization: %s" ex.Message))
        }

    /// Complete the authorization flow by exchanging the authorization code for tokens.
    /// Call this after the user is redirected back with an authorization code.
    let completeAuthorization
        (httpClient: HttpClient)
        (clientMetadata: ClientMetadata)
        (authState: AuthorizationState)
        (authorizationCode: string)
        : Task<Result<OAuthSession, OAuthError>> =
        task {
            try
                let tokenEndpoint = authState.AuthorizationServer.TokenEndpoint

                let dpopProof =
                    DPoP.createProof authState.DpopKeyPair "POST" tokenEndpoint None None

                let content =
                    formContent
                        [ "grant_type", "authorization_code"
                          "code", authorizationCode
                          "redirect_uri", authState.RedirectUri
                          "client_id", clientMetadata.ClientId
                          "code_verifier", authState.Pkce.Verifier ]

                use request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
                request.Content <- content
                request.Headers.TryAddWithoutValidation("DPoP", dpopProof) |> ignore

                let! response = httpClient.SendAsync(request)
                let! body = response.Content.ReadAsStringAsync()

                match Discovery.parseTokenResponse body with
                | Ok tokenResponse ->
                    match Did.parse tokenResponse.Sub with
                    | Ok did ->
                        let session =
                            { AccessToken = tokenResponse.AccessToken
                              RefreshToken = tokenResponse.RefreshToken
                              ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(float tokenResponse.ExpiresIn)
                              Did = did
                              DpopKeyPair = authState.DpopKeyPair
                              TokenEndpoint = tokenEndpoint }

                        return Ok session
                    | Error didError ->
                        return Error(OAuthError.InvalidState(sprintf "Invalid DID in token response sub: %s" didError))
                | Error e -> return Error e
            with ex ->
                return Error(OAuthError.NetworkError(sprintf "Failed to complete authorization: %s" ex.Message))
        }

    /// Refresh an expired access token using the refresh token.
    /// Returns a new OAuthSession with updated tokens.
    let refreshToken
        (httpClient: HttpClient)
        (clientMetadata: ClientMetadata)
        (session: OAuthSession)
        : Task<Result<OAuthSession, OAuthError>> =
        task {
            match session.RefreshToken with
            | None -> return Error(OAuthError.InvalidState "No refresh token available")
            | Some refreshTok ->
                try
                    let dpopProof =
                        DPoP.createProof session.DpopKeyPair "POST" session.TokenEndpoint None None

                    let content =
                        formContent
                            [ "grant_type", "refresh_token"
                              "refresh_token", refreshTok
                              "client_id", clientMetadata.ClientId ]

                    use request = new HttpRequestMessage(HttpMethod.Post, session.TokenEndpoint)
                    request.Content <- content
                    request.Headers.TryAddWithoutValidation("DPoP", dpopProof) |> ignore

                    let! response = httpClient.SendAsync(request)
                    let! body = response.Content.ReadAsStringAsync()

                    match Discovery.parseTokenResponse body with
                    | Ok tokenResponse ->
                        match Did.parse tokenResponse.Sub with
                        | Ok did ->
                            let newSession =
                                { AccessToken = tokenResponse.AccessToken
                                  RefreshToken = tokenResponse.RefreshToken
                                  ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(float tokenResponse.ExpiresIn)
                                  Did = did
                                  DpopKeyPair = session.DpopKeyPair
                                  TokenEndpoint = session.TokenEndpoint }

                            return Ok newSession
                        | Error didError ->
                            return
                                Error(OAuthError.InvalidState(sprintf "Invalid DID in token response sub: %s" didError))
                    | Error e -> return Error e
                with ex ->
                    return Error(OAuthError.NetworkError(sprintf "Failed to refresh token: %s" ex.Message))
        }

    /// Create a DPoP-authenticated HTTP request.
    /// Adds the Authorization header with DPoP token type and a DPoP proof header.
    let createAuthenticatedRequest
        (session: OAuthSession)
        (httpMethod: HttpMethod)
        (url: string)
        (nonce: string option)
        : HttpRequestMessage =
        let ath = DPoP.hashAccessToken session.AccessToken

        let dpopProof =
            DPoP.createProof session.DpopKeyPair (httpMethod.Method) url (Some ath) nonce

        let request = new HttpRequestMessage(httpMethod, url)
        request.Headers.TryAddWithoutValidation("Authorization", sprintf "DPoP %s" session.AccessToken) |> ignore
        request.Headers.TryAddWithoutValidation("DPoP", dpopProof) |> ignore
        request
