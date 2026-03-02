module UriTests

open Expecto
open FSharp.ATProto.Syntax

let validItems = TestHelpers.loadTestLines "syntax/uri_syntax_valid.txt"
let invalidItems = TestHelpers.loadTestLines "syntax/uri_syntax_invalid.txt"

[<Tests>]
let tests =
    testList
        "Uri"
        [ testList
              "valid URIs parse successfully"
              [ for c in validItems do
                    testCase c <| fun () -> Expect.isOk (Uri.parse c) (sprintf "should parse: %s" c) ]
          testList
              "invalid URIs are rejected"
              [ for c in invalidItems do
                    testCase c
                    <| fun () -> Expect.isError (Uri.parse c) (sprintf "should reject: %s" c) ]
          testList
              "roundtrip"
              [ for c in validItems do
                    testCase (sprintf "roundtrip %s" c)
                    <| fun () ->
                        let parsed = Uri.parse c |> Result.defaultWith failwith
                        Expect.equal (Uri.value parsed) c "roundtrip should preserve value" ] ]
