module CidTests

open Expecto
open FSharp.ATProto.Syntax

let validItems = TestHelpers.loadTestLines "syntax/cid_syntax_valid.txt"
let invalidItems = TestHelpers.loadTestLines "syntax/cid_syntax_invalid.txt"

[<Tests>]
let tests =
    testList "Cid" [
        testList "valid CIDs parse successfully" [
            for c in validItems do
                testCase c <| fun () ->
                    Expect.isOk (Cid.parse c) (sprintf "should parse: %s" c)
        ]
        testList "invalid CIDs are rejected" [
            for c in invalidItems do
                testCase c <| fun () ->
                    Expect.isError (Cid.parse c) (sprintf "should reject: %s" c)
        ]
        testList "roundtrip" [
            for c in validItems do
                testCase (sprintf "roundtrip %s" c) <| fun () ->
                    let parsed = Cid.parse c |> Result.defaultWith failwith
                    Expect.equal (Cid.value parsed) c "roundtrip should preserve value"
        ]
    ]
