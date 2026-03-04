module PlcTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open Expecto
open FSharp.ATProto.Syntax
open FSharp.ATProto.Core

let private testDid =
    match Did.parse "did:plc:z72i7hdynmk6r22z27h6tvur" with
    | Ok d -> d
    | Error e -> failwith e

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

/// Create an HttpClient that returns a canned response for the given handler.
let private makeClient (handler : HttpRequestMessage -> HttpResponseMessage) =
    new HttpClient (new TestHelpers.MockHandler (handler))

/// Create a JSON HTTP response.
let private jsonStringResponse (statusCode : HttpStatusCode) (json : string) =
    let response = new HttpResponseMessage (statusCode)
    response.Content <- new StringContent (json, Encoding.UTF8, "application/json")
    response

/// Run a Task<'T> synchronously for tests.
let private runSync (t : System.Threading.Tasks.Task<'a>) =
    t |> Async.AwaitTask |> Async.RunSynchronously

// ---------------------------------------------------------------------------
// Sample JSON payloads
// ---------------------------------------------------------------------------

let private sampleDidDocument =
    """{
  "@context": [
    "https://www.w3.org/ns/did/v1",
    "https://w3id.org/security/multikey/v1",
    "https://w3id.org/security/suites/secp256k1-2019/v1"
  ],
  "id": "did:plc:z72i7hdynmk6r22z27h6tvur",
  "alsoKnownAs": [
    "at://bsky.app"
  ],
  "verificationMethod": [
    {
      "id": "did:plc:z72i7hdynmk6r22z27h6tvur#atproto",
      "type": "Multikey",
      "controller": "did:plc:z72i7hdynmk6r22z27h6tvur",
      "publicKeyMultibase": "zQ3shQo6TF2moaqMTrUZEM1jeuYRQXeHEx4evX9751y2qPqRA"
    }
  ],
  "service": [
    {
      "id": "#atproto_pds",
      "type": "AtprotoPersonalDataServer",
      "serviceEndpoint": "https://puffball.us-east.host.bsky.network"
    }
  ]
}"""

let private sampleAuditLog =
    """[
  {
    "did": "did:plc:z72i7hdynmk6r22z27h6tvur",
    "operation": {
      "sig": "abc123sig",
      "prev": null,
      "type": "plc_operation",
      "services": {
        "atproto_pds": {
          "type": "AtprotoPersonalDataServer",
          "endpoint": "https://bsky.social"
        }
      },
      "alsoKnownAs": ["at://bsky.app"],
      "rotationKeys": ["did:key:zQ3shKey1", "did:key:zQ3shKey2"],
      "verificationMethods": {
        "atproto": "did:key:zQ3shVerKey1"
      }
    },
    "cid": "bafyreigenesis123",
    "nullified": false,
    "createdAt": "2022-11-17T00:35:16.391Z"
  },
  {
    "did": "did:plc:z72i7hdynmk6r22z27h6tvur",
    "operation": {
      "sig": "def456sig",
      "prev": "bafyreigenesis123",
      "type": "plc_operation",
      "services": {
        "atproto_pds": {
          "type": "AtprotoPersonalDataServer",
          "endpoint": "https://puffball.us-east.host.bsky.network"
        }
      },
      "alsoKnownAs": ["at://bsky.app"],
      "rotationKeys": ["did:key:zQ3shKey1", "did:key:zQ3shKey2"],
      "verificationMethods": {
        "atproto": "did:key:zQ3shQo6TF2moaqMTrUZEM1jeuYRQXeHEx4evX9751y2qPqRA"
      }
    },
    "cid": "bafyreiupdate456",
    "nullified": false,
    "createdAt": "2023-06-15T12:00:00.000Z"
  }
]"""

let private sampleExportNdjson =
    """{"did":"did:plc:z72i7hdynmk6r22z27h6tvur","cid":"bafyreigenesis123","createdAt":"2022-11-17T00:35:16.391Z","operation":{"sig":"abc123sig","prev":null,"type":"plc_operation","services":{"atproto_pds":{"type":"AtprotoPersonalDataServer","endpoint":"https://bsky.social"}},"alsoKnownAs":["at://bsky.app"],"rotationKeys":["did:key:zQ3shKey1"],"verificationMethods":{"atproto":"did:key:zQ3shVerKey1"}},"nullified":false}
{"did":"did:plc:z72i7hdynmk6r22z27h6tvur","cid":"bafyreiupdate456","createdAt":"2023-06-15T12:00:00.000Z","operation":{"sig":"def456sig","prev":"bafyreigenesis123","type":"plc_operation","services":{"atproto_pds":{"type":"AtprotoPersonalDataServer","endpoint":"https://puffball.us-east.host.bsky.network"}},"alsoKnownAs":["at://bsky.app"],"rotationKeys":["did:key:zQ3shKey1","did:key:zQ3shKey2"],"verificationMethods":{"atproto":"did:key:zQ3shVerKey2"}},"nullified":false}"""

// ---------------------------------------------------------------------------
// Type tests
// ---------------------------------------------------------------------------

[<Tests>]
let typeTests =
    testList
        "Plc.Types"
        [ testCase "PlcOperationType values"
          <| fun () ->
              Expect.equal
                  (Plc.PlcOperationType.PlcOperation)
                  (Plc.PlcOperationType.PlcOperation)
                  "PlcOperation"

              Expect.equal
                  (Plc.PlcOperationType.PlcTombstone)
                  (Plc.PlcOperationType.PlcTombstone)
                  "PlcTombstone"

              Expect.equal
                  (Plc.PlcOperationType.Create)
                  (Plc.PlcOperationType.Create)
                  "Create"

          testCase "PlcService record"
          <| fun () ->
              let svc : Plc.PlcService =
                  { Type = "AtprotoPersonalDataServer"
                    Endpoint = "https://bsky.social" }

              Expect.equal svc.Type "AtprotoPersonalDataServer" "type"
              Expect.equal svc.Endpoint "https://bsky.social" "endpoint"

          testCase "PlcDocument record"
          <| fun () ->
              let doc : Plc.PlcDocument =
                  { Did = testDid
                    AlsoKnownAs = [ "at://bsky.app" ]
                    VerificationMethods = Map.ofList [ "atproto", "did:key:zQ3sh..." ]
                    RotationKeys = [ "did:key:zQ3shKey1" ]
                    Services =
                        Map.ofList
                            [ "atproto_pds",
                              { Type = "AtprotoPersonalDataServer"
                                Endpoint = "https://bsky.social" } ] }

              Expect.equal (Did.value doc.Did) "did:plc:z72i7hdynmk6r22z27h6tvur" "did"
              Expect.equal doc.AlsoKnownAs.Length 1 "alsoKnownAs count"
              Expect.equal doc.VerificationMethods.Count 1 "verificationMethods count"
              Expect.equal doc.RotationKeys.Length 1 "rotationKeys count"
              Expect.equal doc.Services.Count 1 "services count"

          testCase "PlcError cases"
          <| fun () ->
              let e1 = Plc.PlcError.HttpError (500, "server error")
              let e2 = Plc.PlcError.ParseError "bad json"
              let e3 = Plc.PlcError.NotFound "did:plc:xxx"

              match e1 with
              | Plc.PlcError.HttpError (code, body) ->
                  Expect.equal code 500 "status code"
                  Expect.equal body "server error" "body"
              | _ -> failtest "Expected HttpError"

              match e2 with
              | Plc.PlcError.ParseError msg ->
                  Expect.equal msg "bad json" "message"
              | _ -> failtest "Expected ParseError"

              match e3 with
              | Plc.PlcError.NotFound did ->
                  Expect.equal did "did:plc:xxx" "did"
              | _ -> failtest "Expected NotFound" ]

// ---------------------------------------------------------------------------
// Operation construction tests
// ---------------------------------------------------------------------------

[<Tests>]
let operationConstructionTests =
    testList
        "Plc.OperationConstruction"
        [ testCase "createGenesisOp sets correct fields"
          <| fun () ->
              let op =
                  Plc.createGenesisOp
                      [ "did:key:zQ3shKey1"; "did:key:zQ3shKey2" ]
                      (Map.ofList [ "atproto", "did:key:zQ3shVerKey1" ])
                      [ "at://handle.bsky.social" ]
                      (Map.ofList
                          [ "atproto_pds",
                            { Plc.PlcService.Type = "AtprotoPersonalDataServer"
                              Endpoint = "https://bsky.social" } ])

              Expect.equal op.Type Plc.PlcOperationType.PlcOperation "type is plc_operation"
              Expect.equal op.RotationKeys.Length 2 "2 rotation keys"
              Expect.equal op.VerificationMethods.Count 1 "1 verification method"
              Expect.equal op.AlsoKnownAs [ "at://handle.bsky.social" ] "alsoKnownAs"
              Expect.equal op.Services.Count 1 "1 service"
              Expect.equal op.Prev None "prev is None for genesis"
              Expect.equal op.Sig None "sig is None (unsigned)"

          testCase "createRotationOp sets prev"
          <| fun () ->
              let op =
                  Plc.createRotationOp
                      "bafyreiprevious123"
                      [ "did:key:zQ3shNewKey1" ]
                      (Map.ofList [ "atproto", "did:key:zQ3shNewVerKey1" ])
                      [ "at://new-handle.bsky.social" ]
                      (Map.ofList
                          [ "atproto_pds",
                            { Plc.PlcService.Type = "AtprotoPersonalDataServer"
                              Endpoint = "https://new-pds.example.com" } ])

              Expect.equal op.Type Plc.PlcOperationType.PlcOperation "type is plc_operation"
              Expect.equal op.Prev (Some "bafyreiprevious123") "prev is set"
              Expect.equal op.Sig None "sig is None (unsigned)"
              Expect.equal op.RotationKeys [ "did:key:zQ3shNewKey1" ] "new rotation key"

          testCase "createTombstoneOp"
          <| fun () ->
              let op = Plc.createTombstoneOp "bafyreiprevious123"
              Expect.equal op.Type Plc.PlcOperationType.PlcTombstone "type is plc_tombstone"
              Expect.equal op.Prev (Some "bafyreiprevious123") "prev is set"
              Expect.equal op.RotationKeys [] "no rotation keys"
              Expect.equal op.VerificationMethods Map.empty "no verification methods"
              Expect.equal op.AlsoKnownAs [] "no alsoKnownAs"
              Expect.equal op.Services Map.empty "no services"
              Expect.equal op.Sig None "sig is None (unsigned)" ]

// ---------------------------------------------------------------------------
// Serialization tests
// ---------------------------------------------------------------------------

[<Tests>]
let serializationTests =
    testList
        "Plc.Serialization"
        [ testCase "serializeForSigning produces valid JSON without sig"
          <| fun () ->
              let op =
                  Plc.createGenesisOp
                      [ "did:key:zQ3shKey1" ]
                      (Map.ofList [ "atproto", "did:key:zQ3shVerKey1" ])
                      [ "at://handle.bsky.social" ]
                      (Map.ofList
                          [ "atproto_pds",
                            { Plc.PlcService.Type = "AtprotoPersonalDataServer"
                              Endpoint = "https://bsky.social" } ])

              let bytes = Plc.serializeForSigning op
              let json = Encoding.UTF8.GetString bytes
              let doc = JsonDocument.Parse json

              // Verify structure
              Expect.equal
                  (doc.RootElement.GetProperty("type").GetString ())
                  "plc_operation"
                  "type"

              // Verify prev is null for genesis
              Expect.isTrue
                  (doc.RootElement.GetProperty("prev").ValueKind = JsonValueKind.Null)
                  "prev is null"

              // Verify no sig field
              let hasSig = doc.RootElement.TryGetProperty "sig" |> fst
              Expect.isFalse hasSig "no sig field in signing input"

              // Verify rotationKeys
              let keys = doc.RootElement.GetProperty("rotationKeys")
              Expect.equal (keys.GetArrayLength ()) 1 "1 rotation key"
              Expect.equal (keys.[0].GetString ()) "did:key:zQ3shKey1" "key value"

              // Verify verificationMethods
              let vm = doc.RootElement.GetProperty("verificationMethods")
              Expect.equal (vm.GetProperty("atproto").GetString ()) "did:key:zQ3shVerKey1" "vm value"

              // Verify alsoKnownAs
              let aka = doc.RootElement.GetProperty("alsoKnownAs")
              Expect.equal (aka.[0].GetString ()) "at://handle.bsky.social" "aka value"

              // Verify services
              let svc = doc.RootElement.GetProperty("services").GetProperty("atproto_pds")
              Expect.equal (svc.GetProperty("type").GetString ()) "AtprotoPersonalDataServer" "svc type"
              Expect.equal (svc.GetProperty("endpoint").GetString ()) "https://bsky.social" "svc endpoint"

          testCase "serializeForSigning tombstone has minimal fields"
          <| fun () ->
              let op = Plc.createTombstoneOp "bafyreiprevious123"
              let bytes = Plc.serializeForSigning op
              let json = Encoding.UTF8.GetString bytes
              let doc = JsonDocument.Parse json

              Expect.equal
                  (doc.RootElement.GetProperty("type").GetString ())
                  "plc_tombstone"
                  "type"

              Expect.equal
                  (doc.RootElement.GetProperty("prev").GetString ())
                  "bafyreiprevious123"
                  "prev"

              // Should NOT have rotationKeys, verificationMethods, etc.
              Expect.isFalse
                  (doc.RootElement.TryGetProperty "rotationKeys" |> fst)
                  "no rotationKeys"

              Expect.isFalse
                  (doc.RootElement.TryGetProperty "sig" |> fst)
                  "no sig"

          testCase "serializeWithSig includes sig field"
          <| fun () ->
              let op =
                  { Plc.createGenesisOp
                        [ "did:key:zQ3shKey1" ]
                        (Map.ofList [ "atproto", "did:key:zQ3shVerKey1" ])
                        [ "at://handle.bsky.social" ]
                        (Map.ofList
                            [ "atproto_pds",
                              { Plc.PlcService.Type = "AtprotoPersonalDataServer"
                                Endpoint = "https://bsky.social" } ]) with
                      Sig = Some "test-signature-base64url" }

              let bytes = Plc.serializeWithSig op
              let json = Encoding.UTF8.GetString bytes
              let doc = JsonDocument.Parse json

              Expect.equal
                  (doc.RootElement.GetProperty("sig").GetString ())
                  "test-signature-base64url"
                  "sig is included"

          testCase "serializeForSigning rotation op has prev"
          <| fun () ->
              let op =
                  Plc.createRotationOp
                      "bafyreiprevious123"
                      [ "did:key:zQ3shKey1" ]
                      (Map.ofList [ "atproto", "did:key:zQ3shVerKey1" ])
                      [ "at://handle.bsky.social" ]
                      Map.empty

              let bytes = Plc.serializeForSigning op
              let json = Encoding.UTF8.GetString bytes
              let doc = JsonDocument.Parse json

              Expect.equal
                  (doc.RootElement.GetProperty("prev").GetString ())
                  "bafyreiprevious123"
                  "prev is set" ]

// ---------------------------------------------------------------------------
// Signing tests
// ---------------------------------------------------------------------------

[<Tests>]
let signingTests =
    testList
        "Plc.Signing"
        [ testCase "signOperation adds base64url-encoded sig"
          <| fun () ->
              let op =
                  Plc.createGenesisOp
                      [ "did:key:zQ3shKey1" ]
                      (Map.ofList [ "atproto", "did:key:zQ3shVerKey1" ])
                      [ "at://handle.bsky.social" ]
                      Map.empty

              // Mock sign function: returns 64 bytes of 0xAA
              let mockSign (_data : byte[]) = Array.create 64 0xAAuy

              let signed = Plc.signOperation mockSign op
              Expect.isSome signed.Sig "sig is set"

              // Verify it's valid base64url
              let sigStr = signed.Sig.Value
              Expect.isTrue (sigStr.Length > 0) "sig is non-empty"
              // Base64url should not contain +, /, or =
              Expect.isFalse (sigStr.Contains "+") "no + in base64url"
              Expect.isFalse (sigStr.Contains "/") "no / in base64url"
              Expect.isFalse (sigStr.Contains "=") "no = in base64url"

          testCase "signOperation passes correct data to sign function"
          <| fun () ->
              let op =
                  Plc.createGenesisOp
                      [ "did:key:zQ3shKey1" ]
                      Map.empty
                      []
                      Map.empty

              let mutable capturedData : byte[] option = None

              let captureSign (data : byte[]) =
                  capturedData <- Some data
                  Array.create 64 0x00uy

              let _ = Plc.signOperation captureSign op

              // The signing input should be the serialized operation without sig
              let expectedBytes = Plc.serializeForSigning op
              Expect.isSome capturedData "sign function was called"
              Expect.sequenceEqual capturedData.Value expectedBytes "signing input matches serialized op"

          testCase "signOperation does not modify other fields"
          <| fun () ->
              let op =
                  Plc.createRotationOp
                      "bafyreiprevious123"
                      [ "did:key:zQ3shKey1"; "did:key:zQ3shKey2" ]
                      (Map.ofList [ "atproto", "did:key:zQ3shVerKey1" ])
                      [ "at://handle.bsky.social" ]
                      (Map.ofList
                          [ "atproto_pds",
                            { Plc.PlcService.Type = "AtprotoPersonalDataServer"
                              Endpoint = "https://bsky.social" } ])

              let mockSign (_data : byte[]) = Array.create 64 0xBBuy

              let signed = Plc.signOperation mockSign op

              Expect.equal signed.Type op.Type "type unchanged"
              Expect.equal signed.RotationKeys op.RotationKeys "rotationKeys unchanged"
              Expect.equal signed.VerificationMethods op.VerificationMethods "verificationMethods unchanged"
              Expect.equal signed.AlsoKnownAs op.AlsoKnownAs "alsoKnownAs unchanged"
              Expect.equal signed.Services op.Services "services unchanged"
              Expect.equal signed.Prev op.Prev "prev unchanged"

          testCase "signOperation on tombstone"
          <| fun () ->
              let op = Plc.createTombstoneOp "bafyreiprevious123"
              let mockSign (_data : byte[]) = Array.create 64 0xCCuy
              let signed = Plc.signOperation mockSign op
              Expect.isSome signed.Sig "sig is set"
              Expect.equal signed.Type Plc.PlcOperationType.PlcTombstone "still tombstone" ]

// ---------------------------------------------------------------------------
// Resolve tests (with mock HTTP)
// ---------------------------------------------------------------------------

[<Tests>]
let resolveTests =
    testList
        "Plc.resolve"
        [ testCase "resolves DID document successfully"
          <| fun () ->
              let mutable capturedUrl : string option = None

              let client =
                  makeClient (fun req ->
                      capturedUrl <- Some (string req.RequestUri)
                      jsonStringResponse HttpStatusCode.OK sampleDidDocument)

              let result = Plc.resolve client testDid None |> runSync

              match result with
              | Ok doc ->
                  Expect.equal (Did.value doc.Did) "did:plc:z72i7hdynmk6r22z27h6tvur" "did"
                  Expect.equal doc.AlsoKnownAs [ "at://bsky.app" ] "alsoKnownAs"
                  Expect.equal doc.VerificationMethods.Count 1 "1 verification method"

                  Expect.equal
                      doc.VerificationMethods.["atproto"]
                      "zQ3shQo6TF2moaqMTrUZEM1jeuYRQXeHEx4evX9751y2qPqRA"
                      "verification key"

                  Expect.equal doc.Services.Count 1 "1 service"

                  Expect.equal
                      doc.Services.["atproto_pds"].Type
                      "AtprotoPersonalDataServer"
                      "service type"

                  Expect.equal
                      doc.Services.["atproto_pds"].Endpoint
                      "https://puffball.us-east.host.bsky.network"
                      "service endpoint"
              | Error e -> failtest (sprintf "Expected Ok, got Error: %A" e)

              Expect.stringContains
                  capturedUrl.Value
                  "did:plc:z72i7hdynmk6r22z27h6tvur"
                  "URL contains DID"

          testCase "uses custom base URL"
          <| fun () ->
              let mutable capturedUrl : string option = None

              let client =
                  makeClient (fun req ->
                      capturedUrl <- Some (string req.RequestUri)
                      jsonStringResponse HttpStatusCode.OK sampleDidDocument)

              let _ =
                  Plc.resolve client testDid (Some "https://custom-plc.example.com")
                  |> runSync

              Expect.stringStarts capturedUrl.Value "https://custom-plc.example.com/" "custom base URL"

          testCase "returns NotFound on 404"
          <| fun () ->
              let client =
                  makeClient (fun _ -> TestHelpers.emptyResponse HttpStatusCode.NotFound)

              let result = Plc.resolve client testDid None |> runSync

              match result with
              | Error (Plc.PlcError.NotFound did) ->
                  Expect.equal did "did:plc:z72i7hdynmk6r22z27h6tvur" "did in error"
              | other -> failtest (sprintf "Expected NotFound, got %A" other)

          testCase "returns HttpError on 500"
          <| fun () ->
              let client =
                  makeClient (fun _ -> jsonStringResponse HttpStatusCode.InternalServerError """{"error":"internal"}""")

              let result = Plc.resolve client testDid None |> runSync

              match result with
              | Error (Plc.PlcError.HttpError (code, _body)) ->
                  Expect.equal code 500 "status code"
              | other -> failtest (sprintf "Expected HttpError, got %A" other)

          testCase "returns ParseError on invalid JSON"
          <| fun () ->
              let client =
                  makeClient (fun _ -> jsonStringResponse HttpStatusCode.OK "not valid json{{{")

              let result = Plc.resolve client testDid None |> runSync

              match result with
              | Error (Plc.PlcError.ParseError _msg) -> ()
              | other -> failtest (sprintf "Expected ParseError, got %A" other) ]

// ---------------------------------------------------------------------------
// Audit log tests (with mock HTTP)
// ---------------------------------------------------------------------------

[<Tests>]
let auditLogTests =
    testList
        "Plc.getAuditLog"
        [ testCase "parses audit log successfully"
          <| fun () ->
              let client =
                  makeClient (fun _ -> jsonStringResponse HttpStatusCode.OK sampleAuditLog)

              let result = Plc.getAuditLog client testDid None |> runSync

              match result with
              | Ok entries ->
                  Expect.equal entries.Length 2 "2 entries"

                  // First entry (genesis)
                  let e0 = entries.[0]
                  Expect.equal (Did.value e0.Did) "did:plc:z72i7hdynmk6r22z27h6tvur" "did"
                  Expect.equal e0.Operation.Type Plc.PlcOperationType.PlcOperation "type"
                  Expect.equal e0.Operation.Prev None "genesis has no prev"
                  Expect.equal e0.Operation.Sig (Some "abc123sig") "sig"
                  Expect.equal e0.Operation.RotationKeys.Length 2 "2 rotation keys"
                  Expect.equal e0.Operation.AlsoKnownAs [ "at://bsky.app" ] "alsoKnownAs"
                  Expect.equal e0.Cid "bafyreigenesis123" "cid"
                  Expect.isFalse e0.Nullified "not nullified"
                  Expect.equal e0.CreatedAt.Year 2022 "year"

                  Expect.equal
                      e0.Operation.Services.["atproto_pds"].Endpoint
                      "https://bsky.social"
                      "genesis service endpoint"

                  // Second entry (update)
                  let e1 = entries.[1]
                  Expect.equal e1.Operation.Prev (Some "bafyreigenesis123") "prev points to genesis"
                  Expect.equal e1.Cid "bafyreiupdate456" "update cid"
                  Expect.equal e1.CreatedAt.Year 2023 "update year"

                  Expect.equal
                      e1.Operation.Services.["atproto_pds"].Endpoint
                      "https://puffball.us-east.host.bsky.network"
                      "updated service endpoint"
              | Error e -> failtest (sprintf "Expected Ok, got Error: %A" e)

          testCase "returns NotFound for unknown DID"
          <| fun () ->
              let unknownDid =
                  match Did.parse "did:plc:unknown123456789" with
                  | Ok d -> d
                  | Error e -> failwith e

              let client =
                  makeClient (fun _ -> TestHelpers.emptyResponse HttpStatusCode.NotFound)

              let result = Plc.getAuditLog client unknownDid None |> runSync

              match result with
              | Error (Plc.PlcError.NotFound _) -> ()
              | other -> failtest (sprintf "Expected NotFound, got %A" other)

          testCase "returns ParseError on non-array response"
          <| fun () ->
              let client =
                  makeClient (fun _ -> jsonStringResponse HttpStatusCode.OK """{"not":"an array"}""")

              let result = Plc.getAuditLog client testDid None |> runSync

              match result with
              | Error (Plc.PlcError.ParseError msg) ->
                  Expect.stringContains msg "array" "mentions array"
              | other -> failtest (sprintf "Expected ParseError, got %A" other) ]

// ---------------------------------------------------------------------------
// Export tests (with mock HTTP)
// ---------------------------------------------------------------------------

[<Tests>]
let exportTests =
    testList
        "Plc.export"
        [ testCase "parses NDJSON export successfully"
          <| fun () ->
              let client =
                  makeClient (fun _ ->
                      let response = new HttpResponseMessage (HttpStatusCode.OK)
                      response.Content <- new StringContent (sampleExportNdjson, Encoding.UTF8, "application/jsonl")
                      response)

              let result = Plc.export client None None None |> runSync

              match result with
              | Ok entries ->
                  Expect.equal entries.Length 2 "2 entries"
                  Expect.equal (Did.value entries.[0].Did) "did:plc:z72i7hdynmk6r22z27h6tvur" "did"
                  Expect.equal entries.[0].Cid "bafyreigenesis123" "cid"
                  Expect.isFalse entries.[0].Nullified "not nullified"
                  Expect.equal entries.[1].Operation.Prev (Some "bafyreigenesis123") "prev"
              | Error e -> failtest (sprintf "Expected Ok, got Error: %A" e)

          testCase "passes after and count query parameters"
          <| fun () ->
              let mutable capturedUrl : string option = None

              let client =
                  makeClient (fun req ->
                      capturedUrl <- Some (string req.RequestUri)
                      let response = new HttpResponseMessage (HttpStatusCode.OK)
                      response.Content <- new StringContent ("", Encoding.UTF8, "text/plain")
                      response)

              let _ =
                  Plc.export client (Some "2024-01-01T00:00:00.000Z") (Some 10) None
                  |> runSync

              let url = capturedUrl.Value
              Expect.stringContains url "after=" "has after param"
              Expect.stringContains url "count=10" "has count param"

          testCase "export with no params has no query string"
          <| fun () ->
              let mutable capturedUrl : string option = None

              let client =
                  makeClient (fun req ->
                      capturedUrl <- Some (string req.RequestUri)
                      let response = new HttpResponseMessage (HttpStatusCode.OK)
                      response.Content <- new StringContent ("", Encoding.UTF8, "text/plain")
                      response)

              let _ = Plc.export client None None None |> runSync

              let url = capturedUrl.Value
              Expect.stringEnds url "/export" "no query string" ]

// ---------------------------------------------------------------------------
// Submit operation tests (with mock HTTP)
// ---------------------------------------------------------------------------

[<Tests>]
let submitTests =
    testList
        "Plc.submitOperation"
        [ testCase "rejects unsigned operation"
          <| fun () ->
              let client = makeClient (fun _ -> TestHelpers.emptyResponse HttpStatusCode.OK)

              let op = Plc.createGenesisOp [ "did:key:zQ3shKey1" ] Map.empty [] Map.empty

              let result = Plc.submitOperation client testDid op None |> runSync

              match result with
              | Error (Plc.PlcError.ParseError msg) ->
                  Expect.stringContains msg "signed" "mentions signing"
              | other -> failtest (sprintf "Expected ParseError about signing, got %A" other)

          testCase "submits signed operation via POST"
          <| fun () ->
              let mutable capturedMethod : HttpMethod option = None
              let mutable capturedUrl : string option = None
              let mutable capturedBody : string option = None

              let client =
                  makeClient (fun req ->
                      capturedMethod <- Some req.Method
                      capturedUrl <- Some (string req.RequestUri)
                      capturedBody <- Some (req.Content.ReadAsStringAsync().Result)
                      TestHelpers.emptyResponse HttpStatusCode.OK)

              let op =
                  Plc.createGenesisOp [ "did:key:zQ3shKey1" ] Map.empty [] Map.empty
                  |> Plc.signOperation (fun _ -> Array.create 64 0xAAuy)

              let result = Plc.submitOperation client testDid op None |> runSync

              match result with
              | Ok () -> ()
              | Error e -> failtest (sprintf "Expected Ok, got Error: %A" e)

              Expect.equal capturedMethod.Value HttpMethod.Post "method is POST"
              Expect.stringContains capturedUrl.Value "did:plc:z72i7hdynmk6r22z27h6tvur" "URL has DID"

              // Verify the body is valid JSON with sig field
              let doc = JsonDocument.Parse capturedBody.Value

              Expect.isTrue
                  (doc.RootElement.TryGetProperty "sig" |> fst)
                  "body has sig field"

          testCase "returns HttpError on server rejection"
          <| fun () ->
              let client =
                  makeClient (fun _ ->
                      jsonStringResponse HttpStatusCode.BadRequest """{"error":"InvalidSignature","message":"bad sig"}""")

              let op =
                  Plc.createGenesisOp [ "did:key:zQ3shKey1" ] Map.empty [] Map.empty
                  |> Plc.signOperation (fun _ -> Array.create 64 0xAAuy)

              let result = Plc.submitOperation client testDid op None |> runSync

              match result with
              | Error (Plc.PlcError.HttpError (code, body)) ->
                  Expect.equal code 400 "status code"
                  Expect.stringContains body "InvalidSignature" "error in body"
              | other -> failtest (sprintf "Expected HttpError, got %A" other) ]

// ---------------------------------------------------------------------------
// Round-trip tests
// ---------------------------------------------------------------------------

[<Tests>]
let roundTripTests =
    testList
        "Plc.RoundTrip"
        [ testCase "create -> sign -> serialize -> deserialize round-trip"
          <| fun () ->
              let op =
                  Plc.createGenesisOp
                      [ "did:key:zQ3shKey1"; "did:key:zQ3shKey2" ]
                      (Map.ofList [ "atproto", "did:key:zQ3shVerKey1" ])
                      [ "at://handle.bsky.social" ]
                      (Map.ofList
                          [ "atproto_pds",
                            { Plc.PlcService.Type = "AtprotoPersonalDataServer"
                              Endpoint = "https://bsky.social" } ])

              let signed = Plc.signOperation (fun _ -> Array.create 64 0xFFuy) op
              let bytes = Plc.serializeWithSig signed
              let json = Encoding.UTF8.GetString bytes
              let doc = JsonDocument.Parse json

              // Verify all fields survived the round-trip
              Expect.equal
                  (doc.RootElement.GetProperty("type").GetString ())
                  "plc_operation"
                  "type"

              let keys = doc.RootElement.GetProperty "rotationKeys"
              Expect.equal (keys.GetArrayLength ()) 2 "2 rotation keys"
              Expect.equal (keys.[0].GetString ()) "did:key:zQ3shKey1" "key 1"
              Expect.equal (keys.[1].GetString ()) "did:key:zQ3shKey2" "key 2"

              let vm = doc.RootElement.GetProperty "verificationMethods"
              Expect.equal (vm.GetProperty("atproto").GetString ()) "did:key:zQ3shVerKey1" "vm"

              let aka = doc.RootElement.GetProperty "alsoKnownAs"
              Expect.equal (aka.[0].GetString ()) "at://handle.bsky.social" "aka"

              let svc = doc.RootElement.GetProperty("services").GetProperty "atproto_pds"
              Expect.equal (svc.GetProperty("type").GetString ()) "AtprotoPersonalDataServer" "svc type"
              Expect.equal (svc.GetProperty("endpoint").GetString ()) "https://bsky.social" "svc endpoint"

              Expect.isTrue
                  (doc.RootElement.GetProperty("prev").ValueKind = JsonValueKind.Null)
                  "prev is null"

              // sig should be present
              let sigStr = doc.RootElement.GetProperty("sig").GetString ()
              Expect.isTrue (sigStr.Length > 0) "sig is non-empty"

          testCase "signing is deterministic for same input"
          <| fun () ->
              let op =
                  Plc.createGenesisOp
                      [ "did:key:zQ3shKey1" ]
                      Map.empty
                      []
                      Map.empty

              // Use a deterministic mock: just returns the SHA256 hash of the input (first 64 bytes)
              let detSign (data : byte[]) =
                  let hash = System.Security.Cryptography.SHA256.HashData data
                  Array.append hash hash  // 64 bytes

              let signed1 = Plc.signOperation detSign op
              let signed2 = Plc.signOperation detSign op

              Expect.equal signed1.Sig signed2.Sig "same sig for same input" ]

// ---------------------------------------------------------------------------
// Default base URL tests
// ---------------------------------------------------------------------------

[<Tests>]
let baseUrlTests =
    testList
        "Plc.DefaultBaseUrl"
        [ testCase "default base URL is plc.directory"
          <| fun () ->
              Expect.equal Plc.DefaultBaseUrl "https://plc.directory" "default URL"

          testCase "resolve uses default base URL"
          <| fun () ->
              let mutable capturedUrl : string option = None

              let client =
                  makeClient (fun req ->
                      capturedUrl <- Some (string req.RequestUri)
                      jsonStringResponse HttpStatusCode.OK sampleDidDocument)

              let _ = Plc.resolve client testDid None |> runSync

              Expect.stringStarts capturedUrl.Value "https://plc.directory/" "default base URL"

          testCase "getAuditLog uses default base URL"
          <| fun () ->
              let mutable capturedUrl : string option = None

              let client =
                  makeClient (fun req ->
                      capturedUrl <- Some (string req.RequestUri)
                      jsonStringResponse HttpStatusCode.OK "[]")

              let _ = Plc.getAuditLog client testDid None |> runSync

              Expect.stringStarts capturedUrl.Value "https://plc.directory/" "default base URL"
              Expect.stringContains capturedUrl.Value "/log/audit" "audit path" ]
