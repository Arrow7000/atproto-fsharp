module XrpcTests

open System.Net
open System.Net.Http
open System.Text.Json
open System.Text.Json.Serialization
open Expecto
open FSharp.ATProto.Core
open FSharp.ATProto.Syntax

// Test types
type TestParams = { Actor : string }

type TestOutput =
    { [<JsonPropertyName("displayName")>]
      DisplayName : string
      [<JsonPropertyName("followersCount")>]
      FollowersCount : int64 }

type TestInput =
    { [<JsonPropertyName("repo")>]
      Repo : string
      [<JsonPropertyName("collection")>]
      Collection : string }

type TestProcOutput =
    { [<JsonPropertyName("uri")>]
      Uri : string }

let makeAgent (handler : HttpRequestMessage -> HttpResponseMessage) =
    let client = new HttpClient (new TestHelpers.MockHandler (handler))

    { HttpClient = client
      BaseUrl = System.Uri ("https://bsky.social/")
      Session = None
      ExtraHeaders = []
      AuthenticateRequest = None
      RefreshAuthentication = None
      OnSessionChanged = None }

[<Tests>]
let queryTests =
    testList
        "Xrpc.query"
        [ testCase "sends GET with query params and deserializes response"
          <| fun () ->
              let mutable capturedRequest : HttpRequestMessage option = None

              let agent =
                  makeAgent (fun req ->
                      capturedRequest <- Some req

                      TestHelpers.jsonResponse
                          HttpStatusCode.OK
                          {| displayName = "Alice"
                             followersCount = 42 |})

              let result =
                  Xrpc.query<TestParams, TestOutput>
                      "app.bsky.actor.getProfile"
                      { Actor = "my-handle.bsky.social" }
                      agent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = capturedRequest.Value
              Expect.equal req.Method HttpMethod.Get "should be GET"
              Expect.stringContains (string req.RequestUri) "xrpc/app.bsky.actor.getProfile" "correct path"
              Expect.stringContains (string req.RequestUri) "actor=my-handle.bsky.social" "has query param"

              match result with
              | Ok output ->
                  Expect.equal output.DisplayName "Alice" "display name"
                  Expect.equal output.FollowersCount 42L "followers count"
              | Error e -> failtest $"Expected Ok, got Error: {e}"

          testCase "returns XrpcError on 400"
          <| fun () ->
              let agent =
                  makeAgent (fun _ ->
                      TestHelpers.jsonResponse
                          HttpStatusCode.BadRequest
                          {| error = "InvalidRequest"
                             message = "Bad param" |})

              let result =
                  Xrpc.query<TestParams, TestOutput> "app.bsky.actor.getProfile" { Actor = "bad" } agent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Error e ->
                  Expect.equal e.StatusCode 400 "status code"
                  Expect.equal e.Error (Some "InvalidRequest") "error name"
                  Expect.equal e.Message (Some "Bad param") "error message"
              | Ok _ -> failtest "Expected Error, got Ok"

          testCase "returns XrpcError on 500 with no body"
          <| fun () ->
              let agent =
                  makeAgent (fun _ -> TestHelpers.emptyResponse HttpStatusCode.InternalServerError)

              let result =
                  Xrpc.query<TestParams, TestOutput> "app.bsky.actor.getProfile" { Actor = "x" } agent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Error e ->
                  Expect.equal e.StatusCode 500 "status code"
                  Expect.equal e.Error None "no error name"
              | Ok _ -> failtest "Expected Error, got Ok"

          testCase "includes auth header when session exists"
          <| fun () ->
              let mutable capturedRequest : HttpRequestMessage option = None

              let agent =
                  { makeAgent (fun req ->
                        capturedRequest <- Some req

                        TestHelpers.jsonResponse
                            HttpStatusCode.OK
                            {| displayName = "A"
                               followersCount = 0 |}) with
                      Session =
                          Some
                              { AccessJwt = "tok123"
                                RefreshJwt = "ref"
                                Did = Did.parse "did:plc:x" |> Result.defaultWith failwith
                                Handle = Handle.parse "a.bsky.social" |> Result.defaultWith failwith } }

              Xrpc.query<TestParams, TestOutput> "app.bsky.actor.getProfile" { Actor = "a" } agent
              |> Async.AwaitTask
              |> Async.RunSynchronously
              |> ignore

              let authHeader = capturedRequest.Value.Headers.Authorization
              Expect.isNotNull authHeader "auth header present"
              Expect.equal authHeader.Scheme "Bearer" "Bearer scheme"
              Expect.equal authHeader.Parameter "tok123" "token value" ]

[<Tests>]
let procedureTests =
    testList
        "Xrpc.procedure"
        [ testCase "sends POST with JSON body and deserializes response"
          <| fun () ->
              let mutable capturedRequest : HttpRequestMessage option = None
              let mutable capturedBody : string option = None

              let agent =
                  makeAgent (fun req ->
                      capturedRequest <- Some req
                      capturedBody <- Some (req.Content.ReadAsStringAsync().Result)
                      TestHelpers.jsonResponse HttpStatusCode.OK {| uri = "at://did:plc:x/app.bsky.feed.post/abc" |})

              let result =
                  Xrpc.procedure<TestInput, TestProcOutput>
                      "com.atproto.repo.createRecord"
                      { Repo = "did:plc:x"
                        Collection = "app.bsky.feed.post" }
                      agent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = capturedRequest.Value
              Expect.equal req.Method HttpMethod.Post "should be POST"
              Expect.stringContains (string req.RequestUri) "xrpc/com.atproto.repo.createRecord" "correct path"

              let bodyJson = JsonDocument.Parse (capturedBody.Value)
              Expect.equal (bodyJson.RootElement.GetProperty("repo").GetString ()) "did:plc:x" "repo in body"

              Expect.equal
                  (bodyJson.RootElement.GetProperty("collection").GetString ())
                  "app.bsky.feed.post"
                  "collection in body"

              match result with
              | Ok output -> Expect.equal output.Uri "at://did:plc:x/app.bsky.feed.post/abc" "uri"
              | Error e -> failtest $"Expected Ok, got Error: {e}"

          testCase "returns XrpcError on 401"
          <| fun () ->
              let agent =
                  makeAgent (fun _ ->
                      TestHelpers.jsonResponse
                          HttpStatusCode.Unauthorized
                          {| error = "AuthenticationRequired"
                             message = "Not logged in" |})

              let result =
                  Xrpc.procedure<TestInput, TestProcOutput>
                      "com.atproto.repo.createRecord"
                      { Repo = "x"; Collection = "y" }
                      agent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Error e ->
                  Expect.equal e.StatusCode 401 "status code"
                  Expect.equal e.Error (Some "AuthenticationRequired") "error name"
              | Ok _ -> failtest "Expected Error, got Ok" ]
