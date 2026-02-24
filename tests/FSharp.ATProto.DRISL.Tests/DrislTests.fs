module DrislTests

open System
open Expecto
open FSharp.ATProto.DRISL
open FSharp.ATProto.Syntax

[<Tests>]
let tests =
    testList "Drisl" [
        testList "encode" [
            testCase "null" <| fun () ->
                let bytes = Drisl.encode AtpValue.Null
                Expect.equal bytes [| 0xF6uy |] "null = 0xF6"

            testCase "true" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Bool true)
                Expect.equal bytes [| 0xF5uy |] "true = 0xF5"

            testCase "false" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Bool false)
                Expect.equal bytes [| 0xF4uy |] "false = 0xF4"

            testCase "integer 0" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Integer 0L)
                Expect.equal bytes [| 0x00uy |] "integer 0"

            testCase "integer 23" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Integer 23L)
                Expect.equal bytes [| 0x17uy |] "integer 23 single byte"

            testCase "integer 24" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Integer 24L)
                Expect.equal bytes [| 0x18uy; 0x18uy |] "integer 24 two bytes"

            testCase "integer 123" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Integer 123L)
                Expect.equal bytes [| 0x18uy; 0x7Buy |] "integer 123"

            testCase "negative integer -1" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Integer -1L)
                Expect.equal bytes [| 0x20uy |] "negative -1"

            testCase "string abc" <| fun () ->
                let bytes = Drisl.encode (AtpValue.String "abc")
                Expect.equal bytes [| 0x63uy; 0x61uy; 0x62uy; 0x63uy |] "string abc"

            testCase "empty array" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Array [])
                Expect.equal bytes [| 0x80uy |] "empty array"

            testCase "empty map" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Object Map.empty)
                Expect.equal bytes [| 0xA0uy |] "empty map"

            testCase "map keys sorted by length then lex" <| fun () ->
                // Keys: "b" (1 char), "aa" (2 chars) -> sorted: "b" first (shorter)
                let value = AtpValue.Object (Map.ofList [("aa", AtpValue.Integer 2L); ("b", AtpValue.Integer 1L)])
                let bytes = Drisl.encode value
                // Should be: map(2), "b", 1, "aa", 2
                // 0xA2 0x61 0x62 0x01 0x62 0x61 0x61 0x02
                Expect.equal bytes [| 0xA2uy; 0x61uy; 0x62uy; 0x01uy; 0x62uy; 0x61uy; 0x61uy; 0x02uy |] "keys sorted by length"

            testCase "byte string" <| fun () ->
                let bytes = Drisl.encode (AtpValue.Bytes [| 0x01uy; 0x02uy; 0x03uy |])
                Expect.equal bytes [| 0x43uy; 0x01uy; 0x02uy; 0x03uy |] "byte string"

            testCase "CID link uses tag 42" <| fun () ->
                let cid = CidBinary.compute [||] // CID of empty data
                let bytes = Drisl.encode (AtpValue.Link cid)
                // Should start with tag 42: 0xD8 0x2A
                Expect.equal bytes.[0] 0xD8uy "tag prefix"
                Expect.equal bytes.[1] 0x2Auy "tag 42"
                // Then byte string with 0x00 prefix + CID binary
                // Byte string header: 0x58 0x25 (37 bytes = 1 prefix + 36 CID)
                Expect.equal bytes.[2] 0x58uy "byte string 1-byte length"
                Expect.equal bytes.[3] 0x25uy "37 bytes"
                Expect.equal bytes.[4] 0x00uy "identity multibase prefix"
        ]

        testList "decode" [
            testCase "null" <| fun () ->
                let result = Drisl.decode [| 0xF6uy |]
                Expect.equal result (Ok AtpValue.Null) "null"

            testCase "true" <| fun () ->
                let result = Drisl.decode [| 0xF5uy |]
                Expect.equal result (Ok (AtpValue.Bool true)) "true"

            testCase "integer 123" <| fun () ->
                let result = Drisl.decode [| 0x18uy; 0x7Buy |]
                Expect.equal result (Ok (AtpValue.Integer 123L)) "integer 123"

            testCase "negative integer" <| fun () ->
                let result = Drisl.decode [| 0x20uy |]
                Expect.equal result (Ok (AtpValue.Integer -1L)) "negative -1"

            testCase "string" <| fun () ->
                let result = Drisl.decode [| 0x63uy; 0x61uy; 0x62uy; 0x63uy |]
                Expect.equal result (Ok (AtpValue.String "abc")) "string abc"

            testCase "empty array" <| fun () ->
                let result = Drisl.decode [| 0x80uy |]
                Expect.equal result (Ok (AtpValue.Array [])) "empty array"

            testCase "empty map" <| fun () ->
                let result = Drisl.decode [| 0xA0uy |]
                Expect.equal result (Ok (AtpValue.Object Map.empty)) "empty map"

            testCase "rejects floats" <| fun () ->
                // 0xFB = double float, followed by 8 bytes
                let bytes = Array.concat [| [| 0xFBuy |]; BitConverter.GetBytes(1.5) |> Array.rev |]
                let result = Drisl.decode bytes
                Expect.isError result "should reject floats"

            testCase "rejects trailing bytes" <| fun () ->
                let result = Drisl.decode [| 0xF6uy; 0x00uy |]
                Expect.isError result "should reject trailing bytes"
        ]

        testList "roundtrip" [
            testCase "encode then decode primitives" <| fun () ->
                let values = [
                    AtpValue.Null
                    AtpValue.Bool true
                    AtpValue.Bool false
                    AtpValue.Integer 0L
                    AtpValue.Integer 123L
                    AtpValue.Integer -1L
                    AtpValue.Integer Int64.MaxValue
                    AtpValue.Integer Int64.MinValue
                    AtpValue.String ""
                    AtpValue.String "hello world"
                    AtpValue.Bytes [||]
                    AtpValue.Bytes [| 0x01uy; 0x02uy |]
                ]
                for v in values do
                    let encoded = Drisl.encode v
                    let decoded = Drisl.decode encoded |> Result.defaultWith failwith
                    let reEncoded = Drisl.encode decoded
                    Expect.equal reEncoded encoded (sprintf "roundtrip %A" v)

            testCase "encode then decode nested structure" <| fun () ->
                let value = AtpValue.Object (Map.ofList [
                    ("arr", AtpValue.Array [AtpValue.Integer 1L; AtpValue.Integer 2L])
                    ("nested", AtpValue.Object (Map.ofList [("x", AtpValue.String "y")]))
                ])
                let encoded = Drisl.encode value
                let decoded = Drisl.decode encoded
                Expect.equal decoded (Ok value) "nested roundtrip"

            testCase "encode then decode CID link" <| fun () ->
                let cid = CidBinary.compute [| 0xAAuy; 0xBBuy |]
                let value = AtpValue.Link cid
                let encoded = Drisl.encode value
                let decoded = Drisl.decode encoded
                Expect.equal decoded (Ok value) "CID link roundtrip"
        ]
    ]
