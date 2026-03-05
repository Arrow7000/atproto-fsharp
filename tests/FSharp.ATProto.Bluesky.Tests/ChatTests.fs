module FSharp.ATProto.Bluesky.Tests.ChatTests

open Expecto
open System.Net
open System.Net.Http
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax
open TestHelpers

let private testSession =
    { AccessJwt = "test-jwt"
      RefreshJwt = "test-refresh"
      Did = Did.parse "did:plc:testuser" |> Result.defaultWith failwith
      Handle = Handle.parse "test.bsky.social" |> Result.defaultWith failwith }

/// Creates a mock agent with session set and a request-capture callback.
/// The mock returns a valid chat.bsky.convo.sendMessage-style MessageView response.
let private createChatAgent (captureRequest : HttpRequestMessage -> unit) =
    let agent =
        createMockAgent (fun req ->
            captureRequest req
            // Minimal MessageView JSON that the generated deserializer can parse
            jsonResponse
                HttpStatusCode.OK
                {| id = "msg-1"
                   rev = "rev1"
                   sender = {| did = "did:plc:testuser" |}
                   text = "hello"
                   sentAt = "2026-02-26T00:00:00.000Z" |})

    agent.Session <- Some testSession
    agent

/// Creates a mock agent returning a ListConvos-shaped response.
let private createListConvosAgent (captureRequest : HttpRequestMessage -> unit) =
    let agent =
        createMockAgent (fun req ->
            captureRequest req
            jsonResponse HttpStatusCode.OK {| convos = [||]; cursor = null |})

    agent.Session <- Some testSession
    agent

