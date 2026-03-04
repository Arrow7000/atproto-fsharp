module FSharp.ATProto.XrpcServer.Tests.MiddlewareTests

open System
open System.IO
open System.Text
open System.Text.Json
open Expecto
open Microsoft.AspNetCore.Http
open FSharp.ATProto.XrpcServer

let private jsonOpts = XrpcServerConfig.defaultJsonOptions

/// Create a test HttpContext with the given query parameters.
let private createTestContext (queryParams : (string * string) list) : HttpContext =
    let ctx = DefaultHttpContext ()

    if not (List.isEmpty queryParams) then
        let queryString =
            queryParams
            |> List.map (fun (k, v) -> sprintf "%s=%s" (Uri.EscapeDataString k) (Uri.EscapeDataString v))
            |> String.concat "&"
            |> sprintf "?%s"

        ctx.Request.QueryString <- QueryString queryString

    ctx :> HttpContext

[<Tests>]
let errorFormattingTests =
    testList
        "Middleware error formatting"
        [
          test "xrpcError produces a result" {
              let result = Middleware.xrpcError 400 "InvalidRequest" "Bad parameter" jsonOpts
              Expect.isNotNull (box result) "Should produce a result"
          }

          test "invalidRequest returns a result" {
              let result = Middleware.invalidRequest "Missing feed parameter" jsonOpts
              Expect.isNotNull (box result) "Should produce a result"
          }

          test "methodNotImplemented returns a result" {
              let result = Middleware.methodNotImplemented "com.example.doSomething" jsonOpts
              Expect.isNotNull (box result) "Should produce a result"
          }

          test "authRequired returns a result" {
              let result = Middleware.authRequired "Token expired" jsonOpts
              Expect.isNotNull (box result) "Should produce a result"
          }

          test "rateLimitExceeded produces a result" {
              let result = Middleware.rateLimitExceeded 30.0 jsonOpts
              Expect.isNotNull (box result) "Should produce a result"
          }

          test "XrpcErrorResponse serializes correctly" {
              let body : XrpcErrorResponse = { Error = "InvalidRequest"; Message = "Bad param" }
              let json = JsonSerializer.Serialize (body, jsonOpts)
              Expect.stringContains json "\"error\":\"InvalidRequest\"" "Should contain error"
              Expect.stringContains json "\"message\":\"Bad param\"" "Should contain message"
          }
        ]

[<Tests>]
let errorResponseExecutionTests =
    testList
        "Middleware error response execution"
        [
          testTask "xrpcError writes correct status code and body" {
              let ctx = DefaultHttpContext ()
              ctx.Response.Body <- new MemoryStream ()

              let result = Middleware.xrpcError 400 "InvalidRequest" "Bad param" jsonOpts
              do! result.ExecuteAsync ctx

              Expect.equal ctx.Response.StatusCode 400 "Status should be 400"

              ctx.Response.Body.Position <- 0L
              let reader = new StreamReader (ctx.Response.Body)
              let body : string = reader.ReadToEnd ()
              let parsed = JsonDocument.Parse (body : string)
              let root = parsed.RootElement
              Expect.equal (root.GetProperty("error").GetString ()) "InvalidRequest" "Error should match"
              Expect.equal (root.GetProperty("message").GetString ()) "Bad param" "Message should match"
          }

          testTask "rateLimitExceeded sets Retry-After header and 429 status" {
              let ctx = DefaultHttpContext ()
              ctx.Response.Body <- new MemoryStream ()

              let result = Middleware.rateLimitExceeded 25.3 jsonOpts
              do! result.ExecuteAsync ctx

              Expect.equal ctx.Response.StatusCode 429 "Status should be 429"

              let retryAfter = ctx.Response.Headers.["Retry-After"].ToString ()
              Expect.equal retryAfter "26" "Retry-After should be ceiling of 25.3 = 26"

              ctx.Response.Body.Position <- 0L
              let reader = new StreamReader (ctx.Response.Body)
              let body : string = reader.ReadToEnd ()
              Expect.stringContains body "RateLimitExceeded" "Body should mention rate limit"
          }
        ]

