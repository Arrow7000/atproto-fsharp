module NsidTests

open Expecto
open FSharp.ATProto.Syntax

let validItems = TestHelpers.loadTestLines "syntax/nsid_syntax_valid.txt"
let invalidItems = TestHelpers.loadTestLines "syntax/nsid_syntax_invalid.txt"

[<Tests>]
let tests =
    testList
        "Nsid"
        [ testList
              "valid NSIDs parse successfully"
              [ for n in validItems do
                    testCase n
                    <| fun () -> Expect.isOk (Nsid.parse n) (sprintf "should parse: %s" n) ]
          testList
              "invalid NSIDs are rejected"
              [ for n in invalidItems do
                    testCase n
                    <| fun () -> Expect.isError (Nsid.parse n) (sprintf "should reject: %s" n) ]
          testList
              "roundtrip"
              [ for n in validItems do
                    testCase (sprintf "roundtrip %s" n)
                    <| fun () ->
                        let parsed = Nsid.parse n |> Result.defaultWith failwith
                        Expect.equal (Nsid.value parsed) n "roundtrip should preserve value" ] ]
