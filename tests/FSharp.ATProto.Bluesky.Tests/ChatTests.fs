module FSharp.ATProto.Bluesky.Tests.ChatTests

open Expecto
open System.Net
open System.Net.Http
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax
open TestHelpers

let private testSession =
    { AccessJwt = "test-jwt"; RefreshJwt = "test-refresh"; Did = Did.parse "did:plc:testuser" |> Result.defaultWith failwith; Handle = Handle.parse "test.bsky.social" |> Result.defaultWith failwith }

/// Creates a mock agent with session set and a request-capture callback.
/// The mock returns a valid chat.bsky.convo.sendMessage-style MessageView response.
let private createChatAgent (captureRequest: HttpRequestMessage -> unit) =
    let agent = createMockAgent (fun req ->
        captureRequest req
        // Minimal MessageView JSON that the generated deserializer can parse
        jsonResponse HttpStatusCode.OK
            {| id = "msg-1"
               rev = "rev1"
               sender = {| did = "did:plc:testuser" |}
               text = "hello"
               sentAt = "2026-02-26T00:00:00.000Z" |})
    agent.Session <- Some testSession
    agent

/// Creates a mock agent returning a ListConvos-shaped response.
let private createListConvosAgent (captureRequest: HttpRequestMessage -> unit) =
    let agent = createMockAgent (fun req ->
        captureRequest req
        jsonResponse HttpStatusCode.OK {| convos = [||]; cursor = null |})
    agent.Session <- Some testSession
    agent

/// Creates a mock agent returning a MuteConvo/UnmuteConvo-shaped response (wrapped ConvoView).
let private createConvoViewAgent (captureRequest: HttpRequestMessage -> unit) =
    let agent = createMockAgent (fun req ->
        captureRequest req
        jsonResponse HttpStatusCode.OK
            {| convo =
                {| id = "convo-1"
                   rev = "rev1"
                   members = [||]
                   muted = true
                   unreadCount = 0L |} |})
    agent.Session <- Some testSession
    agent

[<Tests>]
let chatProxyTests =
    testList "Chat auto-proxy" [
        testCase "sendMessage auto-adds atproto-proxy header" <| fun _ ->
            let mutable captured = None
            let agent = createChatAgent (fun req -> captured <- Some req)
            let _result = Chat.sendMessage agent "convo-1" "hello" |> Async.AwaitTask |> Async.RunSynchronously
            let req = captured.Value
            let proxyValues = req.Headers.GetValues("atproto-proxy") |> Seq.toList
            Expect.contains proxyValues "did:web:api.bsky.chat#bsky_chat" "proxy header present"

        testCase "sendMessage works without manual withChatProxy" <| fun _ ->
            let mutable captured = None
            let agent = createChatAgent (fun req -> captured <- Some req)
            // Note: we do NOT call AtpAgent.withChatProxy -- the Chat module handles it
            let result = Chat.sendMessage agent "convo-1" "hello" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isOk result "should succeed without manual proxy setup"

        testCase "sendMessage does not duplicate proxy header when already set" <| fun _ ->
            let mutable captured = None
            let agent = createChatAgent (fun req -> captured <- Some req)
            let agentWithProxy = AtpAgent.withChatProxy agent
            let _result = Chat.sendMessage agentWithProxy "convo-1" "hello" |> Async.AwaitTask |> Async.RunSynchronously
            let req = captured.Value
            let proxyValues = req.Headers.GetValues("atproto-proxy") |> Seq.toList
            Expect.hasLength proxyValues 1 "only one proxy header value"

        testCase "listConvos auto-adds atproto-proxy header" <| fun _ ->
            let mutable captured = None
            let agent = createListConvosAgent (fun req -> captured <- Some req)
            let _result = Chat.listConvos agent None None |> Async.AwaitTask |> Async.RunSynchronously
            let req = captured.Value
            let proxyValues = req.Headers.GetValues("atproto-proxy") |> Seq.toList
            Expect.contains proxyValues "did:web:api.bsky.chat#bsky_chat" "proxy header present"

        testCase "muteConvo auto-adds atproto-proxy header" <| fun _ ->
            let mutable captured = None
            let agent = createConvoViewAgent (fun req -> captured <- Some req)
            let _result = Chat.muteConvo agent "convo-1" |> Async.AwaitTask |> Async.RunSynchronously
            let req = captured.Value
            let proxyValues = req.Headers.GetValues("atproto-proxy") |> Seq.toList
            Expect.contains proxyValues "did:web:api.bsky.chat#bsky_chat" "proxy header present"

        testCase "original agent is not mutated by auto-proxy" <| fun _ ->
            let mutable captured = None
            let agent = createChatAgent (fun req -> captured <- Some req)
            Expect.isEmpty agent.ExtraHeaders "no headers before call"
            let _result = Chat.sendMessage agent "convo-1" "hello" |> Async.AwaitTask |> Async.RunSynchronously
            Expect.isEmpty agent.ExtraHeaders "agent unchanged after call"
    ]
