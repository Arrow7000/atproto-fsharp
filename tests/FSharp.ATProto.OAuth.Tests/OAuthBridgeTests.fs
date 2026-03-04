module OAuthBridgeTests

open System
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Expecto
open FSharp.ATProto.Core
open FSharp.ATProto.OAuth
open FSharp.ATProto.Syntax

type MockHandler (handler : HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler ()

    override _.SendAsync (request : HttpRequestMessage, _ct : CancellationToken) =
        Task.FromResult (handler request)

let jsonResponse (statusCode : HttpStatusCode) (body : obj) =
    let json = JsonSerializer.Serialize body
    let response = new HttpResponseMessage (statusCode)
    response.Content <- new StringContent (json, Encoding.UTF8, "application/json")
    response

let testDid = Did.parse "did:plc:testuser" |> Result.defaultWith failwith

let makeOAuthSession () =
    { AccessToken = "dpop-access-token"
      RefreshToken = Some "dpop-refresh-token"
      ExpiresAt = DateTimeOffset.UtcNow.AddHours 1.0
      Did = testDid
      DpopKeyPair = ECDsa.Create (ECCurve.NamedCurves.nistP256)
      TokenEndpoint = "https://auth.bsky.social/token" }

let testClientMetadata =
    { ClientId = "https://myapp.example.com/client-metadata.json"
      ClientUri = Some "https://myapp.example.com"
      RedirectUris = [ "https://myapp.example.com/callback" ]
      Scope = "atproto transition:generic"
      GrantTypes = [ "authorization_code"; "refresh_token" ]
      ResponseTypes = [ "code" ]
      TokenEndpointAuthMethod = "none"
      ApplicationType = "web"
      DpopBoundAccessTokens = true }

[<Tests>]
let bridgeTests =
    testList
        "OAuthBridge"
        [ testCase "resumeSession adds DPoP auth headers"
          <| fun () ->
              let mutable capturedAuth = ""
              let mutable capturedDPoP = ""

              let handler (req : HttpRequestMessage) =
                  match req.Headers.Authorization with
                  | null -> ()
                  | auth -> capturedAuth <- sprintf "%s %s" auth.Scheme auth.Parameter

                  match req.Headers.TryGetValues "DPoP" with
                  | true, values -> capturedDPoP <- values |> Seq.head
                  | false, _ -> ()

                  jsonResponse HttpStatusCode.OK {| value = "hello" |}

              let client = new HttpClient (new MockHandler (handler))
              let agent = AtpAgent.createWithClient client "https://bsky.social"
              let session = makeOAuthSession ()
              let oauthAgent = OAuthBridge.resumeSession testClientMetadata session None agent

              let result =
                  Xrpc.queryNoParams<{| value : string |}> "test.method" oauthAgent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Ok output ->
                  Expect.equal output.value "hello" "got response"
                  Expect.stringStarts capturedAuth "DPoP dpop-access-token" "DPoP auth header"
                  Expect.isNotEmpty capturedDPoP "DPoP proof header present"
              | Error e -> failtest $"Expected Ok, got Error: {e}"

          testCase "resumeSession preserves extra headers"
          <| fun () ->
              let mutable capturedProxy = ""

              let handler (req : HttpRequestMessage) =
                  match req.Headers.TryGetValues "atproto-proxy" with
                  | true, values -> capturedProxy <- values |> Seq.head
                  | false, _ -> ()

                  jsonResponse HttpStatusCode.OK {| value = "ok" |}

              let client = new HttpClient (new MockHandler (handler))
              let agent = AtpAgent.createWithClient client "https://bsky.social"
              let session = makeOAuthSession ()

              let oauthAgent =
                  OAuthBridge.resumeSession testClientMetadata session None agent
                  |> AtpAgent.withChatProxy

              let result =
                  Xrpc.queryNoParams<{| value : string |}> "test.method" oauthAgent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Ok _ ->
                  Expect.equal capturedProxy "did:web:api.bsky.chat#bsky_chat" "chat proxy header preserved"
              | Error e -> failtest $"Expected Ok, got Error: {e}"

          testCase "resumeSession refresh triggers onSessionUpdate"
          <| fun () ->
              let mutable updatedSession : OAuthSession option = None
              let mutable callCount = 0

              let handler (req : HttpRequestMessage) =
                  let uri = string req.RequestUri

                  if uri.Contains ("token") then
                      // Token refresh endpoint
                      jsonResponse
                          HttpStatusCode.OK
                          {| access_token = "new-access-token"
                             token_type = "DPoP"
                             expires_in = 3600
                             refresh_token = "new-refresh-token"
                             scope = "atproto"
                             sub = "did:plc:testuser" |}
                  else
                      callCount <- callCount + 1

                      if callCount = 1 then
                          jsonResponse
                              HttpStatusCode.Unauthorized
                              {| error = "ExpiredToken"
                                 message = "Token expired" |}
                      else
                          jsonResponse HttpStatusCode.OK {| value = "refreshed" |}

              let client = new HttpClient (new MockHandler (handler))
              let agent = AtpAgent.createWithClient client "https://bsky.social"
              let session = makeOAuthSession ()

              let oauthAgent =
                  OAuthBridge.resumeSession
                      testClientMetadata
                      session
                      (Some (fun s -> updatedSession <- Some s))
                      agent

              let result =
                  Xrpc.queryNoParams<{| value : string |}> "test.method" oauthAgent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Ok output ->
                  Expect.equal output.value "refreshed" "got response after refresh"
                  Expect.isSome updatedSession "onSessionUpdate called"
                  Expect.equal updatedSession.Value.AccessToken "new-access-token" "new access token"
              | Error e -> failtest $"Expected Ok, got Error: {e}"

          testCase "isExpired returns true for expired session"
          <| fun () ->
              let session =
                  { makeOAuthSession () with
                      ExpiresAt = DateTimeOffset.UtcNow.AddMinutes -5.0 }

              Expect.isTrue (OAuthBridge.isExpired session) "expired session"

          testCase "isExpired returns false for valid session"
          <| fun () ->
              let session = makeOAuthSession ()
              Expect.isFalse (OAuthBridge.isExpired session) "valid session"

          testCase "getSessionDid returns DID from session"
          <| fun () ->
              let session = makeOAuthSession ()
              Expect.equal (OAuthBridge.getSessionDid session) testDid "session DID" ]
