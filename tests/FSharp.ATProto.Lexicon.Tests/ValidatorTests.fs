module ValidatorTests

open System.IO
open System.Text.Json
open Expecto
open FSharp.ATProto.Lexicon

let private loadCatalog () : Map<string, LexiconDoc> =
    let catalogDir =
        Path.Combine (TestHelpers.solutionRoot, "extern", "atproto-interop-tests", "lexicon", "catalog")

    Directory.GetFiles (catalogDir, "*.json")
    |> Array.choose (fun path ->
        let json = File.ReadAllText (path)

        match LexiconParser.parse json with
        | Ok doc -> Some (FSharp.ATProto.Syntax.Nsid.value doc.Id, doc)
        | Error _ -> None)
    |> Map.ofArray

[<Tests>]
let validatorTests =
    let catalog = loadCatalog ()
    let validData = TestHelpers.loadInteropJson "lexicon/record-data-valid.json"
    let invalidData = TestHelpers.loadInteropJson "lexicon/record-data-invalid.json"

    testList
        "RecordValidator"
        [ testList
              "valid records"
              [ for item in validData.RootElement.EnumerateArray () do
                    let name = item.GetProperty("name").GetString ()
                    let data = item.GetProperty ("data")

                    testCase name (fun () ->
                        let result = RecordValidator.validate catalog data
                        Expect.isOk result (sprintf "Expected valid record '%s' to validate, got: %A" name result)) ]
          testList
              "invalid records"
              [ for i, item in invalidData.RootElement.EnumerateArray () |> Seq.mapi (fun i x -> i, x) do
                    let name = item.GetProperty("name").GetString ()
                    let label = sprintf "[%d] %s" i name
                    let data = item.GetProperty ("data")

                    testCase label (fun () ->
                        let result = RecordValidator.validate catalog data
                        Expect.isError result (sprintf "Expected invalid record '%s' to fail validation" name)) ] ]
