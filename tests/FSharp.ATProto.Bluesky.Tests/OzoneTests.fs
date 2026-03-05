module FSharp.ATProto.Bluesky.Tests.OzoneTests

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
      Did = Did.parse "did:plc:testmod" |> Result.defaultWith failwith
      Handle = Handle.parse "mod.bsky.social" |> Result.defaultWith failwith }

let private testServiceDid =
    Did.parse "did:plc:ozoneservice" |> Result.defaultWith failwith

let private parseDid s = Did.parse s |> Result.defaultWith failwith
let private parseAtUri s = AtUri.parse s |> Result.defaultWith failwith
let private parseCid s = Cid.parse s |> Result.defaultWith failwith

/// Creates a mock agent with session, capturing the request, and returning the given response body.
let private createOzoneAgent (captureRequest : HttpRequestMessage -> unit) (responseBody : obj) =
    let agent =
        createMockAgent (fun req ->
            captureRequest req
            jsonResponse HttpStatusCode.OK responseBody)

    agent.Session <- Some testSession
    agent

/// A minimal valid ModEventView JSON response that the generated deserializer can parse.
/// Uses $type internal tag with unwrapped record fields (matching AT Protocol JSON format).
let private minimalModEventViewJson =
    """{"createdAt":"2026-03-01T00:00:00.000Z","createdBy":"did:plc:testmod","event":{"$type":"tools.ozone.moderation.defs#modEventAcknowledge"},"id":1,"subject":{"$type":"com.atproto.admin.defs#repoRef","did":"did:plc:target"},"subjectBlobCids":[]}"""

/// Creates a mock agent that returns a pre-built JSON string (not serialized from an object).
let private createOzoneAgentWithRawJson (captureRequest : HttpRequestMessage -> unit) (jsonBody : string) =
    let agent =
        createMockAgent (fun req ->
            captureRequest req
            let response = new HttpResponseMessage(HttpStatusCode.OK)
            response.Content <- new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json")
            response)

    agent.Session <- Some testSession
    agent

// ── Proxy header tests ─────────────────────────────────────────────

[<Tests>]
let ozoneProxyTests =
    testList
        "Ozone proxy header"
        [ testCase "emitEvent adds atproto-proxy header with service DID"
          <| fun _ ->
              let mutable captured = None
              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) minimalModEventViewJson

              let _result =
                  Ozone.emitEvent
                      agent
                      testServiceDid
                      (OzoneSubject.Account(parseDid "did:plc:target"))
                      (ModerationAction.Acknowledge(Some "test ack"))
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = captured.Value
              let proxyValues = req.Headers.GetValues("atproto-proxy") |> Seq.toList
              Expect.contains proxyValues "did:plc:ozoneservice#atproto_labeler" "proxy header present"

          testCase "original agent is not mutated by ozone proxy"
          <| fun _ ->
              let mutable captured = None
              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) minimalModEventViewJson
              Expect.isEmpty agent.ExtraHeaders "no headers before call"

              let _result =
                  Ozone.emitEvent
                      agent
                      testServiceDid
                      (OzoneSubject.Account(parseDid "did:plc:target"))
                      (ModerationAction.Acknowledge None)
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isEmpty agent.ExtraHeaders "agent unchanged after call"

          testCase "queryStatuses adds atproto-proxy header"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createOzoneAgent
                      (fun req -> captured <- Some req)
                      {| subjectStatuses = [||]; cursor = null |}

              let _result =
                  Ozone.queryStatuses agent testServiceDid None None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = captured.Value
              let proxyValues = req.Headers.GetValues("atproto-proxy") |> Seq.toList
              Expect.contains proxyValues "did:plc:ozoneservice#atproto_labeler" "proxy header present"

          testCase "listMembers adds atproto-proxy header"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createOzoneAgent
                      (fun req -> captured <- Some req)
                      {| members = [||]; cursor = null |}

              let _result =
                  Ozone.listMembers agent testServiceDid None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = captured.Value
              let proxyValues = req.Headers.GetValues("atproto-proxy") |> Seq.toList
              Expect.contains proxyValues "did:plc:ozoneservice#atproto_labeler" "proxy header present"

          testCase "listTemplates adds atproto-proxy header"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createOzoneAgent
                      (fun req -> captured <- Some req)
                      {| communicationTemplates = [||] |}

              let _result =
                  Ozone.listTemplates agent testServiceDid
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = captured.Value
              let proxyValues = req.Headers.GetValues("atproto-proxy") |> Seq.toList
              Expect.contains proxyValues "did:plc:ozoneservice#atproto_labeler" "proxy header present"

          testCase "different service DIDs produce different proxy headers"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createOzoneAgent
                      (fun req -> captured <- Some req)
                      {| subjectStatuses = [||]; cursor = null |}

              let otherService = parseDid "did:plc:otherlabeler"

              let _result =
                  Ozone.queryStatuses agent otherService None None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let req = captured.Value
              let proxyValues = req.Headers.GetValues("atproto-proxy") |> Seq.toList
              Expect.contains proxyValues "did:plc:otherlabeler#atproto_labeler" "different service DID in proxy" ]

