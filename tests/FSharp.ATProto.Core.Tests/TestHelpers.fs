module TestHelpers

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks

/// Create a mock HttpMessageHandler that calls the given function for each request.
type MockHandler (handler : HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler ()

    override _.SendAsync (request : HttpRequestMessage, _cancellationToken : CancellationToken) =
        Task.FromResult (handler request)

/// Create an HttpResponseMessage with a JSON body.
let jsonResponse (statusCode : HttpStatusCode) (body : obj) =
    let json = JsonSerializer.Serialize (body)
    let response = new HttpResponseMessage (statusCode)
    response.Content <- new StringContent (json, Encoding.UTF8, "application/json")
    response

/// Create an HttpResponseMessage with no body.
let emptyResponse (statusCode : HttpStatusCode) = new HttpResponseMessage (statusCode)
