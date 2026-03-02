module AtUriTests

open Expecto
open FSharp.ATProto.Syntax

let validItems = TestHelpers.loadTestLines "syntax/aturi_syntax_valid.txt"
let invalidItems = TestHelpers.loadTestLines "syntax/aturi_syntax_invalid.txt"

[<Tests>]
let tests =
    testList
        "AtUri"
        [ testList
              "valid AT-URIs parse successfully"
              [ for c in validItems do
                    testCase c
                    <| fun () -> Expect.isOk (AtUri.parse c) (sprintf "should parse: %s" c) ]
          testList
              "invalid AT-URIs are rejected"
              [ for c in invalidItems do
                    testCase c
                    <| fun () -> Expect.isError (AtUri.parse c) (sprintf "should reject: %s" c) ]
          testList
              "roundtrip"
              [ for c in validItems do
                    testCase (sprintf "roundtrip %s" c)
                    <| fun () ->
                        let parsed = AtUri.parse c |> Result.defaultWith failwith
                        Expect.equal (AtUri.value parsed) c "roundtrip should preserve value" ]
          testList
              "authority accessor"
              [ testCase "extracts DID authority"
                <| fun () ->
                    let uri =
                        AtUri.parse "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/3k2la3b"
                        |> Result.defaultWith failwith

                    Expect.equal (AtUri.authority uri) "did:plc:z72i7hdynmk6r22z27h6tvur" "should extract DID authority"
                testCase "extracts handle authority"
                <| fun () ->
                    let uri =
                        AtUri.parse "at://my-handle.bsky.social/app.bsky.feed.post/3k2la3b"
                        |> Result.defaultWith failwith

                    Expect.equal (AtUri.authority uri) "my-handle.bsky.social" "should extract handle authority"
                testCase "extracts authority from authority-only URI"
                <| fun () ->
                    let uri =
                        AtUri.parse "at://did:plc:z72i7hdynmk6r22z27h6tvur"
                        |> Result.defaultWith failwith

                    Expect.equal
                        (AtUri.authority uri)
                        "did:plc:z72i7hdynmk6r22z27h6tvur"
                        "should extract authority from authority-only URI"
                testCase "extracts authority from authority+collection URI"
                <| fun () ->
                    let uri =
                        AtUri.parse "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post"
                        |> Result.defaultWith failwith

                    Expect.equal
                        (AtUri.authority uri)
                        "did:plc:z72i7hdynmk6r22z27h6tvur"
                        "should extract authority from authority+collection URI" ]
          testList
              "collection accessor"
              [ testCase "extracts collection from full URI"
                <| fun () ->
                    let uri =
                        AtUri.parse "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/3k2la3b"
                        |> Result.defaultWith failwith

                    Expect.equal (AtUri.collection uri) (Some "app.bsky.feed.post") "should extract collection"
                testCase "extracts collection from authority+collection URI"
                <| fun () ->
                    let uri =
                        AtUri.parse "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post"
                        |> Result.defaultWith failwith

                    Expect.equal (AtUri.collection uri) (Some "app.bsky.feed.post") "should extract collection"
                testCase "returns None for authority-only URI"
                <| fun () ->
                    let uri =
                        AtUri.parse "at://did:plc:z72i7hdynmk6r22z27h6tvur"
                        |> Result.defaultWith failwith

                    Expect.equal (AtUri.collection uri) None "should return None for authority-only URI" ]
          testList
              "rkey accessor"
              [ testCase "extracts rkey from full URI"
                <| fun () ->
                    let uri =
                        AtUri.parse "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/3k2la3b"
                        |> Result.defaultWith failwith

                    Expect.equal (AtUri.rkey uri) (Some "3k2la3b") "should extract rkey"
                testCase "returns None for authority+collection URI"
                <| fun () ->
                    let uri =
                        AtUri.parse "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post"
                        |> Result.defaultWith failwith

                    Expect.equal (AtUri.rkey uri) None "should return None for authority+collection URI"
                testCase "returns None for authority-only URI"
                <| fun () ->
                    let uri =
                        AtUri.parse "at://did:plc:z72i7hdynmk6r22z27h6tvur"
                        |> Result.defaultWith failwith

                    Expect.equal (AtUri.rkey uri) None "should return None for authority-only URI"
                testCase "extracts rkey with special characters"
                <| fun () ->
                    let uri =
                        AtUri.parse "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/self"
                        |> Result.defaultWith failwith

                    Expect.equal (AtUri.rkey uri) (Some "self") "should extract 'self' rkey" ] ]
