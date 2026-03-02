module TidTests

open Expecto
open FSharp.ATProto.Syntax

let validTids = TestHelpers.loadTestLines "syntax/tid_syntax_valid.txt"
let invalidTids = TestHelpers.loadTestLines "syntax/tid_syntax_invalid.txt"

[<Tests>]
let tests =
    testList
        "Tid"
        [ testList
              "valid TIDs parse successfully"
              [ for tid in validTids do
                    testCase tid
                    <| fun () -> Expect.isOk (Tid.parse tid) (sprintf "should parse: %s" tid) ]
          testList
              "invalid TIDs are rejected"
              [ for tid in invalidTids do
                    testCase tid
                    <| fun () -> Expect.isError (Tid.parse tid) (sprintf "should reject: %s" tid) ]
          testList
              "roundtrip"
              [ for tid in validTids do
                    testCase (sprintf "roundtrip %s" tid)
                    <| fun () ->
                        let parsed = Tid.parse tid |> Result.defaultWith failwith
                        Expect.equal (Tid.value parsed) tid "roundtrip should preserve value" ] ]
