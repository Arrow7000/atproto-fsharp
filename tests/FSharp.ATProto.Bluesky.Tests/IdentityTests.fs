module FSharp.ATProto.Bluesky.Tests.IdentityTests

open Expecto
open System.Text.Json
open FSharp.ATProto.Bluesky

let private parseJson (json: string) =
    JsonSerializer.Deserialize<JsonElement>(json)

[<Tests>]
let parseTests =
    testList "Identity.parseDidDocument" [
        testCase "parses full PLC DID document" <| fun _ ->
            let doc = parseJson """{
                "id": "did:plc:z72i7hdynmk6r22z27h6tvur",
                "alsoKnownAs": ["at://bsky.app"],
                "verificationMethod": [{
                    "id": "did:plc:z72i7hdynmk6r22z27h6tvur#atproto",
                    "type": "Multikey",
                    "controller": "did:plc:z72i7hdynmk6r22z27h6tvur",
                    "publicKeyMultibase": "zQ3shQo6TF2moaqMTrUZEM1jeuYRQXeHEx4evX9751y2qPqRA"
                }],
                "service": [{
                    "id": "#atproto_pds",
                    "type": "AtprotoPersonalDataServer",
                    "serviceEndpoint": "https://puffball.us-east.host.bsky.network"
                }]
            }"""
            let result = Identity.parseDidDocument doc
            let identity = Expect.wantOk result "should parse"
            Expect.equal identity.Did "did:plc:z72i7hdynmk6r22z27h6tvur" "did"
            Expect.equal identity.Handle (Some "bsky.app") "handle"
            Expect.equal identity.PdsEndpoint (Some "https://puffball.us-east.host.bsky.network") "pds"
            Expect.equal identity.SigningKey (Some "zQ3shQo6TF2moaqMTrUZEM1jeuYRQXeHEx4evX9751y2qPqRA") "key"

        testCase "handles missing optional fields" <| fun _ ->
            let doc = parseJson """{"id": "did:plc:test123"}"""
            let result = Identity.parseDidDocument doc
            let identity = Expect.wantOk result "should parse"
            Expect.equal identity.Did "did:plc:test123" "did"
            Expect.isNone identity.Handle "no handle"
            Expect.isNone identity.PdsEndpoint "no pds"
            Expect.isNone identity.SigningKey "no key"

        testCase "extracts handle from at:// URI" <| fun _ ->
            let doc = parseJson """{
                "id": "did:plc:test",
                "alsoKnownAs": ["https://other.example", "at://alice.example.com"]
            }"""
            let identity = Identity.parseDidDocument doc |> Result.defaultWith failwith
            Expect.equal identity.Handle (Some "alice.example.com") "extracts handle from at:// entry"

        testCase "finds atproto service by fragment id" <| fun _ ->
            let doc = parseJson """{
                "id": "did:plc:test",
                "service": [
                    {"id": "#other_service", "type": "Other", "serviceEndpoint": "https://other.com"},
                    {"id": "#atproto_pds", "type": "AtprotoPersonalDataServer", "serviceEndpoint": "https://my.pds.com"}
                ]
            }"""
            let identity = Identity.parseDidDocument doc |> Result.defaultWith failwith
            Expect.equal identity.PdsEndpoint (Some "https://my.pds.com") "finds correct service"

        testCase "finds verification method by #atproto suffix" <| fun _ ->
            let doc = parseJson """{
                "id": "did:plc:test",
                "verificationMethod": [
                    {"id": "#other", "type": "Other", "publicKeyMultibase": "wrong"},
                    {"id": "did:plc:test#atproto", "type": "Multikey", "publicKeyMultibase": "zCorrectKey"}
                ]
            }"""
            let identity = Identity.parseDidDocument doc |> Result.defaultWith failwith
            Expect.equal identity.SigningKey (Some "zCorrectKey") "finds correct key"

        testCase "returns error when id field missing" <| fun _ ->
            let doc = parseJson """{"alsoKnownAs": []}"""
            let result = Identity.parseDidDocument doc
            Expect.isError result "should error without id"
    ]
