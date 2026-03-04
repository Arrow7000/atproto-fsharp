module FSharp.ATProto.XrpcServer.Tests.ServerTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Security.Claims
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Primitives
open FSharp.ATProto.Syntax
open FSharp.ATProto.XrpcServer

let private unwrap result =
    match result with
    | Ok v -> v
    | Error e -> failtest (sprintf "Expected Ok, got Error: %s" e)

let private testNsid = Nsid.parse "com.example.getInfo" |> unwrap
let private testProcNsid = Nsid.parse "com.example.doAction" |> unwrap

let private jsonOpts = XrpcServerConfig.defaultJsonOptions

let private alwaysSucceedVerifier (token : string) : Task<Result<ClaimsPrincipal, string>> =
    let identity = ClaimsIdentity ([| Claim (ClaimTypes.NameIdentifier, "did:plc:testuser") |], "Bearer")
    Task.FromResult (Ok (ClaimsPrincipal identity))

let private alwaysFailVerifier (_token : string) : Task<Result<ClaimsPrincipal, string>> =
    Task.FromResult (Error "Invalid token")

/// Helper: create a WebApplication with TestServer, run a function, dispose.
/// Uses WebApplicationFactory-like approach: configure builder with UseTestServer.
let private withTestServer (config : XrpcServerConfig) (f : HttpClient -> Task<unit>) : Task<unit> =
    task {
        let builder = WebApplication.CreateBuilder ()
        builder.WebHost.UseTestServer () |> ignore
        let app = builder.Build ()

        // Create rate limiter states for endpoints that have rate limits
        let rateLimiters = System.Collections.Concurrent.ConcurrentDictionary<string, RateLimiter.RateLimiterState> ()

        for ep in config.Endpoints do
            let effectiveLimit =
                match ep.RateLimit with
                | Some rl -> Some rl
                | None -> config.GlobalRateLimit

            match effectiveLimit with
            | Some rl ->
                let nsidStr = Nsid.value ep.Nsid
                rateLimiters.TryAdd (nsidStr, RateLimiter.create rl) |> ignore
            | None -> ()

        // Register health check
        app.MapGet (
            "/_health",
            Func<HttpContext, Task<IResult>> (fun ctx -> XrpcServer.healthCheck () ctx)
        )
        |> ignore

        // Register each XRPC endpoint
        for ep in config.Endpoints do
            let nsidStr = Nsid.value ep.Nsid
            let path = sprintf "/xrpc/%s" nsidStr

            let wrappedHandler =
                Func<HttpContext, Task<IResult>> (fun ctx ->
                    task {
                        if ep.RequireAuth then
                            match config.VerifyToken with
                            | None ->
                                return Middleware.xrpcError 500 "InternalError" "Auth is required but no verifier is configured" config.JsonOptions
                            | Some verifyToken ->
                                let! authResult = Auth.verifyRequest verifyToken ctx

                                match authResult with
                                | Error msg ->
                                    return Middleware.authRequired msg config.JsonOptions
                                | Ok _ ->
                                    match rateLimiters.TryGetValue nsidStr with
                                    | true, limiter ->
                                        let key = Middleware.clientKey ctx
                                        let now = DateTimeOffset.UtcNow

                                        match RateLimiter.tryAllow key now limiter with
                                        | Error retryAfter ->
                                            return Middleware.rateLimitExceeded retryAfter config.JsonOptions
                                        | Ok remaining ->
                                            ctx.Response.Headers.["RateLimit-Remaining"] <- Microsoft.Extensions.Primitives.StringValues (string remaining)
                                            return! ep.Handler ctx
                                    | false, _ ->
                                        return! ep.Handler ctx
                        else
                            match config.VerifyToken with
                            | Some verifyToken ->
                                match Auth.extractBearerToken ctx with
                                | Some _ ->
                                    let! _ = Auth.verifyRequest verifyToken ctx
                                    ()
                                | None -> ()
                            | None -> ()

                            match rateLimiters.TryGetValue nsidStr with
                            | true, limiter ->
                                let key = Middleware.clientKey ctx
                                let now = DateTimeOffset.UtcNow

                                match RateLimiter.tryAllow key now limiter with
                                | Error retryAfter ->
                                    return Middleware.rateLimitExceeded retryAfter config.JsonOptions
                                | Ok remaining ->
                                    ctx.Response.Headers.["RateLimit-Remaining"] <- Microsoft.Extensions.Primitives.StringValues (string remaining)
                                    return! ep.Handler ctx
                            | false, _ ->
                                return! ep.Handler ctx
                    })

            match ep.Method with
            | XrpcMethod.Query ->
                app.MapGet (path, wrappedHandler) |> ignore
            | XrpcMethod.Procedure ->
                app.MapPost (path, wrappedHandler) |> ignore

        do! app.StartAsync ()

        let server = app.Services.GetRequiredService<IServer> () :?> TestServer
        let client = server.CreateClient ()

        try
            do! f client
        finally
            client.Dispose ()
            app.StopAsync().Wait ()
    }

