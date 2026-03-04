namespace FSharp.ATProto.XrpcServer

open System
open System.Security.Claims
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open FSharp.ATProto.Syntax

/// The HTTP method an XRPC endpoint expects.
[<RequireQualifiedAccess>]
type XrpcMethod =
    /// XRPC query -- mapped to HTTP GET.
    | Query
    /// XRPC procedure -- mapped to HTTP POST.
    | Procedure

/// AT Protocol standard error response body.
type XrpcErrorResponse = {
    Error: string
    Message: string
}

/// Configuration for per-endpoint rate limiting.
type RateLimitConfig = {
    /// Maximum number of requests allowed in the window.
    MaxRequests: int
    /// Duration of the sliding window.
    Window: TimeSpan
}

/// A registered XRPC endpoint.
type XrpcEndpoint = {
    /// The NSID for this endpoint (e.g. app.bsky.feed.getFeedSkeleton).
    Nsid: Nsid
    /// Whether this is a query (GET) or procedure (POST).
    Method: XrpcMethod
    /// The handler function.
    Handler: HttpContext -> Task<IResult>
    /// Optional per-endpoint rate limit.
    RateLimit: RateLimitConfig option
    /// Whether the endpoint requires authentication.
    RequireAuth: bool
}

/// Configuration for the XRPC server.
type XrpcServerConfig = {
    /// Registered XRPC endpoints.
    Endpoints: XrpcEndpoint list
    /// Optional token verification function for auth.
    /// Receives the bearer token string and returns a ClaimsPrincipal on success.
    VerifyToken: (string -> Task<Result<ClaimsPrincipal, string>>) option
    /// Global rate limit applied to all endpoints that don't have a per-endpoint limit.
    GlobalRateLimit: RateLimitConfig option
    /// JSON serializer options for request/response bodies.
    JsonOptions: JsonSerializerOptions
}

module XrpcServerConfig =

    /// Default JSON options matching AT Protocol conventions.
    let defaultJsonOptions =
        let opts = JsonSerializerOptions (PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        opts.DefaultIgnoreCondition <- System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        opts

    /// Create a default config with no endpoints or auth.
    let defaults = {
        Endpoints = []
        VerifyToken = None
        GlobalRateLimit = None
        JsonOptions = defaultJsonOptions
    }