// ── emitEvent tests ────────────────────────────────────────────────

[<Tests>]
let emitEventTests =
    testList
        "Ozone.emitEvent"
        [ testCase "emitEvent sends to correct endpoint"
          <| fun _ ->
              let mutable captured = None
              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) minimalModEventViewJson

              let result =
                  Ozone.emitEvent
                      agent
                      testServiceDid
                      (OzoneSubject.Account(parseDid "did:plc:spammer"))
                      (ModerationAction.Takedown(Some "spam", Some 24L))
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"
              Expect.stringContains (req.RequestUri.ToString()) "tools.ozone.moderation.emitEvent" "correct endpoint"

          testCase "emitEvent sends RepoRef subject for Account"
          <| fun _ ->
              let mutable captured = None
              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) minimalModEventViewJson

              let _result =
                  Ozone.emitEvent
                      agent
                      testServiceDid
                      (OzoneSubject.Account(parseDid "did:plc:target"))
                      (ModerationAction.Acknowledge None)
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "com.atproto.admin.defs#repoRef" "subject is RepoRef"
              Expect.stringContains body "did:plc:target" "subject DID"

          testCase "emitEvent sends StrongRef subject for Record"
          <| fun _ ->
              let mutable captured = None
              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) minimalModEventViewJson

              let postRef =
                  { PostRef.Uri = parseAtUri "at://did:plc:author/app.bsky.feed.post/abc"
                    Cid = parseCid "bafyreiabc" }

              let _result =
                  Ozone.emitEvent
                      agent
                      testServiceDid
                      (OzoneSubject.Record postRef)
                      (ModerationAction.Label(None, [ "spam" ], []))
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "com.atproto.repo.strongRef" "subject is StrongRef"
              Expect.stringContains body "at://did:plc:author/app.bsky.feed.post/abc" "subject URI"
              Expect.stringContains body "bafyreiabc" "subject CID"

          testCase "emitEvent sends createdBy as session DID"
          <| fun _ ->
              let mutable captured = None
              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) minimalModEventViewJson

              let _result =
                  Ozone.emitEvent
                      agent
                      testServiceDid
                      (OzoneSubject.Account(parseDid "did:plc:target"))
                      (ModerationAction.Escalate(Some "needs review"))
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "did:plc:testmod" "createdBy is session DID"

          testCase "emitEvent returns NotLoggedIn error without session"
          <| fun _ ->
              let agent = createMockAgent (fun _ -> jsonResponse HttpStatusCode.OK {| |})

              let result =
                  Ozone.emitEvent
                      agent
                      testServiceDid
                      (OzoneSubject.Account(parseDid "did:plc:target"))
                      (ModerationAction.Acknowledge None)
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let err = Expect.wantError result "should fail without session"
              Expect.equal err.StatusCode 401 "status code"
              Expect.equal err.Error (Some "NotLoggedIn") "error code"

          testCase "emitEvent sends comment event type"
          <| fun _ ->
              let mutable captured = None
              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) minimalModEventViewJson

              let _result =
                  Ozone.emitEvent
                      agent
                      testServiceDid
                      (OzoneSubject.Account(parseDid "did:plc:target"))
                      (ModerationAction.Comment("sticky note", Some true))
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "modEventComment" "comment event type"
              Expect.stringContains body "sticky note" "comment text"

          testCase "emitEvent sends tag event with add and remove lists"
          <| fun _ ->
              let mutable captured = None
              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) minimalModEventViewJson

              let _result =
                  Ozone.emitEvent
                      agent
                      testServiceDid
                      (OzoneSubject.Account(parseDid "did:plc:target"))
                      (ModerationAction.Tag(None, [ "priority" ], [ "old-tag" ]))
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "modEventTag" "tag event type"
              Expect.stringContains body "priority" "added tag"
              Expect.stringContains body "old-tag" "removed tag"

          testCase "emitEvent sends mute event with duration"
          <| fun _ ->
              let mutable captured = None
              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) minimalModEventViewJson

              let _result =
                  Ozone.emitEvent
                      agent
                      testServiceDid
                      (OzoneSubject.Account(parseDid "did:plc:target"))
                      (ModerationAction.Mute(None, 48L))
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "modEventMute" "mute event type"
              Expect.stringContains body "48" "duration in hours"

          testCase "emitEvent sends takedown event type"
          <| fun _ ->
              let mutable captured = None
              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) minimalModEventViewJson

              let _result =
                  Ozone.emitEvent
                      agent
                      testServiceDid
                      (OzoneSubject.Account(parseDid "did:plc:spammer"))
                      (ModerationAction.Takedown(Some "spam takedown", Some 72L))
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "modEventTakedown" "takedown event type"
              Expect.stringContains body "spam takedown" "comment"
              Expect.stringContains body "72" "duration"

          testCase "emitEvent sends reverse takedown event type"
          <| fun _ ->
              let mutable captured = None
              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) minimalModEventViewJson

              let _result =
                  Ozone.emitEvent
                      agent
                      testServiceDid
                      (OzoneSubject.Account(parseDid "did:plc:target"))
                      (ModerationAction.ReverseTakedown(Some "reversed"))
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "modEventReverseTakedown" "reverse takedown event type"
              Expect.stringContains body "reversed" "comment"

          testCase "emitEvent sends label event with create and negate vals"
          <| fun _ ->
              let mutable captured = None
              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) minimalModEventViewJson

              let _result =
                  Ozone.emitEvent
                      agent
                      testServiceDid
                      (OzoneSubject.Account(parseDid "did:plc:target"))
                      (ModerationAction.Label(Some "labeling", [ "spam"; "nsfw" ], [ "clean" ]))
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "modEventLabel" "label event type"
              Expect.stringContains body "spam" "create label val"
              Expect.stringContains body "nsfw" "create label val"
              Expect.stringContains body "clean" "negate label val"

          testCase "emitEvent sends unmute reporter event type"
          <| fun _ ->
              let mutable captured = None
              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) minimalModEventViewJson

              let _result =
                  Ozone.emitEvent
                      agent
                      testServiceDid
                      (OzoneSubject.Account(parseDid "did:plc:target"))
                      (ModerationAction.UnmuteReporter(Some "unmuting"))
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "modEventUnmuteReporter" "unmute reporter event type"

          testCase "emitEvent sends resolve appeal event type"
          <| fun _ ->
              let mutable captured = None
              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) minimalModEventViewJson

              let _result =
                  Ozone.emitEvent
                      agent
                      testServiceDid
                      (OzoneSubject.Account(parseDid "did:plc:target"))
                      (ModerationAction.ResolveAppeal(Some "appeal resolved"))
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "modEventResolveAppeal" "resolve appeal event type" ]

