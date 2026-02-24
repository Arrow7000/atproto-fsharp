module Base32Tests

open Expecto
open FSharp.ATProto.DRISL

[<Tests>]
let tests =
    testList "Base32" [
        testList "encode" [
            testCase "empty" <| fun () ->
                Expect.equal (Base32.encode [||]) "" "empty"
            testCase "single byte" <| fun () ->
                // 0x66 = 01100110 -> 01100 11000 -> 12, 24 -> 'm', 'y'
                // Actually let's use RFC 4648 test vectors (lowercased)
                // "f" (0x66) -> "my" (RFC 4648: "MY" -> "my")
                Expect.equal (Base32.encode [| 0x66uy |]) "my" "single byte 'f'"
            testCase "two bytes" <| fun () ->
                // "fo" (0x66 0x6F) -> "mzxq" (RFC 4648: "MZXQ" -> "mzxq")
                Expect.equal (Base32.encode [| 0x66uy; 0x6Fuy |]) "mzxq" "two bytes 'fo'"
            testCase "three bytes" <| fun () ->
                // "foo" -> "mzxw6"
                Expect.equal (Base32.encode [| 0x66uy; 0x6Fuy; 0x6Fuy |]) "mzxw6" "three bytes 'foo'"
            testCase "six bytes" <| fun () ->
                // "foobar" -> "mzxw6ytboi"
                Expect.equal (Base32.encode "foobar"B) "mzxw6ytboi" "six bytes 'foobar'"
            testCase "CID header bytes" <| fun () ->
                // CIDv1 + dag-cbor + sha256 header: [0x01, 0x71, 0x12, 0x20]
                let result = Base32.encode [| 0x01uy; 0x71uy; 0x12uy; 0x20uy |]
                // Should start with "afyrei" (the 'b' multibase prefix is NOT added by Base32)
                Expect.stringStarts result "afyrei" "CID header prefix"
        ]
        testList "decode" [
            testCase "empty" <| fun () ->
                Expect.equal (Base32.decode "") [||] "empty"
            testCase "single byte" <| fun () ->
                Expect.equal (Base32.decode "my") [| 0x66uy |] "decode 'my'"
            testCase "two bytes" <| fun () ->
                Expect.equal (Base32.decode "mzxq") [| 0x66uy; 0x6Fuy |] "decode 'mzxq'"
            testCase "three bytes" <| fun () ->
                Expect.equal (Base32.decode "mzxw6") [| 0x66uy; 0x6Fuy; 0x6Fuy |] "decode 'mzxw6'"
        ]
        testList "roundtrip" [
            testCase "various lengths" <| fun () ->
                for len in [0; 1; 2; 3; 4; 5; 10; 32; 36] do
                    let data = Array.init len (fun i -> byte (i % 256))
                    let encoded = Base32.encode data
                    let decoded = Base32.decode encoded
                    Expect.equal decoded data (sprintf "roundtrip length %d" len)
        ]
    ]
