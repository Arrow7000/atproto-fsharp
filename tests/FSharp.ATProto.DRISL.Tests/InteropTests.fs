module InteropTests

open Expecto
open System
open System.Text.Json
open FSharp.ATProto.DRISL
open FSharp.ATProto.Syntax

let fixturesDoc = TestHelpers.loadInteropJson "data-model/data-model-fixtures.json"
let validDoc = TestHelpers.loadInteropJson "data-model/data-model-valid.json"
let invalidDoc = TestHelpers.loadInteropJson "data-model/data-model-invalid.json"

let private padBase64 (s : string) =
    match s.Length % 4 with
    | 2 -> s + "=="
    | 3 -> s + "="
    | _ -> s

[<Tests>]
let tests =
    testList
        "Interop"
        [ testList
              "fixtures"
              [ let fixtures = fixturesDoc.RootElement.EnumerateArray () |> Seq.toArray

                for i in 0 .. fixtures.Length - 1 do
                    let fixture = fixtures.[i]
                    let jsonValue = fixture.GetProperty ("json")
                    let expectedCborBase64 = fixture.GetProperty("cbor_base64").GetString ()
                    let expectedCid = fixture.GetProperty("cid").GetString ()

                    testCase (sprintf "fixture %d: JSON -> AtpValue" i)
                    <| fun () ->
                        let result = AtpJson.fromJson jsonValue
                        Expect.isOk result (sprintf "fixture %d should parse" i)

                    testCase (sprintf "fixture %d: encode matches expected CBOR" i)
                    <| fun () ->
                        let atpValue = AtpJson.fromJson jsonValue |> Result.defaultWith failwith
                        let actualCbor = Drisl.encode atpValue
                        let expectedCbor = Convert.FromBase64String (padBase64 expectedCborBase64)
                        Expect.equal actualCbor expectedCbor (sprintf "fixture %d CBOR bytes should match" i)

                    testCase (sprintf "fixture %d: CID matches expected" i)
                    <| fun () ->
                        let atpValue = AtpJson.fromJson jsonValue |> Result.defaultWith failwith
                        let cbor = Drisl.encode atpValue
                        let actualCid = CidBinary.compute cbor
                        Expect.equal (Cid.value actualCid) expectedCid (sprintf "fixture %d CID should match" i)

                    testCase (sprintf "fixture %d: decode roundtrip" i)
                    <| fun () ->
                        let atpValue = AtpJson.fromJson jsonValue |> Result.defaultWith failwith
                        let cbor = Drisl.encode atpValue
                        let decoded = Drisl.decode cbor
                        Expect.isOk decoded (sprintf "fixture %d should decode" i)
                        let reEncoded = Drisl.encode (decoded |> Result.defaultWith failwith)
                        Expect.equal reEncoded cbor (sprintf "fixture %d re-encoding should be identical" i) ]

          testList
              "valid cases"
              [ let cases = validDoc.RootElement.EnumerateArray () |> Seq.toArray

                for i in 0 .. cases.Length - 1 do
                    let case = cases.[i]
                    let note = case.GetProperty("note").GetString ()
                    let jsonValue = case.GetProperty ("json")

                    testCase (sprintf "valid: %s" note)
                    <| fun () ->
                        let result = AtpJson.fromJson jsonValue
                        Expect.isOk result (sprintf "should accept: %s" note) ]

          testList
              "invalid cases"
              [ let cases = invalidDoc.RootElement.EnumerateArray () |> Seq.toArray

                for i in 0 .. cases.Length - 1 do
                    let case = cases.[i]
                    let note = case.GetProperty("note").GetString ()
                    let jsonValue = case.GetProperty ("json")

                    testCase (sprintf "invalid: %s" note)
                    <| fun () ->
                        let result = AtpJson.fromJson jsonValue
                        Expect.isError result (sprintf "should reject: %s" note) ] ]