// ── Query tests ────────────────────────────────────────────────────

[<Tests>]
let queryTests =
    testList
        "Ozone query operations"
        [ testCase "queryEvents calls correct endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createOzoneAgent
                      (fun req -> captured <- Some req)
                      {| events = [||]; cursor = null |}

              let result =
                  Ozone.queryEvents agent testServiceDid None None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.stringContains (req.RequestUri.ToString()) "tools.ozone.moderation.queryEvents" "correct endpoint"

          testCase "queryStatuses calls correct endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createOzoneAgent
                      (fun req -> captured <- Some req)
                      {| subjectStatuses = [||]; cursor = null |}

              let result =
                  Ozone.queryStatuses agent testServiceDid None None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.stringContains (req.RequestUri.ToString()) "tools.ozone.moderation.queryStatuses" "correct endpoint"

          testCase "queryStatuses returns page with empty items"
          <| fun _ ->
              let agent =
                  createOzoneAgent
                      (fun _ -> ())
                      {| subjectStatuses = [||]; cursor = null |}

              let result =
                  Ozone.queryStatuses agent testServiceDid None None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.isEmpty page.Items "empty items"
              Expect.isNone page.Cursor "no cursor"

          testCase "queryEvents returns page with empty events"
          <| fun _ ->
              let agent =
                  createOzoneAgent
                      (fun _ -> ())
                      {| events = [||]; cursor = null |}

              let result =
                  Ozone.queryEvents agent testServiceDid None None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.isEmpty page.Items "empty items"
              Expect.isNone page.Cursor "no cursor"

          testCase "getEvent calls correct endpoint"
          <| fun _ ->
              let mutable captured = None
              let eventJson =
                  """{"createdAt":"2026-03-01T00:00:00.000Z","createdBy":"did:plc:testmod","event":{"$type":"tools.ozone.moderation.defs#modEventAcknowledge"},"id":42,"subject":{"$type":"tools.ozone.moderation.defs#repoView","did":"did:plc:target","handle":"target.bsky.social","indexedAt":"2026-03-01T00:00:00.000Z","moderation":{},"relatedRecords":[]},"subjectBlobs":[]}"""

              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) eventJson

              let result =
                  Ozone.getEvent agent testServiceDid 42L
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.stringContains (req.RequestUri.ToString()) "tools.ozone.moderation.getEvent" "correct endpoint"

          testCase "getRepo calls correct endpoint"
          <| fun _ ->
              let mutable captured = None
              let repoJson =
                  """{"did":"did:plc:target","handle":"target.bsky.social","indexedAt":"2026-03-01T00:00:00.000Z","moderation":{"subjectStatus":null},"relatedRecords":[]}"""

              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) repoJson

              let result =
                  Ozone.getRepo agent testServiceDid (parseDid "did:plc:target")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.stringContains (req.RequestUri.ToString()) "tools.ozone.moderation.getRepo" "correct endpoint"

          testCase "getRecord calls correct endpoint"
          <| fun _ ->
              let mutable captured = None
              let recordJson =
                  """{"uri":"at://did:plc:author/app.bsky.feed.post/abc","cid":"bafyreiabc","indexedAt":"2026-03-01T00:00:00.000Z","moderation":{},"repo":{"did":"did:plc:author","handle":"author.bsky.social","indexedAt":"2026-03-01T00:00:00.000Z","moderation":{},"relatedRecords":[]},"blobs":[],"blobCids":[],"value":{}}"""

              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) recordJson

              let result =
                  Ozone.getRecord agent testServiceDid (parseAtUri "at://did:plc:author/app.bsky.feed.post/abc")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.stringContains (req.RequestUri.ToString()) "tools.ozone.moderation.getRecord" "correct endpoint"

          testCase "searchRepos calls correct endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createOzoneAgent
                      (fun req -> captured <- Some req)
                      {| repos = [||]; cursor = null |}

              let result =
                  Ozone.searchRepos agent testServiceDid "spammer" None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.stringContains (req.RequestUri.ToString()) "tools.ozone.moderation.searchRepos" "correct endpoint"

          testCase "getSubjects calls correct endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createOzoneAgent
                      (fun req -> captured <- Some req)
                      {| subjects = [||] |}

              let result =
                  Ozone.getSubjects agent testServiceDid [ "did:plc:target" ]
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.stringContains (req.RequestUri.ToString()) "tools.ozone.moderation.getSubjects" "correct endpoint" ]

