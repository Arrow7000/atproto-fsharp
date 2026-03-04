namespace FSharp.ATProto.Core

open System
open System.Net.Http
open System.Threading.Tasks
open FSharp.ATProto.Syntax

/// <summary>
/// Represents an error response from an XRPC endpoint.
/// AT Protocol servers return JSON error bodies with optional <c>error</c> and <c>message</c> fields
/// alongside an HTTP status code.
/// </summary>
/// <remarks>
/// Common error codes include <c>ExpiredToken</c> (401), <c>RateLimitExceeded</c> (429),
/// and <c>InvalidRequest</c> (400). The <see cref="Xrpc"/> module handles <c>ExpiredToken</c>
/// and rate-limit errors automatically.
/// </remarks>
type XrpcError =
    {
        /// <summary>The HTTP status code returned by the server (e.g. 400, 401, 429, 500).</summary>
        StatusCode : int
        /// <summary>The machine-readable error code from the response body (e.g. "ExpiredToken", "InvalidRequest"), or <c>None</c> if absent.</summary>
        Error : string option
        /// <summary>A human-readable error message from the response body, or <c>None</c> if absent.</summary>
        Message : string option
    }

/// <summary>
/// An authenticated session with an AT Protocol Personal Data Server (PDS).
/// Obtained by calling <see cref="AtpAgent.login"/> with valid credentials.
/// </summary>
/// <remarks>
/// Sessions contain a short-lived access JWT for API calls and a longer-lived refresh JWT
/// for obtaining new access tokens. The <see cref="Xrpc"/> module automatically refreshes
/// expired access tokens using the refresh JWT.
/// </remarks>
type AtpSession =
    {
        /// <summary>The short-lived JWT used to authenticate XRPC requests.</summary>
        AccessJwt : string
        /// <summary>The longer-lived JWT used to refresh the session when the access token expires.</summary>
        RefreshJwt : string
        /// <summary>The DID (Decentralized Identifier) of the authenticated user (e.g. "did:plc:xyz123").</summary>
        Did : Did
        /// <summary>The handle of the authenticated user (e.g. "my-handle.bsky.social").</summary>
        Handle : Handle
    }

/// <summary>
/// Client agent for communicating with an AT Protocol Personal Data Server (PDS).
/// Holds the HTTP client, base URL, optional authenticated session, and extra headers.
/// </summary>
/// <remarks>
/// Create an agent with <see cref="AtpAgent.create"/> or <see cref="AtpAgent.createWithClient"/>,
/// then authenticate with <see cref="AtpAgent.login"/>. The agent's <see cref="Session"/> field
/// is mutable: it is updated automatically on login and token refresh.
/// </remarks>
/// <example>
/// <code>
/// let agent = AtpAgent.create "https://bsky.social"
/// let! session = AtpAgent.login "my-handle.bsky.social" "app-password-here" agent
/// </code>
/// </example>
type AtpAgent =
    {
        /// <summary>The <see cref="System.Net.Http.HttpClient"/> used for all HTTP requests to the PDS.</summary>
        HttpClient : HttpClient
        /// <summary>The base URL of the PDS, always ending with a trailing slash (e.g. "https://bsky.social/").</summary>
        BaseUrl : System.Uri
        /// <summary>
        /// The current authenticated session, or <c>None</c> if not logged in.
        /// This field is mutable and is updated automatically by <see cref="AtpAgent.login"/>
        /// and by the automatic token refresh logic in <see cref="Xrpc"/>.
        /// </summary>
        mutable Session : AtpSession option
        /// <summary>
        /// Additional HTTP headers sent with every request (e.g. the <c>atproto-proxy</c> header
        /// for Bluesky Chat service proxying).
        /// </summary>
        ExtraHeaders : (string * string) list
        /// <summary>
        /// Custom authentication handler. When set, overrides the default Bearer token auth.
        /// The function receives the <see cref="HttpRequestMessage"/> (with Method and RequestUri already set)
        /// and should add appropriate Authorization/DPoP headers.
        /// Used by OAuth/DPoP bridge to inject DPoP-bound authentication.
        /// </summary>
        AuthenticateRequest : (HttpRequestMessage -> unit) option
        /// <summary>
        /// Custom session refresh handler. When set, overrides the default app-password refresh
        /// (<c>com.atproto.server.refreshSession</c>). Should update internal session state and return
        /// <c>Ok ()</c> on success or <c>Error</c> with an <see cref="XrpcError"/> on failure.
        /// Used by OAuth bridge for DPoP token refresh.
        /// </summary>
        RefreshAuthentication : (unit -> Task<Result<unit, XrpcError>>) option
        /// <summary>
        /// Called when session state changes (login, token refresh, session resume).
        /// Consumers can use this to persist sessions to disk or database.
        /// </summary>
        OnSessionChanged : (unit -> unit) option
    }
