module FSharp.ATProto.Pds.Tests.PdsTests

open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open FSharp.ATProto.Pds
open FSharp.ATProto.Core
open FSharp.ATProto.Syntax

let private withPds (f : HttpClient -> Task<unit>) : Task<unit> =
    task {
        let webAppBuilder = WebApplication.CreateBuilder ()
        webAppBuilder.WebHost.UseTestServer () |> ignore
        let app = webAppBuilder.Build ()

        let config = Pds.create "test.example.com"
        Pds.mapEndpoints config app |> ignore

        do! app.StartAsync ()

        let server =
            app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer> ()
            :?> TestServer

        let client = server.CreateClient ()

        try
            do! f client
        finally
            do app.StopAsync().GetAwaiter().GetResult ()
            client.Dispose ()
    }

let private withPdsBuilder
    (builder : PdsBuilder)
    (f : HttpClient -> Task<unit>)
    : Task<unit> =
    task {
        let webAppBuilder = WebApplication.CreateBuilder ()
        webAppBuilder.WebHost.UseTestServer () |> ignore
        let app = webAppBuilder.Build ()

        Pds.mapEndpoints builder app |> ignore

        do! app.StartAsync ()

        let server =
            app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer> ()
            :?> TestServer

        let client = server.CreateClient ()

        try
            do! f client
        finally
            do app.StopAsync().GetAwaiter().GetResult ()
            client.Dispose ()
    }

let private postJson (client : HttpClient) (url : string) (body : string) : Task<HttpResponseMessage> =
    client.PostAsync (url, new StringContent (body, Encoding.UTF8, "application/json"))

let private getJson (response : HttpResponseMessage) : Task<JsonElement> =
    task {
        let! body = response.Content.ReadAsStringAsync ()
        return JsonDocument.Parse(body).RootElement
    }