[<Tests>]
let endpointBuilderTests =
    testList
        "XrpcServer endpoint builder"
        [
          test "endpoint creates basic endpoint" {
              let handler (_ctx : HttpContext) = Task.FromResult (Results.Ok () :> IResult)
              let ep = XrpcServer.endpoint testNsid XrpcMethod.Query handler

              Expect.equal ep.Nsid testNsid "Nsid should match"
              Expect.equal ep.Method XrpcMethod.Query "Method should be Query"
              Expect.isFalse ep.RequireAuth "RequireAuth defaults to false"
              Expect.isNone ep.RateLimit "RateLimit defaults to None"
          }

          test "withRateLimit sets rate limit" {
              let handler (_ctx : HttpContext) = Task.FromResult (Results.Ok () :> IResult)
              let rl : RateLimitConfig = { MaxRequests = 10; Window = TimeSpan.FromMinutes 1.0 }

              let ep =
                  XrpcServer.endpoint testNsid XrpcMethod.Query handler
                  |> XrpcServer.withRateLimit rl

              Expect.equal ep.RateLimit (Some rl) "Rate limit should be set"
          }

          test "withAuth sets RequireAuth" {
              let handler (_ctx : HttpContext) = Task.FromResult (Results.Ok () :> IResult)

              let ep =
                  XrpcServer.endpoint testNsid XrpcMethod.Query handler
                  |> XrpcServer.withAuth

              Expect.isTrue ep.RequireAuth "RequireAuth should be true"
          }

          test "builders compose" {
              let handler (_ctx : HttpContext) = Task.FromResult (Results.Ok () :> IResult)
              let rl : RateLimitConfig = { MaxRequests = 5; Window = TimeSpan.FromSeconds 30.0 }

              let ep =
                  XrpcServer.endpoint testNsid XrpcMethod.Procedure handler
                  |> XrpcServer.withRateLimit rl
                  |> XrpcServer.withAuth

              Expect.isTrue ep.RequireAuth "Should require auth"
              Expect.equal ep.RateLimit (Some rl) "Should have rate limit"
              Expect.equal ep.Method XrpcMethod.Procedure "Should be Procedure"
          }
        ]

[<Tests>]
let configBuilderTests =
    testList
        "XrpcServer config builder"
        [
          test "addEndpoint adds endpoint to config" {
              let handler (_ctx : HttpContext) = Task.FromResult (Results.Ok () :> IResult)
              let ep = XrpcServer.endpoint testNsid XrpcMethod.Query handler

              let config =
                  XrpcServerConfig.defaults
                  |> XrpcServer.addEndpoint ep

              Expect.equal config.Endpoints.Length 1 "Should have 1 endpoint"
              Expect.equal config.Endpoints.[0].Nsid testNsid "Nsid should match"
          }

          test "addEndpoint preserves order" {
              let handler (_ctx : HttpContext) = Task.FromResult (Results.Ok () :> IResult)
              let ep1 = XrpcServer.endpoint testNsid XrpcMethod.Query handler
              let ep2 = XrpcServer.endpoint testProcNsid XrpcMethod.Procedure handler

              let config =
                  XrpcServerConfig.defaults
                  |> XrpcServer.addEndpoint ep1
                  |> XrpcServer.addEndpoint ep2

              Expect.equal config.Endpoints.Length 2 "Should have 2 endpoints"
              Expect.equal config.Endpoints.[0].Nsid testNsid "First should be getInfo"
              Expect.equal config.Endpoints.[1].Nsid testProcNsid "Second should be doAction"
          }

          test "withTokenVerifier sets verifier" {
              let config =
                  XrpcServerConfig.defaults
                  |> XrpcServer.withTokenVerifier alwaysSucceedVerifier

              Expect.isSome config.VerifyToken "VerifyToken should be set"
          }

          test "withGlobalRateLimit sets global limit" {
              let rl : RateLimitConfig = { MaxRequests = 1000; Window = TimeSpan.FromMinutes 5.0 }

              let config =
                  XrpcServerConfig.defaults
                  |> XrpcServer.withGlobalRateLimit rl

              Expect.equal config.GlobalRateLimit (Some rl) "GlobalRateLimit should be set"
          }

          test "withJsonOptions sets custom options" {
              let opts = JsonSerializerOptions ()
              opts.PropertyNamingPolicy <- JsonNamingPolicy.SnakeCaseLower

              let config =
                  XrpcServerConfig.defaults
                  |> XrpcServer.withJsonOptions opts

              Expect.equal config.JsonOptions.PropertyNamingPolicy JsonNamingPolicy.SnakeCaseLower "Should use snake_case"
          }
        ]