/// Creates a mock agent returning a MuteConvo/UnmuteConvo-shaped response (wrapped ConvoView).
let private createConvoViewAgent (captureRequest : HttpRequestMessage -> unit) =
    let agent =
        createMockAgent (fun req ->
            captureRequest req

            jsonResponse
                HttpStatusCode.OK
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
    testList
        "Chat auto-proxy"
        [ testCase "sendMessage auto-adds atproto-proxy header"
          <| fun _ ->
              let mutable captured = None
              let agent = createChatAgent (fun req -> captured <- Some req)

              let _result =
                  Chat.sendMessage agent "convo-1" "hello"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = captured.Value
              let proxyValues = req.Headers.GetValues ("atproto-proxy") |> Seq.toList
              Expect.contains proxyValues "did:web:api.bsky.chat#bsky_chat" "proxy header present"

          testCase "sendMessage works without manual withChatProxy"
          <| fun _ ->
              let mutable captured = None
              let agent = createChatAgent (fun req -> captured <- Some req)
              // Note: we do NOT call AtpAgent.withChatProxy -- the Chat module handles it
              let result =
                  Chat.sendMessage agent "convo-1" "hello"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed without manual proxy setup"

          testCase "sendMessage does not duplicate proxy header when already set"
          <| fun _ ->
              let mutable captured = None
              let agent = createChatAgent (fun req -> captured <- Some req)
              let agentWithProxy = AtpAgent.withChatProxy agent

              let _result =
                  Chat.sendMessage agentWithProxy "convo-1" "hello"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = captured.Value
              let proxyValues = req.Headers.GetValues ("atproto-proxy") |> Seq.toList
              Expect.hasLength proxyValues 1 "only one proxy header value"

          testCase "listConvos auto-adds atproto-proxy header"
          <| fun _ ->
              let mutable captured = None
              let agent = createListConvosAgent (fun req -> captured <- Some req)

              let _result =
                  Chat.listConvos agent None None |> Async.AwaitTask |> Async.RunSynchronously

              let req = captured.Value
              let proxyValues = req.Headers.GetValues ("atproto-proxy") |> Seq.toList
              Expect.contains proxyValues "did:web:api.bsky.chat#bsky_chat" "proxy header present"

          testCase "muteConvo auto-adds atproto-proxy header"
          <| fun _ ->
              let mutable captured = None
              let agent = createConvoViewAgent (fun req -> captured <- Some req)

              let _result =
                  Chat.muteConvo agent "convo-1" |> Async.AwaitTask |> Async.RunSynchronously

              let req = captured.Value
              let proxyValues = req.Headers.GetValues ("atproto-proxy") |> Seq.toList
              Expect.contains proxyValues "did:web:api.bsky.chat#bsky_chat" "proxy header present"

          testCase "original agent is not mutated by auto-proxy"
          <| fun _ ->
              let mutable captured = None
              let agent = createChatAgent (fun req -> captured <- Some req)
              Expect.isEmpty agent.ExtraHeaders "no headers before call"

              let _result =
                  Chat.sendMessage agent "convo-1" "hello"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isEmpty agent.ExtraHeaders "agent unchanged after call" ]

// ── Helpers for new chat operation tests ────────────────────────────

/// Creates a mock agent with session set returning a minimal AcceptConvo/LeaveConvo-shaped response.
let private createVoidChatAgent (captureRequest : HttpRequestMessage -> unit) (responseBody : obj) =
    let agent =
        createMockAgent (fun req ->
            captureRequest req
            jsonResponse HttpStatusCode.OK responseBody)

    agent.Session <- Some testSession
    agent

// ── New chat operation tests ─────────────────────────────────────────

[<Tests>]
let acceptConvoTests =
    testList
        "Chat.acceptConvo"
        [ testCase "acceptConvo calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createVoidChatAgent (fun req -> captured <- Some req) {| rev = "rev1" |}

              let result =
                  Chat.acceptConvo agent "convo-123"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"

              Expect.stringContains
                  (req.RequestUri.ToString ())
                  "chat.bsky.convo.acceptConvo"
                  "correct endpoint"

          testCase "acceptConvo auto-adds chat proxy header"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createVoidChatAgent (fun req -> captured <- Some req) {| rev = "rev1" |}

              let _result =
                  Chat.acceptConvo agent "convo-123"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = captured.Value
              let proxyValues = req.Headers.GetValues ("atproto-proxy") |> Seq.toList
              Expect.contains proxyValues "did:web:api.bsky.chat#bsky_chat" "proxy header present"

          testCase "acceptConvo sends convoId in request body"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createVoidChatAgent (fun req -> captured <- Some req) {| rev = "rev1" |}

              let _result =
                  Chat.acceptConvo agent "convo-abc"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "convo-abc" "convoId in body" ]

[<Tests>]
let leaveConvoTests =
    testList
        "Chat.leaveConvo"
        [ testCase "leaveConvo calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createVoidChatAgent
                      (fun req -> captured <- Some req)
                      {| convoId = "convo-123"; rev = "rev1" |}

              let result =
                  Chat.leaveConvo agent "convo-123"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"

              Expect.stringContains
                  (req.RequestUri.ToString ())
                  "chat.bsky.convo.leaveConvo"
                  "correct endpoint"

          testCase "leaveConvo auto-adds chat proxy header"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createVoidChatAgent
                      (fun req -> captured <- Some req)
                      {| convoId = "convo-123"; rev = "rev1" |}

              let _result =
                  Chat.leaveConvo agent "convo-123"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = captured.Value
              let proxyValues = req.Headers.GetValues ("atproto-proxy") |> Seq.toList
              Expect.contains proxyValues "did:web:api.bsky.chat#bsky_chat" "proxy header present"

          testCase "leaveConvo sends convoId in request body"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createVoidChatAgent
                      (fun req -> captured <- Some req)
                      {| convoId = "convo-leave"; rev = "rev1" |}

              let _result =
                  Chat.leaveConvo agent "convo-leave"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "convo-leave" "convoId in body" ]

[<Tests>]
let addReactionTests =
    testList
        "Chat.addReaction"
        [ testCase "addReaction calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createVoidChatAgent
                      (fun req -> captured <- Some req)
                      {| message =
                          {| id = "msg-1"
                             rev = "rev1"
                             sender = {| did = "did:plc:testuser" |}
                             text = "hello"
                             sentAt = "2026-02-26T00:00:00.000Z" |} |}

              let result =
                  Chat.addReaction agent "convo-123" "msg-1" "\U0001F44D"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"

              Expect.stringContains
                  (req.RequestUri.ToString ())
                  "chat.bsky.convo.addReaction"
                  "correct endpoint"

          testCase "addReaction auto-adds chat proxy header"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createVoidChatAgent
                      (fun req -> captured <- Some req)
                      {| message =
                          {| id = "msg-1"
                             rev = "rev1"
                             sender = {| did = "did:plc:testuser" |}
                             text = "hello"
                             sentAt = "2026-02-26T00:00:00.000Z" |} |}

              let _result =
                  Chat.addReaction agent "convo-123" "msg-1" "\U0001F44D"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = captured.Value
              let proxyValues = req.Headers.GetValues ("atproto-proxy") |> Seq.toList
              Expect.contains proxyValues "did:web:api.bsky.chat#bsky_chat" "proxy header present"

          testCase "addReaction sends convoId, messageId, and value in request body"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createVoidChatAgent
                      (fun req -> captured <- Some req)
                      {| message =
                          {| id = "msg-1"
                             rev = "rev1"
                             sender = {| did = "did:plc:testuser" |}
                             text = "hello"
                             sentAt = "2026-02-26T00:00:00.000Z" |} |}

              let _result =
                  Chat.addReaction agent "convo-react" "msg-react" "\u2764\uFE0F"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "convo-react" "convoId in body"
              Expect.stringContains body "msg-react" "messageId in body" ]

[<Tests>]
let removeReactionTests =
    testList
        "Chat.removeReaction"
        [ testCase "removeReaction calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createVoidChatAgent
                      (fun req -> captured <- Some req)
                      {| message =
                          {| id = "msg-1"
                             rev = "rev1"
                             sender = {| did = "did:plc:testuser" |}
                             text = "hello"
                             sentAt = "2026-02-26T00:00:00.000Z" |} |}

              let result =
                  Chat.removeReaction agent "convo-123" "msg-1" "\U0001F44D"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"

              Expect.stringContains
                  (req.RequestUri.ToString ())
                  "chat.bsky.convo.removeReaction"
                  "correct endpoint"

          testCase "removeReaction auto-adds chat proxy header"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createVoidChatAgent
                      (fun req -> captured <- Some req)
                      {| message =
                          {| id = "msg-1"
                             rev = "rev1"
                             sender = {| did = "did:plc:testuser" |}
                             text = "hello"
                             sentAt = "2026-02-26T00:00:00.000Z" |} |}

              let _result =
                  Chat.removeReaction agent "convo-123" "msg-1" "\U0001F44D"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = captured.Value
              let proxyValues = req.Headers.GetValues ("atproto-proxy") |> Seq.toList
              Expect.contains proxyValues "did:web:api.bsky.chat#bsky_chat" "proxy header present" ]

// ── ConvoSummary for SRTP tests ─────────────────────────────────────

let private testConvoSummary =
    { ConvoSummary.Id = "convo-123"
      Members = []
      LastMessage =
          Some
              { Text = "hello"
                Sender = Did.parse "did:plc:sender" |> Result.defaultWith failwith
                SentAt = System.DateTimeOffset.UtcNow }
      UnreadCount = 0L
      IsMuted = false }

[<Tests>]
let convoWitnessSrtpTests =
    testList
        "ConvoWitness SRTP (Chat functions accept ConvoSummary)"
        [ testCase "sendMessage accepts ConvoSummary directly"
          <| fun _ ->
              let mutable captured = None
              let agent = createChatAgent (fun req -> captured <- Some req)

              let result =
                  Chat.sendMessage agent testConvoSummary "hello"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed with ConvoSummary"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "convo-123" "convoId extracted from ConvoSummary"

          testCase "getMessages accepts ConvoSummary directly"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createVoidChatAgent
                      (fun req -> captured <- Some req)
                      {| messages = [||]; cursor = null |}

              let result =
                  Chat.getMessages agent testConvoSummary None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed with ConvoSummary"
              let uri = captured.Value.RequestUri.ToString ()
              Expect.stringContains uri "convo-123" "convoId extracted from ConvoSummary"

          testCase "deleteMessage accepts ConvoSummary directly"
          <| fun _ ->
              let mutable captured = None
              let agent = createChatAgent (fun req -> captured <- Some req)

              let result =
                  Chat.deleteMessage agent testConvoSummary "msg-1"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed with ConvoSummary"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "convo-123" "convoId extracted from ConvoSummary"

          testCase "markRead accepts ConvoSummary directly"
          <| fun _ ->
              let mutable captured = None
              let agent = createConvoViewAgent (fun req -> captured <- Some req)

              let result =
                  Chat.markRead agent testConvoSummary
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed with ConvoSummary"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "convo-123" "convoId extracted from ConvoSummary"

          testCase "muteConvo accepts ConvoSummary directly"
          <| fun _ ->
              let mutable captured = None
              let agent = createConvoViewAgent (fun req -> captured <- Some req)

              let result =
                  Chat.muteConvo agent testConvoSummary
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed with ConvoSummary"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "convo-123" "convoId extracted from ConvoSummary"

          testCase "unmuteConvo accepts ConvoSummary directly"
          <| fun _ ->
              let mutable captured = None
              let agent = createConvoViewAgent (fun req -> captured <- Some req)

              let result =
                  Chat.unmuteConvo agent testConvoSummary
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed with ConvoSummary"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "convo-123" "convoId extracted from ConvoSummary"

          testCase "acceptConvo accepts ConvoSummary directly"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createVoidChatAgent (fun req -> captured <- Some req) {| rev = "rev1" |}

              let result =
                  Chat.acceptConvo agent testConvoSummary
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed with ConvoSummary"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "convo-123" "convoId extracted from ConvoSummary"

          testCase "leaveConvo accepts ConvoSummary directly"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createVoidChatAgent
                      (fun req -> captured <- Some req)
                      {| convoId = "convo-123"; rev = "rev1" |}

              let result =
                  Chat.leaveConvo agent testConvoSummary
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed with ConvoSummary"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "convo-123" "convoId extracted from ConvoSummary"

          testCase "addReaction accepts ConvoSummary directly"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createVoidChatAgent
                      (fun req -> captured <- Some req)
                      {| message =
                          {| id = "msg-1"
                             rev = "rev1"
                             sender = {| did = "did:plc:testuser" |}
                             text = "hello"
                             sentAt = "2026-02-26T00:00:00.000Z" |} |}

              let result =
                  Chat.addReaction agent testConvoSummary "msg-1" "\U0001F44D"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed with ConvoSummary"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "convo-123" "convoId extracted from ConvoSummary"

          testCase "removeReaction accepts ConvoSummary directly"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createVoidChatAgent
                      (fun req -> captured <- Some req)
                      {| message =
                          {| id = "msg-1"
                             rev = "rev1"
                             sender = {| did = "did:plc:testuser" |}
                             text = "hello"
                             sentAt = "2026-02-26T00:00:00.000Z" |} |}

              let result =
                  Chat.removeReaction agent testConvoSummary "msg-1" "\U0001F44D"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed with ConvoSummary"
              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "convo-123" "convoId extracted from ConvoSummary"

          testCase "getConvo accepts ConvoSummary directly"
          <| fun _ ->
              let mutable captured = None
              let agent = createConvoViewAgent (fun req -> captured <- Some req)

              let result =
                  Chat.getConvo agent testConvoSummary
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed with ConvoSummary"
              let uri = captured.Value.RequestUri.ToString ()
              Expect.stringContains uri "convo-123" "convoId extracted from ConvoSummary" ]

[<Tests>]
let getConvoTests =
    testList
        "Chat.getConvo"
        [ testCase "getConvo calls correct XRPC endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createConvoViewAgent (fun req -> captured <- Some req)

              let result =
                  Chat.getConvo agent "convo-1"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Get "GET method"
              Expect.stringContains (req.RequestUri.ToString ()) "chat.bsky.convo.getConvo" "correct endpoint"

          testCase "getConvo auto-adds chat proxy header"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createConvoViewAgent (fun req -> captured <- Some req)

              let _result =
                  Chat.getConvo agent "convo-1"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = captured.Value
              let proxyValues = req.Headers.GetValues ("atproto-proxy") |> Seq.toList
              Expect.contains proxyValues "did:web:api.bsky.chat#bsky_chat" "proxy header present"

          testCase "getConvo returns ConvoSummary from response"
          <| fun _ ->
              let agent =
                  createConvoViewAgent (fun _ -> ())

              let result =
                  Chat.getConvo agent "convo-1"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let convo = Expect.wantOk result "should succeed"
              Expect.equal convo.Id "convo-1" "convo ID" ]
