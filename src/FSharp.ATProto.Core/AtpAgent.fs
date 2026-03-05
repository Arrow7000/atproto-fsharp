namespace FSharp.ATProto.Core

open System
open System.Net.Http
open System.Threading.Tasks

/// <summary>
/// Functions for creating and authenticating <see cref="AtpAgent"/> instances.
/// </summary>
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module AtpAgent =

    /// <summary>
    /// Creates a new unauthenticated agent pointing at the given PDS base URL.
    /// A new <see cref="System.Net.Http.HttpClient"/> is allocated internally.
    /// </summary>
    /// <param name="baseUrl">
    /// The PDS base URL (e.g. <c>"https://bsky.social"</c>). A trailing slash is appended if not present.
    /// </param>
    /// <returns>An unauthenticated <see cref="AtpAgent"/> ready for <see cref="login"/>.</returns>
    /// <example>
    /// <code>
    /// let agent = AtpAgent.create "https://bsky.social"
    /// </code>
    /// </example>
    let create (baseUrl : string) : AtpAgent =
        let uri =
            if baseUrl.EndsWith ("/") then
                Uri (baseUrl)
            else
                Uri (baseUrl + "/")

        { HttpClient = new HttpClient ()
          BaseUrl = uri
          Session = None
          ExtraHeaders = []
          AuthenticateRequest = None
          RefreshAuthentication = None
          OnSessionChanged = None }

    /// <summary>
    /// Creates a new unauthenticated agent with a caller-supplied <see cref="System.Net.Http.HttpClient"/>.
    /// Useful for testing with mock HTTP handlers or custom client configuration.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for all requests.</param>
    /// <param name="baseUrl">
    /// The PDS base URL (e.g. <c>"https://bsky.social"</c>). A trailing slash is appended if not present.
    /// </param>
    /// <returns>An unauthenticated <see cref="AtpAgent"/> using the provided HTTP client.</returns>
    /// <example>
    /// <code>
    /// let handler = new MockHttpMessageHandler()
    /// let client = new HttpClient(handler)
    /// let agent = AtpAgent.createWithClient client "https://bsky.social"
    /// </code>
    /// </example>
    let createWithClient (httpClient : HttpClient) (baseUrl : string) : AtpAgent =
        let uri =
            if baseUrl.EndsWith ("/") then
                Uri (baseUrl)
            else
                Uri (baseUrl + "/")

        { HttpClient = httpClient
          BaseUrl = uri
          Session = None
          ExtraHeaders = []
          AuthenticateRequest = None
          RefreshAuthentication = None
          OnSessionChanged = None }

    /// <summary>
    /// Returns a copy of the agent configured to proxy requests through the Bluesky Chat service.
    /// Adds the <c>atproto-proxy: did:web:api.bsky.chat#bsky_chat</c> header.
    /// </summary>
    /// <param name="agent">The agent to copy with chat proxy configuration.</param>
    /// <returns>A new <see cref="AtpAgent"/> with the chat proxy header prepended to <see cref="AtpAgent.ExtraHeaders"/>.</returns>
    /// <remarks>
    /// The returned agent shares the same <see cref="System.Net.Http.HttpClient"/> as the original
    /// but has an independent <see cref="AtpAgent.Session"/> field (it is a record copy).
    /// If you need chat functionality, prefer using the <see cref="FSharp.ATProto.Bluesky.Chat"/>
    /// module functions directly — they handle the proxy header automatically and always use the
    /// current session from the original agent.
    /// </remarks>
    let withChatProxy (agent : AtpAgent) : AtpAgent =
        { agent with
            ExtraHeaders = ("atproto-proxy", "did:web:api.bsky.chat#bsky_chat") :: agent.ExtraHeaders }

    /// <summary>
    /// Returns a copy of the agent configured with the <c>atproto-accept-labelers</c> header.
    /// This tells the server which labeler services to include labels from in responses.
    /// </summary>
    /// <param name="labelers">
    /// A list of labeler DIDs and optional redact flags. Each entry is a tuple of
    /// (labeler DID string, redact flag). When <c>redact=true</c>, the labeler's labels
    /// can cause content to be entirely removed from responses.
    /// </param>
    /// <param name="agent">The agent to copy with labeler configuration.</param>
    /// <returns>A new <see cref="AtpAgent"/> with the <c>atproto-accept-labelers</c> header.</returns>
    /// <remarks>
    /// The header format follows IETF RFC-8941 Structured Field Values:
    /// <c>did:plc:abc123;redact, did:plc:def456</c>
    /// </remarks>
    let configureLabelers (labelers : (string * bool) list) (agent : AtpAgent) : AtpAgent =
        let headerValue =
            labelers
            |> List.map (fun (did, redact) ->
                if redact then $"{did};redact"
                else did)
            |> String.concat ", "

        // Remove any existing atproto-accept-labelers header
        let filteredHeaders =
            agent.ExtraHeaders
            |> List.filter (fun (key, _) -> key <> "atproto-accept-labelers")

        { agent with
            ExtraHeaders = ("atproto-accept-labelers", headerValue) :: filteredHeaders }

    /// <summary>
    /// Logs in to a PDS with an identifier and app password.
    /// On success, stores the session on the agent for subsequent authenticated requests.
    /// </summary>
    /// <param name="identifier">A handle (e.g. <c>"my-handle.bsky.social"</c>) or DID (e.g. <c>"did:plc:xyz123"</c>).</param>
    /// <param name="password">An app password (not the account password).</param>
    /// <param name="agent">The agent to authenticate.</param>
    /// <returns>
    /// A <c>Task</c> resolving to <c>Ok</c> with the <see cref="AtpSession"/> on success,
    /// or <c>Error</c> with an <see cref="XrpcError"/> on failure.
    /// </returns>
    /// <remarks>
    /// Calls the <c>com.atproto.server.createSession</c> XRPC procedure.
    /// The session is stored mutably on the agent's <see cref="AtpAgent.Session"/> field.
    /// All subsequent XRPC calls through this agent will include the access token automatically.
    /// </remarks>
    /// <example>
    /// <code>
    /// let agent = AtpAgent.create "https://bsky.social"
    /// let! result = AtpAgent.login "my-handle.bsky.social" "app-password" agent
    /// match result with
    /// | Ok session -> printfn "Logged in as %s" session.Handle
    /// | Error e -> printfn "Login failed: %A" e.Message
    /// </code>
    /// </example>
    let login (identifier : string) (password : string) (agent : AtpAgent) : Task<Result<AtpSession, XrpcError>> =
        task {
            let input =
                {| identifier = identifier
                   password = password |}

            let! result =
                Xrpc.procedure<
                    {| identifier : string
                       password : string |},
                    AtpSession
                 >
                    "com.atproto.server.createSession"
                    input
                    agent

            match result with
            | Ok session ->
                agent.Session <- Some session
                agent.OnSessionChanged |> Option.iter (fun f -> f ())
                return Ok session
            | Error e -> return Error e
        }
