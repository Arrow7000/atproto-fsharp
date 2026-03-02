module CidBinaryTests

open Expecto
open FSharp.ATProto.DRISL
open FSharp.ATProto.Syntax

// Known CIDs from interop fixture data
let knownCid1 = "bafyreiclp443lavogvhj3d2ob2cxbfuscni2k5jk7bebjzg7khl3esabwq"
let knownCid2 = "bafyreidfayvfuwqa7qlnopdjiqrxzs6blmoeu4rujcjtnci5beludirz2a"
let knownCidRaw = "bafkreiccldh766hwcnuxnf2wh6jgzepf2nlu2lvcllt63eww5p6chi4ity"

[<Tests>]
let tests =
    testList
        "CidBinary"
        [ testList
              "toBytes and fromBytes roundtrip"
              [ testCase "dag-cbor CID"
                <| fun () ->
                    let cid = Cid.parse knownCid1 |> Result.defaultWith failwith
                    let bytes = CidBinary.toBytes cid
                    // CIDv1 dag-cbor SHA-256: 1 + 1 + 1 + 1 + 32 = 36 bytes
                    Expect.equal bytes.Length 36 "CID binary should be 36 bytes"
                    Expect.equal bytes.[0] 0x01uy "version = 1"
                    Expect.equal bytes.[1] 0x71uy "codec = dag-cbor"
                    Expect.equal bytes.[2] 0x12uy "hash = sha256"
                    Expect.equal bytes.[3] 0x20uy "digest length = 32"
                    let result = CidBinary.fromBytes bytes
                    Expect.isOk result "fromBytes should succeed"
                    let roundtripped = result |> Result.defaultWith failwith
                    Expect.equal (Cid.value roundtripped) knownCid1 "roundtrip should preserve CID"

                testCase "raw CID"
                <| fun () ->
                    let cid = Cid.parse knownCidRaw |> Result.defaultWith failwith
                    let bytes = CidBinary.toBytes cid
                    Expect.equal bytes.[0] 0x01uy "version = 1"
                    Expect.equal bytes.[1] 0x55uy "codec = raw"
                    Expect.equal bytes.[2] 0x12uy "hash = sha256"
                    let result = CidBinary.fromBytes bytes
                    Expect.isOk result "fromBytes should succeed"
                    let roundtripped = result |> Result.defaultWith failwith
                    Expect.equal (Cid.value roundtripped) knownCidRaw "roundtrip raw CID" ]

          testList
              "compute"
              [ testCase "empty bytes hash"
                <| fun () ->
                    // SHA-256 of empty input is known: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
                    let cid = CidBinary.compute [||]
                    let cidStr = Cid.value cid
                    Expect.stringStarts cidStr "bafyrei" "CID should start with bafyrei (CIDv1 dag-cbor)"

                testCase "compute is deterministic"
                <| fun () ->
                    let data = [| 0x01uy; 0x02uy; 0x03uy |]
                    let cid1 = CidBinary.compute data
                    let cid2 = CidBinary.compute data
                    Expect.equal (Cid.value cid1) (Cid.value cid2) "same input -> same CID"

                testCase "different data -> different CID"
                <| fun () ->
                    let cid1 = CidBinary.compute [| 0x01uy |]
                    let cid2 = CidBinary.compute [| 0x02uy |]
                    Expect.notEqual (Cid.value cid1) (Cid.value cid2) "different input -> different CID" ]

          testList
              "fromBytes validation"
              [ testCase "rejects CIDv0"
                <| fun () ->
                    let result = CidBinary.fromBytes [| 0x00uy; 0x71uy; 0x12uy; 0x20uy |]
                    Expect.isError result "should reject version 0"

                testCase "rejects unsupported codec"
                <| fun () ->
                    // Version 1, codec 0x50 (not dag-cbor or raw), sha256
                    let bytes =
                        Array.concat
                            [| Varint.encode 1UL
                               Varint.encode 0x50UL
                               Varint.encode 0x12UL
                               Varint.encode 0x20UL
                               Array.zeroCreate 32 |]

                    let result = CidBinary.fromBytes bytes
                    Expect.isError result "should reject unsupported codec"

                testCase "rejects unsupported hash"
                <| fun () ->
                    // Version 1, dag-cbor, hash 0x13 (not sha256)
                    let bytes =
                        Array.concat
                            [| Varint.encode 1UL
                               Varint.encode 0x71UL
                               Varint.encode 0x13UL
                               Varint.encode 0x20UL
                               Array.zeroCreate 32 |]

                    let result = CidBinary.fromBytes bytes
                    Expect.isError result "should reject unsupported hash function" ] ]
