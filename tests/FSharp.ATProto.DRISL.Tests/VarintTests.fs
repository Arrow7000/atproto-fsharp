module VarintTests

open Expecto
open FSharp.ATProto.DRISL

[<Tests>]
let tests =
    testList "Varint" [
        testList "encode" [
            testCase "zero" <| fun () ->
                Expect.equal (Varint.encode 0UL) [| 0x00uy |] "zero"
            testCase "one" <| fun () ->
                Expect.equal (Varint.encode 1UL) [| 0x01uy |] "one"
            testCase "127 single byte" <| fun () ->
                Expect.equal (Varint.encode 127UL) [| 0x7Fuy |] "127"
            testCase "128 two bytes" <| fun () ->
                Expect.equal (Varint.encode 128UL) [| 0x80uy; 0x01uy |] "128"
            testCase "0x71 dag-cbor codec" <| fun () ->
                Expect.equal (Varint.encode 0x71UL) [| 0x71uy |] "0x71"
            testCase "0x55 raw codec" <| fun () ->
                Expect.equal (Varint.encode 0x55UL) [| 0x55uy |] "0x55"
            testCase "0x12 sha256 code" <| fun () ->
                Expect.equal (Varint.encode 0x12UL) [| 0x12uy |] "0x12"
            testCase "0x20 digest length" <| fun () ->
                Expect.equal (Varint.encode 0x20UL) [| 0x20uy |] "0x20"
            testCase "300 two bytes" <| fun () ->
                // 300 = 0x12C -> low 7 bits: 0x2C | 0x80 = 0xAC, high: 0x02
                Expect.equal (Varint.encode 300UL) [| 0xACuy; 0x02uy |] "300"
            testCase "16384 three bytes" <| fun () ->
                // 16384 = 2^14 -> 0x80, 0x80, 0x01
                Expect.equal (Varint.encode 16384UL) [| 0x80uy; 0x80uy; 0x01uy |] "16384"
        ]
        testList "decode" [
            testCase "zero" <| fun () ->
                Expect.equal (Varint.decode [| 0x00uy |] 0) (0UL, 1) "zero"
            testCase "one" <| fun () ->
                Expect.equal (Varint.decode [| 0x01uy |] 0) (1UL, 1) "one"
            testCase "127" <| fun () ->
                Expect.equal (Varint.decode [| 0x7Fuy |] 0) (127UL, 1) "127"
            testCase "128" <| fun () ->
                Expect.equal (Varint.decode [| 0x80uy; 0x01uy |] 0) (128UL, 2) "128"
            testCase "300" <| fun () ->
                Expect.equal (Varint.decode [| 0xACuy; 0x02uy |] 0) (300UL, 2) "300"
            testCase "offset" <| fun () ->
                // Decode starting at offset 2
                Expect.equal (Varint.decode [| 0xFFuy; 0xFFuy; 0x71uy |] 2) (0x71UL, 1) "offset"
        ]
        testList "roundtrip" [
            testCase "encode then decode" <| fun () ->
                for v in [0UL; 1UL; 127UL; 128UL; 255UL; 300UL; 16384UL; 1000000UL] do
                    let encoded = Varint.encode v
                    let (decoded, len) = Varint.decode encoded 0
                    Expect.equal decoded v (sprintf "roundtrip %d" v)
                    Expect.equal len encoded.Length (sprintf "consumed all bytes for %d" v)
        ]
    ]