[<Tests>]
let pdsTests =
    testList
        "PDS integration"
        [
          testTask "health check returns 200" {
              do!
                  withPds (fun client ->
                      task {
                          let! response = client.GetAsync "/_health"
                          Expect.equal response.StatusCode HttpStatusCode.OK "health 200"
                      })
          }

          testTask "describeServer returns server info" {
              do!
                  withPds (fun client ->
                      task {
                          let! response = client.GetAsync "/xrpc/com.atproto.server.describeServer"
                          Expect.equal response.StatusCode HttpStatusCode.OK "describe 200"
                          let! json = getJson response
                          Expect.isTrue (json.TryGetProperty ("did") |> fst) "has did"
                      })
          }

          testTask "full lifecycle: create account, login, create record, read, delete" {
              do!
                  withPds (fun client ->
                      task {
                          let! createResp =
                              postJson
                                  client
                                  "/xrpc/com.atproto.server.createAccount"
                                  """{"handle":"alice.test.example.com","password":"hunter2"}"""

                          Expect.equal createResp.StatusCode HttpStatusCode.OK "createAccount 200"
                          let! createJson = getJson createResp
                          let did = createJson.GetProperty("did").GetString ()
                          Expect.isTrue (did.StartsWith "did:plc:") "DID is did:plc"

                          let! sessionResp =
                              postJson
                                  client
                                  "/xrpc/com.atproto.server.createSession"
                                  """{"identifier":"alice.test.example.com","password":"hunter2"}"""

                          Expect.equal sessionResp.StatusCode HttpStatusCode.OK "createSession 200"
                          let! sessionJson = getJson sessionResp
                          let loginAccessJwt = sessionJson.GetProperty("accessJwt").GetString ()

                          client.DefaultRequestHeaders.Authorization <-
                              System.Net.Http.Headers.AuthenticationHeaderValue ("Bearer", loginAccessJwt)

                          let! getSessionResp = client.GetAsync "/xrpc/com.atproto.server.getSession"
                          Expect.equal getSessionResp.StatusCode HttpStatusCode.OK "getSession 200"
                          let! getSessionJson = getJson getSessionResp
                          Expect.equal (getSessionJson.GetProperty("did").GetString ()) did "session DID matches"

                          let recordBody =
                              sprintf
                                  """{"repo":"%s","collection":"app.bsky.feed.post","rkey":"","record":{"$type":"app.bsky.feed.post","text":"Hello from PDS!","createdAt":"2024-01-01T00:00:00Z"}}"""
                                  did

                          let! createRecResp =
                              postJson client "/xrpc/com.atproto.repo.createRecord" recordBody

                          Expect.equal createRecResp.StatusCode HttpStatusCode.OK "createRecord 200"
                          let! recJson = getJson createRecResp
                          let uri = recJson.GetProperty("uri").GetString ()
                          Expect.isTrue (uri.StartsWith "at://") "URI is at://"

                          let rkey = uri.Split('/') |> Array.last

                          let! getRecResp =
                              client.GetAsync (
                                  sprintf
                                      "/xrpc/com.atproto.repo.getRecord?repo=%s&collection=app.bsky.feed.post&rkey=%s"
                                      did
                                      rkey
                              )

                          Expect.equal getRecResp.StatusCode HttpStatusCode.OK "getRecord 200"
                          let! getRecJson = getJson getRecResp
                          let text = getRecJson.GetProperty("value").GetProperty("text").GetString ()
                          Expect.equal text "Hello from PDS!" "record text matches"

                          let deleteBody =
                              sprintf
                                  """{"repo":"%s","collection":"app.bsky.feed.post","rkey":"%s"}"""
                                  did
                                  rkey

                          let! deleteResp =
                              postJson client "/xrpc/com.atproto.repo.deleteRecord" deleteBody

                          Expect.equal deleteResp.StatusCode HttpStatusCode.OK "deleteRecord 200"

                          let! getDeletedResp =
                              client.GetAsync (
                                  sprintf
                                      "/xrpc/com.atproto.repo.getRecord?repo=%s&collection=app.bsky.feed.post&rkey=%s"
                                      did
                                      rkey
                              )

                          Expect.equal getDeletedResp.StatusCode HttpStatusCode.BadRequest "deleted record returns 400"
                      })
          }

          testTask "resolveHandle returns DID" {
              do!
                  withPds (fun client ->
                      task {
                          let! _ =
                              postJson
                                  client
                                  "/xrpc/com.atproto.server.createAccount"
                                  """{"handle":"bob.test.example.com","password":"pw123"}"""

                          let! resolveResp =
                              client.GetAsync "/xrpc/com.atproto.identity.resolveHandle?handle=bob.test.example.com"

                          Expect.equal resolveResp.StatusCode HttpStatusCode.OK "resolveHandle 200"
                          let! json = getJson resolveResp
                          let did = json.GetProperty("did").GetString ()
                          Expect.isTrue (did.StartsWith "did:plc:") "resolved to did:plc"
                      })
          }

          testTask "unauthenticated createRecord returns 401" {
              do!
                  withPds (fun client ->
                      task {
                          let! resp =
                              postJson
                                  client
                                  "/xrpc/com.atproto.repo.createRecord"
                                  """{"repo":"did:plc:x","collection":"test","rkey":"","record":{}}"""

                          Expect.equal resp.StatusCode HttpStatusCode.Unauthorized "should be 401"
                      })
          }

          testTask "wrong password returns 401" {
              do!
                  withPds (fun client ->
                      task {
                          let! _ =
                              postJson
                                  client
                                  "/xrpc/com.atproto.server.createAccount"
                                  """{"handle":"carol.test.example.com","password":"correct"}"""

                          let! resp =
                              postJson
                                  client
                                  "/xrpc/com.atproto.server.createSession"
                                  """{"identifier":"carol.test.example.com","password":"wrong"}"""

                          Expect.equal resp.StatusCode HttpStatusCode.Unauthorized "wrong password = 401"
                      })
          }

          testTask "createUser returns authenticated AtpAgent" {
              do!
                  withPds (fun client ->
                      task {
                          let baseUrl = client.BaseAddress.ToString().TrimEnd '/'
                          let agent = AtpAgent.create baseUrl
                          agent.HttpClient.Dispose ()

                          let agentWithClient =
                              { agent with HttpClient = client }

                          let! createResult =
                              Xrpc.procedure<
                                  {| handle : string; password : string |},
                                  AtpSession
                               >
                                  "com.atproto.server.createAccount"
                                  {| handle = "dave.test.example.com"
                                     password = "pw123" |}
                                  agentWithClient

                          match createResult with
                          | Ok session ->
                              agentWithClient.Session <- Some session
                              Expect.isTrue (Did.value(session.Did).StartsWith "did:plc:") "DID starts with did:plc"
                              Expect.equal (Handle.value session.Handle) "dave.test.example.com" "handle matches"

                              let! getSessionResult =
                                  Xrpc.queryNoParams<{| did : string; handle : string |}>
                                      "com.atproto.server.getSession"
                                      agentWithClient

                              match getSessionResult with
                              | Ok sess ->
                                  Expect.equal sess.did (Did.value session.Did) "getSession DID matches"
                              | Error e ->
                                  failtest (sprintf "getSession failed: %A" e)
                          | Error e ->
                              failtest (sprintf "createAccount via Xrpc failed: %A" e)
                      })
          }

          testTask "onAccountCreated fires on account creation" {
              let mutable fired = None

              let builder =
                  Pds.create "test.example.com"
                  |> Pds.onAccountCreated (fun e -> fired <- Some e)

              do!
                  withPdsBuilder builder (fun client ->
                      task {
                          let! resp =
                              postJson
                                  client
                                  "/xrpc/com.atproto.server.createAccount"
                                  """{"handle":"eve.test.example.com","password":"pw123"}"""

                          Expect.equal resp.StatusCode HttpStatusCode.OK "createAccount 200"
                          Expect.isSome fired "hook should have fired"
                          let event = fired.Value
                          Expect.equal (Handle.value event.Handle) "eve.test.example.com" "event handle matches"
                          Expect.isTrue (Did.value(event.Did).StartsWith "did:plc:") "event DID"
                      })
          }

          testTask "onRecordCreated fires on record creation" {
              let mutable fired = None

              let builder =
                  Pds.create "test.example.com"
                  |> Pds.onRecordCreated (fun e -> fired <- Some e)

              do!
                  withPdsBuilder builder (fun client ->
                      task {
                          let! createResp =
                              postJson
                                  client
                                  "/xrpc/com.atproto.server.createAccount"
                                  """{"handle":"frank.test.example.com","password":"pw123"}"""

                          let! createJson = getJson createResp
                          let did = createJson.GetProperty("did").GetString ()
                          let token = createJson.GetProperty("accessJwt").GetString ()

                          client.DefaultRequestHeaders.Authorization <-
                              System.Net.Http.Headers.AuthenticationHeaderValue ("Bearer", token)

                          let recordBody =
                              sprintf
                                  """{"repo":"%s","collection":"app.bsky.feed.post","rkey":"","record":{"text":"hello"}}"""
                                  did

                          let! resp = postJson client "/xrpc/com.atproto.repo.createRecord" recordBody
                          Expect.equal resp.StatusCode HttpStatusCode.OK "createRecord 200"
                          Expect.isSome fired "hook should have fired"
                          Expect.equal fired.Value.Collection "app.bsky.feed.post" "event collection"
                      })
          }

          testTask "onRecordDeleted fires on record deletion" {
              let mutable fired = None

              let builder =
                  Pds.create "test.example.com"
                  |> Pds.onRecordDeleted (fun e -> fired <- Some e)

              do!
                  withPdsBuilder builder (fun client ->
                      task {
                          let! createResp =
                              postJson
                                  client
                                  "/xrpc/com.atproto.server.createAccount"
                                  """{"handle":"grace.test.example.com","password":"pw123"}"""

                          let! createJson = getJson createResp
                          let did = createJson.GetProperty("did").GetString ()
                          let token = createJson.GetProperty("accessJwt").GetString ()

                          client.DefaultRequestHeaders.Authorization <-
                              System.Net.Http.Headers.AuthenticationHeaderValue ("Bearer", token)

                          let recordBody =
                              sprintf
                                  """{"repo":"%s","collection":"app.bsky.feed.post","rkey":"test1","record":{"text":"hello"}}"""
                                  did

                          let! _ = postJson client "/xrpc/com.atproto.repo.createRecord" recordBody

                          let deleteBody =
                              sprintf
                                  """{"repo":"%s","collection":"app.bsky.feed.post","rkey":"test1"}"""
                                  did

                          let! resp = postJson client "/xrpc/com.atproto.repo.deleteRecord" deleteBody
                          Expect.equal resp.StatusCode HttpStatusCode.OK "deleteRecord 200"
                          Expect.isSome fired "hook should have fired"
                          Expect.equal fired.Value.Collection "app.bsky.feed.post" "event collection"
                          Expect.equal fired.Value.Rkey "test1" "event rkey"
                      })
          }
        ]
