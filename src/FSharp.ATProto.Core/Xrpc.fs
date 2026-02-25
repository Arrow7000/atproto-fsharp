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
                // Auto-refresh on ExpiredToken
                if error.StatusCode = 401 && error.Error = Some "ExpiredToken" && agent.Session.IsSome then
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
                // Auto-refresh on ExpiredToken
                if error.StatusCode = 401 && error.Error = Some "ExpiredToken" && agent.Session.IsSome then
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
                // Auto-refresh on ExpiredToken
                if error.StatusCode = 401 && error.Error = Some "ExpiredToken" && agent.Session.IsSome then
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