// ── Team management tests ──────────────────────────────────────────

[<Tests>]
let teamTests =
    testList
        "Ozone team management"
        [ testCase "listMembers calls correct endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createOzoneAgent
                      (fun req -> captured <- Some req)
                      {| members = [||]; cursor = null |}

              let result =
                  Ozone.listMembers agent testServiceDid None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.stringContains (req.RequestUri.ToString()) "tools.ozone.team.listMembers" "correct endpoint"

          testCase "listMembers returns empty page"
          <| fun _ ->
              let agent =
                  createOzoneAgent
                      (fun _ -> ())
                      {| members = [||]; cursor = null |}

              let result =
                  Ozone.listMembers agent testServiceDid None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let page = Expect.wantOk result "should succeed"
              Expect.isEmpty page.Items "empty members"
              Expect.isNone page.Cursor "no cursor"

          testCase "addMember calls correct endpoint with POST"
          <| fun _ ->
              let mutable captured = None
              let memberJson =
                  """{"did":"did:plc:newmod","role":"tools.ozone.team.defs#roleModerator","disabled":false,"createdAt":"2026-03-01T00:00:00.000Z","updatedAt":"2026-03-01T00:00:00.000Z"}"""

              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) memberJson

              let result =
                  Ozone.addMember agent testServiceDid (parseDid "did:plc:newmod") TeamRole.Moderator
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"
              Expect.stringContains (req.RequestUri.ToString()) "tools.ozone.team.addMember" "correct endpoint"

          testCase "addMember sends correct role in body"
          <| fun _ ->
              let mutable captured = None
              let memberJson =
                  """{"did":"did:plc:newmod","role":"tools.ozone.team.defs#roleTriage","disabled":false}"""

              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) memberJson

              let _result =
                  Ozone.addMember agent testServiceDid (parseDid "did:plc:newmod") TeamRole.Triage
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "did:plc:newmod" "member DID in body"
              Expect.stringContains body "roleTriage" "role in body"

          testCase "addMember maps response to TeamMember domain type"
          <| fun _ ->
              let memberJson =
                  """{"did":"did:plc:newmod","role":"tools.ozone.team.defs#roleAdmin","disabled":false,"createdAt":"2026-03-01T00:00:00.000Z","updatedAt":"2026-03-01T00:00:00.000Z"}"""

              let agent = createOzoneAgentWithRawJson (fun _ -> ()) memberJson

              let result =
                  Ozone.addMember agent testServiceDid (parseDid "did:plc:newmod") TeamRole.Admin
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let member' = Expect.wantOk result "should succeed"
              Expect.equal (Did.value member'.Did) "did:plc:newmod" "member DID"
              Expect.equal member'.Role TeamRole.Admin "member role"
              Expect.isFalse member'.Disabled "not disabled"

          testCase "removeMember calls correct endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createOzoneAgent
                      (fun req -> captured <- Some req)
                      {| |}

              let result =
                  Ozone.removeMember agent testServiceDid (parseDid "did:plc:oldmod")
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"
              Expect.stringContains (req.RequestUri.ToString()) "tools.ozone.team.deleteMember" "correct endpoint"

          testCase "updateMember calls correct endpoint"
          <| fun _ ->
              let mutable captured = None
              let memberJson =
                  """{"did":"did:plc:existingmod","role":"tools.ozone.team.defs#roleAdmin","disabled":false}"""

              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) memberJson

              let result =
                  Ozone.updateMember agent testServiceDid (parseDid "did:plc:existingmod") (Some TeamRole.Admin) None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"
              Expect.stringContains (req.RequestUri.ToString()) "tools.ozone.team.updateMember" "correct endpoint" ]

