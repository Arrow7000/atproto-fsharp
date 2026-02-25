module ParserTests

open Expecto
open FSharp.ATProto.Lexicon

[<Tests>]
let parserTests =
    let validDoc = TestHelpers.loadInteropJson "lexicon/lexicon-valid.json"
    let invalidDoc = TestHelpers.loadInteropJson "lexicon/lexicon-invalid.json"

    testList "LexiconParser" [
        testList "valid lexicons" [
            for item in validDoc.RootElement.EnumerateArray() do
                let name = item.GetProperty("name").GetString()
                let lexicon = item.GetProperty("lexicon")
                testCase name (fun () ->
                    let result = LexiconParser.parseElement lexicon
                    Expect.isOk result
                        (sprintf "Expected valid lexicon '%s' to parse, got: %A" name result)
                )
        ]
        testList "invalid lexicons" [
            for item in invalidDoc.RootElement.EnumerateArray() do
                let name = item.GetProperty("name").GetString()
                let lexicon = item.GetProperty("lexicon")
                testCase name (fun () ->
                    let result = LexiconParser.parseElement lexicon
                    Expect.isError result
                        (sprintf "Expected invalid lexicon '%s' to fail parsing" name)
                )
        ]
    ]
