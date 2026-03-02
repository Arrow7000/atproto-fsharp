module RateLimitTests

open System.Net
open System.Net.Http
open System.Text.Json.Serialization
open Expecto
open FSharp.ATProto.Core

type SimpleOutput =
    { [<JsonPropertyName("ok")>]
      Ok : bool }

type SimpleParams = { X : string }

[<Tests>]
let tests =
    testList
        "rate limiting"
        [ testCase "retries on 429 with Retry-After"
          <| fun () ->
              let mutable callCount = 0

              let handler =
                  new TestHelpers.MockHandler (fun _ ->
                      callCount <- callCount + 1

                      if callCount = 1 then
                          let resp = TestHelpers.emptyResponse (enum<HttpStatusCode> 429)
                          resp.Headers.Add ("Retry-After", "0")
                          resp
                      else
                          TestHelpers.jsonResponse HttpStatusCode.OK {| ok = true |})

              let agent =
                  { HttpClient = new HttpClient (handler)
                    BaseUrl = System.Uri ("https://bsky.social/")
                    Session = None
                    ExtraHeaders = [] }

              let result =
                  Xrpc.query<SimpleParams, SimpleOutput> "test.method" { X = "a" } agent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Ok output -> Expect.isTrue output.Ok "succeeded on retry"
              | Error e -> failtest $"Expected Ok, got Error: {e}"

              Expect.equal callCount 2 "called twice (original + retry)"

          testCase "returns error if retry also fails"
          <| fun () ->
              let handler =
                  new TestHelpers.MockHandler (fun _ ->
                      let resp = TestHelpers.emptyResponse (enum<HttpStatusCode> 429)
                      resp.Headers.Add ("Retry-After", "0")
                      resp)

              let agent =
                  { HttpClient = new HttpClient (handler)
                    BaseUrl = System.Uri ("https://bsky.social/")
                    Session = None
                    ExtraHeaders = [] }

              let result =
                  Xrpc.query<SimpleParams, SimpleOutput> "test.method" { X = "a" } agent
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              match result with
              | Error e -> Expect.equal e.StatusCode 429 "429 returned"
              | Ok _ -> failtest "Expected Error, got Ok" ]