[<Tests>]
let queryParamTests =
    testList
        "Middleware query parameter parsing"
        [
          test "requireQueryParam returns value when present" {
              let ctx = createTestContext [ "feed", "at://did:plc:abc/app.bsky.feed.generator/my-feed" ]

              match Middleware.requireQueryParam "feed" ctx with
              | Ok value -> Expect.equal value "at://did:plc:abc/app.bsky.feed.generator/my-feed" "Value should match"
              | Error _ -> failtest "Should have found the parameter"
          }

          test "requireQueryParam returns Error when missing" {
              let ctx = createTestContext []

              match Middleware.requireQueryParam "feed" ctx with
              | Error msg -> Expect.stringContains msg "feed" "Error should mention the parameter name"
              | Ok _ -> failtest "Should have returned Error for missing param"
          }

          test "optionalQueryParam returns Some when present" {
              let ctx = createTestContext [ "cursor", "abc123" ]
              let result = Middleware.optionalQueryParam "cursor" ctx
              Expect.equal result (Some "abc123") "Should return Some cursor"
          }

          test "optionalQueryParam returns None when missing" {
              let ctx = createTestContext []
              let result = Middleware.optionalQueryParam "cursor" ctx
              Expect.isNone result "Should return None"
          }

          test "intQueryParam returns parsed value within bounds" {
              let ctx = createTestContext [ "limit", "42" ]
              let result = Middleware.intQueryParam "limit" 50 1 100 ctx
              Expect.equal result 42 "Should parse 42"
          }

          test "intQueryParam returns default when missing" {
              let ctx = createTestContext []
              let result = Middleware.intQueryParam "limit" 50 1 100 ctx
              Expect.equal result 50 "Should return default 50"
          }

          test "intQueryParam clamps to min" {
              let ctx = createTestContext [ "limit", "0" ]
              let result = Middleware.intQueryParam "limit" 50 1 100 ctx
              Expect.equal result 1 "Should clamp to min 1"
          }

          test "intQueryParam clamps to max" {
              let ctx = createTestContext [ "limit", "200" ]
              let result = Middleware.intQueryParam "limit" 50 1 100 ctx
              Expect.equal result 100 "Should clamp to max 100"
          }

          test "intQueryParam returns default for non-numeric" {
              let ctx = createTestContext [ "limit", "abc" ]
              let result = Middleware.intQueryParam "limit" 50 1 100 ctx
              Expect.equal result 50 "Should return default for non-numeric"
          }
        ]

[<Tests>]
let clientKeyTests =
    testList
        "Middleware clientKey"
        [
          test "returns unknown when no remote IP" {
              let ctx = DefaultHttpContext ()
              let key = Middleware.clientKey ctx
              Expect.equal key "unknown" "Should return unknown"
          }

          test "returns IP address string when present" {
              let ctx = DefaultHttpContext ()
              ctx.Connection.RemoteIpAddress <- System.Net.IPAddress.Loopback
              let key = Middleware.clientKey ctx
              Expect.equal key "127.0.0.1" "Should return loopback IP"
          }
        ]

[<Tests>]
let jsonBodyTests =
    testList
        "Middleware JSON body parsing"
        [
          test "tryReadJsonBody deserializes valid JSON" {
              let ctx = DefaultHttpContext ()
              let bodyStr = """{"name":"test","value":42}"""
              ctx.Request.Body <- new MemoryStream (Encoding.UTF8.GetBytes bodyStr)
              ctx.Request.ContentType <- "application/json"
              ctx.Request.ContentLength <- System.Nullable<int64> (int64 (Encoding.UTF8.GetByteCount bodyStr))

              let result =
                  (Middleware.tryReadJsonBody<{| name: string; value: int |}> jsonOpts ctx).Result

              match result with
              | Ok parsed ->
                  Expect.equal parsed.name "test" "Name should match"
                  Expect.equal parsed.value 42 "Value should match"
              | Error msg -> failtest (sprintf "Should have parsed: %s" msg)
          }

          test "tryReadJsonBody returns Error for invalid JSON" {
              let ctx = DefaultHttpContext ()
              let bodyStr = """not valid json"""
              ctx.Request.Body <- new MemoryStream (Encoding.UTF8.GetBytes bodyStr)
              ctx.Request.ContentType <- "application/json"

              let result =
                  (Middleware.tryReadJsonBody<{| name: string |}> jsonOpts ctx).Result

              match result with
              | Error msg -> Expect.stringContains msg "JSON" "Should mention JSON in error"
              | Ok _ -> failtest "Should have returned Error for invalid JSON"
          }
        ]
