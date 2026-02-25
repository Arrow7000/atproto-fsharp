namespace FSharp.ATProto.Core

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Threading.Tasks

/// XRPC transport for AT Protocol API calls.
module Xrpc =

    let private addAuth (agent: AtpAgent) (request: HttpRequestMessage) =
        match agent.Session with
        | Some session ->
            request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", session.AccessJwt)
        | None -> ()

    let private tryDeserializeError (response: HttpResponseMessage) : Task<XrpcError> =
        task {
            try
                let! body = response.Content.ReadAsStringAsync()
                let doc = JsonDocument.Parse(body)
                let root = doc.RootElement
                let error =
                    match root.TryGetProperty("error") with
                    | true, v -> Some(v.GetString())
                    | false, _ -> None
                let message =
                    match root.TryGetProperty("message") with
                    | true, v -> Some(v.GetString())
                    | false, _ -> None
                return { StatusCode = int response.StatusCode; Error = error; Message = message }
            with _ ->
                return { StatusCode = int response.StatusCode; Error = None; Message = None }
        }

    /// Refresh the current session using the refresh JWT.
    /// Uses raw HTTP to avoid circular dependency with procedure.
    let private refreshSession (agent: AtpAgent) : Task<Result<AtpSession, XrpcError>> =
        task {
            match agent.Session with
            | None ->
                return Error { StatusCode = 401; Error = Some "NoSession"; Message = Some "No session to refresh" }
            | Some session ->
                let url = $"{agent.BaseUrl}xrpc/com.atproto.server.refreshSession"
                let request = new HttpRequestMessage(HttpMethod.Post, url)
                // refreshSession uses the refresh JWT, not the access JWT
                request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", session.RefreshJwt)

                let! response = agent.HttpClient.SendAsync(request)

                if response.IsSuccessStatusCode then
                    let! body = response.Content.ReadAsStringAsync()
                    let newSession = JsonSerializer.Deserialize<AtpSession>(body, Json.options)
                    agent.Session <- Some newSession
                    return Ok newSession
                else
                    let! error = tryDeserializeError response
                    return Error error
        }

    let private waitForRateLimit (response: HttpResponseMessage) : Task<bool> =
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
                do! Task.Delay(int (retryAfter * 1000.0))
                return true
            else
                return false
        }

    /// Execute an XRPC query (HTTP GET) with no parameters.
    let queryNoParams<'O> (nsid: string) (agent: AtpAgent) : Task<Result<'O, XrpcError>> =
        task {
            let url = $"{agent.BaseUrl}xrpc/{nsid}"
            let request = new HttpRequestMessage(HttpMethod.Get, url)
            addAuth agent request

            let! response = agent.HttpClient.SendAsync(request)

            if response.IsSuccessStatusCode then
                let! body = response.Content.ReadAsStringAsync()
                let output = JsonSerializer.Deserialize<'O>(body, Json.options)
                return Ok output
            else
                let! error = tryDeserializeError response
                // Rate limit retry
                if error.StatusCode = 429 then
                    let! _ = waitForRateLimit response
                    let retryRequest = new HttpRequestMessage(HttpMethod.Get, url)
                    addAuth agent retryRequest
                    let! retryResponse = agent.HttpClient.SendAsync(retryRequest)
                    if retryResponse.IsSuccessStatusCode then
                        let! retryBody = retryResponse.Content.ReadAsStringAsync()
                        return Ok(JsonSerializer.Deserialize<'O>(retryBody, Json.options))
                    else
                        let! retryError = tryDeserializeError retryResponse
                        return Error retryError
                // Auto-refresh on ExpiredToken
                elif error.StatusCode = 401 && error.Error = Some "ExpiredToken" && agent.Session.IsSome then
                    let! refreshResult = refreshSession agent
                    match refreshResult with
                    | Ok _ ->
                        let retryRequest = new HttpRequestMessage(HttpMethod.Get, url)
                        addAuth agent retryRequest
                        let! retryResponse = agent.HttpClient.SendAsync(retryRequest)
                        if retryResponse.IsSuccessStatusCode then
                            let! retryBody = retryResponse.Content.ReadAsStringAsync()
                            return Ok(JsonSerializer.Deserialize<'O>(retryBody, Json.options))
                        else
                            let! retryError = tryDeserializeError retryResponse
                            return Error retryError
                    | Error refreshError ->
                        return Error refreshError
                else
                    return Error error
        }

    /// Execute an XRPC query (HTTP GET).
    let query<'P, 'O> (nsid: string) (parameters: 'P) (agent: AtpAgent) : Task<Result<'O, XrpcError>> =
        task {
            let queryString = QueryParams.toQueryString parameters
            let url = $"{agent.BaseUrl}xrpc/{nsid}{queryString}"
            let request = new HttpRequestMessage(HttpMethod.Get, url)
            addAuth agent request

            let! response = agent.HttpClient.SendAsync(request)

            if response.IsSuccessStatusCode then
                let! body = response.Content.ReadAsStringAsync()
                let output = JsonSerializer.Deserialize<'O>(body, Json.options)
                return Ok output
            else
                let! error = tryDeserializeError response
                // Rate limit retry
                if error.StatusCode = 429 then
                    let! _ = waitForRateLimit response
                    let retryRequest = new HttpRequestMessage(HttpMethod.Get, url)
                    addAuth agent retryRequest
                    let! retryResponse = agent.HttpClient.SendAsync(retryRequest)
                    if retryResponse.IsSuccessStatusCode then
                        let! retryBody = retryResponse.Content.ReadAsStringAsync()
                        return Ok(JsonSerializer.Deserialize<'O>(retryBody, Json.options))
                    else
                        let! retryError = tryDeserializeError retryResponse
                        return Error retryError
                // Auto-refresh on ExpiredToken
                elif error.StatusCode = 401 && error.Error = Some "ExpiredToken" && agent.Session.IsSome then
                    let! refreshResult = refreshSession agent
                    match refreshResult with
                    | Ok _ ->
                        // Retry the original request with new token
                        let retryRequest = new HttpRequestMessage(HttpMethod.Get, url)
                        addAuth agent retryRequest
                        let! retryResponse = agent.HttpClient.SendAsync(retryRequest)
                        if retryResponse.IsSuccessStatusCode then
                            let! retryBody = retryResponse.Content.ReadAsStringAsync()
                            return Ok(JsonSerializer.Deserialize<'O>(retryBody, Json.options))
                        else
                            let! retryError = tryDeserializeError retryResponse
                            return Error retryError
                    | Error refreshError ->
                        return Error refreshError
                else
                    return Error error
        }

    /// Execute an XRPC procedure (HTTP POST with JSON body).
    let procedure<'I, 'O> (nsid: string) (input: 'I) (agent: AtpAgent) : Task<Result<'O, XrpcError>> =
        task {
            let url = $"{agent.BaseUrl}xrpc/{nsid}"
            let json = JsonSerializer.Serialize(input, Json.options)
            let request = new HttpRequestMessage(HttpMethod.Post, url)
            request.Content <- new StringContent(json, Encoding.UTF8, "application/json")
            addAuth agent request

            let! response = agent.HttpClient.SendAsync(request)

            if response.IsSuccessStatusCode then
                let! body = response.Content.ReadAsStringAsync()
                let output = JsonSerializer.Deserialize<'O>(body, Json.options)
                return Ok output
            else
                let! error = tryDeserializeError response
                // Rate limit retry
                if error.StatusCode = 429 then
                    let! _ = waitForRateLimit response
                    let retryRequest = new HttpRequestMessage(HttpMethod.Post, url)
                    retryRequest.Content <- new StringContent(json, Encoding.UTF8, "application/json")
                    addAuth agent retryRequest
                    let! retryResponse = agent.HttpClient.SendAsync(retryRequest)
                    if retryResponse.IsSuccessStatusCode then
                        let! retryBody = retryResponse.Content.ReadAsStringAsync()
                        return Ok(JsonSerializer.Deserialize<'O>(retryBody, Json.options))
                    else
                        let! retryError = tryDeserializeError retryResponse
                        return Error retryError
                // Auto-refresh on ExpiredToken
                elif error.StatusCode = 401 && error.Error = Some "ExpiredToken" && agent.Session.IsSome then
                    let! refreshResult = refreshSession agent
                    match refreshResult with
                    | Ok _ ->
                        // Retry the original request with new token
                        let retryRequest = new HttpRequestMessage(HttpMethod.Post, url)
                        retryRequest.Content <- new StringContent(json, Encoding.UTF8, "application/json")
                        addAuth agent retryRequest
                        let! retryResponse = agent.HttpClient.SendAsync(retryRequest)
                        if retryResponse.IsSuccessStatusCode then
                            let! retryBody = retryResponse.Content.ReadAsStringAsync()
                            return Ok(JsonSerializer.Deserialize<'O>(retryBody, Json.options))
                        else
                            let! retryError = tryDeserializeError retryResponse
                            return Error retryError
                    | Error refreshError ->
                        return Error refreshError
                else
                    return Error error
        }

    /// Execute an XRPC procedure with no response body.
    let procedureVoid<'I> (nsid: string) (input: 'I) (agent: AtpAgent) : Task<Result<unit, XrpcError>> =
        task {
            let url = $"{agent.BaseUrl}xrpc/{nsid}"
            let json = JsonSerializer.Serialize(input, Json.options)
            let request = new HttpRequestMessage(HttpMethod.Post, url)
            request.Content <- new StringContent(json, Encoding.UTF8, "application/json")
            addAuth agent request

            let! response = agent.HttpClient.SendAsync(request)

            if response.IsSuccessStatusCode then
                return Ok()
            else
                let! error = tryDeserializeError response
                // Rate limit retry
                if error.StatusCode = 429 then
                    let! _ = waitForRateLimit response
                    let retryRequest = new HttpRequestMessage(HttpMethod.Post, url)
                    retryRequest.Content <- new StringContent(json, Encoding.UTF8, "application/json")
                    addAuth agent retryRequest
                    let! retryResponse = agent.HttpClient.SendAsync(retryRequest)
                    if retryResponse.IsSuccessStatusCode then
                        return Ok()
                    else
                        let! retryError = tryDeserializeError retryResponse
                        return Error retryError
                // Auto-refresh on ExpiredToken
                elif error.StatusCode = 401 && error.Error = Some "ExpiredToken" && agent.Session.IsSome then
                    let! refreshResult = refreshSession agent
                    match refreshResult with
                    | Ok _ ->
                        // Retry the original request with new token
                        let retryRequest = new HttpRequestMessage(HttpMethod.Post, url)
                        retryRequest.Content <- new StringContent(json, Encoding.UTF8, "application/json")
                        addAuth agent retryRequest
                        let! retryResponse = agent.HttpClient.SendAsync(retryRequest)
                        if retryResponse.IsSuccessStatusCode then
                            return Ok()
                        else
                            let! retryError = tryDeserializeError retryResponse
                            return Error retryError
                    | Error refreshError ->
                        return Error refreshError
                else
                    return Error error
        }

    /// Paginate through a cursor-based XRPC query.
    let paginate<'P, 'O>
        (nsid: string)
        (initialParams: 'P)
        (getCursor: 'O -> string option)
        (setCursor: string option -> 'P -> 'P)
        (agent: AtpAgent)
        : Collections.Generic.IAsyncEnumerable<Result<'O, XrpcError>> =
        { new Collections.Generic.IAsyncEnumerable<Result<'O, XrpcError>> with
            member _.GetAsyncEnumerator(_ct) =
                let mutable currentParams = initialParams
                let mutable finished = false
                let mutable current = Unchecked.defaultof<Result<'O, XrpcError>>
                { new Collections.Generic.IAsyncEnumerator<Result<'O, XrpcError>> with
                    member _.Current = current
                    member _.MoveNextAsync() =
                        if finished then
                            ValueTask<bool>(false)
                        else
                            ValueTask<bool>(task {
                                let! result = query<'P, 'O> nsid currentParams agent
                                current <- result
                                match result with
                                | Ok output ->
                                    match getCursor output with
                                    | Some cursor ->
                                        currentParams <- setCursor (Some cursor) currentParams
                                    | None ->
                                        finished <- true
                                | Error _ ->
                                    finished <- true
                                return true
                            })
                    member _.DisposeAsync() = ValueTask()
                }
        }
