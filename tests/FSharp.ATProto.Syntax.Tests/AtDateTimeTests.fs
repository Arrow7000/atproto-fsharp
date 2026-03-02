module AtDateTimeTests

open Expecto
open FSharp.ATProto.Syntax

let validItems = TestHelpers.loadTestLines "syntax/datetime_syntax_valid.txt"
let invalidItems = TestHelpers.loadTestLines "syntax/datetime_syntax_invalid.txt"

[<Tests>]
let tests =
    testList
        "AtDateTime"
        [ testList
              "valid datetimes parse successfully"
              [ for c in validItems do
                    testCase c
                    <| fun () -> Expect.isOk (AtDateTime.parse c) (sprintf "should parse: %s" c) ]
          testList
              "invalid datetimes are rejected"
              [ for c in invalidItems do
                    testCase c
                    <| fun () -> Expect.isError (AtDateTime.parse c) (sprintf "should reject: %s" c) ]
          testList
              "roundtrip"
              [ for c in validItems do
                    testCase (sprintf "roundtrip %s" c)
                    <| fun () ->
                        let parsed = AtDateTime.parse c |> Result.defaultWith failwith
                        Expect.equal (AtDateTime.value parsed) c "roundtrip should preserve value" ] ]
