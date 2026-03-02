module RealLexiconTests

open System.IO
open Expecto
open FSharp.ATProto.Lexicon

[<Tests>]
let realLexiconTests =
    let lexiconDir =
        Path.Combine (TestHelpers.solutionRoot, "extern", "atproto", "lexicons")

    testList
        "Real Lexicon Files"
        [ for file in Directory.GetFiles (lexiconDir, "*.json", SearchOption.AllDirectories) do
              let relativePath = Path.GetRelativePath (lexiconDir, file)

              testCase relativePath (fun () ->
                  let json = File.ReadAllText (file)
                  let result = LexiconParser.parse json
                  Expect.isOk result (sprintf "Failed to parse %s: %A" relativePath result)) ]
