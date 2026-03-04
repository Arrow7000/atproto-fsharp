module FSharp.ATProto.Streaming.Tests.CarParserTests

open System.Formats.Cbor
open Expecto
open FSharp.ATProto.Streaming
open FSharp.ATProto.DRISL
open FSharp.ATProto.Syntax

/// Build a CIDv1 dag-cbor SHA-256 from raw data.
let private computeCidBytes (data : byte[]) : byte[] =
    use sha = System.Security.Cryptography.SHA256.Create ()
    let hash = sha.ComputeHash data

    Array.concat
        [| Varint.encode 1UL // version
           Varint.encode 0x71UL // dag-cbor codec
           Varint.encode 0x12UL // sha2-256
           Varint.encode 0x20UL // 32 bytes
           hash |]

/// Encode a CID as a DAG-CBOR tag-42 link (0x00 prefix + CID bytes).
let private writeCborCidLink (writer : CborWriter) (cidBytes : byte[]) =
    writer.WriteTag (LanguagePrimitives.EnumOfValue 42UL)
    writer.WriteByteString (Array.concat [| [| 0uy |] ; cidBytes |])

/// Build a minimal CAR v1 file with given blocks.
let private buildCar (roots : byte[] list) (blocks : (byte[] * byte[]) list) : byte[] =
    // Encode header
    let headerWriter = CborWriter (CborConformanceMode.Lax)
    headerWriter.WriteStartMap (System.Nullable 2)
    headerWriter.WriteTextString "version"
    headerWriter.WriteInt32 1
    headerWriter.WriteTextString "roots"
    headerWriter.WriteStartArray (System.Nullable roots.Length)

    for root in roots do
        writeCborCidLink headerWriter root

    headerWriter.WriteEndArray ()
    headerWriter.WriteEndMap ()
    let headerCbor = headerWriter.Encode ()
    let headerLenVarint = Varint.encode (uint64 headerCbor.Length)

    // Encode blocks: [varint(cid+data len) | cid bytes | data bytes]
    let blockParts =
        blocks
        |> List.map (fun (cidBytes, data) ->
            let blockContent = Array.concat [| cidBytes ; data |]
            let lenVarint = Varint.encode (uint64 blockContent.Length)
            Array.concat [| lenVarint ; blockContent |])

    Array.concat (headerLenVarint :: headerCbor :: blockParts)

[<Tests>]
let carParserTests =
    testList
        "CarParser"
        [
          test "parses CAR with single block" {
              let blockData = [| 0x01uy ; 0x02uy ; 0x03uy |]
              let cidBytes = computeCidBytes blockData
              let car = buildCar [ cidBytes ] [ (cidBytes, blockData) ]

              match CarParser.parse car with
              | Ok carFile ->
                  Expect.equal carFile.Roots.Length 1 "one root"
                  Expect.equal carFile.Blocks.Count 1 "one block"

                  let rootKey = Cid.value carFile.Roots.[0]
                  Expect.isTrue (carFile.Blocks.ContainsKey rootKey) "block keyed by root CID"
                  Expect.equal carFile.Blocks.[rootKey] blockData "block data matches"
              | Error e -> failtest $"Parse failed: {e}"
          }

          test "parses CAR with multiple blocks" {
              let data1 = [| 0xAAuy ; 0xBBuy |]
              let data2 = [| 0xCCuy ; 0xDDuy ; 0xEEuy |]
              let cid1 = computeCidBytes data1
              let cid2 = computeCidBytes data2
              let car = buildCar [ cid1 ] [ (cid1, data1) ; (cid2, data2) ]

              match CarParser.parse car with
              | Ok carFile ->
                  Expect.equal carFile.Roots.Length 1 "one root"
                  Expect.equal carFile.Blocks.Count 2 "two blocks"
              | Error e -> failtest $"Parse failed: {e}"
          }

          test "parses CAR with no blocks" {
              let rootData = [| 0x42uy |]
              let rootCid = computeCidBytes rootData
              let car = buildCar [ rootCid ] []

              match CarParser.parse car with
              | Ok carFile ->
                  Expect.equal carFile.Roots.Length 1 "one root"
                  Expect.equal carFile.Blocks.Count 0 "no blocks"
              | Error e -> failtest $"Parse failed: {e}"
          }

          test "parses CAR with multiple roots" {
              let data1 = [| 0x01uy |]
              let data2 = [| 0x02uy |]
              let cid1 = computeCidBytes data1
              let cid2 = computeCidBytes data2
              let car = buildCar [ cid1 ; cid2 ] [ (cid1, data1) ; (cid2, data2) ]

              match CarParser.parse car with
              | Ok carFile ->
                  Expect.equal carFile.Roots.Length 2 "two roots"
                  Expect.equal carFile.Blocks.Count 2 "two blocks"
              | Error e -> failtest $"Parse failed: {e}"
          }

          test "returns error for truncated data" {
              match CarParser.parse [| 0x01uy |] with
              | Error _ -> ()
              | Ok _ -> failtest "Should have failed"
          }

          test "returns error for empty data" {
              match CarParser.parse [||] with
              | Error _ -> ()
              | Ok _ -> failtest "Should have failed"
          }

          test "readCidBytes parses CIDv1 dag-cbor SHA-256" {
              let data = [| 0xFFuy |]
              let cidBytes = computeCidBytes data

              match CarParser.readCidBytes cidBytes 0 with
              | Ok (cidStr, consumed) ->
                  Expect.equal consumed cidBytes.Length "consumed all bytes"
                  Expect.stringStarts cidStr "b" "multibase b prefix"
              | Error e -> failtest $"readCidBytes failed: {e}"
          }

          test "readCidBytes at offset" {
              let data = [| 0xFFuy |]
              let cidBytes = computeCidBytes data
              let padded = Array.concat [| [| 0x00uy ; 0x00uy |] ; cidBytes |]

              match CarParser.readCidBytes padded 2 with
              | Ok (cidStr, consumed) ->
                  Expect.equal consumed cidBytes.Length "consumed CID bytes"
                  Expect.stringStarts cidStr "b" "multibase b prefix"
              | Error e -> failtest $"readCidBytes at offset failed: {e}"
          }

          test "block data can be large" {
              let blockData = Array.zeroCreate 10000
              blockData.[0] <- 0xABuy
              blockData.[9999] <- 0xCDuy
              let cidBytes = computeCidBytes blockData
              let car = buildCar [ cidBytes ] [ (cidBytes, blockData) ]

              match CarParser.parse car with
              | Ok carFile ->
                  let rootKey = Cid.value carFile.Roots.[0]
                  Expect.equal carFile.Blocks.[rootKey].Length 10000 "block size"
                  Expect.equal carFile.Blocks.[rootKey].[0] 0xABuy "first byte"
                  Expect.equal carFile.Blocks.[rootKey].[9999] 0xCDuy "last byte"
              | Error e -> failtest $"Parse failed: {e}"
          }
        ]