// ── Communication template tests ────────────────────────────────────

let private templateJson =
    """{"id":"tmpl-1","name":"Warning","contentMarkdown":"You have been warned.","subject":"Warning Notice","disabled":false,"lastUpdatedBy":"did:plc:testmod","createdAt":"2026-03-01T00:00:00.000Z","updatedAt":"2026-03-01T00:00:00.000Z"}"""

[<Tests>]
let templateTests =
    testList
        "Ozone communication templates"
        [ testCase "listTemplates calls correct endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createOzoneAgent
                      (fun req -> captured <- Some req)
                      {| communicationTemplates = [||] |}

              let result =
                  Ozone.listTemplates agent testServiceDid
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.stringContains (req.RequestUri.ToString()) "tools.ozone.communication.listTemplates" "correct endpoint"

          testCase "listTemplates returns empty list"
          <| fun _ ->
              let agent =
                  createOzoneAgent
                      (fun _ -> ())
                      {| communicationTemplates = [||] |}

              let result =
                  Ozone.listTemplates agent testServiceDid
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let templates = Expect.wantOk result "should succeed"
              Expect.isEmpty templates "empty templates"

          testCase "createTemplate calls correct endpoint"
          <| fun _ ->
              let mutable captured = None
              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) templateJson

              let result =
                  Ozone.createTemplate agent testServiceDid "Warning" "You have been warned." "Warning Notice"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"
              Expect.stringContains (req.RequestUri.ToString()) "tools.ozone.communication.createTemplate" "correct endpoint"

          testCase "createTemplate maps response to CommunicationTemplate"
          <| fun _ ->
              let agent = createOzoneAgentWithRawJson (fun _ -> ()) templateJson

              let result =
                  Ozone.createTemplate agent testServiceDid "Warning" "You have been warned." "Warning Notice"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let template = Expect.wantOk result "should succeed"
              Expect.equal template.Id "tmpl-1" "template ID"
              Expect.equal template.Name "Warning" "template name"
              Expect.equal template.ContentMarkdown "You have been warned." "template content"
              Expect.equal template.Subject (Some "Warning Notice") "template subject"
              Expect.isFalse template.Disabled "not disabled"

          testCase "createTemplate sends correct body"
          <| fun _ ->
              let mutable captured = None
              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) templateJson

              let _result =
                  Ozone.createTemplate agent testServiceDid "Ban Notice" "You are banned." "Ban"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let body = captured.Value.Content.ReadAsStringAsync().Result
              Expect.stringContains body "Ban Notice" "name in body"
              Expect.stringContains body "You are banned." "content in body"

          testCase "updateTemplate calls correct endpoint"
          <| fun _ ->
              let mutable captured = None
              let agent = createOzoneAgentWithRawJson (fun req -> captured <- Some req) templateJson

              let result =
                  Ozone.updateTemplate agent testServiceDid "tmpl-1" (Some "Updated Warning") (Some "Updated content.") None None
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"
              Expect.stringContains (req.RequestUri.ToString()) "tools.ozone.communication.updateTemplate" "correct endpoint"

          testCase "deleteTemplate calls correct endpoint"
          <| fun _ ->
              let mutable captured = None

              let agent =
                  createOzoneAgent
                      (fun req -> captured <- Some req)
                      {| |}

              let result =
                  Ozone.deleteTemplate agent testServiceDid "tmpl-1"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.isOk result "should succeed"
              let req = captured.Value
              Expect.equal req.Method HttpMethod.Post "POST method"
              Expect.stringContains (req.RequestUri.ToString()) "tools.ozone.communication.deleteTemplate" "correct endpoint" ]