[<Tests>]
let healthCheckTests =
    testList
        "XrpcServer healthCheck"
        [
          test "health check handler returns IResult" {
              let handler = XrpcServer.healthCheck ()
              Expect.isNotNull (box handler) "Handler should not be null"
          }

          testTask "health check via TestServer returns version" {
              do! withTestServer XrpcServerConfig.defaults (fun (client : HttpClient) ->
                  task {
                      let! (response : HttpResponseMessage) = client.GetAsync "/_health"
                      Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                      let! (bodyStr : string) = response.Content.ReadAsStringAsync ()
                      let parsed = JsonDocument.Parse (bodyStr : string)
                      Expect.isTrue (parsed.RootElement.TryGetProperty ("version") |> fst) "Should have version field"
                  })
          }
        ]

[<Tests>]
let integrationTests =
    testList
        "XrpcServer integration"
        [
          test "configure creates app" {
              let config = XrpcServerConfig.defaults
              let app = XrpcServer.configure config
              Expect.isNotNull (box app) "App should not be null"
          }

          test "full config pipeline builds correctly" {
              let queryHandler (_ctx : HttpContext) =
                  Task.FromResult (Results.Ok () :> IResult)

              let procHandler (_ctx : HttpContext) =
                  Task.FromResult (Results.Ok () :> IResult)

              let queryEp =
                  XrpcServer.endpoint testNsid XrpcMethod.Query queryHandler
                  |> XrpcServer.withRateLimit { MaxRequests = 100; Window = TimeSpan.FromMinutes 1.0 }

              let procEp =
                  XrpcServer.endpoint testProcNsid XrpcMethod.Procedure procHandler
                  |> XrpcServer.withAuth
                  |> XrpcServer.withRateLimit { MaxRequests = 10; Window = TimeSpan.FromMinutes 1.0 }

              let config =
                  XrpcServerConfig.defaults
                  |> XrpcServer.addEndpoint queryEp
                  |> XrpcServer.addEndpoint procEp
                  |> XrpcServer.withTokenVerifier alwaysSucceedVerifier
                  |> XrpcServer.withGlobalRateLimit { MaxRequests = 1000; Window = TimeSpan.FromMinutes 5.0 }

              Expect.equal config.Endpoints.Length 2 "Should have 2 endpoints"
              Expect.isTrue config.Endpoints.[1].RequireAuth "Procedure should require auth"
              Expect.isSome config.VerifyToken "Should have verifier"
              Expect.isSome config.GlobalRateLimit "Should have global rate limit"
          }
        ]

