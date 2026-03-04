module TestHelpers

open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks

type MockHandler (handler : HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler ()

    override _.SendAsync (request, _cancellationToken) = Task.FromResult (handler request)

let jsonResponse (statusCode : HttpStatusCode) (body : obj) =
    let json = JsonSerializer.Serialize (body)
    let response = new HttpResponseMessage (statusCode)
    response.Content <- new StringContent (json, Encoding.UTF8, "application/json")
    response

let binaryResponse (statusCode : HttpStatusCode) (bytes : byte[]) =
    let response = new HttpResponseMessage (statusCode)
    response.Content <- new ByteArrayContent (bytes)
    response.Content.Headers.ContentType <- Headers.MediaTypeHeaderValue ("application/octet-stream")
    response

let emptyResponse (statusCode : HttpStatusCode) = new HttpResponseMessage (statusCode)

let createMockAgent (handler : HttpRequestMessage -> HttpResponseMessage) =
    let httpClient = new HttpClient (new MockHandler (handler))
    FSharp.ATProto.Core.AtpAgent.createWithClient httpClient "https://bsky.social"
