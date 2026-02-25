---
title: Identity Resolution
category: Guides
categoryindex: 2
index: 4
description: Resolve AT Protocol handles and DIDs with bidirectional verification
keywords: identity, did, handle, resolution, verification
---

# Identity Resolution

The AT Protocol uses two kinds of identifiers for accounts:

- **Handles** -- human-readable names like `alice.bsky.social`
- **DIDs** -- stable, cryptographic identifiers like `did:plc:z72i7hdynmk6r22z27h6tvur`

Handles can change; DIDs cannot. The `Identity` module provides functions to resolve between them, with optional bidirectional verification to ensure they agree.

## Resolving a Handle to a DID

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

let! result = Identity.resolveHandle agent "alice.bsky.social"

match result with
| Ok did -> printfn "DID: %s" did
| Error e -> printfn "Resolution failed: %A" e.Message
```

This calls `com.atproto.identity.resolveHandle` on the PDS. The agent must be authenticated.

## Resolving a DID to Identity Info

```fsharp
let! result = Identity.resolveDid agent "did:plc:z72i7hdynmk6r22z27h6tvur"

match result with
| Ok identity ->
    printfn "DID: %s" identity.Did
    printfn "Handle: %s" (identity.Handle |> Option.defaultValue "(none)")
    printfn "PDS: %s" (identity.PdsEndpoint |> Option.defaultValue "(none)")
    printfn "Signing key: %s" (identity.SigningKey |> Option.defaultValue "(none)")
| Error msg ->
    printfn "Failed: %s" msg
```

`resolveDid` fetches the DID document and extracts:

- The **handle** from the `alsoKnownAs` field (the `at://` entry)
- The **PDS endpoint** from the `service` entries (type `AtprotoPersonalDataServer`)
- The **signing key** from the `verificationMethod` entries (the `#atproto` key)

Both `did:plc:` (resolved via the PLC directory at `plc.directory`) and `did:web:` (resolved via `.well-known/did.json`) methods are supported.

## Bidirectional Verification with `resolveIdentity`

`resolveIdentity` is the recommended way to resolve an identifier. It performs bidirectional verification: it checks that the handle and DID agree with each other. If they don't, the handle is stripped from the result.

```fsharp
// Works with both handles and DIDs as input
let! result = Identity.resolveIdentity agent "alice.bsky.social"

match result with
| Ok identity ->
    printfn "Verified DID: %s" identity.Did
    match identity.Handle with
    | Some h -> printfn "Verified handle: %s" h
    | None -> printfn "Handle could not be verified"
| Error msg ->
    printfn "Resolution failed: %s" msg
```

The verification works as follows:

1. **Input is a handle**: resolve handle to DID, then fetch the DID document and check that it claims the same handle in its `alsoKnownAs` field.
2. **Input is a DID**: fetch the DID document to get the claimed handle, then resolve that handle back to a DID and check they match.

If the bidirectional check fails, the returned `AtprotoIdentity` will have `Handle = None` rather than an unverified value.

## The `AtprotoIdentity` Type

```fsharp
type AtprotoIdentity =
    { Did: string             // Always present
      Handle: string option   // Present only if verified
      PdsEndpoint: string option
      SigningKey: string option }
```

## Parsing DID Documents Directly

If you already have a DID document as a `JsonElement`, you can parse it without making any network calls:

```fsharp
let! response = agent.HttpClient.GetAsync("https://plc.directory/did:plc:z72i7hdynmk6r22z27h6tvur")
let! json = response.Content.ReadAsStringAsync()
let doc = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json)

match Identity.parseDidDocument doc with
| Ok identity -> printfn "Parsed: %s" identity.Did
| Error msg -> printfn "Invalid document: %s" msg
```

## When to Use Which Function

| Function | Input | Network Calls | Verification |
|----------|-------|---------------|-------------|
| `resolveHandle` | Handle | 1 (XRPC) | None |
| `resolveDid` | DID | 1 (HTTP) | None |
| `resolveIdentity` | Handle or DID | 2 (forward + reverse) | Bidirectional |
| `parseDidDocument` | JsonElement | 0 | None |

Use `resolveIdentity` when you need confidence that a handle and DID actually belong together. Use `resolveHandle` or `resolveDid` when you only need a quick lookup and trust the input.
