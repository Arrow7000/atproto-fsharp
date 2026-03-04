namespace FSharp.ATProto.XrpcServer

open System
open System.Collections.Concurrent
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open FSharp.ATProto.Syntax

/// XRPC server builder and configuration.
module XrpcServer =

    /// Health check handler returning server status.
    let healthCheck () : HttpContext -> Task<IResult> =
        fun (_ctx : HttpContext) ->
            task {
                return Results.Json ({| version = "1.0.0" |}, XrpcServerConfig.defaultJsonOptions)
            }

    /// Create an XRPC endpoint definition.
    let endpoint (nsid : Nsid) (method : XrpcMethod) (handler : HttpContext -> Task<IResult>) : XrpcEndpoint = {
        Nsid = nsid
        Method = method
        Handler = handler
        RateLimit = None
        RequireAuth = false
    }

    /// Set a rate limit on an endpoint.
    let withRateLimit (config : RateLimitConfig) (ep : XrpcEndpoint) : XrpcEndpoint =
        { ep with RateLimit = Some config }

    /// Mark an endpoint as requiring authentication.
    let withAuth (ep : XrpcEndpoint) : XrpcEndpoint =
        { ep with RequireAuth = true }

    /// Add an endpoint to the server config.
    let addEndpoint (ep : XrpcEndpoint) (config : XrpcServerConfig) : XrpcServerConfig =
        { config with Endpoints = config.Endpoints @ [ ep ] }

    /// Set the token verification function.
    let withTokenVerifier (verifyToken : string -> Task<Result<System.Security.Claims.ClaimsPrincipal, string>>) (config : XrpcServerConfig) : XrpcServerConfig =
        { config with VerifyToken = Some verifyToken }

    /// Set a global rate limit for endpoints without per-endpoint limits.
    let withGlobalRateLimit (rateLimit : RateLimitConfig) (config : XrpcServerConfig) : XrpcServerConfig =
        { config with GlobalRateLimit = Some rateLimit }

    /// Set custom JSON options.
    let withJsonOptions (options : JsonSerializerOptions) (config : XrpcServerConfig) : XrpcServerConfig =
        { config with JsonOptions = options }

    /// Build and configure a WebApplication with all registered XRPC endpoints.
    let configure (config : XrpcServerConfig) : WebApplication =
        let builder = WebApplication.CreateBuilder ()
        let app = builder.Build ()

        // Create rate limiter states for endpoints that have rate limits
        let rateLimiters = ConcurrentDictionary<string, RateLimiter.RateLimiterState> ()

        // Initialize rate limiters
        for ep in config.Endpoints do
            let effectiveLimit =
                match ep.RateLimit with
                | Some rl -> Some rl
                | None -> config.GlobalRateLimit

            match effectiveLimit with
            | Some rl ->
                let nsidStr = Nsid.value ep.Nsid
                rateLimiters.TryAdd (nsidStr, RateLimiter.create rl) |> ignore
            | None -> ()

        // Register health check
        app.MapGet (
            "/_health",
            Func<HttpContext, Task<IResult>> (fun ctx -> healthCheck () ctx)
        )
        |> ignore

        // Register each XRPC endpoint
        for ep in config.Endpoints do
            let nsidStr = Nsid.value ep.Nsid
            let path = sprintf "/xrpc/%s" nsidStr

            let wrappedHandler =
                Func<HttpContext, Task<IResult>> (fun ctx ->
                    task {
                        // 1. Auth check
                        if ep.RequireAuth then
                            match config.VerifyToken with
                            | None ->
                                return Middleware.xrpcError 500 "InternalError" "Auth is required but no verifier is configured" config.JsonOptions
                            | Some verifyToken ->
                                let! authResult = Auth.verifyRequest verifyToken ctx

                                match authResult with
                                | Error msg ->
                                    return Middleware.authRequired msg config.JsonOptions
                                | Ok _ ->
                                    // Auth passed, continue to rate limit + handler
                                    // 2. Rate limit check
                                    match rateLimiters.TryGetValue nsidStr with
                                    | true, limiter ->
                                        let key = Middleware.clientKey ctx
                                        let now = DateTimeOffset.UtcNow

                                        match RateLimiter.tryAllow key now limiter with
                                        | Error retryAfter ->
                                            return Middleware.rateLimitExceeded retryAfter config.JsonOptions
                                        | Ok remaining ->
                                            ctx.Response.Headers.["RateLimit-Remaining"] <- Microsoft.Extensions.Primitives.StringValues (string remaining)
                                            // 3. Call the handler
                                            return! ep.Handler ctx
                                    | false, _ ->
                                        return! ep.Handler ctx
                        else
                            // No auth required
                            // Try optional auth (extract token if present but don't fail)
                            match config.VerifyToken with
                            | Some verifyToken ->
                                match Auth.extractBearerToken ctx with
                                | Some _ ->
                                    let! _ = Auth.verifyRequest verifyToken ctx
                                    ()
                                | None -> ()
                            | None -> ()

                            // Rate limit check
                            match rateLimiters.TryGetValue nsidStr with
                            | true, limiter ->
                                let key = Middleware.clientKey ctx
                                let now = DateTimeOffset.UtcNow

                                match RateLimiter.tryAllow key now limiter with
                                | Error retryAfter ->
                                    return Middleware.rateLimitExceeded retryAfter config.JsonOptions
                                | Ok remaining ->
                                    ctx.Response.Headers.["RateLimit-Remaining"] <- Microsoft.Extensions.Primitives.StringValues (string remaining)
                                    return! ep.Handler ctx
                            | false, _ ->
                                return! ep.Handler ctx
                    })

            match ep.Method with
            | XrpcMethod.Query ->
                app.MapGet (path, wrappedHandler) |> ignore
            | XrpcMethod.Procedure ->
                app.MapPost (path, wrappedHandler) |> ignore

        app

    /// Configure and set the server to listen on the given port.
    let configureWithPort (port : int) (config : XrpcServerConfig) : WebApplication =
        let builder = WebApplication.CreateBuilder ()
        builder.WebHost.UseUrls (sprintf "http://0.0.0.0:%d" port) |> ignore
        let app = builder.Build ()

        // Create rate limiter states
        let rateLimiters = ConcurrentDictionary<string, RateLimiter.RateLimiterState> ()

        for ep in config.Endpoints do
            let effectiveLimit =
                match ep.RateLimit with
                | Some rl -> Some rl
                | None -> config.GlobalRateLimit

            match effectiveLimit with
            | Some rl ->
                let nsidStr = Nsid.value ep.Nsid
                rateLimiters.TryAdd (nsidStr, RateLimiter.create rl) |> ignore
            | None -> ()

        // Register health check
        app.MapGet (
            "/_health",
            Func<HttpContext, Task<IResult>> (fun ctx -> healthCheck () ctx)
        )
        |> ignore

        // Register each XRPC endpoint
        for ep in config.Endpoints do
            let nsidStr = Nsid.value ep.Nsid
            let path = sprintf "/xrpc/%s" nsidStr

            let wrappedHandler =
                Func<HttpContext, Task<IResult>> (fun ctx ->
                    task {
                        // Auth check
                        if ep.RequireAuth then
                            match config.VerifyToken with
                            | None ->
                                return Middleware.xrpcError 500 "InternalError" "Auth is required but no verifier is configured" config.JsonOptions
                            | Some verifyToken ->
                                let! authResult = Auth.verifyRequest verifyToken ctx

                                match authResult with
                                | Error msg ->
                                    return Middleware.authRequired msg config.JsonOptions
                                | Ok _ ->
                                    match rateLimiters.TryGetValue nsidStr with
                                    | true, limiter ->
                                        let key = Middleware.clientKey ctx
                                        let now = DateTimeOffset.UtcNow

                                        match RateLimiter.tryAllow key now limiter with
                                        | Error retryAfter ->
                                            return Middleware.rateLimitExceeded retryAfter config.JsonOptions
                                        | Ok remaining ->
                                            ctx.Response.Headers.["RateLimit-Remaining"] <- Microsoft.Extensions.Primitives.StringValues (string remaining)
                                            return! ep.Handler ctx
                                    | false, _ ->
                                        return! ep.Handler ctx
                        else
                            match config.VerifyToken with
                            | Some verifyToken ->
                                match Auth.extractBearerToken ctx with
                                | Some _ ->
                                    let! _ = Auth.verifyRequest verifyToken ctx
                                    ()
                                | None -> ()
                            | None -> ()

                            match rateLimiters.TryGetValue nsidStr with
                            | true, limiter ->
                                let key = Middleware.clientKey ctx
                                let now = DateTimeOffset.UtcNow

                                match RateLimiter.tryAllow key now limiter with
                                | Error retryAfter ->
                                    return Middleware.rateLimitExceeded retryAfter config.JsonOptions
                                | Ok remaining ->
                                    ctx.Response.Headers.["RateLimit-Remaining"] <- Microsoft.Extensions.Primitives.StringValues (string remaining)
                                    return! ep.Handler ctx
                            | false, _ ->
                                return! ep.Handler ctx
                    })

            match ep.Method with
            | XrpcMethod.Query ->
                app.MapGet (path, wrappedHandler) |> ignore
            | XrpcMethod.Procedure ->
                app.MapPost (path, wrappedHandler) |> ignore

        app
