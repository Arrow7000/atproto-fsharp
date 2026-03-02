module LanguageTests

open Expecto
open FSharp.ATProto.Syntax

let validItems = TestHelpers.loadTestLines "syntax/language_syntax_valid.txt"
let invalidItems = TestHelpers.loadTestLines "syntax/language_syntax_invalid.txt"

[<Tests>]
let tests =
    testList
        "Language"
        [ testList
              "valid language tags parse successfully"
              [ for c in validItems do
                    testCase c
                    <| fun () -> Expect.isOk (Language.parse c) (sprintf "should parse: %s" c) ]
          testList
              "invalid language tags are rejected"
              [ for c in invalidItems do
                    testCase c
                    <| fun () -> Expect.isError (Language.parse c) (sprintf "should reject: %s" c) ]
          testList
              "roundtrip"
              [ for c in validItems do
                    testCase (sprintf "roundtrip %s" c)
                    <| fun () ->
                        let parsed = Language.parse c |> Result.defaultWith failwith
                        Expect.equal (Language.value parsed) c "roundtrip should preserve value" ] ]
