(**
---
title: Error Handling
category: Getting Started
categoryindex: 1
index: 4
description: XrpcError, taskResult CE, retry behaviour, and rate limits
keywords: fsharp, atproto, bluesky, error-handling, taskresult, xrpc-error, rate-limit
---
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
let post = Unchecked.defaultof<PostRef>
(***)

(**
# Error Handling

Every fallible operation in FSharp.ATProto returns `Task<Result<'T, XrpcError>>`. No exceptions are thrown for protocol-level failures. This guide covers the error type, the `taskResult` computation expression for chaining these operations, and the automatic retry behaviour built into the XRPC layer.

> Code samples throughout the docs use `taskResult {}`, the computation expression explained on this page.

## The taskResult CE

The `taskResult` computation expression is defined in `FSharp.ATProto.Core` and auto-opened into scope. It chains `Task<Result<'T, 'E>>` values with automatic error short-circuiting: if any `let!` binding produces an `Error`, the entire expression returns that error immediately without executing subsequent steps.
*)

let workflow =
    taskResult {
        let! agent = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"
        let! post = Bluesky.post agent "Hello from F#!"
        let! like = Bluesky.like agent post
        return post
    }

(**
If `Bluesky.login` fails, the post and like are never attempted. The error propagates out as the result of the whole expression.

Without `taskResult`, the equivalent code requires manual matching at each step:
*)

let workflowManual =
    task {
        let! loginResult = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"
        match loginResult with
        | Error err -> return Error err
        | Ok agent ->
            let! postResult = Bluesky.post agent "Hello from F#!"
            match postResult with
            | Error err -> return Error err
            | Ok post ->
                let! likeResult = Bluesky.like agent post
                match likeResult with
                | Error err -> return Error err
                | Ok _ -> return Ok post
    }

(**
We recommend `taskResult` for most use cases.

## XrpcError

All errors are represented as an `XrpcError` record:

```
type XrpcError =
    { StatusCode: int
      Error: string option
      Message: string option }
```

Common errors you may encounter:

| Status | Error String | Meaning |
|--------|-------------|---------|
| 400 | `InvalidRequest` | Malformed input (e.g., invalid [AT-URI](../concepts.html), missing required field) |
| 401 | `ExpiredToken` | Access token expired -- handled automatically (see below) |
| 401 | `AuthenticationRequired` | No session or invalid credentials |
| 404 | (varies) | Resource not found (deleted post, unknown [DID](../concepts.html)) |
| 429 | `RateLimitExceeded` | Too many requests -- handled automatically (see below) |
| 500 | `InternalServerError` | Server-side failure |

## Automatic Retry

The XRPC layer handles two transient error cases transparently. You do not need to implement retry logic for these.

**Expired tokens (401).** When a request fails with `ExpiredToken`, the library automatically refreshes the session using the refresh JWT stored in the `AtpAgent`, then retries the original request once with the new access token. If the refresh itself fails, that error is returned to the caller.

**Rate limiting (429).** When a request is rate-limited, the library reads the `Retry-After` header from the response, waits for the specified duration (defaulting to 1 second if the header is absent), then retries once. If the retry also fails, its error is returned.

All other errors are returned immediately with no retry.

## Handling Errors

At the boundary of your program, match on the result to handle success and failure:
*)

let run =
    task {
        let! result =
            taskResult {
                let! agent = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"
                let! post = Bluesky.post agent "Hello!"
                return post
            }

        match result with
        | Ok post ->
            printfn "Posted: %s" (AtUri.value post.Uri)
        | Error err ->
            printfn "Failed (%d): %s"
                err.StatusCode
                (err.Message |> Option.defaultValue "unknown")
    }

(**
You can also pattern match on specific error codes to take different actions:
*)

(*** hide ***)
let result = Unchecked.defaultof<Result<PostRef, XrpcError>>
(***)

match result with
| Ok post -> printfn "Posted: %s" (AtUri.value post.Uri)
| Error { StatusCode = 401 } -> printfn "Not authenticated"
| Error { StatusCode = 400; Message = Some msg } -> printfn "Bad request: %s" msg
| Error err -> printfn "Unexpected error (%d)" err.StatusCode

(**
## When to Use task vs taskResult

Use `taskResult` when you have a chain of fallible operations and want errors to short-circuit through the whole chain. This is the common case for workflows like "log in, fetch data, do something with it."

Use `task {}` when you want to handle each error individually at the call site -- for example, if a failure at step 2 should trigger a different recovery path rather than aborting the whole workflow:
*)

let taskExample =
    task {
        let! agent = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"
        match agent with
        | Error err -> printfn "Login failed: %A" err
        | Ok agent ->
            let! postResult = Bluesky.post agent "Hello!"
            match postResult with
            | Ok post -> printfn "Posted: %s" (AtUri.value post.Uri)
            | Error _ -> printfn "Post failed, but continuing..."
    }

(**
For pagination-specific error handling, see the [Pagination guide](pagination.html).
*)
