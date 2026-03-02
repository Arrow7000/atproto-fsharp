---
title: Identity Resolution
category: Guides
categoryindex: 1
index: 9
description: Resolve AT Protocol handles and DIDs with bidirectional verification
keywords: identity, did, handle, resolution, verification
---

# Identity Resolution

The AT Protocol uses two kinds of identifiers for accounts:

- **Handles** -- human-readable names like `my-handle.bsky.social`
- **DIDs** -- stable, cryptographic identifiers like `did:plc:z72i7hdynmk6r22z27h6tvur`

Handles can change; DIDs cannot. The `Identity` module provides functions to resolve between them, with optional bidirectional verification to ensure they agree.

## Resolving a Handle to a DID

Given a typed `Handle`, `resolveHandle` calls `com.atproto.identity.resolveHandle` on the PDS and returns the corresponding `Did`. The agent must be authenticated.

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax

let handle = Handle.parse "my-handle.bsky.social" |> Result.defaultWith failwith
let! result = Identity.resolveHandle agent handle

match result with
| Ok did -> printfn "DID: %s" (Did.value did)
| Error e -> printfn "Resolution failed: %A" e
```

`resolveHandle` takes a typed `Handle` (not a raw string) and returns `Task<Result<Did, IdentityError>>`. If you have a raw string, parse it first with `Handle.parse`.

## Resolving a DID to Identity Info

`resolveDid` fetches the DID document for a given `Did` and extracts everything useful from it into an `AtprotoIdentity` record.

```fsharp
let did = Did.parse "did:plc:z72i7hdynmk6r22z27h6tvur" |> Result.defaultWith failwith
let! result = Identity.resolveDid agent did

match result with
| Ok identity ->
    printfn "DID: %s" (Did.value identity.Did)
    printfn "Handle: %s" (identity.Handle |> Option.map Handle.value |> Option.defaultValue "(none)")
    printfn "PDS: %s" (identity.PdsEndpoint |> Option.map string |> Option.defaultValue "(none)")
    printfn "Signing key: %s" (identity.SigningKey |> Option.defaultValue "(none)")
| Error e ->
    printfn "Failed: %A" e
```

The function extracts:

- The **handle** from the `alsoKnownAs` field (the `at://` entry)
- The **PDS endpoint** from the `service` entries (type `AtprotoPersonalDataServer`)
- The **signing key** from the `verificationMethod` entries (the `#atproto` key)

Both `did:plc:` (resolved via the PLC directory at `plc.directory`) and `did:web:` (resolved via `.well-known/did.json`) methods are supported.

## Bidirectional Verification

`resolveIdentity` is the recommended way to resolve an identifier when you need confidence that a handle and DID actually belong together. It performs forward and reverse resolution, checking that both sides agree.

```fsharp
// Works with both handles and DIDs as a plain string
let! result = Identity.resolveIdentity agent "my-handle.bsky.social"

match result with
| Ok identity ->
    printfn "Verified DID: %s" (Did.value identity.Did)
    match identity.Handle with
    | Some h -> printfn "Verified handle: %s" (Handle.value h)
    | None -> printfn "Handle could not be verified"
| Error e ->
    printfn "Resolution failed: %A" e
```

Unlike `resolveHandle` and `resolveDid` which take typed `Handle` and `Did` respectively, `resolveIdentity` accepts a plain `string` -- it figures out whether you gave it a handle or a DID and does the right thing.

The verification works as follows:

1. **Input is a handle**: resolve handle to DID, then fetch the DID document and check that it claims the same handle in its `alsoKnownAs` field.
2. **Input is a DID**: fetch the DID document to get the claimed handle, then resolve that handle back to a DID and check they match.

If the bidirectional check fails, the returned `AtprotoIdentity` will have `Handle = None` rather than an unverified value.

## The AtprotoIdentity Type

All fields use the library's typed identifiers, not raw strings:

```fsharp
type AtprotoIdentity =
    { Did: Did                  // Typed DID -- use Did.value to get the string
      Handle: Handle option     // Typed Handle -- present only if verified
      PdsEndpoint: Uri option   // FSharp.ATProto.Syntax.Uri
      SigningKey: string option }
```

To print or display these values, use the accessor functions:

```fsharp
let showIdentity (id: Identity.AtprotoIdentity) =
    printfn "DID: %s" (Did.value id.Did)
    printfn "Handle: %s" (id.Handle |> Option.map Handle.value |> Option.defaultValue "(unverified)")
    printfn "PDS: %s" (id.PdsEndpoint |> Option.map string |> Option.defaultValue "(unknown)")
    printfn "Key: %s" (id.SigningKey |> Option.defaultValue "(none)")
```

## Error Handling

Identity resolution functions return errors via the `IdentityError` discriminated union:

```fsharp
type IdentityError =
    | XrpcError of XrpcError         // An XRPC call failed (network, auth, etc.)
    | DocumentParseError of string   // The DID document couldn't be fetched or parsed
```

You can pattern match on this to handle each case differently:

```fsharp
let! result = Identity.resolveIdentity agent "some-handle.bsky.social"

match result with
| Ok identity ->
    printfn "Resolved: %s" (Did.value identity.Did)
| Error (IdentityError.XrpcError xrpcErr) ->
    printfn "Network or API error: %A" xrpcErr
| Error (IdentityError.DocumentParseError msg) ->
    printfn "Malformed DID document: %s" msg
```

`XrpcError` covers failures during XRPC calls -- authentication issues, network problems, rate limiting, and so on. `DocumentParseError` covers cases where the DID document was retrieved but couldn't be parsed into a valid identity (missing required fields, malformed JSON, etc.).

## Parsing DID Documents Directly

If you already have a DID document as a `JsonElement`, you can parse it without making any network calls:

```fsharp
let! response = agent.HttpClient.GetAsync("https://plc.directory/did:plc:z72i7hdynmk6r22z27h6tvur")
let! json = response.Content.ReadAsStringAsync()
let doc = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json)

match Identity.parseDidDocument doc with
| Ok identity -> printfn "Parsed: %s" (Did.value identity.Did)
| Error msg -> printfn "Invalid document: %s" msg
```

Note that `parseDidDocument` returns `Result<AtprotoIdentity, string>` (not `IdentityError`), since there's no network call involved -- the only failure mode is a malformed document.

## When to Use Which Function

| Function | Input | Returns | Network Calls | Verification |
|----------|-------|---------|---------------|-------------|
| `resolveHandle` | `Handle` | `Result<Did, IdentityError>` | 1 (XRPC) | None |
| `resolveDid` | `Did` | `Result<AtprotoIdentity, IdentityError>` | 1 (HTTP) | None |
| `resolveIdentity` | `string` | `Result<AtprotoIdentity, IdentityError>` | 2 (forward + reverse) | Bidirectional |
| `parseDidDocument` | `JsonElement` | `Result<AtprotoIdentity, string>` | 0 | None |

Use `resolveIdentity` when you need confidence that a handle and DID actually belong together -- it's the safest choice for displaying user identity in your application. Use `resolveHandle` or `resolveDid` when you only need a quick lookup and trust the input. Use `parseDidDocument` when you already have the raw DID document in hand.
