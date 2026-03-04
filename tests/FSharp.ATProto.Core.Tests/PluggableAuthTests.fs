module PluggableAuthTests

open System.Net
open System.Net.Http
open System.Text.Json.Serialization
open Expecto
open FSharp.ATProto.Core

type TestOutput =
    { [<JsonPropertyName("value")>]
      Value : string }

type TestParams = { Key : string }

let makeAgent handler =
    let client = new HttpClient (new TestHelpers.MockHandler (handler))
    AtpAgent.createWithClient client "https://bsky.social"

[<Tests>]
let authenticateRequestTests =
    testList
        "AuthenticateRequest"
        [ testCase "custom auth handler adds headers to request"
          <| fun () ->
              let mutable capturedAuth = ""
              let mutable capturedCustom = ""

              let agent =
                  makeAgent (fun req ->
                      capturedAuth <-
                          match req.Headers.Authorization with
                          | null -> ""
                          | auth -> sprintf "%s %s" auth.Scheme auth.Parameter

                      match req.Headers.TryGetValues "X-Custom" with
                      | true, values -> capturedCustom <- values |> Seq.head
                      | false, _ -> ()

                      TestHelpers.jsonResponse HttpStatusCode.OK {| value = "ok" |})

              let customAgent =
                  { agent with
                      AuthenticateRequest =
                          Some (fun request ->
                              request.Headers.TryAddWithoutValidation ("Authorization", "DPoP mytoken")
                              |> ignore

                              request.Headers.TryAddWithoutValidation ("X-Custom", "proof123") |> ignore) }

              let result =
                  Xrpc.query<TestParams, TestOutput> "test.method" { Key = "x" } customAgent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Ok output ->
                  Expect.equal output.Value "ok" "got response"
                  Expect.equal capturedAuth "DPoP mytoken" "custom auth header sent"
                  Expect.equal capturedCustom "proof123" "custom header sent"
              | Error e -> failtest $"Expected Ok, got Error: {e}"

          testCase "extra headers still added alongside custom auth"
          <| fun () ->
              let mutable capturedProxy = ""

              let agent =
                  makeAgent (fun req ->
                      match req.Headers.TryGetValues "atproto-proxy" with
                      | true, values -> capturedProxy <- values |> Seq.head
                      | false, _ -> ()

                      TestHelpers.jsonResponse HttpStatusCode.OK {| value = "ok" |})

              let customAgent =
                  { agent with
                      AuthenticateRequest = Some (fun request ->
                          request.Headers.TryAddWithoutValidation ("Authorization", "DPoP tok") |> ignore)
                      ExtraHeaders = [ "atproto-proxy", "did:web:api.bsky.chat#bsky_chat" ] }

              let result =
                  Xrpc.queryNoParams<TestOutput> "test.method" customAgent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Ok _ -> Expect.equal capturedProxy "did:web:api.bsky.chat#bsky_chat" "proxy header sent"
              | Error e -> failtest $"Expected Ok, got Error: {e}"

          testCase "default bearer auth when AuthenticateRequest is None"
          <| fun () ->
              let mutable capturedAuth = ""

              let agent =
                  makeAgent (fun req ->
                      match req.Headers.Authorization with
                      | null -> ()
                      | auth -> capturedAuth <- sprintf "%s %s" auth.Scheme auth.Parameter

                      TestHelpers.jsonResponse HttpStatusCode.OK {| value = "ok" |})

              agent.Session <-
                  Some
                      { AccessJwt = "bearer-token"
                        RefreshJwt = "refresh"
                        Did = FSharp.ATProto.Syntax.Did.parse "did:plc:test" |> Result.defaultWith failwith
                        Handle =
                            FSharp.ATProto.Syntax.Handle.parse "test.bsky.social"
                            |> Result.defaultWith failwith }

              let result =
                  Xrpc.queryNoParams<TestOutput> "test.method" agent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Ok _ -> Expect.equal capturedAuth "Bearer bearer-token" "bearer auth used"
              | Error e -> failtest $"Expected Ok, got Error: {e}" ]

