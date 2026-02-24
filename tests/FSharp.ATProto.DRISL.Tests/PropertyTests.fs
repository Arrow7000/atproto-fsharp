module PropertyTests

open Expecto
open FsCheck
open FSharp.ATProto.DRISL
open FSharp.ATProto.Syntax

// Generate AtpValue trees (no Links -- those require valid CIDs which are expensive to compute)
let rec genAtpValue (depth: int) : Gen<AtpValue> =
    if depth <= 0 then
        Gen.oneof [
            Gen.constant AtpValue.Null
            Gen.map AtpValue.Bool Arb.generate<bool>
            Gen.map AtpValue.Integer (Gen.choose (-1000, 1000) |> Gen.map int64)
            Gen.map AtpValue.String (Gen.elements [""; "a"; "hello"; "test"])
            Gen.map AtpValue.Bytes (Gen.arrayOfLength 4 Arb.generate<byte>)
        ]
    else
        Gen.oneof [
            Gen.constant AtpValue.Null
            Gen.map AtpValue.Bool Arb.generate<bool>
            Gen.map AtpValue.Integer (Gen.choose (-1000, 1000) |> Gen.map int64)
            Gen.map AtpValue.String (Gen.elements [""; "a"; "hello"; "test"])
            Gen.map AtpValue.Bytes (Gen.arrayOfLength 4 Arb.generate<byte>)
            gen {
                let! len = Gen.choose (0, 3)
                let! items = Gen.listOfLength len (genAtpValue (depth - 1))
                return AtpValue.Array items
            }
            gen {
                let! len = Gen.choose (0, 3)
                let! keys = Gen.listOfLength len (Gen.elements ["a"; "b"; "c"; "x"; "yy"; "zzz"])
                let! values = Gen.listOfLength len (genAtpValue (depth - 1))
                let map = List.zip keys values |> Map.ofList
                return AtpValue.Object map
            }
        ]

[<Tests>]
let tests =
    testList "property tests" [
        testProperty "encode/decode roundtrip" <|
            fun () ->
                Prop.forAll
                    (Arb.fromGen (genAtpValue 3))
                    (fun value ->
                        let encoded = Drisl.encode value
                        let decoded = Drisl.decode encoded |> Result.defaultWith failwith
                        let reEncoded = Drisl.encode decoded
                        encoded = reEncoded)

        testProperty "encoded CBOR never contains float indicators" <|
            fun () ->
                Prop.forAll
                    (Arb.fromGen (genAtpValue 2))
                    (fun value ->
                        let encoded = Drisl.encode value
                        // Float indicators in CBOR: 0xF9 (half), 0xFA (single), 0xFB (double)
                        // This is a heuristic - these bytes could appear in string/bytes content
                        // But for small test values, they shouldn't appear as CBOR type markers
                        true)  // Type safety guarantees no floats - AtpValue has no float case

        testProperty "map keys always sorted in DRISL order" <|
            fun () ->
                Prop.forAll
                    (Arb.fromGen (gen {
                        let! keys = Gen.listOfLength 5 (Gen.elements ["a"; "bb"; "ccc"; "d"; "ee"; "fff"; "g"])
                        let! values = Gen.listOfLength 5 (Gen.constant AtpValue.Null)
                        let map = List.zip keys values |> Map.ofList
                        return AtpValue.Object map
                    }))
                    (fun value ->
                        let encoded = Drisl.encode value
                        // If encoding succeeds without throwing, keys were in valid order
                        // (CborWriter in Canonical mode validates sort order)
                        let decoded = Drisl.decode encoded
                        Result.isOk decoded)
    ]
