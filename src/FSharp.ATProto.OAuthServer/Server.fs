namespace FSharp.ATProto.OAuthServer

open System
open System.Net.Http
open System.Security.Cryptography
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing

/// Server builder for composing the OAuth authorization server.
module OAuthServer =

    /// Builder record for configuring the OAuth server.
    type OAuthServerBuilder =
        { Config: OAuthServerConfig
          TokenStore: ITokenStore option
          RequestStore: IRequestStore option
          ReplayStore: IReplayStore option
          AccountStore: IAccountStore option
          SigningKey: ECDsa option
          Port: int option
          ConsentPath: string }

    /// Default builder with sensible defaults. Issuer must be set before building.
    let defaults =
        { Config = OAuthServerConfig.defaults
          TokenStore = None
          RequestStore = None
          ReplayStore = None
          AccountStore = None
          SigningKey = None
          Port = None
          ConsentPath = "/consent" }

    /// Set the issuer URL for the OAuth server.
    let withIssuer (issuer: string) (builder: OAuthServerBuilder) : OAuthServerBuilder =
        { builder with Config = { builder.Config with Issuer = issuer } }

    /// Set a custom token store.
    let withTokenStore (store: ITokenStore) (builder: OAuthServerBuilder) : OAuthServerBuilder =
        { builder with TokenStore = Some store }

    /// Set a custom request store.
    let withRequestStore (store: IRequestStore) (builder: OAuthServerBuilder) : OAuthServerBuilder =
        { builder with RequestStore = Some store }

    /// Set a custom replay detection store.
    let withReplayStore (store: IReplayStore) (builder: OAuthServerBuilder) : OAuthServerBuilder =
        { builder with ReplayStore = Some store }

    /// Set a custom account store.
    let withAccountStore (store: IAccountStore) (builder: OAuthServerBuilder) : OAuthServerBuilder =
        { builder with AccountStore = Some store }

    /// Set the signing key for token creation.
    let withSigningKey (key: ECDsa) (builder: OAuthServerBuilder) : OAuthServerBuilder =
        { builder with SigningKey = Some key }

    /// Set the port the server listens on.
    let withPort (port: int) (builder: OAuthServerBuilder) : OAuthServerBuilder =
        { builder with Port = Some port }

    /// Set the consent UI path (default: "/consent").
    let withConsentPath (path: string) (builder: OAuthServerBuilder) : OAuthServerBuilder =
        { builder with ConsentPath = path }

    /// Set the service DID for the server.
    let withServiceDid (did: FSharp.ATProto.Syntax.Did) (builder: OAuthServerBuilder) : OAuthServerBuilder =
        { builder with Config = { builder.Config with ServiceDid = Some did } }

    /// Set the access token lifetime.
    let withAccessTokenLifetime (lifetime: TimeSpan) (builder: OAuthServerBuilder) : OAuthServerBuilder =
        { builder with Config = { builder.Config with AccessTokenLifetime = lifetime } }

    /// Set the refresh token lifetime.
    let withRefreshTokenLifetime (lifetime: TimeSpan) (builder: OAuthServerBuilder) : OAuthServerBuilder =
        { builder with Config = { builder.Config with RefreshTokenLifetime = lifetime } }

    /// Set the supported scopes.
    let withScopesSupported (scopes: string list) (builder: OAuthServerBuilder) : OAuthServerBuilder =
        { builder with Config = { builder.Config with ScopesSupported = scopes } }

    /// Build and configure a WebApplication with all OAuth routes mapped.
    let configure (builder: OAuthServerBuilder) : WebApplication =
        // Resolve signing key (generate if not provided)
        let signingKey =
            match builder.SigningKey with
            | Some key -> key
            | None -> TokenSigner.createSigningKey ()

        // Build config with signing key
        let config =
            { builder.Config with
                SigningKey = TokenSigner.makeSigningFunction signingKey
                PublicKeyJwk = TokenSigner.exportPublicJwk signingKey }

        // Resolve stores (use in-memory defaults if not provided)
        let tokenStore =
            match builder.TokenStore with
            | Some store -> store
            | None -> InMemoryTokenStore() :> ITokenStore

        let requestStore =
            match builder.RequestStore with
            | Some store -> store
            | None -> InMemoryRequestStore() :> IRequestStore

        let replayStore =
            match builder.ReplayStore with
            | Some store -> store
            | None -> InMemoryReplayStore() :> IReplayStore

        let accountStore =
            match builder.AccountStore with
            | Some store -> store
            | None -> InMemoryAccountStore() :> IAccountStore

        // Create HttpClient and ClientCache
        let httpClient = new HttpClient()
        let clientCache = ClientDiscovery.ClientCache(TimeSpan.FromMinutes 10.0)

        // Generate nonce secret
        let nonceSecret = RandomNumberGenerator.GetBytes(32)

        // Bundle dependencies
        let deps: Endpoints.EndpointDeps =
            { Config = config
              TokenStore = tokenStore
              RequestStore = requestStore
              ReplayStore = replayStore
              AccountStore = accountStore
              HttpClient = httpClient
              ClientCache = clientCache
              NonceSecret = nonceSecret }

        // Build WebApplication
        let webAppBuilder = WebApplication.CreateBuilder()
        let app = webAppBuilder.Build()

        match builder.Port with
        | Some port -> app.Urls.Add(sprintf "http://+:%d" port)
        | None -> ()

        // Map OAuth discovery endpoints
        app.MapGet(
            "/.well-known/oauth-authorization-server",
            Func<HttpContext, _>(fun ctx -> Endpoints.serverMetadata deps ctx)
        )
        |> ignore

        app.MapGet(
            "/.well-known/oauth-protected-resource",
            Func<HttpContext, _>(fun ctx -> Endpoints.protectedResourceMetadata deps ctx)
        )
        |> ignore

        // Map OAuth protocol endpoints
        app.MapGet(
            "/oauth/jwks",
            Func<HttpContext, _>(fun ctx -> Endpoints.jwks deps ctx)
        )
        |> ignore

        app.MapPost(
            "/oauth/par",
            Func<HttpContext, _>(fun ctx -> Endpoints.par deps ctx)
        )
        |> ignore

        app.MapGet(
            "/oauth/authorize",
            Func<HttpContext, _>(fun ctx -> Endpoints.authorize deps ctx)
        )
        |> ignore

        app.MapPost(
            "/oauth/token",
            Func<HttpContext, _>(fun ctx -> Endpoints.token deps ctx)
        )
        |> ignore

        app.MapPost(
            "/oauth/revoke",
            Func<HttpContext, _>(fun ctx -> Endpoints.revoke deps ctx)
        )
        |> ignore

        // Map Consent API endpoints
        app.MapPost(
            "/api/sign-in",
            Func<HttpContext, _>(fun ctx -> ConsentApi.signIn deps ctx)
        )
        |> ignore

        app.MapPost(
            "/api/consent",
            Func<HttpContext, _>(fun ctx -> ConsentApi.consent deps ctx)
        )
        |> ignore

        app.MapPost(
            "/api/reject",
            Func<HttpContext, _>(fun ctx -> ConsentApi.reject deps ctx)
        )
        |> ignore

        app
