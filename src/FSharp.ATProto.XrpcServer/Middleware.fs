namespace FSharp.ATProto.XrpcServer

open System
open System.Collections.Concurrent
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

/// XRPC middleware for error formatting, rate limiting, and request validation.
module Middleware =

    /// Format an AT Protocol error response.
    /// Writes the JSON body directly to avoid DI dependency on JsonSerializerOptions service.
    let xrpcError (statusCode : int) (error : string) (message : string) (jsonOptions : JsonSerializerOptions) : IResult =
        let body = { Error = error; Message = message }

        { new IResult with
            member _.ExecuteAsync (ctx) =
                task {
                    ctx.Response.StatusCode <- statusCode
                    ctx.Response.ContentType <- "application/json"
                    let json = JsonSerializer.Serialize (body, jsonOptions)
                    do! ctx.Response.WriteAsync json
                } :> Task }

    /// Format an InvalidRequest error (400).
    let invalidRequest (message : string) (jsonOptions : JsonSerializerOptions) : IResult =
        xrpcError 400 "InvalidRequest" message jsonOptions

    /// Format a MethodNotImplemented error (501).
    let methodNotImplemented (nsid : string) (jsonOptions : JsonSerializerOptions) : IResult =
        xrpcError 501 "MethodNotImplemented" (sprintf "Method not implemented: %s" nsid) jsonOptions

    /// Format an AuthRequired error (401).
    let authRequired (message : string) (jsonOptions : JsonSerializerOptions) : IResult =
        xrpcError 401 "AuthenticationRequired" message jsonOptions

    /// Format a rate limit exceeded error (429) with Retry-After header.
    let rateLimitExceeded (retryAfterSeconds : float) (jsonOptions : JsonSerializerOptions) : IResult =
        let retryAfter = int (Math.Ceiling retryAfterSeconds)
        let body = { Error = "RateLimitExceeded"; Message = sprintf "Rate limit exceeded. Retry after %d seconds." retryAfter }
        // Use a custom result that sets the Retry-After header
        { new IResult with
            member _.ExecuteAsync (ctx) =
                task {
                    ctx.Response.StatusCode <- 429
                    ctx.Response.Headers.["Retry-After"] <- Microsoft.Extensions.Primitives.StringValues (string retryAfter)
                    ctx.Response.ContentType <- "application/json"
                    let json = JsonSerializer.Serialize (body, jsonOptions)
                    do! ctx.Response.WriteAsync json
                } :> Task }

    /// Extract the client key for rate limiting (uses remote IP or "unknown").
    let clientKey (ctx : HttpContext) : string =
        match ctx.Connection.RemoteIpAddress with
        | null -> "unknown"
        | ip -> ip.ToString ()

    /// Parse a required query parameter. Returns None with an error message if missing.
    let requireQueryParam (name : string) (ctx : HttpContext) : Result<string, string> =
        match ctx.Request.Query.[name].ToString () with
        | "" -> Error (sprintf "Required parameter '%s' is missing" name)
        | value -> Ok value

    /// Parse an optional query parameter.
    let optionalQueryParam (name : string) (ctx : HttpContext) : string option =
        match ctx.Request.Query.[name].ToString () with
        | "" -> None
        | value -> Some value

    /// Parse a query parameter as an integer with a default value and bounds.
    let intQueryParam (name : string) (defaultValue : int) (minValue : int) (maxValue : int) (ctx : HttpContext) : int =
        match ctx.Request.Query.[name].ToString () with
        | "" -> defaultValue
        | s ->
            match Int32.TryParse s with
            | true, v -> max minValue (min maxValue v)
            | false, _ -> defaultValue

    /// Try to read and deserialize the JSON request body.
    let tryReadJsonBody<'T> (jsonOptions : JsonSerializerOptions) (ctx : HttpContext) : Task<Result<'T, string>> =
        task {
            try
                let! body = ctx.Request.ReadFromJsonAsync<'T> (jsonOptions)

                if obj.ReferenceEquals (body, null) then
                    return Error "Request body is required"
                else
                    return Ok body
            with
            | :? JsonException as ex ->
                return Error (sprintf "Invalid JSON: %s" ex.Message)
            | ex ->
                return Error (sprintf "Failed to read request body: %s" ex.Message)
        }
