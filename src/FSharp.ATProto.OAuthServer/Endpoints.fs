namespace FSharp.ATProto.OAuthServer

open System
open System.IO
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open FSharp.ATProto.Syntax

/// OAuth HTTP endpoint handlers.
/// Each handler is a pure function taking EndpointDeps and returning an HttpContext -> Task<IResult>.
module Endpoints =

    /// Bundled dependencies for all endpoint handlers.
    type EndpointDeps =
        { Config: OAuthServerConfig
          TokenStore: ITokenStore
          RequestStore: IRequestStore
          ReplayStore: IReplayStore
          AccountStore: IAccountStore
          HttpClient: HttpClient
          ClientCache: ClientDiscovery.ClientCache
          NonceSecret: byte[] }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// Extract a form field value from the request.
    let private getFormValue (name: string) (form: IFormCollection) : string option =
        match form.TryGetValue(name) with
        | true, values when values.Count > 0 ->
            let v = values.[0]
            if String.IsNullOrWhiteSpace(v) then None else Some v
        | _ -> None

    /// Construct the full request URL from an HttpContext.
    let private getRequestUrl (ctx: HttpContext) : string =
        let req = ctx.Request
        let scheme = req.Scheme
        let host = req.Host.ToString()
        let path = req.Path.ToString()
        sprintf "%s://%s%s" scheme host path

    /// Create an OAuth error JSON response.
    let private oauthErrorResult (statusCode: int) (error: OAuthServerError) : IResult =
        let ms = new MemoryStream()
        use writer = new Utf8JsonWriter(ms)
        writer.WriteStartObject()
        writer.WriteString("error", error.ErrorCode)
        writer.WriteString("error_description", error.Description)
        writer.WriteEndObject()
        writer.Flush()
        let json = Encoding.UTF8.GetString(ms.ToArray())
        Results.Text(json, "application/json", Encoding.UTF8, statusCode)

    /// Create a token response JSON result.
    let private tokenResponseJson
        (accessToken: string)
        (expiresIn: int)
        (refreshToken: string)
        (scope: string)
        (sub: string)
        : IResult =
        let ms = new MemoryStream()
        use writer = new Utf8JsonWriter(ms)
        writer.WriteStartObject()
        writer.WriteString("access_token", accessToken)
        writer.WriteString("token_type", "DPoP")
        writer.WriteNumber("expires_in", expiresIn)
        writer.WriteString("refresh_token", refreshToken)
        writer.WriteString("scope", scope)
        writer.WriteString("sub", sub)
        writer.WriteEndObject()
        writer.Flush()
        let json = Encoding.UTF8.GetString(ms.ToArray())
        Results.Text(json, "application/json", Encoding.UTF8, 200)

    /// Generate and set a DPoP-Nonce response header.
    let private setDpopNonce (deps: EndpointDeps) (ctx: HttpContext) : unit =
        let nonce =
            DPoPValidator.generateNonce deps.NonceSecret DateTimeOffset.UtcNow deps.Config.DpopNonceLifetime

        ctx.Response.Headers.["DPoP-Nonce"] <- nonce

    /// Validate a DPoP proof from the request header, returning the JWK thumbprint on success.
    let private validateDpop
        (deps: EndpointDeps)
        (ctx: HttpContext)
        (requireNonce: bool)
        : Task<Result<string, OAuthServerError>> =
        task {
            let dpopHeader =
                match ctx.Request.Headers.TryGetValue("DPoP") with
                | true, values when values.Count > 0 -> Some(values.[0])
                | _ -> None

            match dpopHeader with
            | None ->
                return Error(OAuthServerError.InvalidDpopProof "Missing DPoP header")
            | Some dpopJwt ->
                let httpMethod = ctx.Request.Method
                let httpUrl = getRequestUrl ctx

                let expectedNonce =
                    if requireNonce then
                        let nonce =
                            DPoPValidator.generateNonce
                                deps.NonceSecret
                                DateTimeOffset.UtcNow
                                deps.Config.DpopNonceLifetime

                        Some nonce
                    else
                        None

                return!
                    DPoPValidator.parseAndVerifyProof
                        dpopJwt
                        httpMethod
                        httpUrl
                        deps.ReplayStore
                        None
                        expectedNonce
                        DateTimeOffset.UtcNow
                        (TimeSpan.FromMinutes 5.0)
        }

    // ── Endpoint handlers ────────────────────────────────────────────────

    /// GET /.well-known/oauth-authorization-server
    /// Returns OAuth server metadata document.
    let serverMetadata (deps: EndpointDeps) (ctx: HttpContext) : Task<IResult> =
        task {
            let issuer = deps.Config.Issuer

            let ms = new MemoryStream()
            use writer = new Utf8JsonWriter(ms)
            writer.WriteStartObject()
            writer.WriteString("issuer", issuer)
            writer.WriteString("authorization_endpoint", issuer + "/oauth/authorize")
            writer.WriteString("token_endpoint", issuer + "/oauth/token")
            writer.WriteString("pushed_authorization_request_endpoint", issuer + "/oauth/par")
            writer.WriteString("revocation_endpoint", issuer + "/oauth/revoke")
            writer.WriteString("jwks_uri", issuer + "/oauth/jwks")

            writer.WritePropertyName("scopes_supported")
            writer.WriteStartArray()

            for s in deps.Config.ScopesSupported do
                writer.WriteStringValue(s)

            writer.WriteEndArray()

            writer.WritePropertyName("response_types_supported")
            writer.WriteStartArray()
            writer.WriteStringValue("code")
            writer.WriteEndArray()

            writer.WritePropertyName("grant_types_supported")
            writer.WriteStartArray()
            writer.WriteStringValue("authorization_code")
            writer.WriteStringValue("refresh_token")
            writer.WriteEndArray()

            writer.WritePropertyName("token_endpoint_auth_methods_supported")
            writer.WriteStartArray()
            writer.WriteStringValue("none")
            writer.WriteStringValue("private_key_jwt")
            writer.WriteEndArray()

            writer.WritePropertyName("dpop_signing_alg_values_supported")
            writer.WriteStartArray()
            writer.WriteStringValue("ES256")
            writer.WriteEndArray()

            writer.WriteBoolean("require_pushed_authorization_requests", true)

            writer.WritePropertyName("code_challenge_methods_supported")
            writer.WriteStartArray()
            writer.WriteStringValue("S256")
            writer.WriteEndArray()

            writer.WriteBoolean("authorization_response_iss_parameter_supported", true)
            writer.WriteEndObject()
            writer.Flush()

            let json = Encoding.UTF8.GetString(ms.ToArray())
            ctx.Response.Headers.["Access-Control-Allow-Origin"] <- "*"
            return Results.Text(json, "application/json", Encoding.UTF8, 200)
        }

    /// GET /.well-known/oauth-protected-resource
    /// Returns protected resource metadata document.
    let protectedResourceMetadata (deps: EndpointDeps) (ctx: HttpContext) : Task<IResult> =
        task {
            let issuer = deps.Config.Issuer

            let ms = new MemoryStream()
            use writer = new Utf8JsonWriter(ms)
            writer.WriteStartObject()
            writer.WriteString("resource", issuer)

            writer.WritePropertyName("authorization_servers")
            writer.WriteStartArray()
            writer.WriteStringValue(issuer)
            writer.WriteEndArray()

            writer.WritePropertyName("scopes_supported")
            writer.WriteStartArray()

            for s in deps.Config.ScopesSupported do
                writer.WriteStringValue(s)

            writer.WriteEndArray()

            writer.WriteEndObject()
            writer.Flush()

            let json = Encoding.UTF8.GetString(ms.ToArray())
            return Results.Text(json, "application/json", Encoding.UTF8, 200)
        }

    /// GET /oauth/jwks
    /// Returns the server's public key set (JWKS).
    let jwks (deps: EndpointDeps) (_ctx: HttpContext) : Task<IResult> =
        task {
            let publicKeyJwk = deps.Config.PublicKeyJwk

            let ms = new MemoryStream()
            use writer = new Utf8JsonWriter(ms)
            writer.WriteStartObject()
            writer.WritePropertyName("keys")
            writer.WriteStartArray()
            let doc = JsonDocument.Parse(publicKeyJwk)
            doc.RootElement.WriteTo(writer)
            writer.WriteEndArray()
            writer.WriteEndObject()
            writer.Flush()

            let json = Encoding.UTF8.GetString(ms.ToArray())
            return Results.Text(json, "application/json", Encoding.UTF8, 200)
        }

    /// POST /oauth/par
    /// Pushed Authorization Request endpoint.
    let par (deps: EndpointDeps) (ctx: HttpContext) : Task<IResult> =
        task {
            setDpopNonce deps ctx

            // Validate DPoP proof (no nonce required on first PAR request)
            let! dpopResult = validateDpop deps ctx false

            match dpopResult with
            | Error e ->
                return oauthErrorResult 400 e
            | Ok dpopJkt ->

            // Read form body
            let! form = ctx.Request.ReadFormAsync()

            let clientId = getFormValue "client_id" form
            let redirectUri = getFormValue "redirect_uri" form
            let responseType = getFormValue "response_type" form
            let scope = getFormValue "scope" form
            let state = getFormValue "state" form
            let codeChallenge = getFormValue "code_challenge" form
            let codeChallengeMethod = getFormValue "code_challenge_method" form

            // Validate required fields
            match clientId with
            | None ->
                return oauthErrorResult 400 (OAuthServerError.InvalidRequest "Missing required parameter: client_id")
            | Some clientId ->

            match redirectUri with
            | None ->
                return oauthErrorResult 400 (OAuthServerError.InvalidRequest "Missing required parameter: redirect_uri")
            | Some redirectUri ->

            match responseType with
            | None ->
                return oauthErrorResult 400 (OAuthServerError.InvalidRequest "Missing required parameter: response_type")
            | Some responseType ->

            match scope with
            | None ->
                return oauthErrorResult 400 (OAuthServerError.InvalidRequest "Missing required parameter: scope")
            | Some scope ->

            match codeChallenge with
            | None ->
                return oauthErrorResult 400 (OAuthServerError.InvalidRequest "Missing required parameter: code_challenge")
            | Some codeChallenge ->

            match codeChallengeMethod with
            | None ->
                return oauthErrorResult 400 (OAuthServerError.InvalidRequest "Missing required parameter: code_challenge_method")
            | Some codeChallengeMethod ->

            // Validate response_type
            if responseType <> "code" then
                return oauthErrorResult 400 (OAuthServerError.InvalidRequest "response_type must be 'code'")
            else

            // Validate code_challenge_method
            if codeChallengeMethod <> "S256" then
                return oauthErrorResult 400 (OAuthServerError.InvalidRequest "code_challenge_method must be 'S256'")
            else

            // Validate scope
            let requestedScopes = OAuthScope.parse scope

            if not (OAuthScope.hasAtproto requestedScopes) then
                return oauthErrorResult 400 (OAuthServerError.InvalidScope "Scope must include 'atproto'")
            else

            if not (OAuthScope.isValid deps.Config.ScopesSupported requestedScopes) then
                return oauthErrorResult 400 (OAuthServerError.InvalidScope "Requested scope contains unsupported values")
            else

            // Fetch and validate client metadata
            let! clientResult = deps.ClientCache.GetOrFetch deps.HttpClient clientId

            match clientResult with
            | Error e ->
                return oauthErrorResult 400 e
            | Ok clientMetadata ->

            // Validate redirect_uri is in client's allowed list
            if not (List.contains redirectUri clientMetadata.RedirectUris) then
                return oauthErrorResult 400 (OAuthServerError.InvalidRequest "redirect_uri not registered for this client")
            else

            // Create request data
            let now = DateTimeOffset.UtcNow
            let requestId = TokenSigner.generateRandomString ()

            let requestData =
                { ClientId = clientId
                  RedirectUri = redirectUri
                  Scope = scope
                  State = state
                  CodeChallenge = codeChallenge
                  CodeChallengeMethod = codeChallengeMethod
                  DpopJkt = dpopJkt
                  Code = None
                  AuthorizedSub = None
                  ExpiresAt = now + deps.Config.RequestLifetime
                  CreatedAt = now }

            do! deps.RequestStore.CreateRequest(requestId, requestData)

            let expiresIn = int deps.Config.RequestLifetime.TotalSeconds
            let requestUri = sprintf "urn:ietf:params:oauth:request_uri:%s" requestId

            let ms = new MemoryStream()
            use writer = new Utf8JsonWriter(ms)
            writer.WriteStartObject()
            writer.WriteString("request_uri", requestUri)
            writer.WriteNumber("expires_in", expiresIn)
            writer.WriteEndObject()
            writer.Flush()

            let json = Encoding.UTF8.GetString(ms.ToArray())
            return Results.Text(json, "application/json", Encoding.UTF8, 201)
        }

    /// GET /oauth/authorize
    /// Authorization endpoint. Redirects to consent UI.
    let authorize (deps: EndpointDeps) (ctx: HttpContext) : Task<IResult> =
        task {
            let requestUri =
                match ctx.Request.Query.TryGetValue("request_uri") with
                | true, values when values.Count > 0 -> Some(values.[0])
                | _ -> None

            match requestUri with
            | None ->
                return oauthErrorResult 400 (OAuthServerError.InvalidRequest "Missing required parameter: request_uri")
            | Some requestUri ->

            // Extract request ID from URN
            let prefix = "urn:ietf:params:oauth:request_uri:"

            if not (requestUri.StartsWith(prefix, StringComparison.Ordinal)) then
                return oauthErrorResult 400 (OAuthServerError.InvalidRequest "Invalid request_uri format")
            else

            let requestId = requestUri.Substring(prefix.Length)
            let! requestOpt = deps.RequestStore.ReadRequest(requestId)

            match requestOpt with
            | None ->
                return oauthErrorResult 400 (OAuthServerError.InvalidRequest "Unknown or expired request_uri")
            | Some request ->

            // Check expiration
            if request.ExpiresAt < DateTimeOffset.UtcNow then
                do! deps.RequestStore.DeleteRequest(requestId)
                return oauthErrorResult 400 (OAuthServerError.InvalidRequest "Authorization request has expired")
            else

            let consentUrl = sprintf "%s/consent?request_id=%s" deps.Config.Issuer requestId
            return Results.Redirect(consentUrl)
        }

    /// POST /oauth/token
    /// Token endpoint. Handles authorization_code and refresh_token grants.
    let token (deps: EndpointDeps) (ctx: HttpContext) : Task<IResult> =
        task {
            setDpopNonce deps ctx

            let! form = ctx.Request.ReadFormAsync()
            let grantType = getFormValue "grant_type" form

            match grantType with
            | None ->
                return oauthErrorResult 400 (OAuthServerError.InvalidRequest "Missing required parameter: grant_type")
            | Some "authorization_code" ->
                // Validate DPoP
                let! dpopResult = validateDpop deps ctx false

                match dpopResult with
                | Error e ->
                    return oauthErrorResult 400 e
                | Ok dpopJkt ->

                let code = getFormValue "code" form
                let redirectUri = getFormValue "redirect_uri" form
                let clientId = getFormValue "client_id" form
                let codeVerifier = getFormValue "code_verifier" form

                match code with
                | None ->
                    return oauthErrorResult 400 (OAuthServerError.InvalidRequest "Missing required parameter: code")
                | Some code ->

                match redirectUri with
                | None ->
                    return oauthErrorResult 400 (OAuthServerError.InvalidRequest "Missing required parameter: redirect_uri")
                | Some redirectUri ->

                match clientId with
                | None ->
                    return oauthErrorResult 400 (OAuthServerError.InvalidRequest "Missing required parameter: client_id")
                | Some clientId ->

                match codeVerifier with
                | None ->
                    return oauthErrorResult 400 (OAuthServerError.InvalidRequest "Missing required parameter: code_verifier")
                | Some codeVerifier ->

                // Consume code atomically
                let! requestOpt = deps.RequestStore.ConsumeCode(code)

                match requestOpt with
                | None ->
                    return oauthErrorResult 400 (OAuthServerError.InvalidGrant "Invalid or already consumed authorization code")
                | Some request ->

                // Verify PKCE
                if not (ClientDiscovery.validatePkceS256 codeVerifier request.CodeChallenge) then
                    return oauthErrorResult 400 (OAuthServerError.InvalidGrant "Invalid code_verifier: PKCE verification failed")
                else

                // Verify DPoP thumbprint matches
                if dpopJkt <> request.DpopJkt then
                    return oauthErrorResult 400 (OAuthServerError.InvalidGrant "DPoP key mismatch: proof key does not match original request")
                else

                // Verify redirect_uri matches
                if redirectUri <> request.RedirectUri then
                    return oauthErrorResult 400 (OAuthServerError.InvalidGrant "redirect_uri does not match the original request")
                else

                // Verify client_id matches
                if clientId <> request.ClientId then
                    return oauthErrorResult 400 (OAuthServerError.InvalidGrant "client_id does not match the original request")
                else

                // Must have authorized sub
                match request.AuthorizedSub with
                | None ->
                    return oauthErrorResult 400 (OAuthServerError.InvalidGrant "Authorization request was not approved")
                | Some sub ->

                // Generate tokens
                let now = DateTimeOffset.UtcNow

                let accessToken =
                    TokenSigner.createAccessToken deps.Config sub clientId request.Scope dpopJkt now

                let refreshToken = TokenSigner.createRefreshToken ()
                let tokenId = TokenSigner.generateRandomString ()

                let tokenData =
                    { Sub = sub
                      ClientId = clientId
                      Scope = request.Scope
                      DpopJkt = dpopJkt
                      AccessToken = accessToken
                      RefreshToken = refreshToken
                      ExpiresAt = now + deps.Config.AccessTokenLifetime
                      CreatedAt = now }

                do! deps.TokenStore.CreateToken(tokenId, tokenData)

                let expiresIn = int deps.Config.AccessTokenLifetime.TotalSeconds
                return tokenResponseJson accessToken expiresIn refreshToken request.Scope (Did.value sub)

            | Some "refresh_token" ->
                // Validate DPoP
                let! dpopResult = validateDpop deps ctx false

                match dpopResult with
                | Error e ->
                    return oauthErrorResult 400 e
                | Ok dpopJkt ->

                let refreshTokenValue = getFormValue "refresh_token" form
                let clientId = getFormValue "client_id" form

                match refreshTokenValue with
                | None ->
                    return oauthErrorResult 400 (OAuthServerError.InvalidRequest "Missing required parameter: refresh_token")
                | Some refreshTokenValue ->

                match clientId with
                | None ->
                    return oauthErrorResult 400 (OAuthServerError.InvalidRequest "Missing required parameter: client_id")
                | Some clientId ->

                // Find token by refresh token
                let! tokenOpt = deps.TokenStore.FindByRefreshToken(refreshTokenValue)

                match tokenOpt with
                | None ->
                    return oauthErrorResult 400 (OAuthServerError.InvalidGrant "Invalid refresh token")
                | Some(tokenId, tokenData) ->

                // Verify DPoP thumbprint matches
                if dpopJkt <> tokenData.DpopJkt then
                    return oauthErrorResult 400 (OAuthServerError.InvalidGrant "DPoP key mismatch")
                else

                // Verify client_id matches
                if clientId <> tokenData.ClientId then
                    return oauthErrorResult 400 (OAuthServerError.InvalidGrant "client_id does not match")
                else

                // Generate new tokens
                let now = DateTimeOffset.UtcNow

                let newAccessToken =
                    TokenSigner.createAccessToken deps.Config tokenData.Sub clientId tokenData.Scope dpopJkt now

                let newRefreshToken = TokenSigner.createRefreshToken ()
                let newTokenId = TokenSigner.generateRandomString ()

                let newTokenData =
                    { Sub = tokenData.Sub
                      ClientId = clientId
                      Scope = tokenData.Scope
                      DpopJkt = dpopJkt
                      AccessToken = newAccessToken
                      RefreshToken = newRefreshToken
                      ExpiresAt = now + deps.Config.AccessTokenLifetime
                      CreatedAt = now }

                do! deps.TokenStore.RotateToken(tokenId, newTokenId, newRefreshToken, newTokenData)

                let expiresIn = int deps.Config.AccessTokenLifetime.TotalSeconds

                return
                    tokenResponseJson newAccessToken expiresIn newRefreshToken tokenData.Scope (Did.value tokenData.Sub)

            | Some unsupported ->
                return
                    oauthErrorResult
                        400
                        (OAuthServerError.UnsupportedGrantType(sprintf "Unsupported grant_type: %s" unsupported))
        }

    /// POST /oauth/revoke
    /// Token revocation endpoint (RFC 7009).
    let revoke (deps: EndpointDeps) (ctx: HttpContext) : Task<IResult> =
        task {
            try
                let! form = ctx.Request.ReadFormAsync()
                let tokenValue = getFormValue "token" form

                match tokenValue with
                | None ->
                    // RFC 7009: always return 200
                    return Results.Ok()
                | Some tokenValue ->

                // Try to find by refresh token and delete
                let! tokenOpt = deps.TokenStore.FindByRefreshToken(tokenValue)

                match tokenOpt with
                | Some(tokenId, _) -> do! deps.TokenStore.DeleteToken(tokenId)
                | None -> ()

                return Results.Ok()
            with _ ->
                // RFC 7009: suppress errors, always return 200
                return Results.Ok()
        }
