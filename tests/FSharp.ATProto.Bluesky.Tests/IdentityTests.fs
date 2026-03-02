module FSharp.ATProto.Bluesky.Tests.IdentityTests

open Expecto
open System.Net
open System.Text.Json
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Core
open FSharp.ATProto.Syntax
open TestHelpers

let private parseJson (json : string) = JsonSerializer.Deserialize<JsonElement> (json)

let private parseDid' s = Did.parse s |> Result.defaultWith failwith
let private parseHandle' s = Handle.parse s |> Result.defaultWith failwith

[<Tests>]
let parseTests =
    testList
        "Identity.parseDidDocument"
        [ testCase "parses full PLC DID document"
          <| fun _ ->
              let doc =
                  parseJson
                      """{
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
              Expect.equal (Did.value identity.Did) "did:plc:z72i7hdynmk6r22z27h6tvur" "did"
              Expect.equal (identity.Handle |> Option.map Handle.value) (Some "bsky.app") "handle"

              Expect.equal
                  (identity.PdsEndpoint |> Option.map Uri.value)
                  (Some "https://puffball.us-east.host.bsky.network")
                  "pds"

              Expect.equal identity.SigningKey (Some "zQ3shQo6TF2moaqMTrUZEM1jeuYRQXeHEx4evX9751y2qPqRA") "key"

          testCase "handles missing optional fields"
          <| fun _ ->
              let doc = parseJson """{"id": "did:plc:test123"}"""
              let result = Identity.parseDidDocument doc
              let identity = Expect.wantOk result "should parse"
              Expect.equal (Did.value identity.Did) "did:plc:test123" "did"
              Expect.isNone identity.Handle "no handle"
              Expect.isNone identity.PdsEndpoint "no pds"
              Expect.isNone identity.SigningKey "no key"

          testCase "extracts handle from at:// URI"
          <| fun _ ->
              let doc =
                  parseJson
                      """{
                "id": "did:plc:test",
                "alsoKnownAs": ["https://other.example", "at://alice.example.com"]
            }"""

              let identity = Identity.parseDidDocument doc |> Result.defaultWith failwith

              Expect.equal
                  (identity.Handle |> Option.map Handle.value)
                  (Some "alice.example.com")
                  "extracts handle from at:// entry"

          testCase "finds atproto service by fragment id"
          <| fun _ ->
              let doc =
                  parseJson
                      """{
                "id": "did:plc:test",
                "service": [
                    {"id": "#other_service", "type": "Other", "serviceEndpoint": "https://other.com"},
                    {"id": "#atproto_pds", "type": "AtprotoPersonalDataServer", "serviceEndpoint": "https://my.pds.com"}
                ]
            }"""

              let identity = Identity.parseDidDocument doc |> Result.defaultWith failwith

              Expect.equal
                  (identity.PdsEndpoint |> Option.map Uri.value)
                  (Some "https://my.pds.com")
                  "finds correct service"

          testCase "finds verification method by #atproto suffix"
          <| fun _ ->
              let doc =
                  parseJson
                      """{
                "id": "did:plc:test",
                "verificationMethod": [
                    {"id": "#other", "type": "Other", "publicKeyMultibase": "wrong"},
                    {"id": "did:plc:test#atproto", "type": "Multikey", "publicKeyMultibase": "zCorrectKey"}
                ]
            }"""

              let identity = Identity.parseDidDocument doc |> Result.defaultWith failwith
              Expect.equal identity.SigningKey (Some "zCorrectKey") "finds correct key"

          testCase "returns error when id field missing"
          <| fun _ ->
              let doc = parseJson """{"alsoKnownAs": []}"""
              let result = Identity.parseDidDocument doc
              Expect.isError result "should error without id" ]

let private plcDidDoc =
    """{
    "id": "did:plc:abc123",
    "alsoKnownAs": ["at://alice.example.com"],
    "verificationMethod": [{"id": "#atproto", "type": "Multikey", "publicKeyMultibase": "zKey123"}],
    "service": [{"id": "#atproto_pds", "type": "AtprotoPersonalDataServer", "serviceEndpoint": "https://pds.example.com"}]
}"""

[<Tests>]
let resolveTests =
    testList
        "Identity resolution"
        [ testCase "resolveDid resolves did:plc via PLC directory"
          <| fun _ ->
              let agent =
                  createMockAgent (fun req ->
                      if req.RequestUri.Host = "plc.directory" then
                          jsonResponse HttpStatusCode.OK (JsonSerializer.Deserialize<JsonElement> (plcDidDoc))
                      else
                          emptyResponse HttpStatusCode.NotFound)

              let did = Did.parse "did:plc:abc123" |> Result.defaultWith failwith

              let result =
                  Identity.resolveDid agent did |> Async.AwaitTask |> Async.RunSynchronously

              let identity = Expect.wantOk result "should resolve"
              Expect.equal (Did.value identity.Did) "did:plc:abc123" "did"
              Expect.equal (identity.Handle |> Option.map Handle.value) (Some "alice.example.com") "handle"
              Expect.equal (identity.PdsEndpoint |> Option.map Uri.value) (Some "https://pds.example.com") "pds"

          testCase "resolveDid resolves did:web via .well-known"
          <| fun _ ->
              let webDidDoc =
                  """{"id": "did:web:bob.example.com", "alsoKnownAs": ["at://bob.example.com"]}"""

              let agent =
                  createMockAgent (fun req ->
                      if req.RequestUri.PathAndQuery.Contains (".well-known/did.json") then
                          jsonResponse HttpStatusCode.OK (JsonSerializer.Deserialize<JsonElement> (webDidDoc))
                      else
                          emptyResponse HttpStatusCode.NotFound)

              let did = Did.parse "did:web:bob.example.com" |> Result.defaultWith failwith

              let result =
                  Identity.resolveDid agent did |> Async.AwaitTask |> Async.RunSynchronously

              let identity = Expect.wantOk result "should resolve"
              Expect.equal (Did.value identity.Did) "did:web:bob.example.com" "did"

          testCase "resolveDid returns error for unsupported method"
          <| fun _ ->
              let agent = createMockAgent (fun _ -> emptyResponse HttpStatusCode.NotFound)
              let did = Did.parse "did:key:abc" |> Result.defaultWith failwith

              let result =
                  Identity.resolveDid agent did |> Async.AwaitTask |> Async.RunSynchronously

              Expect.isError result "unsupported DID method"

          testCase "resolveHandle calls XRPC resolveHandle"
          <| fun _ ->
              let agent =
                  createMockAgent (fun req ->
                      if req.RequestUri.PathAndQuery.Contains ("resolveHandle") then
                          jsonResponse HttpStatusCode.OK {| did = "did:plc:abc123" |}
                      else
                          emptyResponse HttpStatusCode.NotFound)

              agent.Session <-
                  Some
                      { AccessJwt = "t"
                        RefreshJwt = "t"
                        Did = parseDid' "did:plc:me"
                        Handle = parseHandle' "me.test" }

              let handle = Handle.parse "alice.example.com" |> Result.defaultWith failwith

              let result =
                  Identity.resolveHandle agent handle |> Async.AwaitTask |> Async.RunSynchronously

              let did = Expect.wantOk result "should resolve"
              Expect.equal (Did.value did) "did:plc:abc123" "resolved DID"

          testCase "resolveIdentity does bidirectional verification from handle"
          <| fun _ ->
              let agent =
                  createMockAgent (fun req ->
                      if req.RequestUri.PathAndQuery.Contains ("resolveHandle") then
                          jsonResponse HttpStatusCode.OK {| did = "did:plc:abc123" |}
                      elif req.RequestUri.Host = "plc.directory" then
                          jsonResponse HttpStatusCode.OK (JsonSerializer.Deserialize<JsonElement> (plcDidDoc))
                      else
                          emptyResponse HttpStatusCode.NotFound)

              agent.Session <-
                  Some
                      { AccessJwt = "t"
                        RefreshJwt = "t"
                        Did = parseDid' "did:plc:me"
                        Handle = parseHandle' "me.test" }

              let result =
                  Identity.resolveIdentity agent "alice.example.com"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let identity = Expect.wantOk result "should resolve"
              Expect.equal (Did.value identity.Did) "did:plc:abc123" "did"
              Expect.equal (identity.Handle |> Option.map Handle.value) (Some "alice.example.com") "verified handle"

          testCase "resolveIdentity clears handle when bidirectional check fails"
          <| fun _ ->
              // resolveHandle returns did:plc:abc123, but DID doc says handle is "other.com"
              let mismatchDoc = """{"id": "did:plc:abc123", "alsoKnownAs": ["at://other.com"]}"""

              let agent =
                  createMockAgent (fun req ->
                      if req.RequestUri.PathAndQuery.Contains ("resolveHandle") then
                          jsonResponse HttpStatusCode.OK {| did = "did:plc:abc123" |}
                      elif req.RequestUri.Host = "plc.directory" then
                          jsonResponse HttpStatusCode.OK (JsonSerializer.Deserialize<JsonElement> (mismatchDoc))
                      else
                          emptyResponse HttpStatusCode.NotFound)

              agent.Session <-
                  Some
                      { AccessJwt = "t"
                        RefreshJwt = "t"
                        Did = parseDid' "did:plc:me"
                        Handle = parseHandle' "me.test" }

              let result =
                  Identity.resolveIdentity agent "alice.example.com"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              let identity = Expect.wantOk result "should resolve but with no handle"
              Expect.isNone identity.Handle "handle cleared due to mismatch" ]