[<Tests>]
let refreshAuthenticationTests =
    testList
        "RefreshAuthentication"
        [ testCase "custom refresh handler called on 401 ExpiredToken"
          <| fun () ->
              let mutable refreshCalled = false
              let mutable callCount = 0

              let agent =
                  makeAgent (fun _ ->
                      callCount <- callCount + 1

                      if callCount = 1 then
                          TestHelpers.jsonResponse
                              HttpStatusCode.Unauthorized
                              {| error = "ExpiredToken"
                                 message = "Token expired" |}
                      else
                          TestHelpers.jsonResponse HttpStatusCode.OK {| value = "after-refresh" |})

              let customAgent =
                  { agent with
                      AuthenticateRequest = Some (fun request ->
                          request.Headers.TryAddWithoutValidation ("Authorization", "DPoP tok") |> ignore)
                      RefreshAuthentication =
                          Some (fun () ->
                              refreshCalled <- true
                              System.Threading.Tasks.Task.FromResult (Ok ())) }

              let result =
                  Xrpc.queryNoParams<TestOutput> "test.method" customAgent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Ok output ->
                  Expect.isTrue refreshCalled "custom refresh was called"
                  Expect.equal output.Value "after-refresh" "got response after refresh"
              | Error e -> failtest $"Expected Ok, got Error: {e}"

          testCase "custom refresh error propagated"
          <| fun () ->
              let agent =
                  makeAgent (fun _ ->
                      TestHelpers.jsonResponse
                          HttpStatusCode.Unauthorized
                          {| error = "ExpiredToken"
                             message = "Token expired" |})

              let customAgent =
                  { agent with
                      AuthenticateRequest = Some (fun request ->
                          request.Headers.TryAddWithoutValidation ("Authorization", "DPoP tok") |> ignore)
                      RefreshAuthentication =
                          Some (fun () ->
                              System.Threading.Tasks.Task.FromResult (
                                  Error
                                      { StatusCode = 401
                                        Error = Some "OAuthRefreshFailed"
                                        Message = Some "No refresh token" }
                              )) }

              let result =
                  Xrpc.queryNoParams<TestOutput> "test.method" customAgent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Error e ->
                  Expect.equal e.Error (Some "OAuthRefreshFailed") "custom refresh error returned"
              | Ok _ -> failtest "Expected Error, got Ok"

          testCase "refresh triggered when RefreshAuthentication set and no Session"
          <| fun () ->
              let mutable refreshCalled = false
              let mutable callCount = 0

              let agent =
                  makeAgent (fun _ ->
                      callCount <- callCount + 1

                      if callCount = 1 then
                          TestHelpers.jsonResponse
                              HttpStatusCode.Unauthorized
                              {| error = "ExpiredToken"
                                 message = "Token expired" |}
                      else
                          TestHelpers.jsonResponse HttpStatusCode.OK {| value = "ok" |})

              // No Session set, but RefreshAuthentication is set
              let customAgent =
                  { agent with
                      AuthenticateRequest = Some (fun _ -> ())
                      RefreshAuthentication =
                          Some (fun () ->
                              refreshCalled <- true
                              System.Threading.Tasks.Task.FromResult (Ok ())) }

              Expect.isNone customAgent.Session "no session"

              let result =
                  Xrpc.queryNoParams<TestOutput> "test.method" customAgent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Ok _ -> Expect.isTrue refreshCalled "refresh called even without session"
              | Error e -> failtest $"Expected Ok, got Error: {e}" ]

[<Tests>]
let sessionChangedTests =
    testList
        "OnSessionChanged"
        [ testCase "fired on login"
          <| fun () ->
              let mutable changed = false

              let agent =
                  makeAgent (fun _ ->
                      TestHelpers.jsonResponse
                          HttpStatusCode.OK
                          {| accessJwt = "a"
                             refreshJwt = "r"
                             did = "did:plc:test"
                             handle = "test.bsky.social" |})

              let agentWithCallback = { agent with OnSessionChanged = Some (fun () -> changed <- true) }

              let result =
                  AtpAgent.login "test" "pass" agentWithCallback
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Ok _ -> Expect.isTrue changed "OnSessionChanged fired on login"
              | Error e -> failtest $"Login failed: {e}"

          testCase "fired on custom refresh"
          <| fun () ->
              let mutable changeCount = 0
              let mutable callCount = 0

              let agent =
                  makeAgent (fun _ ->
                      callCount <- callCount + 1

                      if callCount = 1 then
                          TestHelpers.jsonResponse
                              HttpStatusCode.Unauthorized
                              {| error = "ExpiredToken"
                                 message = "Token expired" |}
                      else
                          TestHelpers.jsonResponse HttpStatusCode.OK {| value = "ok" |})

              let customAgent =
                  { agent with
                      AuthenticateRequest = Some (fun _ -> ())
                      RefreshAuthentication =
                          Some (fun () -> System.Threading.Tasks.Task.FromResult (Ok ()))
                      OnSessionChanged = Some (fun () -> changeCount <- changeCount + 1) }

              let result =
                  Xrpc.queryNoParams<TestOutput> "test.method" customAgent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Ok _ -> Expect.equal changeCount 1 "OnSessionChanged fired once on refresh"
              | Error e -> failtest $"Expected Ok, got Error: {e}"

          testCase "not fired on failed login"
          <| fun () ->
              let mutable changed = false

              let agent =
                  makeAgent (fun _ ->
                      TestHelpers.jsonResponse
                          HttpStatusCode.Unauthorized
                          {| error = "AuthenticationRequired"
                             message = "Bad password" |})

              let agentWithCallback = { agent with OnSessionChanged = Some (fun () -> changed <- true) }

              let _ =
                  AtpAgent.login "test" "pass" agentWithCallback
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isFalse changed "OnSessionChanged not fired on failed login" ]
