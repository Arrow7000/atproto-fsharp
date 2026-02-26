module AtpAgentTests

open System.Net
open System.Net.Http
open System.Text.Json
open System.Text.Json.Serialization
open Expecto
open FSharp.ATProto.Core
open FSharp.ATProto.Syntax

let private parseDid s = Did.parse s |> Result.defaultWith failwith
let private parseHandle s = Handle.parse s |> Result.defaultWith failwith

let makeAgent (handler: HttpRequestMessage -> HttpResponseMessage) =
    let client = new HttpClient(new TestHelpers.MockHandler(handler))
    AtpAgent.createWithClient client "https://bsky.social"

[<Tests>]
let loginTests =
    testList "AtpAgent.login" [
        testCase "successful login stores session" <| fun () ->
            let agent = makeAgent (fun req ->
                Expect.stringContains (string req.RequestUri) "com.atproto.server.createSession" "calls createSession"
                Expect.equal req.Method HttpMethod.Post "is POST"
                TestHelpers.jsonResponse HttpStatusCode.OK
                    {| accessJwt = "access1"; refreshJwt = "refresh1"; did = "did:plc:alice"; handle = "my-handle.bsky.social" |})

            let result =
                AtpAgent.login "my-handle.bsky.social" "app-password" agent
                |> Async.AwaitTask |> Async.RunSynchronously

            match result with
            | Ok session ->
                Expect.equal (Did.value session.Did) "did:plc:alice" "did"
                Expect.equal (Handle.value session.Handle) "my-handle.bsky.social" "handle"
                Expect.equal session.AccessJwt "access1" "access jwt"
                Expect.isSome agent.Session "session stored on agent"
            | Error e -> failtest $"Expected Ok, got Error: {e}"

        testCase "failed login returns error" <| fun () ->
            let agent = makeAgent (fun _ ->
                TestHelpers.jsonResponse HttpStatusCode.Unauthorized
                    {| error = "AuthenticationRequired"; message = "Invalid identifier or password" |})

            let result =
                AtpAgent.login "bad" "bad" agent
                |> Async.AwaitTask |> Async.RunSynchronously

            match result with
            | Error e ->
                Expect.equal e.StatusCode 401 "status code"
                Expect.equal e.Error (Some "AuthenticationRequired") "error"
            | Ok _ -> failtest "Expected Error, got Ok"
    ]

type TestOutput =
    { [<JsonPropertyName("displayName")>]
      DisplayName: string }

type TestParams = { Actor: string }

[<Tests>]
let refreshTests =
    testList "Xrpc auto-refresh" [
        testCase "retries with new token on 401 ExpiredToken" <| fun () ->
            let mutable callCount = 0
            let agent = makeAgent (fun req ->
                callCount <- callCount + 1
                let uri = string req.RequestUri
                if uri.Contains("refreshSession") then
                    TestHelpers.jsonResponse HttpStatusCode.OK
                        {| accessJwt = "access2"; refreshJwt = "refresh2"; did = "did:plc:alice"; handle = "my-handle.bsky.social" |}
                elif callCount = 1 then
                    TestHelpers.jsonResponse HttpStatusCode.Unauthorized
                        {| error = "ExpiredToken"; message = "Token expired" |}
                else
                    TestHelpers.jsonResponse HttpStatusCode.OK
                        {| displayName = "Alice" |})

            agent.Session <- Some { AccessJwt = "old"; RefreshJwt = "refresh1"; Did = parseDid "did:plc:alice"; Handle = parseHandle "my-handle.bsky.social" }

            let result =
                Xrpc.query<TestParams, TestOutput> "app.bsky.actor.getProfile"
                    { Actor = "a" } agent
                |> Async.AwaitTask |> Async.RunSynchronously

            match result with
            | Ok output ->
                Expect.equal output.DisplayName "Alice" "got result after refresh"
                Expect.equal agent.Session.Value.AccessJwt "access2" "session updated"
            | Error e -> failtest $"Expected Ok after refresh, got Error: {e}"

        testCase "does not refresh when no session exists" <| fun () ->
            let agent = makeAgent (fun _ ->
                TestHelpers.jsonResponse HttpStatusCode.Unauthorized
                    {| error = "AuthenticationRequired"; message = "Not logged in" |})

            let result =
                Xrpc.query<TestParams, TestOutput> "app.bsky.actor.getProfile"
                    { Actor = "a" } agent
                |> Async.AwaitTask |> Async.RunSynchronously

            match result with
            | Error e -> Expect.equal e.StatusCode 401 "401 returned without refresh attempt"
            | Ok _ -> failtest "Expected Error, got Ok"

        testCase "returns refresh error if refresh itself fails" <| fun () ->
            let agent = makeAgent (fun req ->
                let uri = string req.RequestUri
                if uri.Contains("refreshSession") then
                    TestHelpers.jsonResponse HttpStatusCode.Unauthorized
                        {| error = "ExpiredToken"; message = "Refresh token expired" |}
                else
                    TestHelpers.jsonResponse HttpStatusCode.Unauthorized
                        {| error = "ExpiredToken"; message = "Token expired" |})

            agent.Session <- Some { AccessJwt = "old"; RefreshJwt = "oldref"; Did = parseDid "did:plc:x"; Handle = parseHandle "x.test" }

            let result =
                Xrpc.query<TestParams, TestOutput> "app.bsky.actor.getProfile"
                    { Actor = "a" } agent
                |> Async.AwaitTask |> Async.RunSynchronously

            match result with
            | Error e -> Expect.equal e.Error (Some "ExpiredToken") "returns refresh error"
            | Ok _ -> failtest "Expected Error, got Ok"
    ]
