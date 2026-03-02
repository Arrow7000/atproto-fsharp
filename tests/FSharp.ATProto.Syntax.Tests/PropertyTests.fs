module PropertyTests

open Expecto
open FsCheck
open FSharp.ATProto.Syntax

// -- TID generators and properties --
let tidCharset = "234567abcdefghijklmnopqrstuvwxyz"
let tidFirstCharset = "234567abcdefghij"

let genTidString =
    gen {
        let! first = Gen.elements (tidFirstCharset |> Seq.toList)
        let! rest = Gen.arrayOfLength 12 (Gen.elements (tidCharset |> Seq.toList))
        return System.String (Array.append [| first |] rest)
    }

let genValidTid = genTidString |> Gen.map (Tid.parse >> Result.defaultWith failwith)

// -- DID generators --
let genDidString =
    gen {
        let! chars = Gen.arrayOfLength 24 (Gen.elements ([ 'a' .. 'z' ] @ [ '0' .. '9' ]))
        return sprintf "did:plc:%s" (System.String (chars))
    }

let genValidDid = genDidString |> Gen.map (Did.parse >> Result.defaultWith failwith)

// -- RecordKey generators --
let rkeyCharset =
    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._~:-"

let genRecordKeyString =
    gen {
        let! len = Gen.choose (1, 64)
        let! chars = Gen.arrayOfLength len (Gen.elements (rkeyCharset |> Seq.toList))
        let s = System.String (chars)
        if s = "." || s = ".." then return "valid-key" else return s
    }

let genValidRecordKey =
    genRecordKeyString |> Gen.map (RecordKey.parse >> Result.defaultWith failwith)

[<Tests>]
let tests =
    testList
        "property tests"
        [ testList
              "Tid"
              [ testProperty "parse(value(tid)) roundtrips for generated TIDs"
                <| fun () ->
                    Prop.forAll (Arb.fromGen genValidTid) (fun tid ->
                        let s = Tid.value tid

                        match Tid.parse s with
                        | Ok t2 -> Tid.value t2 = s
                        | Error _ -> false)

                testProperty "arbitrary strings rarely parse as valid TIDs"
                <| fun (s : string) ->
                    let result = s = null || s.Length <> 13 || (Tid.parse s |> Result.isError)

                    result
                    |> Prop.ofTestable
                    |> Prop.classify (s <> null && s.Length = 13) "length-13 strings" ]

          testList
              "Did"
              [ testProperty "parse(value(did)) roundtrips for generated DIDs"
                <| fun () ->
                    Prop.forAll (Arb.fromGen genValidDid) (fun did ->
                        let s = Did.value did

                        match Did.parse s with
                        | Ok d2 -> Did.value d2 = s
                        | Error _ -> false) ]

          testList
              "RecordKey"
              [ testProperty "parse(value(rkey)) roundtrips for generated RecordKeys"
                <| fun () ->
                    Prop.forAll (Arb.fromGen genValidRecordKey) (fun rkey ->
                        let s = RecordKey.value rkey

                        match RecordKey.parse s with
                        | Ok r2 -> RecordKey.value r2 = s
                        | Error _ -> false) ] ]