[<Tests>]
let serverWithTestServerTests =
    testList
        "XrpcServer with TestServer"
        [
          testTask "GET /xrpc/... returns handler response" {
              let handler (_ctx : HttpContext) =
                  task {
                      return Results.Json ({| greeting = "hello world" |}, jsonOpts) :> IResult
                  }

              let ep = XrpcServer.endpoint testNsid XrpcMethod.Query handler

              let config =
                  XrpcServerConfig.defaults
                  |> XrpcServer.addEndpoint ep

              do! withTestServer config (fun (client : HttpClient) ->
                  task {
                      let! (response : HttpResponseMessage) = client.GetAsync "/xrpc/com.example.getInfo"

                      Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                      let! (bodyStr : string) = response.Content.ReadAsStringAsync ()
                      let parsed = JsonDocument.Parse (bodyStr : string)
                      Expect.equal (parsed.RootElement.GetProperty("greeting").GetString ()) "hello world" "Body should match"
                  })
          }

          testTask "GET /_health returns health status" {
              do! withTestServer XrpcServerConfig.defaults (fun (client : HttpClient) ->
                  task {
                      let! (response : HttpResponseMessage) = client.GetAsync "/_health"

                      Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                      let! (bodyStr : string) = response.Content.ReadAsStringAsync ()
                      let parsed = JsonDocument.Parse (bodyStr : string)
                      Expect.isTrue (parsed.RootElement.TryGetProperty ("version") |> fst) "Should have version"
                  })
          }

          testTask "POST /xrpc/... for procedure endpoint" {
              let handler (ctx : HttpContext) =
                  task {
                      let! bodyResult = Middleware.tryReadJsonBody<{| name: string |}> jsonOpts ctx

                      match bodyResult with
                      | Ok parsed ->
                          return Results.Json ({| echo = parsed.name |}, jsonOpts) :> IResult
                      | Error msg ->
                          return Middleware.invalidRequest msg jsonOpts
                  }

              let ep = XrpcServer.endpoint testProcNsid XrpcMethod.Procedure handler

              let config =
                  XrpcServerConfig.defaults
                  |> XrpcServer.addEndpoint ep

              do! withTestServer config (fun (client : HttpClient) ->
                  task {
                      let content = new StringContent ("""{"name":"atproto"}""", Encoding.UTF8, "application/json")
                      let! (response : HttpResponseMessage) = client.PostAsync ("/xrpc/com.example.doAction", content)

                      Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                      let! (bodyStr : string) = response.Content.ReadAsStringAsync ()
                      let parsed = JsonDocument.Parse (bodyStr : string)
                      Expect.equal (parsed.RootElement.GetProperty("echo").GetString ()) "atproto" "Should echo the name"
                  })
          }

          testTask "auth-required endpoint rejects unauthenticated requests" {
              let handler (_ctx : HttpContext) =
                  task {
                      return Results.Json ({| data = "secret" |}, jsonOpts) :> IResult
                  }

              let ep =
                  XrpcServer.endpoint testNsid XrpcMethod.Query handler
                  |> XrpcServer.withAuth

              let config =
                  XrpcServerConfig.defaults
                  |> XrpcServer.addEndpoint ep
                  |> XrpcServer.withTokenVerifier alwaysSucceedVerifier

              do! withTestServer config (fun (client : HttpClient) ->
                  task {
                      let! (response : HttpResponseMessage) = client.GetAsync "/xrpc/com.example.getInfo"

                      Expect.equal response.StatusCode HttpStatusCode.Unauthorized "Should return 401"

                      let! (bodyStr : string) = response.Content.ReadAsStringAsync ()
                      Expect.stringContains bodyStr "AuthenticationRequired" "Should have auth error"
                  })
          }

          testTask "auth-required endpoint accepts authenticated requests" {
              let handler (ctx : HttpContext) =
                  task {
                      let did = Auth.getClaim ClaimTypes.NameIdentifier ctx
                      return Results.Json ({| did = did |}, jsonOpts) :> IResult
                  }

              let ep =
                  XrpcServer.endpoint testNsid XrpcMethod.Query handler
                  |> XrpcServer.withAuth

              let config =
                  XrpcServerConfig.defaults
                  |> XrpcServer.addEndpoint ep
                  |> XrpcServer.withTokenVerifier alwaysSucceedVerifier

              do! withTestServer config (fun (client : HttpClient) ->
                  task {
                      client.DefaultRequestHeaders.Authorization <- Headers.AuthenticationHeaderValue ("Bearer", "valid-token")
                      let! (response : HttpResponseMessage) = client.GetAsync "/xrpc/com.example.getInfo"

                      Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                      let! (bodyStr : string) = response.Content.ReadAsStringAsync ()
                      let parsed = JsonDocument.Parse (bodyStr : string)
                      Expect.equal (parsed.RootElement.GetProperty("did").GetString ()) "did:plc:testuser" "Should have the DID"
                  })
          }

          testTask "auth-required endpoint rejects invalid tokens" {
              let handler (_ctx : HttpContext) =
                  task {
                      return Results.Json ({| data = "secret" |}, jsonOpts) :> IResult
                  }

              let ep =
                  XrpcServer.endpoint testNsid XrpcMethod.Query handler
                  |> XrpcServer.withAuth

              let config =
                  XrpcServerConfig.defaults
                  |> XrpcServer.addEndpoint ep
                  |> XrpcServer.withTokenVerifier alwaysFailVerifier

              do! withTestServer config (fun (client : HttpClient) ->
                  task {
                      client.DefaultRequestHeaders.Authorization <- Headers.AuthenticationHeaderValue ("Bearer", "bad-token")
                      let! (response : HttpResponseMessage) = client.GetAsync "/xrpc/com.example.getInfo"

                      Expect.equal response.StatusCode HttpStatusCode.Unauthorized "Should return 401"
                  })
          }

          testTask "rate-limited endpoint returns 429 when limit exceeded" {
              let handler (_ctx : HttpContext) =
                  task {
                      return Results.Json ({| ok = true |}, jsonOpts) :> IResult
                  }

              let ep =
                  XrpcServer.endpoint testNsid XrpcMethod.Query handler
                  |> XrpcServer.withRateLimit { MaxRequests = 2; Window = TimeSpan.FromMinutes 1.0 }

              let config =
                  XrpcServerConfig.defaults
                  |> XrpcServer.addEndpoint ep

              do! withTestServer config (fun (client : HttpClient) ->
                  task {
                      let! (r1 : HttpResponseMessage) = client.GetAsync "/xrpc/com.example.getInfo"
                      Expect.equal r1.StatusCode HttpStatusCode.OK "First request should be OK"

                      let! (r2 : HttpResponseMessage) = client.GetAsync "/xrpc/com.example.getInfo"
                      Expect.equal r2.StatusCode HttpStatusCode.OK "Second request should be OK"

                      let! (r3 : HttpResponseMessage) = client.GetAsync "/xrpc/com.example.getInfo"
                      Expect.equal (int r3.StatusCode) 429 "Third request should be 429"

                      let! (bodyStr : string) = r3.Content.ReadAsStringAsync ()
                      Expect.stringContains bodyStr "RateLimitExceeded" "Body should mention rate limit"
                  })
          }

          testTask "query parameters are accessible in handler" {
              let handler (ctx : HttpContext) =
                  task {
                      match Middleware.requireQueryParam "actor" ctx with
                      | Ok actor ->
                          let limit = Middleware.intQueryParam "limit" 50 1 100 ctx
                          return Results.Json ({| actor = actor; limit = limit |}, jsonOpts) :> IResult
                      | Error msg ->
                          return Middleware.invalidRequest msg jsonOpts
                  }

              let ep = XrpcServer.endpoint testNsid XrpcMethod.Query handler

              let config =
                  XrpcServerConfig.defaults
                  |> XrpcServer.addEndpoint ep

              do! withTestServer config (fun (client : HttpClient) ->
                  task {
                      let! (response : HttpResponseMessage) = client.GetAsync "/xrpc/com.example.getInfo?actor=alice.bsky.social&limit=25"

                      Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                      let! (bodyStr : string) = response.Content.ReadAsStringAsync ()
                      let parsed = JsonDocument.Parse (bodyStr : string)
                      Expect.equal (parsed.RootElement.GetProperty("actor").GetString ()) "alice.bsky.social" "Actor should match"
                      Expect.equal (parsed.RootElement.GetProperty("limit").GetInt32 ()) 25 "Limit should match"
                  })
          }

          testTask "missing required query param returns 400" {
              let handler (ctx : HttpContext) =
                  task {
                      match Middleware.requireQueryParam "actor" ctx with
                      | Ok actor ->
                          return Results.Json ({| actor = actor |}, jsonOpts) :> IResult
                      | Error msg ->
                          return Middleware.invalidRequest msg jsonOpts
                  }

              let ep = XrpcServer.endpoint testNsid XrpcMethod.Query handler

              let config =
                  XrpcServerConfig.defaults
                  |> XrpcServer.addEndpoint ep

              do! withTestServer config (fun (client : HttpClient) ->
                  task {
                      let! (response : HttpResponseMessage) = client.GetAsync "/xrpc/com.example.getInfo"

                      Expect.equal response.StatusCode HttpStatusCode.BadRequest "Should return 400"

                      let! (bodyStr : string) = response.Content.ReadAsStringAsync ()
                      Expect.stringContains bodyStr "InvalidRequest" "Should have InvalidRequest error"
                      Expect.stringContains bodyStr "actor" "Should mention the missing param"
                  })
          }

          testTask "rate limit remaining header is set" {
              let handler (_ctx : HttpContext) =
                  task {
                      return Results.Json ({| ok = true |}, jsonOpts) :> IResult
                  }

              let ep =
                  XrpcServer.endpoint testNsid XrpcMethod.Query handler
                  |> XrpcServer.withRateLimit { MaxRequests = 10; Window = TimeSpan.FromMinutes 1.0 }

              let config =
                  XrpcServerConfig.defaults
                  |> XrpcServer.addEndpoint ep

              do! withTestServer config (fun (client : HttpClient) ->
                  task {
                      let! (response : HttpResponseMessage) = client.GetAsync "/xrpc/com.example.getInfo"

                      Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                      let hasHeader = response.Headers.Contains "RateLimit-Remaining"
                      Expect.isTrue hasHeader "Should have RateLimit-Remaining header"
                  })
          }
        ]
