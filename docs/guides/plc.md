---
title: PLC Directory
category: Infrastructure
categoryindex: 5
index: 28
description: DID PLC resolution, audit logs, and directory operations
keywords: fsharp, atproto, plc, did, directory, resolution, audit
---

# PLC Directory

The PLC Directory is the primary DID registry for AT Protocol. All `did:plc:*` identifiers resolve through it. The `Plc` module in `FSharp.ATProto.Core` provides read operations (resolve, audit log, export) that require no authentication, and operation construction helpers for creating or updating DID documents.

All read functions take an `HttpClient` directly -- no `AtpAgent` needed.

## Types

```fsharp
type PlcDocument =
    { Did : Did
      AlsoKnownAs : string list                  // e.g. ["at://handle.bsky.social"]
      VerificationMethods : Map<string, string>   // fragment ID -> did:key
      RotationKeys : string list                  // did:key values
      Services : Map<string, PlcService> }        // service ID -> { Type; Endpoint }

type PlcService = { Type : string; Endpoint : string }

type PlcError =
    | HttpError of statusCode: int * body: string
    | ParseError of message: string
    | NotFound of did: string
```

## Resolving a DID

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Syntax

let client = new System.Net.Http.HttpClient()
let did = Did.parse "did:plc:ewvi7nxzyoun6zhxrhs64oiz" |> Result.defaultWith failwith

task {
    match! Plc.resolve client did None with
    | Ok doc ->
        printfn "Handle: %A" doc.AlsoKnownAs
        for svc in doc.Services do
            printfn "Service %s: %s" svc.Key svc.Value.Endpoint
    | Error (PlcError.NotFound d) ->
        printfn "DID not found: %s" d
    | Error (PlcError.HttpError (code, body)) ->
        printfn "HTTP %d: %s" code body
    | Error (PlcError.ParseError msg) ->
        printfn "Parse error: %s" msg
}
```

The optional third parameter overrides the PLC Directory URL (defaults to `https://plc.directory`).

## Audit Log

Get the full operation history for a DID:

```fsharp
task {
    match! Plc.getAuditLog client did None with
    | Ok entries ->
        for entry in entries do
            printfn "[%O] %A (nullified: %b)" entry.CreatedAt entry.Operation.Type entry.Nullified
    | Error err -> printfn "Error: %A" err
}
```

Each `AuditEntry` contains the signed `PlcOperation`, its CID, whether it has been nullified, and the timestamp.

## Bulk Export

The export endpoint returns operations across all DIDs as NDJSON, useful for building mirrors or analytics:

```fsharp
task {
    match! Plc.export client None (Some 100) None with
    | Ok entries -> printfn "Got %d entries" entries.Length
    | Error err -> printfn "Error: %A" err
}
```

Parameters: `after` (ISO 8601 cursor for pagination), `count` (max entries), and `baseUrl`.

## Creating Operations

Build unsigned operations for DID document management:

```fsharp
// Genesis operation (first operation for a new DID)
let genesis =
    Plc.createGenesisOp
        [ "did:key:zQ3sh..." ]                         // rotation keys
        (Map.ofList [ "atproto", "did:key:zDnae..." ]) // verification methods
        [ "at://handle.example.com" ]                   // alsoKnownAs
        (Map.ofList [ "atproto_pds",
                       { Type = "AtprotoPersonalDataServer"
                         Endpoint = "https://pds.example.com" } ])

// Update operation (references previous operation CID)
let rotation =
    Plc.createRotationOp prevCid rotationKeys verificationMethods alsoKnownAs services

// Tombstone (deactivate the DID)
let tombstone = Plc.createTombstoneOp prevCid
```

## Signing and Submitting

Sign an operation and submit it to the PLC Directory. The signing function is injected -- this module does not depend on `FSharp.ATProto.Crypto` directly.

```fsharp
open FSharp.ATProto.Crypto

let keyPair = Keys.generate Algorithm.P256
let sign = Signing.sign keyPair

// Sign the operation
let signed = Plc.signOperation sign genesis

// Submit to the directory
task {
    match! Plc.submitOperation client did signed None with
    | Ok () -> printfn "Operation submitted"
    | Error err -> printfn "Error: %A" err
}
```

`signOperation` serializes the operation to canonical JSON (omitting the `sig` field), signs it, and returns the operation with the base64url-encoded signature populated.
