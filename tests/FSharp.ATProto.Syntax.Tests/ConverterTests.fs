module ConverterTests

open System.Text.Json
open Expecto
open FSharp.ATProto.Syntax

/// JSON options with all Syntax converters registered for testing.
let private options =
    let opts = JsonSerializerOptions()
    opts.Converters.Add(DidConverter())
    opts.Converters.Add(HandleConverter())
    opts.Converters.Add(AtUriConverter())
    opts.Converters.Add(CidConverter())
    opts.Converters.Add(NsidConverter())
    opts.Converters.Add(TidConverter())
    opts.Converters.Add(RecordKeyConverter())
    opts.Converters.Add(AtDateTimeConverter())
    opts.Converters.Add(LanguageConverter())
    opts.Converters.Add(SyntaxUriConverter())
    opts

/// Helper: serialize a value to JSON and deserialize it back.
let private roundtrip<'a> (value: 'a) : 'a =
    let json = JsonSerializer.Serialize(value, options)
    JsonSerializer.Deserialize<'a>(json, options)

/// Helper: assert that deserializing invalid JSON throws JsonException.
let private expectInvalid<'a> (json: string) =
    Expect.throwsT<JsonException>
        (fun () -> JsonSerializer.Deserialize<'a>(json, options) |> ignore)
        "Should reject invalid input"

[<Tests>]
let tests =
    testList "JSON Converters" [
        testList "Did" [
            testCase "roundtrip" <| fun () ->
                let did = Did.parse "did:plc:z72i7hdynmk6r22z27h6tvur" |> Result.defaultWith failwith
                let result = roundtrip did
                Expect.equal (Did.value result) "did:plc:z72i7hdynmk6r22z27h6tvur" "DID roundtrip"

            testCase "rejects invalid" <| fun () ->
                expectInvalid<Did> "\"not-a-did\""
        ]

        testList "Handle" [
            testCase "roundtrip" <| fun () ->
                let handle = Handle.parse "alice.bsky.social" |> Result.defaultWith failwith
                let result = roundtrip handle
                Expect.equal (Handle.value result) "alice.bsky.social" "Handle roundtrip"

            testCase "rejects invalid" <| fun () ->
                expectInvalid<Handle> "\"not a handle!\""
        ]

        testList "AtUri" [
            testCase "roundtrip" <| fun () ->
                let atUri = AtUri.parse "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/3k2la3b" |> Result.defaultWith failwith
                let result = roundtrip atUri
                Expect.equal (AtUri.value result) "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/3k2la3b" "AtUri roundtrip"

            testCase "rejects invalid" <| fun () ->
                expectInvalid<AtUri> "\"https://example.com\""
        ]

        testList "Cid" [
            testCase "roundtrip" <| fun () ->
                let cid = Cid.parse "bafyreidfayvfkwc" |> Result.defaultWith failwith
                let result = roundtrip cid
                Expect.equal (Cid.value result) "bafyreidfayvfkwc" "Cid roundtrip"

            testCase "rejects invalid" <| fun () ->
                expectInvalid<Cid> "\"ab\""
        ]

        testList "Nsid" [
            testCase "roundtrip" <| fun () ->
                let nsid = Nsid.parse "app.bsky.feed.post" |> Result.defaultWith failwith
                let result = roundtrip nsid
                Expect.equal (Nsid.value result) "app.bsky.feed.post" "Nsid roundtrip"

            testCase "rejects invalid" <| fun () ->
                expectInvalid<Nsid> "\"not-an-nsid\""
        ]

        testList "Tid" [
            testCase "roundtrip" <| fun () ->
                let tid = Tid.parse "3k2la3bhx2s22" |> Result.defaultWith failwith
                let result = roundtrip tid
                Expect.equal (Tid.value result) "3k2la3bhx2s22" "Tid roundtrip"

            testCase "rejects invalid" <| fun () ->
                expectInvalid<Tid> "\"tooshort\""
        ]

        testList "RecordKey" [
            testCase "roundtrip" <| fun () ->
                let rkey = RecordKey.parse "self" |> Result.defaultWith failwith
                let result = roundtrip rkey
                Expect.equal (RecordKey.value result) "self" "RecordKey roundtrip"

            testCase "rejects invalid" <| fun () ->
                expectInvalid<RecordKey> "\".\""
        ]

        testList "AtDateTime" [
            testCase "roundtrip" <| fun () ->
                let dt = AtDateTime.parse "2023-11-23T12:34:56.789Z" |> Result.defaultWith failwith
                let result = roundtrip dt
                Expect.equal (AtDateTime.value result) "2023-11-23T12:34:56.789Z" "AtDateTime roundtrip"

            testCase "rejects invalid" <| fun () ->
                expectInvalid<AtDateTime> "\"not-a-datetime\""
        ]

        testList "Language" [
            testCase "roundtrip" <| fun () ->
                let lang = Language.parse "en" |> Result.defaultWith failwith
                let result = roundtrip lang
                Expect.equal (Language.value result) "en" "Language roundtrip"

            testCase "rejects invalid" <| fun () ->
                expectInvalid<Language> "\"123\""
        ]

        testList "Uri" [
            testCase "roundtrip" <| fun () ->
                let uri = Uri.parse "https://example.com/path" |> Result.defaultWith failwith
                let result = roundtrip uri
                Expect.equal (Uri.value result) "https://example.com/path" "Uri roundtrip"

            testCase "rejects invalid" <| fun () ->
                expectInvalid<Uri> "\"not a uri with spaces\""
        ]

        testCase "serializes to JSON string" <| fun () ->
            let did = Did.parse "did:plc:z72i7hdynmk6r22z27h6tvur" |> Result.defaultWith failwith
            let json = JsonSerializer.Serialize(did, options)
            Expect.equal json "\"did:plc:z72i7hdynmk6r22z27h6tvur\"" "Should serialize as JSON string"
    ]