// ── Domain type tests ──────────────────────────────────────────────

[<Tests>]
let domainTypeTests =
    testList
        "Ozone domain types"
        [ testCase "OzoneSubject.Account wraps Did"
          <| fun _ ->
              let did = parseDid "did:plc:test"
              let subject = OzoneSubject.Account did

              match subject with
              | OzoneSubject.Account d -> Expect.equal (Did.value d) "did:plc:test" "DID matches"
              | _ -> failwith "Expected Account"

          testCase "OzoneSubject.Record wraps PostRef"
          <| fun _ ->
              let postRef =
                  { PostRef.Uri = parseAtUri "at://did:plc:author/app.bsky.feed.post/abc"
                    Cid = parseCid "bafyreiabc" }

              let subject = OzoneSubject.Record postRef

              match subject with
              | OzoneSubject.Record pr ->
                  Expect.equal (AtUri.value pr.Uri) "at://did:plc:author/app.bsky.feed.post/abc" "URI matches"
                  Expect.equal (Cid.value pr.Cid) "bafyreiabc" "CID matches"
              | _ -> failwith "Expected Record"

          testCase "ModerationAction.Takedown holds comment and duration"
          <| fun _ ->
              let action = ModerationAction.Takedown(Some "spam", Some 24L)

              match action with
              | ModerationAction.Takedown (comment, duration) ->
                  Expect.equal comment (Some "spam") "comment"
                  Expect.equal duration (Some 24L) "duration"
              | _ -> failwith "Expected Takedown"

          testCase "ModerationAction covers all cases"
          <| fun _ ->
              let actions : ModerationAction list =
                  [ ModerationAction.Takedown(None, None)
                    ModerationAction.ReverseTakedown None
                    ModerationAction.Acknowledge None
                    ModerationAction.Escalate None
                    ModerationAction.Label(None, [], [])
                    ModerationAction.Comment("test", None)
                    ModerationAction.Mute(None, 24L)
                    ModerationAction.Unmute None
                    ModerationAction.MuteReporter(None, None)
                    ModerationAction.UnmuteReporter None
                    ModerationAction.Tag(None, [], [])
                    ModerationAction.ResolveAppeal None ]

              Expect.hasLength actions 12 "twelve moderation action types"

          testCase "TeamRole covers all cases"
          <| fun _ ->
              let roles = [ TeamRole.Admin; TeamRole.Moderator; TeamRole.Triage; TeamRole.Verifier ]
              Expect.hasLength roles 4 "four team roles"

          testCase "TeamMember accessible through addMember"
          <| fun _ ->
              let memberJson =
                  """{"did":"did:plc:member","role":"tools.ozone.team.defs#roleModerator","disabled":true,"createdAt":"2026-03-01T00:00:00.000Z","updatedAt":"2026-03-01T12:00:00.000Z"}"""

              let agent = createOzoneAgentWithRawJson (fun _ -> ()) memberJson

              let result =
                  Ozone.addMember agent testServiceDid (parseDid "did:plc:member") TeamRole.Moderator
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let member' = Expect.wantOk result "should succeed"
              Expect.equal (Did.value member'.Did) "did:plc:member" "DID"
              Expect.equal member'.Role TeamRole.Moderator "role"
              Expect.isTrue member'.Disabled "disabled flag"
              Expect.isSome member'.CreatedAt "has created at"
              Expect.isSome member'.UpdatedAt "has updated at"

          testCase "CommunicationTemplate accessible through createTemplate"
          <| fun _ ->
              let tmplJson =
                  """{"id":"tmpl-1","name":"Test Template","contentMarkdown":"Content here","subject":"Subject line","disabled":false,"lastUpdatedBy":"did:plc:admin","createdAt":"2026-03-01T00:00:00.000Z","updatedAt":"2026-03-01T12:00:00.000Z","lang":null}"""

              let agent = createOzoneAgentWithRawJson (fun _ -> ()) tmplJson

              let result =
                  Ozone.createTemplate agent testServiceDid "Test Template" "Content here" "Subject line"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let template = Expect.wantOk result "should succeed"
              Expect.equal template.Id "tmpl-1" "ID"
              Expect.equal template.Name "Test Template" "name"
              Expect.equal template.ContentMarkdown "Content here" "content"
              Expect.equal template.Subject (Some "Subject line") "subject"
              Expect.isFalse template.Disabled "not disabled"
              Expect.equal (Did.value template.LastUpdatedBy) "did:plc:admin" "last updated by" ]
