namespace FSharp.ATProto.Core

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Threading.Tasks

/// <summary>
/// XRPC transport layer for AT Protocol API calls.
/// Provides functions for executing queries (HTTP GET) and procedures (HTTP POST)
/// against an authenticated <see cref="AtpAgent"/>.
/// </summary>
/// <remarks>
/// All public functions in this module automatically handle:
/// <list type="bullet">
///   <item><description>Bearer token authentication from the agent's session.</description></item>
///   <item><description>Automatic session refresh on 401 <c>ExpiredToken</c> responses (retries once with a new access token).</description></item>
///   <item><description>Rate-limit retry on 429 responses (waits for the <c>Retry-After</c> duration, then retries once).</description></item>
///   <item><description>JSON serialization/deserialization using <see cref="Json.options"/>.</description></item>
///   <item><description>Extra headers from <see cref="AtpAgent.ExtraHeaders"/> (e.g. proxy headers).</description></item>
/// </list>
/// </remarks>
module Xrpc =

    let private addAuth (agent : AtpAgent) (request : HttpRequestMessage) =
        match agent.AuthenticateRequest with
        | Some authenticate ->
            authenticate request
        | None ->
            match agent.Session with
            | Some session -> request.Headers.Authorization <- AuthenticationHeaderValue ("Bearer", session.AccessJwt)
            | None -> ()

        for (key, value) in agent.ExtraHeaders do
            request.Headers.TryAddWithoutValidation (key, value) |> ignore

    let private tryDeserializeError (response : HttpResponseMessage) : Task<XrpcError> =
        task {
            try
                let! body = response.Content.ReadAsStringAsync ()
                let doc = JsonDocument.Parse (body)
                let root = doc.RootElement

                let error =
                    match root.TryGetProperty ("error") with
                    | true, v -> Some (v.GetString ())
                    | false, _ -> None

                let message =
                    match root.TryGetProperty ("message") with
                    | true, v -> Some (v.GetString ())
                    | false, _ -> None

                return
                    { StatusCode = int response.StatusCode
                      Error = error
                      Message = message }
            with _ ->
                return
                    { StatusCode = int response.StatusCode
                      Error = None
                      Message = None }
        }

    /// Refresh the current session using the refresh JWT.
    /// Uses raw HTTP to avoid circular dependency with procedure.
    let private refreshSession (agent : AtpAgent) : Task<Result<AtpSession, XrpcError>> =
        task {
            match agent.Session with
            | None ->
                return
                    Error
                        { StatusCode = 401
                          Error = Some "NoSession"
                          Message = Some "No session to refresh" }
            | Some session ->
                let url = $"{agent.BaseUrl}xrpc/com.atproto.server.refreshSession"
                let request = new HttpRequestMessage (HttpMethod.Post, url)
                // refreshSession uses the refresh JWT, not the access JWT
                request.Headers.Authorization <- AuthenticationHeaderValue ("Bearer", session.RefreshJwt)

                let! response = agent.HttpClient.SendAsync (request)

                if response.IsSuccessStatusCode then
                    let! body = response.Content.ReadAsStringAsync ()
                    let newSession = JsonSerializer.Deserialize<AtpSession> (body, Json.options)
                    agent.Session <- Some newSession
                    agent.OnSessionChanged |> Option.iter (fun f -> f ())
                    return Ok newSession
                else
                    let! error = tryDeserializeError response
                    return Error error
        }

    /// Try to refresh authentication using custom handler or default app-password refresh.
    let private tryRefresh (agent : AtpAgent) : Task<Result<unit, XrpcError>> =
        task {
            match agent.RefreshAuthentication with
            | Some refresh ->
                let! result = refresh ()
                match result with
                | Ok () ->
                    agent.OnSessionChanged |> Option.iter (fun f -> f ())
                    return Ok ()
                | Error e -> return Error e
            | None ->
                let! result = refreshSession agent
                return result |> Result.map ignore
        }

    let private waitForRateLimit (response : HttpResponseMessage) : Task<bool> =
        task {
            if int response.StatusCode = 429 then
                let retryAfter =
                    match response.Headers.RetryAfter with
                    | null -> 1.0
                    | ra when ra.Delta.HasValue -> ra.Delta.Value.TotalSeconds
                    | ra when ra.Date.HasValue ->
                        let diff = ra.Date.Value - DateTimeOffset.UtcNow
                        max 0.0 diff.TotalSeconds
                    | _ -> 1.0

                do! Task.Delay (int (retryAfter * 1000.0))
                return true
            else
                return false
        }

    /// <summary>
    /// Executes an XRPC query (HTTP GET) with no query-string parameters.
    /// </summary>
    /// <param name="nsid">The NSID of the XRPC method (e.g. <c>"app.bsky.actor.getProfile"</c>).</param>
    /// <param name="agent">The <see cref="AtpAgent"/> to send the request through.</param>
    /// <typeparam name="O">The output type to deserialize the JSON response body into.</typeparam>
    /// <returns>
    /// A <c>Task</c> resolving to <c>Ok</c> with the deserialized output on success,
    /// or <c>Error</c> with an <see cref="XrpcError"/> on failure.
    /// </returns>
    /// <remarks>
    /// Automatically retries once on 429 (rate limit) after waiting for the <c>Retry-After</c> duration.
    /// Automatically refreshes the session and retries once on 401 <c>ExpiredToken</c>.
    /// </remarks>
    let queryNoParams<'O> (nsid : string) (agent : AtpAgent) : Task<Result<'O, XrpcError>> =
        task {
            let url = $"{agent.BaseUrl}xrpc/{nsid}"
            let request = new HttpRequestMessage (HttpMethod.Get, url)
            addAuth agent request

            let! response = agent.HttpClient.SendAsync (request)

            if response.IsSuccessStatusCode then
                let! body = response.Content.ReadAsStringAsync ()
                let output = JsonSerializer.Deserialize<'O> (body, Json.options)
                return Ok output
            else
                let! error = tryDeserializeError response
                // Rate limit retry
                if error.StatusCode = 429 then
                    let! _ = waitForRateLimit response
                    let retryRequest = new HttpRequestMessage (HttpMethod.Get, url)
                    addAuth agent retryRequest
                    let! retryResponse = agent.HttpClient.SendAsync (retryRequest)

                    if retryResponse.IsSuccessStatusCode then
                        let! retryBody = retryResponse.Content.ReadAsStringAsync ()
                        return Ok (JsonSerializer.Deserialize<'O> (retryBody, Json.options))
                    else
                        let! retryError = tryDeserializeError retryResponse
                        return Error retryError
                // Auto-refresh on ExpiredToken
                elif
                    (error.StatusCode = 400 || error.StatusCode = 401)
                    && error.Error = Some "ExpiredToken"
                    && (agent.Session.IsSome || agent.RefreshAuthentication.IsSome)
                then
                    let! refreshResult = tryRefresh agent

                    match refreshResult with
                    | Ok () ->
                        let retryRequest = new HttpRequestMessage (HttpMethod.Get, url)
                        addAuth agent retryRequest
                        let! retryResponse = agent.HttpClient.SendAsync (retryRequest)

                        if retryResponse.IsSuccessStatusCode then
                            let! retryBody = retryResponse.Content.ReadAsStringAsync ()
                            return Ok (JsonSerializer.Deserialize<'O> (retryBody, Json.options))
                        else
                            let! retryError = tryDeserializeError retryResponse
                            return Error retryError
                    | Error refreshError -> return Error refreshError
                else
                    return Error error
        }

    /// <summary>
    /// Executes an XRPC query (HTTP GET) with query-string parameters.
    /// </summary>
    /// <param name="nsid">The NSID of the XRPC method (e.g. <c>"app.bsky.feed.getTimeline"</c>).</param>
    /// <param name="parameters">
    /// An F# record whose fields are serialized to query-string parameters via <see cref="QueryParams.toQueryString"/>.
    /// Option fields that are <c>None</c> are omitted; list fields are emitted as repeated parameters.
    /// </param>
    /// <param name="agent">The <see cref="AtpAgent"/> to send the request through.</param>
    /// <typeparam name="P">The parameter record type.</typeparam>
    /// <typeparam name="O">The output type to deserialize the JSON response body into.</typeparam>
    /// <returns>
    /// A <c>Task</c> resolving to <c>Ok</c> with the deserialized output on success,
    /// or <c>Error</c> with an <see cref="XrpcError"/> on failure.
    /// </returns>
    /// <remarks>
    /// Automatically retries once on 429 (rate limit) after waiting for the <c>Retry-After</c> duration.
    /// Automatically refreshes the session and retries once on 401 <c>ExpiredToken</c>.
    /// </remarks>
    /// <example>
    /// <code>
    /// type GetProfileParams = { Actor: string }
    /// let! result = Xrpc.query "app.bsky.actor.getProfile" { Actor = "my-handle.bsky.social" } agent
    /// </code>
    /// </example>
    let query<'P, 'O> (nsid : string) (parameters : 'P) (agent : AtpAgent) : Task<Result<'O, XrpcError>> =
        task {
            let queryString = QueryParams.toQueryString parameters
            let url = $"{agent.BaseUrl}xrpc/{nsid}{queryString}"
            let request = new HttpRequestMessage (HttpMethod.Get, url)
            addAuth agent request

            let! response = agent.HttpClient.SendAsync (request)

            if response.IsSuccessStatusCode then
                let! body = response.Content.ReadAsStringAsync ()
                let output = JsonSerializer.Deserialize<'O> (body, Json.options)
                return Ok output
            else
                let! error = tryDeserializeError response
                // Rate limit retry
                if error.StatusCode = 429 then
                    let! _ = waitForRateLimit response
                    let retryRequest = new HttpRequestMessage (HttpMethod.Get, url)
                    addAuth agent retryRequest
                    let! retryResponse = agent.HttpClient.SendAsync (retryRequest)

                    if retryResponse.IsSuccessStatusCode then
                        let! retryBody = retryResponse.Content.ReadAsStringAsync ()
                        return Ok (JsonSerializer.Deserialize<'O> (retryBody, Json.options))
                    else
                        let! retryError = tryDeserializeError retryResponse
                        return Error retryError
                // Auto-refresh on ExpiredToken
                elif
                    (error.StatusCode = 400 || error.StatusCode = 401)
                    && error.Error = Some "ExpiredToken"
                    && (agent.Session.IsSome || agent.RefreshAuthentication.IsSome)
                then
                    let! refreshResult = tryRefresh agent

                    match refreshResult with
                    | Ok () ->
                        // Retry the original request with new token
                        let retryRequest = new HttpRequestMessage (HttpMethod.Get, url)
                        addAuth agent retryRequest
                        let! retryResponse = agent.HttpClient.SendAsync (retryRequest)

                        if retryResponse.IsSuccessStatusCode then
                            let! retryBody = retryResponse.Content.ReadAsStringAsync ()
                            return Ok (JsonSerializer.Deserialize<'O> (retryBody, Json.options))
                        else
                            let! retryError = tryDeserializeError retryResponse
                            return Error retryError
                    | Error refreshError -> return Error refreshError
                else
                    return Error error
        }

    /// <summary>
    /// Executes an XRPC query (HTTP GET) with query-string parameters that returns raw bytes
    /// instead of JSON. Used for binary endpoints such as <c>com.atproto.sync.*</c>.
    /// </summary>
    /// <param name="nsid">The NSID of the XRPC method (e.g. <c>"com.atproto.sync.getBlob"</c>).</param>
    /// <param name="parameters">
    /// An F# record whose fields are serialized to query-string parameters via <see cref="QueryParams.toQueryString"/>.
    /// </param>
    /// <param name="agent">The <see cref="AtpAgent"/> to send the request through.</param>
    /// <typeparam name="P">The parameter record type.</typeparam>
    /// <returns>
    /// A <c>Task</c> resolving to <c>Ok</c> with the raw response bytes on success,
    /// or <c>Error</c> with an <see cref="XrpcError"/> on failure.
    /// </returns>
    /// <remarks>
    /// Automatically retries once on 429 (rate limit) after waiting for the <c>Retry-After</c> duration.
    /// Automatically refreshes the session and retries once on 401 <c>ExpiredToken</c>.
    /// </remarks>
    let queryBinary<'P> (nsid : string) (parameters : 'P) (agent : AtpAgent) : Task<Result<byte[], XrpcError>> =
        task {
            let queryString = QueryParams.toQueryString parameters
            let url = $"{agent.BaseUrl}xrpc/{nsid}{queryString}"
            let request = new HttpRequestMessage (HttpMethod.Get, url)
            addAuth agent request

            let! response = agent.HttpClient.SendAsync (request)

            if response.IsSuccessStatusCode then
                let! bytes = response.Content.ReadAsByteArrayAsync ()
                return Ok bytes
            else
                let! error = tryDeserializeError response
                // Rate limit retry
                if error.StatusCode = 429 then
                    let! _ = waitForRateLimit response
                    let retryRequest = new HttpRequestMessage (HttpMethod.Get, url)
                    addAuth agent retryRequest
                    let! retryResponse = agent.HttpClient.SendAsync (retryRequest)

                    if retryResponse.IsSuccessStatusCode then
                        let! retryBytes = retryResponse.Content.ReadAsByteArrayAsync ()
                        return Ok retryBytes
                    else
                        let! retryError = tryDeserializeError retryResponse
                        return Error retryError
                // Auto-refresh on ExpiredToken
                elif
                    (error.StatusCode = 400 || error.StatusCode = 401)
                    && error.Error = Some "ExpiredToken"
                    && (agent.Session.IsSome || agent.RefreshAuthentication.IsSome)
                then
                    let! refreshResult = tryRefresh agent

                    match refreshResult with
                    | Ok () ->
                        // Retry the original request with new token
                        let retryRequest = new HttpRequestMessage (HttpMethod.Get, url)
                        addAuth agent retryRequest
                        let! retryResponse = agent.HttpClient.SendAsync (retryRequest)

                        if retryResponse.IsSuccessStatusCode then
                            let! retryBytes = retryResponse.Content.ReadAsByteArrayAsync ()
                            return Ok retryBytes
                        else
                            let! retryError = tryDeserializeError retryResponse
                            return Error retryError
                    | Error refreshError -> return Error refreshError
                else
                    return Error error
        }

    /// <summary>
    /// Executes an XRPC procedure (HTTP POST) with a JSON request body and a JSON response body.
    /// </summary>
    /// <param name="nsid">The NSID of the XRPC method (e.g. <c>"com.atproto.repo.createRecord"</c>).</param>
    /// <param name="input">The input value to serialize as the JSON request body.</param>
    /// <param name="agent">The <see cref="AtpAgent"/> to send the request through.</param>
    /// <typeparam name="I">The input type to serialize as the JSON request body.</typeparam>
    /// <typeparam name="O">The output type to deserialize the JSON response body into.</typeparam>
    /// <returns>
    /// A <c>Task</c> resolving to <c>Ok</c> with the deserialized output on success,
    /// or <c>Error</c> with an <see cref="XrpcError"/> on failure.
    /// </returns>
    /// <remarks>
    /// The request body is serialized as <c>application/json</c> using <see cref="Json.options"/>.
    /// Automatically retries once on 429 (rate limit) after waiting for the <c>Retry-After</c> duration.
    /// Automatically refreshes the session and retries once on 401 <c>ExpiredToken</c>.
    /// </remarks>
    /// <example>
    /// <code>
    /// let input = {| repo = did; collection = "app.bsky.feed.post"; record = post |}
    /// let! result = Xrpc.procedure "com.atproto.repo.createRecord" input agent
    /// </code>
    /// </example>
    let procedure<'I, 'O> (nsid : string) (input : 'I) (agent : AtpAgent) : Task<Result<'O, XrpcError>> =
        task {
            let url = $"{agent.BaseUrl}xrpc/{nsid}"
            let json = JsonSerializer.Serialize (input, Json.options)
            let request = new HttpRequestMessage (HttpMethod.Post, url)
            request.Content <- new StringContent (json, Encoding.UTF8, "application/json")
            addAuth agent request

            let! response = agent.HttpClient.SendAsync (request)

            if response.IsSuccessStatusCode then
                let! body = response.Content.ReadAsStringAsync ()
                let output = JsonSerializer.Deserialize<'O> (body, Json.options)
                return Ok output
            else
                let! error = tryDeserializeError response
                // Rate limit retry
                if error.StatusCode = 429 then
                    let! _ = waitForRateLimit response
                    let retryRequest = new HttpRequestMessage (HttpMethod.Post, url)
                    retryRequest.Content <- new StringContent (json, Encoding.UTF8, "application/json")
                    addAuth agent retryRequest
                    let! retryResponse = agent.HttpClient.SendAsync (retryRequest)

                    if retryResponse.IsSuccessStatusCode then
                        let! retryBody = retryResponse.Content.ReadAsStringAsync ()
                        return Ok (JsonSerializer.Deserialize<'O> (retryBody, Json.options))
                    else
                        let! retryError = tryDeserializeError retryResponse
                        return Error retryError
                // Auto-refresh on ExpiredToken
                elif
                    (error.StatusCode = 400 || error.StatusCode = 401)
                    && error.Error = Some "ExpiredToken"
                    && (agent.Session.IsSome || agent.RefreshAuthentication.IsSome)
                then
                    let! refreshResult = tryRefresh agent

                    match refreshResult with
                    | Ok () ->
                        // Retry the original request with new token
                        let retryRequest = new HttpRequestMessage (HttpMethod.Post, url)
                        retryRequest.Content <- new StringContent (json, Encoding.UTF8, "application/json")
                        addAuth agent retryRequest
                        let! retryResponse = agent.HttpClient.SendAsync (retryRequest)

                        if retryResponse.IsSuccessStatusCode then
                            let! retryBody = retryResponse.Content.ReadAsStringAsync ()
                            return Ok (JsonSerializer.Deserialize<'O> (retryBody, Json.options))
                        else
                            let! retryError = tryDeserializeError retryResponse
                            return Error retryError
                    | Error refreshError -> return Error refreshError
                else
                    return Error error
        }

    /// <summary>
    /// Executes an XRPC procedure (HTTP POST) with a JSON request body that returns no response body.
    /// </summary>
    /// <param name="nsid">The NSID of the XRPC method (e.g. <c>"com.atproto.repo.deleteRecord"</c>).</param>
    /// <param name="input">The input value to serialize as the JSON request body.</param>
    /// <param name="agent">The <see cref="AtpAgent"/> to send the request through.</param>
    /// <typeparam name="I">The input type to serialize as the JSON request body.</typeparam>
    /// <returns>
    /// A <c>Task</c> resolving to <c>Ok ()</c> on success,
    /// or <c>Error</c> with an <see cref="XrpcError"/> on failure.
    /// </returns>
    /// <remarks>
    /// Use this for XRPC procedures that return 200 with no body (e.g. delete operations).
    /// The request body is serialized as <c>application/json</c> using <see cref="Json.options"/>.
    /// Automatically retries once on 429 (rate limit) after waiting for the <c>Retry-After</c> duration.
    /// Automatically refreshes the session and retries once on 401 <c>ExpiredToken</c>.
    /// </remarks>
    let procedureVoid<'I> (nsid : string) (input : 'I) (agent : AtpAgent) : Task<Result<unit, XrpcError>> =
        task {
            let url = $"{agent.BaseUrl}xrpc/{nsid}"
            let json = JsonSerializer.Serialize (input, Json.options)
            let request = new HttpRequestMessage (HttpMethod.Post, url)
            request.Content <- new StringContent (json, Encoding.UTF8, "application/json")
            addAuth agent request

            let! response = agent.HttpClient.SendAsync (request)

            if response.IsSuccessStatusCode then
                return Ok ()
            else
                let! error = tryDeserializeError response
                // Rate limit retry
                if error.StatusCode = 429 then
                    let! _ = waitForRateLimit response
                    let retryRequest = new HttpRequestMessage (HttpMethod.Post, url)
                    retryRequest.Content <- new StringContent (json, Encoding.UTF8, "application/json")
                    addAuth agent retryRequest
                    let! retryResponse = agent.HttpClient.SendAsync (retryRequest)

                    if retryResponse.IsSuccessStatusCode then
                        return Ok ()
                    else
                        let! retryError = tryDeserializeError retryResponse
                        return Error retryError
                // Auto-refresh on ExpiredToken
                elif
                    (error.StatusCode = 400 || error.StatusCode = 401)
                    && error.Error = Some "ExpiredToken"
                    && (agent.Session.IsSome || agent.RefreshAuthentication.IsSome)
                then
                    let! refreshResult = tryRefresh agent

                    match refreshResult with
                    | Ok () ->
                        // Retry the original request with new token
                        let retryRequest = new HttpRequestMessage (HttpMethod.Post, url)
                        retryRequest.Content <- new StringContent (json, Encoding.UTF8, "application/json")
                        addAuth agent retryRequest
                        let! retryResponse = agent.HttpClient.SendAsync (retryRequest)

                        if retryResponse.IsSuccessStatusCode then
                            return Ok ()
                        else
                            let! retryError = tryDeserializeError retryResponse
                            return Error retryError
                    | Error refreshError -> return Error refreshError
                else
                    return Error error
        }

    /// <summary>
    /// Paginates through a cursor-based XRPC query, returning an <c>IAsyncEnumerable</c> of pages.
    /// </summary>
    /// <param name="nsid">The NSID of the XRPC query method (e.g. <c>"app.bsky.feed.getTimeline"</c>).</param>
    /// <param name="initialParams">The initial query parameters (typically with cursor set to <c>None</c>).</param>
    /// <param name="getCursor">
    /// A function that extracts the next-page cursor from a response.
    /// Return <c>None</c> to signal that there are no more pages.
    /// </param>
    /// <param name="setCursor">
    /// A function that produces updated parameters with the given cursor value set.
    /// </param>
    /// <param name="agent">The <see cref="AtpAgent"/> to send requests through.</param>
    /// <typeparam name="P">The parameter record type.</typeparam>
    /// <typeparam name="O">The output type for each page of results.</typeparam>
    /// <returns>
    /// An <c>IAsyncEnumerable</c> that yields one <c>Result</c> per page.
    /// Enumeration stops when <paramref name="getCursor"/> returns <c>None</c> or when an error occurs.
    /// On error, the error result is yielded as the final element.
    /// </returns>
    /// <remarks>
    /// Each page is fetched lazily as the caller iterates the async enumerable.
    /// The underlying <see cref="query"/> function handles rate limiting and token refresh automatically.
    /// </remarks>
    /// <example>
    /// <code>
    /// let pages = Xrpc.paginate
    ///     "app.bsky.feed.getTimeline"
    ///     { Limit = Some 50; Cursor = None }
    ///     (fun output -> output.Cursor)
    ///     (fun cursor p -> { p with Cursor = cursor })
    ///     agent
    /// </code>
    /// </example>
    let paginate<'P, 'O>
        (nsid : string)
        (initialParams : 'P)
        (getCursor : 'O -> string option)
        (setCursor : string option -> 'P -> 'P)
        (agent : AtpAgent)
        : Collections.Generic.IAsyncEnumerable<Result<'O, XrpcError>> =
        { new Collections.Generic.IAsyncEnumerable<Result<'O, XrpcError>> with
            member _.GetAsyncEnumerator (_ct) =
                let mutable currentParams = initialParams
                let mutable finished = false
                let mutable current = Unchecked.defaultof<Result<'O, XrpcError>>

                { new Collections.Generic.IAsyncEnumerator<Result<'O, XrpcError>> with
                    member _.Current = current

                    member _.MoveNextAsync () =
                        if finished then
                            ValueTask<bool> (false)
                        else
                            ValueTask<bool> (
                                task {
                                    let! result = query<'P, 'O> nsid currentParams agent
                                    current <- result

                                    match result with
                                    | Ok output ->
                                        match getCursor output with
                                        | Some cursor -> currentParams <- setCursor (Some cursor) currentParams
                                        | None -> finished <- true
                                    | Error _ -> finished <- true

                                    return true
                                }
                            )

                    member _.DisposeAsync () = ValueTask () } }
