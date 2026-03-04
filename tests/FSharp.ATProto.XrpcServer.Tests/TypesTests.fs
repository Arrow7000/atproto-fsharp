module FSharp.ATProto.XrpcServer.Tests.TypesTests

open System
open System.Text.Json
open System.Threading.Tasks
open Expecto
open Microsoft.AspNetCore.Http
open FSharp.ATProto.Syntax
open FSharp.ATProto.XrpcServer

let private unwrap result =
    match result with
    | Ok v -> v
    | Error e -> failtest (sprintf "Expected Ok, got Error: %s" e)

let private testNsid = Nsid.parse "app.bsky.feed.getFeedSkeleton" |> unwrap

[<Tests>]
let xrpcMethodTests =
    testList
        "XrpcMethod"
        [
          test "Query and Procedure are distinct" {
              Expect.notEqual XrpcMethod.Query XrpcMethod.Procedure "Query and Procedure should differ"
          }
        ]

[<Tests>]
let xrpcErrorResponseTests =
    testList
        "XrpcErrorResponse"
        [
          test "error response stores error and message" {
              let err : XrpcErrorResponse = { Error = "InvalidRequest"; Message = "Missing parameter" }
              Expect.equal err.Error "InvalidRequest" "Error should match"
              Expect.equal err.Message "Missing parameter" "Message should match"
          }

          test "error response serializes to JSON" {
              let err : XrpcErrorResponse = { Error = "NotFound"; Message = "Record not found" }
              let json = JsonSerializer.Serialize (err, XrpcServerConfig.defaultJsonOptions)
              Expect.stringContains json "\"error\":\"NotFound\"" "Should contain error field"
              Expect.stringContains json "\"message\":\"Record not found\"" "Should contain message field"
          }
        ]

[<Tests>]
let rateLimitConfigTests =
    testList
        "RateLimitConfig"
        [
          test "stores max requests and window" {
              let config : RateLimitConfig = { MaxRequests = 100; Window = TimeSpan.FromMinutes 1.0 }
              Expect.equal config.MaxRequests 100 "MaxRequests should be 100"
              Expect.equal config.Window (TimeSpan.FromMinutes 1.0) "Window should be 1 minute"
          }
        ]

[<Tests>]
let xrpcEndpointTests =
    testList
        "XrpcEndpoint"
        [
          test "stores all fields" {
              let handler (_ctx : HttpContext) = Task.FromResult (Results.Ok () :> IResult)

              let ep : XrpcEndpoint = {
                  Nsid = testNsid
                  Method = XrpcMethod.Query
                  Handler = handler
                  RateLimit = Some { MaxRequests = 50; Window = TimeSpan.FromSeconds 30.0 }
                  RequireAuth = true
              }

              Expect.equal ep.Nsid testNsid "Nsid should match"
              Expect.equal ep.Method XrpcMethod.Query "Method should be Query"
              Expect.isTrue ep.RequireAuth "RequireAuth should be true"
              Expect.isSome ep.RateLimit "RateLimit should be Some"
          }

          test "defaults to no rate limit and no auth" {
              let handler (_ctx : HttpContext) = Task.FromResult (Results.Ok () :> IResult)

              let ep : XrpcEndpoint = {
                  Nsid = testNsid
                  Method = XrpcMethod.Procedure
                  Handler = handler
                  RateLimit = None
                  RequireAuth = false
              }

              Expect.isFalse ep.RequireAuth "RequireAuth should be false"
              Expect.isNone ep.RateLimit "RateLimit should be None"
          }
        ]

[<Tests>]
let xrpcServerConfigTests =
    testList
        "XrpcServerConfig"
        [
          test "defaults has empty endpoints and no auth" {
              let config = XrpcServerConfig.defaults
              Expect.isEmpty config.Endpoints "Endpoints should be empty"
              Expect.isNone config.VerifyToken "VerifyToken should be None"
              Expect.isNone config.GlobalRateLimit "GlobalRateLimit should be None"
          }

          test "default JSON options use camelCase" {
              let opts = XrpcServerConfig.defaultJsonOptions
              Expect.equal opts.PropertyNamingPolicy JsonNamingPolicy.CamelCase "Should use camelCase"
          }
        ]
