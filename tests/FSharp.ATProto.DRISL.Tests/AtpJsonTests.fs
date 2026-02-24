module AtpJsonTests

open Expecto
open System.Text.Json
open FSharp.ATProto.DRISL
open FSharp.ATProto.Syntax

let parse (json: string) = JsonDocument.Parse(json).RootElement

[<Tests>]
let tests =
    testList "AtpJson" [
        testList "fromJson basics" [
            testCase "simple object" <| fun () ->
                let result = AtpJson.fromJson (parse """{"a": 123, "b": "hello"}""")
                Expect.isOk result "should parse"

            testCase "integer-like float accepted" <| fun () ->
                let result = AtpJson.fromJson (parse """{"a": 123.0}""")
                Expect.isOk result "123.0 should be accepted as integer"
                let value = result |> Result.defaultWith failwith
                match value with
                | AtpValue.Object m ->
                    Expect.equal (Map.find "a" m) (AtpValue.Integer 123L) "123.0 -> Integer 123"
                | _ -> failwith "expected object"

            testCase "rejects non-integer float" <| fun () ->
                let result = AtpJson.fromJson (parse """{"a": 123.456}""")
                Expect.isError result "should reject float"

            testCase "rejects bare string at top level" <| fun () ->
                let result = AtpJson.fromJson (parse "\"blah\"")
                Expect.isError result "top-level must be object"

            testCase "rejects bare number at top level" <| fun () ->
                let result = AtpJson.fromJson (parse "123")
                Expect.isError result "top-level must be object"
        ]

        testList "fromJson $link" [
            testCase "valid link" <| fun () ->
                let json = """{"a": {"$link": "bafyreidfayvfuwqa7qlnopdjiqrxzs6blmoeu4rujcjtnci5beludirz2a"}}"""
                let result = AtpJson.fromJson (parse json)
                Expect.isOk result "should parse link"
                let value = result |> Result.defaultWith failwith
                match value with
                | AtpValue.Object m ->
                    match Map.find "a" m with
                    | AtpValue.Link cid -> Expect.equal (Cid.value cid) "bafyreidfayvfuwqa7qlnopdjiqrxzs6blmoeu4rujcjtnci5beludirz2a" "CID value"
                    | x -> failwithf "expected Link, got %A" x
                | _ -> failwith "expected object"

            testCase "rejects link with wrong type" <| fun () ->
                let result = AtpJson.fromJson (parse """{"a": {"$link": 1234}}""")
                Expect.isError result "link value must be string"

            testCase "rejects link with extra fields" <| fun () ->
                let result = AtpJson.fromJson (parse """{"a": {"$link": "bafyreidfayvfuwqa7qlnopdjiqrxzs6blmoeu4rujcjtnci5beludirz2a", "other": "blah"}}""")
                Expect.isError result "link must have exactly one key"
        ]

        testList "fromJson $bytes" [
            testCase "valid bytes" <| fun () ->
                let json = """{"a": {"$bytes": "AQID"}}"""
                let result = AtpJson.fromJson (parse json)
                Expect.isOk result "should parse bytes"
                let value = result |> Result.defaultWith failwith
                match value with
                | AtpValue.Object m ->
                    match Map.find "a" m with
                    | AtpValue.Bytes b -> Expect.equal b [| 1uy; 2uy; 3uy |] "decoded bytes"
                    | x -> failwithf "expected Bytes, got %A" x
                | _ -> failwith "expected object"

            testCase "rejects bytes with wrong type" <| fun () ->
                let result = AtpJson.fromJson (parse """{"a": {"$bytes": [1,2,3]}}""")
                Expect.isError result "bytes value must be string"

            testCase "rejects bytes with extra fields" <| fun () ->
                let result = AtpJson.fromJson (parse """{"a": {"$bytes": "AQID", "other": "x"}}""")
                Expect.isError result "bytes must have exactly one key"
        ]

        testList "fromJson $type" [
            testCase "valid $type" <| fun () ->
                let result = AtpJson.fromJson (parse """{"$type": "com.example.thing", "a": 1}""")
                Expect.isOk result "valid $type"

            testCase "rejects null $type" <| fun () ->
                let result = AtpJson.fromJson (parse """{"$type": null, "a": 1}""")
                Expect.isError result "$type cannot be null"

            testCase "rejects non-string $type" <| fun () ->
                let result = AtpJson.fromJson (parse """{"$type": 123, "a": 1}""")
                Expect.isError result "$type must be string"

            testCase "rejects empty $type" <| fun () ->
                let result = AtpJson.fromJson (parse """{"$type": "", "a": 1}""")
                Expect.isError result "$type cannot be empty"
        ]

        testList "fromJson blob" [
            testCase "valid blob" <| fun () ->
                let json = """{"blb": {"$type": "blob", "ref": {"$link": "bafkreiccldh766hwcnuxnf2wh6jgzepf2nlu2lvcllt63eww5p6chi4ity"}, "mimeType": "image/jpeg", "size": 10000}}"""
                let result = AtpJson.fromJson (parse json)
                Expect.isOk result "valid blob"

            testCase "rejects blob with string size" <| fun () ->
                let json = """{"blb": {"$type": "blob", "ref": {"$link": "bafkreiccldh766hwcnuxnf2wh6jgzepf2nlu2lvcllt63eww5p6chi4ity"}, "mimeType": "image/jpeg", "size": "10000"}}"""
                let result = AtpJson.fromJson (parse json)
                Expect.isError result "blob size must be integer"

            testCase "rejects blob with missing ref" <| fun () ->
                let json = """{"blb": {"$type": "blob", "mimeType": "image/jpeg", "size": 10000}}"""
                let result = AtpJson.fromJson (parse json)
                Expect.isError result "blob must have ref"
        ]
    ]
