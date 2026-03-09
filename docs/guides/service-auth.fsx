(**
---
title: Service Auth
category: Infrastructure
categoryindex: 5
index: 27
description: Inter-service JWT authentication for AT Protocol services
keywords: fsharp, atproto, service-auth, jwt, inter-service, authentication
---

# Service Auth

AT Protocol backend services -- feed generators, labelers, and other service providers -- authenticate to each other using signed JWTs. The `ServiceAuth` module in `FSharp.ATProto.Core` handles creating, parsing, and validating these tokens, and can configure an `AtpAgent` to attach service auth headers automatically.

## Algorithm

```fsharp
type Algorithm = ES256 | ES256K
```

`ES256` corresponds to P-256 ECDSA, `ES256K` to secp256k1 ECDSA. Most AT Protocol services use ES256.

## Claims

Service auth JWTs carry these claims:

```fsharp
type Claims =
    { Iss : Did               // Issuer -- your service's DID
      Aud : Did               // Audience -- the target service's DID
      Lxm : Nsid option       // Lexicon method being called (optional)
      Exp : DateTimeOffset    // Expiration time
      Iat : DateTimeOffset }  // Issued-at time
```

## Creating Tokens

The `createToken` function takes a signing function (`byte[] -> byte[]`) that should produce a 64-byte compact ECDSA signature. If you are using `FSharp.ATProto.Crypto`, this is `Signing.sign keyPair`.
*)

(*** hide ***)
#nowarn "20"
#r "../../src/FSharp.ATProto.Syntax/bin/Release/net10.0/FSharp.ATProto.Syntax.dll"
#r "../../src/FSharp.ATProto.DRISL/bin/Release/net10.0/FSharp.ATProto.DRISL.dll"
#r "../../src/FSharp.ATProto.Core/bin/Release/net10.0/FSharp.ATProto.Core.dll"
open FSharp.ATProto.Syntax
open FSharp.ATProto.Core
open System
(***)

open FSharp.ATProto.Core
open FSharp.ATProto.Syntax

(*** hide ***)
module Crypto =
    module Keys =
        type Algorithm = P256
        type KeyPair = { Dummy: int }
        let generate (_alg: Algorithm) : KeyPair = Unchecked.defaultof<_>
    module Signing =
        let sign (_kp: Keys.KeyPair) : (byte[] -> byte[]) = Unchecked.defaultof<_>
        let verify (_pub: unit) (_data: byte[]) (_sig: byte[]) : bool = Unchecked.defaultof<_>

let keyPair = Crypto.Keys.generate Crypto.Keys.P256
let sign = Crypto.Signing.sign keyPair
(***)

(**
```fsharp
open FSharp.ATProto.Crypto

let keyPair = Keys.generate Algorithm.P256
let sign = Signing.sign keyPair
```
*)

let iss = Did.parse "did:web:feed.example.com" |> Result.defaultWith failwith
let aud = Did.parse "did:plc:target-pds" |> Result.defaultWith failwith

// Full control over claims
let claims : ServiceAuth.Claims =
    { Iss = iss
      Aud = aud
      Exp = DateTimeOffset.UtcNow.AddMinutes 5.0
      Iat = DateTimeOffset.UtcNow
      Lxm = Nsid.parse "app.bsky.feed.getFeedSkeleton" |> Result.toOption }

let token = ServiceAuth.createToken ServiceAuth.Algorithm.ES256 sign claims

(**
For the common case where you want a token that expires in 60 seconds:
*)

let token2 =
    ServiceAuth.createTokenNow
        ServiceAuth.Algorithm.ES256
        sign
        iss
        aud
        (Nsid.parse "app.bsky.feed.getFeedSkeleton" |> Result.toOption)

(**
## Parsing and Validating Tokens

Parse claims from a JWT **without** verifying the signature:
*)

match ServiceAuth.parseClaims token with
| Ok (claims, alg) ->
    printfn "Issuer: %s" (Did.value claims.Iss)
    printfn "Algorithm: %A" alg
| Error msg ->
    printfn "Parse error: %s" msg

(**
Validate a JWT by verifying both the signature and the expiration:
*)

(*** hide ***)
let verifyFn = Unchecked.defaultof<byte[] -> byte[] -> bool>
(***)

(**
```fsharp
let verifyFn = Signing.verify (Keys.publicKey keyPair)
```
*)

match ServiceAuth.validateToken verifyFn token with
| Ok claims -> printfn "Valid token from %s" (Did.value claims.Iss)
| Error msg -> printfn "Invalid: %s" msg

(**
`validateToken` returns `Error` if the signature is invalid or the token has expired.

## Agent Integration

`withServiceAuth` configures an `AtpAgent` to automatically attach a `Bearer` token to every request. The NSID is extracted from the request URL path, so each request gets a correctly scoped token.
*)

open FSharp.ATProto.Core

(**
```fsharp
open FSharp.ATProto.Crypto

let keyPair = Keys.generate Algorithm.P256
let sign = Signing.sign keyPair
```
*)

let agent =
    AtpAgent.create "https://bsky.social"
    |> ServiceAuth.withServiceAuth
        ServiceAuth.Algorithm.ES256
        sign
        iss   // your service's DID
        aud   // the target PDS DID

(**
The agent will generate a fresh JWT for each request with a 60-second expiry and the correct `lxm` claim.
*)
