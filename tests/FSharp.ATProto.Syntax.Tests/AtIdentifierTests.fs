module AtIdentifierTests

open Expecto
open FSharp.ATProto.Syntax

let validItems = TestHelpers.loadTestLines "syntax/atidentifier_syntax_valid.txt"

let invalidItems =
    TestHelpers.loadTestLines "syntax/atidentifier_syntax_invalid.txt"

[<Tests>]
let tests =
    testList
        "AtIdentifier"
        [ testList
              "valid AT identifiers parse successfully"
              [ for c in validItems do
                    testCase c
                    <| fun () -> Expect.isOk (AtIdentifier.parse c) (sprintf "should parse: %s" c) ]
          testList
              "invalid AT identifiers are rejected"
              [ for c in invalidItems do
                    testCase c
                    <| fun () -> Expect.isError (AtIdentifier.parse c) (sprintf "should reject: %s" c) ]
          testList
              "roundtrip"
              [ for c in validItems do
                    testCase (sprintf "roundtrip %s" c)
                    <| fun () ->
                        let parsed = AtIdentifier.parse c |> Result.defaultWith failwith
                        Expect.equal (AtIdentifier.value parsed) c "roundtrip should preserve value" ] ]
