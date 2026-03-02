module DidTests

open Expecto
open FSharp.ATProto.Syntax

let validItems = TestHelpers.loadTestLines "syntax/did_syntax_valid.txt"
let invalidItems = TestHelpers.loadTestLines "syntax/did_syntax_invalid.txt"

[<Tests>]
let tests =
    testList
        "Did"
        [ testList
              "valid DIDs parse successfully"
              [ for d in validItems do
                    testCase d <| fun () -> Expect.isOk (Did.parse d) (sprintf "should parse: %s" d) ]
          testList
              "invalid DIDs are rejected"
              [ for d in invalidItems do
                    testCase d
                    <| fun () -> Expect.isError (Did.parse d) (sprintf "should reject: %s" d) ]
          testList
              "roundtrip"
              [ for d in validItems do
                    testCase (sprintf "roundtrip %s" d)
                    <| fun () ->
                        let parsed = Did.parse d |> Result.defaultWith failwith
                        Expect.equal (Did.value parsed) d "roundtrip should preserve value" ] ]
