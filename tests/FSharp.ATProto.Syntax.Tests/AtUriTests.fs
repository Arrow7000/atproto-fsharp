module AtUriTests

open Expecto
open FSharp.ATProto.Syntax

let validItems = TestHelpers.loadTestLines "syntax/aturi_syntax_valid.txt"
let invalidItems = TestHelpers.loadTestLines "syntax/aturi_syntax_invalid.txt"

[<Tests>]
let tests =
    testList "AtUri" [
        testList "valid AT-URIs parse successfully" [
            for c in validItems do
                testCase c <| fun () ->
                    Expect.isOk (AtUri.parse c) (sprintf "should parse: %s" c)
        ]
        testList "invalid AT-URIs are rejected" [
            for c in invalidItems do
                testCase c <| fun () ->
                    Expect.isError (AtUri.parse c) (sprintf "should reject: %s" c)
        ]
        testList "roundtrip" [
            for c in validItems do
                testCase (sprintf "roundtrip %s" c) <| fun () ->
                    let parsed = AtUri.parse c |> Result.defaultWith failwith
                    Expect.equal (AtUri.value parsed) c "roundtrip should preserve value"
        ]
    ]
