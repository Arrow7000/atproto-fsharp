module PaginationTests

open System.Net
open System.Net.Http
open System.Text.Json.Serialization
open Expecto
open FSharp.ATProto.Core

type PageParams =
    { Limit : int64
      Cursor : string option }

type PageOutput =
    { [<JsonPropertyName("items")>]
      Items : string list
      [<JsonPropertyName("cursor")>]
      Cursor : string option }

/// Helper to collect IAsyncEnumerable into a list.
module AsyncSeq =
    let toList (source : System.Collections.Generic.IAsyncEnumerable<'T>) : 'T list =
        task {
            let results = System.Collections.Generic.List<'T> ()
            let enumerator = source.GetAsyncEnumerator ()

            try
                let mutable hasMore = true

                while hasMore do
                    let! moved = enumerator.MoveNextAsync ()

                    if moved then
                        results.Add (enumerator.Current)
                    else
                        hasMore <- false

                return results |> Seq.toList
            finally
                enumerator.DisposeAsync().AsTask().Wait ()
        }
        |> Async.AwaitTask
        |> Async.RunSynchronously

[<Tests>]
let tests =
    testList
        "pagination"
        [ testCase "iterates through pages until no cursor"
          <| fun () ->
              let handler =
                  new TestHelpers.MockHandler (fun req ->
                      let uri = string req.RequestUri

                      if uri.Contains ("cursor=page2") then
                          TestHelpers.jsonResponse
                              HttpStatusCode.OK
                              {| items = [| "c"; "d" |]
                                 cursor = (null : string) |}
                      else
                          TestHelpers.jsonResponse
                              HttpStatusCode.OK
                              {| items = [| "a"; "b" |]
                                 cursor = "page2" |})

              let agent =
                  { HttpClient = new HttpClient (handler)
                    BaseUrl = System.Uri ("https://bsky.social/")
                    Session = None
                    ExtraHeaders = [] }

              let pages =
                  Xrpc.paginate<PageParams, PageOutput>
                      "test.list"
                      { Limit = 2L; Cursor = None }
                      (fun output -> output.Cursor)
                      (fun cursor ps -> { ps with Cursor = cursor })
                      agent
                  |> AsyncSeq.toList

              Expect.equal pages.Length 2 "two pages"

              match pages.[0] with
              | Ok p -> Expect.equal p.Items [ "a"; "b" ] "first page"
              | Error e -> failtest $"page 1 error: {e}"

              match pages.[1] with
              | Ok p -> Expect.equal p.Items [ "c"; "d" ] "second page"
              | Error e -> failtest $"page 2 error: {e}"

          testCase "stops on error"
          <| fun () ->
              let mutable callCount = 0

              let handler =
                  new TestHelpers.MockHandler (fun _ ->
                      callCount <- callCount + 1

                      if callCount = 1 then
                          TestHelpers.jsonResponse
                              HttpStatusCode.OK
                              {| items = [| "a" |]
                                 cursor = "page2" |}
                      else
                          TestHelpers.jsonResponse
                              HttpStatusCode.InternalServerError
                              {| error = "ServerError"
                                 message = "oops" |})

              let agent =
                  { HttpClient = new HttpClient (handler)
                    BaseUrl = System.Uri ("https://bsky.social/")
                    Session = None
                    ExtraHeaders = [] }

              let pages =
                  Xrpc.paginate<PageParams, PageOutput>
                      "test.list"
                      { Limit = 2L; Cursor = None }
                      (fun output -> output.Cursor)
                      (fun cursor ps -> { ps with Cursor = cursor })
                      agent
                  |> AsyncSeq.toList

              Expect.equal pages.Length 2 "two results (one ok, one error)"

              match pages.[1] with
              | Error e -> Expect.equal e.StatusCode 500 "error on page 2"
              | Ok _ -> failtest "Expected Error on page 2" ]
