module HandleTests

open Expecto
open FSharp.ATProto.Syntax

let validItems = TestHelpers.loadTestLines "syntax/handle_syntax_valid.txt"
let invalidItems = TestHelpers.loadTestLines "syntax/handle_syntax_invalid.txt"

[<Tests>]
let tests =
    testList "Handle" [
        testList "valid handles parse successfully" [
            for h in validItems do
                testCase h <| fun () ->
                    Expect.isOk (Handle.parse h) (sprintf "should parse: %s" h)
        ]
        testList "invalid handles are rejected" [
            for h in invalidItems do
                testCase h <| fun () ->
                    Expect.isError (Handle.parse h) (sprintf "should reject: %s" h)
        ]
        testList "roundtrip" [
            for h in validItems do
                testCase (sprintf "roundtrip %s" h) <| fun () ->
                    let parsed = Handle.parse h |> Result.defaultWith failwith
                    Expect.equal (Handle.value parsed) h "roundtrip should preserve value"
        ]
    ]
