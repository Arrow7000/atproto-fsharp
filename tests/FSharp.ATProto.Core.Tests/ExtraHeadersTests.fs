module ExtraHeadersTests

open System.Net
open System.Net.Http
open System.Text.Json.Serialization
open Expecto
open FSharp.ATProto.Core

type TestOutput =
    { [<JsonPropertyName("ok")>]
      Ok: bool }

type TestParams = { X: string }

type TestInput =
    { [<JsonPropertyName("data")>]
      Data: string }

let makeAgent (handler: HttpRequestMessage -> HttpResponseMessage) =
    AtpAgent.createWithClient (new HttpClient(new TestHelpers.MockHandler(handler))) "https://bsky.social"

[<Tests>]
let withChatProxyTests =
    testList "AtpAgent.withChatProxy" [
        testCase "adds atproto-proxy header" <| fun () ->
            let agent = makeAgent (fun _ -> TestHelpers.jsonResponse HttpStatusCode.OK {| ok = true |})
            let chatAgent = AtpAgent.withChatProxy agent
            Expect.contains chatAgent.ExtraHeaders ("atproto-proxy", "did:web:api.bsky.chat#bsky_chat") "proxy header present"

        testCase "preserves existing extra headers" <| fun () ->
            let agent = makeAgent (fun _ -> TestHelpers.jsonResponse HttpStatusCode.OK {| ok = true |})
            let agentWithHeader = { agent with ExtraHeaders = [("x-custom", "value")] }
            let chatAgent = AtpAgent.withChatProxy agentWithHeader
            Expect.hasLength chatAgent.ExtraHeaders 2 "two headers"
            Expect.contains chatAgent.ExtraHeaders ("atproto-proxy", "did:web:api.bsky.chat#bsky_chat") "proxy header"
            Expect.contains chatAgent.ExtraHeaders ("x-custom", "value") "existing header preserved"

        testCase "new agent has empty extra headers" <| fun () ->
            let agent = makeAgent (fun _ -> TestHelpers.jsonResponse HttpStatusCode.OK {| ok = true |})
            Expect.isEmpty agent.ExtraHeaders "empty by default"
    ]

[<Tests>]
let extraHeadersInXrpcTests =
    testList "XRPC extra headers" [
        testCase "query includes extra headers in request" <| fun () ->
            let mutable capturedRequest: HttpRequestMessage option = None
            let agent = makeAgent (fun req ->
                capturedRequest <- Some req
                TestHelpers.jsonResponse HttpStatusCode.OK {| ok = true |})
            let agentWithHeaders = { agent with ExtraHeaders = [("atproto-proxy", "did:web:api.bsky.chat#bsky_chat")] }

            Xrpc.query<TestParams, TestOutput> "test.method" { X = "a" } agentWithHeaders
            |> Async.AwaitTask |> Async.RunSynchronously |> ignore

            let req = capturedRequest.Value
            let proxyValues = req.Headers.GetValues("atproto-proxy") |> Seq.toList
            Expect.equal proxyValues ["did:web:api.bsky.chat#bsky_chat"] "proxy header sent"

        testCase "procedure includes extra headers in request" <| fun () ->
            let mutable capturedRequest: HttpRequestMessage option = None
            let agent = makeAgent (fun req ->
                capturedRequest <- Some req
                TestHelpers.jsonResponse HttpStatusCode.OK {| ok = true |})
            let agentWithHeaders = { agent with ExtraHeaders = [("atproto-proxy", "did:web:api.bsky.chat#bsky_chat")] }

            Xrpc.procedure<TestInput, TestOutput> "test.method" { Data = "x" } agentWithHeaders
            |> Async.AwaitTask |> Async.RunSynchronously |> ignore

            let req = capturedRequest.Value
            let proxyValues = req.Headers.GetValues("atproto-proxy") |> Seq.toList
            Expect.equal proxyValues ["did:web:api.bsky.chat#bsky_chat"] "proxy header sent"

        testCase "procedureVoid includes extra headers in request" <| fun () ->
            let mutable capturedRequest: HttpRequestMessage option = None
            let agent = makeAgent (fun req ->
                capturedRequest <- Some req
                TestHelpers.emptyResponse HttpStatusCode.OK)
            let agentWithHeaders = { agent with ExtraHeaders = [("atproto-proxy", "did:web:api.bsky.chat#bsky_chat")] }

            Xrpc.procedureVoid<TestInput> "test.method" { Data = "x" } agentWithHeaders
            |> Async.AwaitTask |> Async.RunSynchronously |> ignore

            let req = capturedRequest.Value
            let proxyValues = req.Headers.GetValues("atproto-proxy") |> Seq.toList
            Expect.equal proxyValues ["did:web:api.bsky.chat#bsky_chat"] "proxy header sent"

        testCase "queryNoParams includes extra headers in request" <| fun () ->
            let mutable capturedRequest: HttpRequestMessage option = None
            let agent = makeAgent (fun req ->
                capturedRequest <- Some req
                TestHelpers.jsonResponse HttpStatusCode.OK {| ok = true |})
            let agentWithHeaders = { agent with ExtraHeaders = [("atproto-proxy", "did:web:api.bsky.chat#bsky_chat")] }

            Xrpc.queryNoParams<TestOutput> "test.method" agentWithHeaders
            |> Async.AwaitTask |> Async.RunSynchronously |> ignore

            let req = capturedRequest.Value
            let proxyValues = req.Headers.GetValues("atproto-proxy") |> Seq.toList
            Expect.equal proxyValues ["did:web:api.bsky.chat#bsky_chat"] "proxy header sent"

        testCase "multiple extra headers are all applied" <| fun () ->
            let mutable capturedRequest: HttpRequestMessage option = None
            let agent = makeAgent (fun req ->
                capturedRequest <- Some req
                TestHelpers.jsonResponse HttpStatusCode.OK {| ok = true |})
            let agentWithHeaders =
                { agent with ExtraHeaders = [("atproto-proxy", "did:web:api.bsky.chat#bsky_chat"); ("x-custom", "hello")] }

            Xrpc.queryNoParams<TestOutput> "test.method" agentWithHeaders
            |> Async.AwaitTask |> Async.RunSynchronously |> ignore

            let req = capturedRequest.Value
            let proxyValues = req.Headers.GetValues("atproto-proxy") |> Seq.toList
            Expect.equal proxyValues ["did:web:api.bsky.chat#bsky_chat"] "proxy header"
            let customValues = req.Headers.GetValues("x-custom") |> Seq.toList
            Expect.equal customValues ["hello"] "custom header"
    ]
