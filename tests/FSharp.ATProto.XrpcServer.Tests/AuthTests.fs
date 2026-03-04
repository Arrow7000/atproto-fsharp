module FSharp.ATProto.XrpcServer.Tests.AuthTests

open System.Security.Claims
open System.Threading.Tasks
open Expecto
open Microsoft.AspNetCore.Http
open FSharp.ATProto.XrpcServer

let private alwaysSucceedVerifier (token : string) : Task<Result<ClaimsPrincipal, string>> =
    let identity = ClaimsIdentity ([| Claim (ClaimTypes.NameIdentifier, "did:plc:testuser"); Claim ("token", token) |], "Bearer")
    Task.FromResult (Ok (ClaimsPrincipal identity))

let private alwaysFailVerifier (_token : string) : Task<Result<ClaimsPrincipal, string>> =
    Task.FromResult (Error "Invalid token")

[<Tests>]
let extractBearerTokenTests =
    testList
        "Auth.extractBearerToken"
        [
          test "extracts token from valid Bearer header" {
              let ctx = DefaultHttpContext ()
              ctx.Request.Headers.Authorization <- Microsoft.Extensions.Primitives.StringValues "Bearer eyJhbGciOiJSUzI1NiJ9.test"
              let result = Auth.extractBearerToken ctx
              Expect.equal result (Some "eyJhbGciOiJSUzI1NiJ9.test") "Should extract the token"
          }

          test "returns None for missing header" {
              let ctx = DefaultHttpContext ()
              let result = Auth.extractBearerToken ctx
              Expect.isNone result "Should return None for missing header"
          }

          test "returns None for non-Bearer scheme" {
              let ctx = DefaultHttpContext ()
              ctx.Request.Headers.Authorization <- Microsoft.Extensions.Primitives.StringValues "Basic dXNlcjpwYXNz"
              let result = Auth.extractBearerToken ctx
              Expect.isNone result "Should return None for Basic auth"
          }

          test "returns None for empty Authorization header" {
              let ctx = DefaultHttpContext ()
              ctx.Request.Headers.Authorization <- Microsoft.Extensions.Primitives.StringValues ""
              let result = Auth.extractBearerToken ctx
              Expect.isNone result "Should return None for empty header"
          }

          test "handles Bearer with extra whitespace" {
              let ctx = DefaultHttpContext ()
              ctx.Request.Headers.Authorization <- Microsoft.Extensions.Primitives.StringValues "  Bearer mytoken123  "
              let result = Auth.extractBearerToken ctx
              // Trim() strips leading/trailing whitespace from the full header, then we take after "Bearer "
              Expect.equal result (Some "mytoken123") "Should extract token after trimming"
          }
        ]

[<Tests>]
let verifyRequestTests =
    testList
        "Auth.verifyRequest"
        [
          testTask "succeeds with valid token and stores principal" {
              let ctx = DefaultHttpContext ()
              ctx.Request.Headers.Authorization <- Microsoft.Extensions.Primitives.StringValues "Bearer test-token-123"

              let! result = Auth.verifyRequest alwaysSucceedVerifier (ctx :> HttpContext)

              match result with
              | Ok (principal : ClaimsPrincipal) ->
                  let did = principal.FindFirst(ClaimTypes.NameIdentifier).Value
                  Expect.equal did "did:plc:testuser" "Should have correct DID"

                  let tokenClaim = principal.FindFirst("token").Value
                  Expect.equal tokenClaim "test-token-123" "Verifier should receive the token"
              | Error msg -> failtest (sprintf "Should succeed: %s" msg)
          }

          testTask "returns error when no Authorization header" {
              let ctx = DefaultHttpContext ()

              let! result = Auth.verifyRequest alwaysSucceedVerifier (ctx :> HttpContext)

              match result with
              | Error msg -> Expect.stringContains msg "Authorization" "Should mention Authorization header"
              | Ok _ -> failtest "Should fail without Authorization header"
          }

          testTask "returns error when verifier rejects token" {
              let ctx = DefaultHttpContext ()
              ctx.Request.Headers.Authorization <- Microsoft.Extensions.Primitives.StringValues "Bearer bad-token"

              let! result = Auth.verifyRequest alwaysFailVerifier (ctx :> HttpContext)

              match result with
              | Error msg -> Expect.equal msg "Invalid token" "Should return verifier error message"
              | Ok _ -> failtest "Should fail with invalid token"
          }

          testTask "stores principal in HttpContext.Items" {
              let ctx = DefaultHttpContext ()
              ctx.Request.Headers.Authorization <- Microsoft.Extensions.Primitives.StringValues "Bearer test-token"

              let! _ = Auth.verifyRequest alwaysSucceedVerifier (ctx :> HttpContext)

              let principal = Auth.getPrincipal ctx
              Expect.isSome principal "Principal should be stored in Items"
          }
        ]

[<Tests>]
let getPrincipalTests =
    testList
        "Auth.getPrincipal"
        [
          test "returns None when not authenticated" {
              let ctx = DefaultHttpContext ()
              let result = Auth.getPrincipal ctx
              Expect.isNone result "Should return None"
          }

          testTask "returns Some after successful auth" {
              let ctx = DefaultHttpContext ()
              ctx.Request.Headers.Authorization <- Microsoft.Extensions.Primitives.StringValues "Bearer token"

              let! _ = Auth.verifyRequest alwaysSucceedVerifier (ctx :> HttpContext)
              let result = Auth.getPrincipal ctx
              Expect.isSome result "Should return Some principal"
          }
        ]

[<Tests>]
let getClaimTests =
    testList
        "Auth.getClaim"
        [
          testTask "returns claim value when present" {
              let ctx = DefaultHttpContext ()
              ctx.Request.Headers.Authorization <- Microsoft.Extensions.Primitives.StringValues "Bearer token"

              let! _ = Auth.verifyRequest alwaysSucceedVerifier (ctx :> HttpContext)
              let result = Auth.getClaim ClaimTypes.NameIdentifier ctx
              Expect.equal result (Some "did:plc:testuser") "Should return the DID claim"
          }

          testTask "returns None for missing claim type" {
              let ctx = DefaultHttpContext ()
              ctx.Request.Headers.Authorization <- Microsoft.Extensions.Primitives.StringValues "Bearer token"

              let! _ = Auth.verifyRequest alwaysSucceedVerifier (ctx :> HttpContext)
              let result = Auth.getClaim "nonexistent-claim" ctx
              Expect.isNone result "Should return None for missing claim"
          }

          test "returns None when not authenticated" {
              let ctx = DefaultHttpContext ()
              let result = Auth.getClaim ClaimTypes.NameIdentifier ctx
              Expect.isNone result "Should return None when unauthenticated"
          }
        ]
