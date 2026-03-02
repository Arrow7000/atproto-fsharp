module RecordKeyTests

open Expecto
open FSharp.ATProto.Syntax

let validItems = TestHelpers.loadTestLines "syntax/recordkey_syntax_valid.txt"
let invalidItems = TestHelpers.loadTestLines "syntax/recordkey_syntax_invalid.txt"

[<Tests>]
let tests =
    testList
        "RecordKey"
        [ testList
              "valid record keys parse successfully"
              [ for r in validItems do
                    testCase r
                    <| fun () -> Expect.isOk (RecordKey.parse r) (sprintf "should parse: %s" r) ]
          testList
              "invalid record keys are rejected"
              [ for r in invalidItems do
                    testCase r
                    <| fun () -> Expect.isError (RecordKey.parse r) (sprintf "should reject: %s" r) ]
          testList
              "roundtrip"
              [ for r in validItems do
                    testCase (sprintf "roundtrip %s" r)
                    <| fun () ->
                        let parsed = RecordKey.parse r |> Result.defaultWith failwith
                        Expect.equal (RecordKey.value parsed) r "roundtrip should preserve value" ] ]
