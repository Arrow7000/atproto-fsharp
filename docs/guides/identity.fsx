(**
---
title: Identity Resolution
category: Advanced Guides
categoryindex: 3
index: 13
description: Resolve AT Protocol handles and DIDs with bidirectional verification
keywords: identity, did, handle, resolution, verification
---

# Identity Resolution

All examples use `taskResult {}`. See the [Error Handling guide](error-handling.html) for details.

The AT Protocol uses two kinds of identifiers for accounts:

- **[Handles](../concepts.html)** -- human-readable names like `my-handle.bsky.social`
- **[DIDs](../concepts.html)** -- stable, cryptographic identifiers like `did:plc:z72i7hdynmk6r22z27h6tvur`

Handles can change; DIDs cannot. The `Identity` module provides functions to resolve between them, with optional bidirectional verification to ensure they agree.

## Resolving an Identity (Recommended)

`resolveIdentity` is the recommended entry point. It accepts a plain `string` -- either a handle or a DID -- performs forward and reverse resolution, and checks that both sides agree.
*)

(*** hide ***)
#nowarn "20"
#r "../../src/FSharp.ATProto.Syntax/bin/Release/net10.0/FSharp.ATProto.Syntax.dll"
#r "../../src/FSharp.ATProto.Core/bin/Release/net10.0/FSharp.ATProto.Core.dll"
#r "../../src/FSharp.ATProto.Bluesky/bin/Release/net10.0/FSharp.ATProto.Bluesky.dll"
open FSharp.ATProto.Syntax
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

let agent = Unchecked.defaultof<AtpAgent>
let handle = Unchecked.defaultof<Handle>
let did = Unchecked.defaultof<Did>
(***)

open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax

taskResult {
    let! identity = Identity.resolveIdentity agent "my-handle.bsky.social"
    printfn "Verified DID: %s" (Did.value identity.Did)

    match identity.Handle with
    | Some h -> printfn "Verified handle: %s" (Handle.value h)
    | None -> printfn "Handle could not be verified"

    return identity
}

(**
Unlike `resolveHandle` and `resolveDid` which take typed `Handle` and `Did` respectively, `resolveIdentity` accepts a plain `string` -- it figures out whether you gave it a handle or a DID and does the right thing.

The verification works as follows:

1. **Input is a handle**: resolve handle to DID, then fetch the DID document and check that it claims the same handle in its `alsoKnownAs` field.
2. **Input is a DID**: fetch the DID document to get the claimed handle, then resolve that handle back to a DID and check they match.

If the bidirectional check fails, the returned `AtprotoIdentity` will have `Handle = None` rather than an unverified value.

## Typed Alternatives

If you already have a typed `Handle` or `Did`, you can use the single-direction functions directly. These perform one lookup without bidirectional verification.

**Resolve a Handle to a DID:**
*)

// Handle.parse returns Result<Handle, string> -- handle the error first
match Handle.parse "my-handle.bsky.social" with
| Ok handle ->
    taskResult {
        let! did = Identity.resolveHandle agent handle
        printfn "DID: %s" (Did.value did)
    }
    |> ignore
| Error msg -> printfn "Invalid handle: %s" msg

(**
**Resolve a DID to full identity info:**
*)

match Did.parse "did:plc:z72i7hdynmk6r22z27h6tvur" with
| Ok did ->
    taskResult {
        let! identity = Identity.resolveDid agent did
        printfn "Handle: %s" (identity.Handle |> Option.map Handle.value |> Option.defaultValue "(none)")
        printfn "PDS: %s" (identity.PdsEndpoint |> Option.map string |> Option.defaultValue "(none)")
    }
    |> ignore
| Error msg -> printfn "Invalid DID: %s" msg

(**
`resolveDid` fetches the DID document and extracts the **handle** from `alsoKnownAs`, the **PDS endpoint** from the `service` entries, and the **signing key** from the `verificationMethod` entries. Both `did:plc:` (via the PLC directory) and `did:web:` (via `.well-known/did.json`) methods are supported.

## The AtprotoIdentity Type

All fields use the library's typed identifiers, not raw strings:

```
type AtprotoIdentity =
    { Did: Did                  // Typed DID -- use Did.value to get the string
      Handle: Handle option     // Typed Handle -- present only if verified
      PdsEndpoint: Uri option   // FSharp.ATProto.Syntax.Uri
      SigningKey: string option }
```

To print or display these values, use the accessor functions:
*)

let showIdentity (id: Identity.AtprotoIdentity) =
    printfn "DID: %s" (Did.value id.Did)
    printfn "Handle: %s" (id.Handle |> Option.map Handle.value |> Option.defaultValue "(unverified)")
    printfn "PDS: %s" (id.PdsEndpoint |> Option.map string |> Option.defaultValue "(unknown)")
    printfn "Key: %s" (id.SigningKey |> Option.defaultValue "(none)")

(**
## Error Handling

Identity resolution functions return errors via the `IdentityError` discriminated union:

```
type IdentityError =
    | XrpcError of XrpcError         // An XRPC call failed (network, auth, etc.)
    | DocumentParseError of string   // The DID document couldn't be fetched or parsed
```

You can pattern match on this to handle each case differently:
*)

(*** hide ***)
let result = Unchecked.defaultof<Result<Identity.AtprotoIdentity, IdentityError>>
(***)

match result with
| Ok identity ->
    printfn "Resolved: %s" (Did.value identity.Did)
| Error (IdentityError.XrpcError xrpcErr) ->
    printfn "Network or API error: %A" xrpcErr
| Error (IdentityError.DocumentParseError msg) ->
    printfn "Malformed DID document: %s" msg

(**
`XrpcError` covers failures during XRPC calls -- authentication issues, network problems, rate limiting. `DocumentParseError` covers cases where a DID document was retrieved but couldn't be parsed into a valid identity (missing required fields, malformed JSON, etc.).

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
| `resolveIdentity` | `string` | `Result<AtprotoIdentity, IdentityError>` | 2 (forward + reverse) | Bidirectional |
| `resolveHandle` | `Handle` | `Result<Did, IdentityError>` | 1 (XRPC) | None |
| `resolveDid` | `Did` | `Result<AtprotoIdentity, IdentityError>` | 1 (HTTP) | None |
| `parseDidDocument` | `JsonElement` | `Result<AtprotoIdentity, string>` | 0 | None |

Use `resolveIdentity` when you need confidence that a handle and DID actually belong together -- it's the safest choice for displaying user identity in your application. Use `resolveHandle` or `resolveDid` when you only need a quick lookup and trust the input. Use `parseDidDocument` when you already have the raw DID document in hand.
*)
