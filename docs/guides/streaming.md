---
title: Streaming
category: Advanced Guides
categoryindex: 3
index: 19
description: Real-time event streams via Jetstream and Firehose
keywords: fsharp, atproto, bluesky, streaming, jetstream, firehose, websocket, events
---

# Streaming

The `FSharp.ATProto.Streaming` package provides real-time event streams over WebSocket. Two protocols are supported: **Jetstream** (JSON, simpler) and **Firehose** (CBOR, full fidelity). Both return `IAsyncEnumerable<Result<Event, StreamError>>` for incremental consumption.

```fsharp
open FSharp.ATProto.Streaming
open FSharp.ATProto.Syntax
open System.Threading
```

## Jetstream

Jetstream is a JSON-based relay that provides a filtered, lightweight view of the AT Protocol event stream. It is the recommended starting point for most applications.

### Key Types

| Type | Description |
|---|---|
| `JetstreamEvent` | `CommitEvent`, `IdentityEvent`, `AccountEvent`, `UnknownEvent` |
| `CommitInfo` | Commit details: `Rev`, `Operation`, `Collection`, `Rkey`, `Record`, `Cid` |
| `CommitOperation` | `Create`, `Update`, `Delete` |
| `JetstreamOptions` | Connection config: `Endpoint`, `WantedCollections`, `WantedDids`, `Cursor` |
| `StreamError` | `ConnectionFailed`, `WebSocketError`, `DeserializationError`, `Closed` |

### Functions

| Function | Description |
|---|---|
| `Jetstream.subscribe` | Subscribe to events; returns `IAsyncEnumerable` |
| `Jetstream.parseEvent` | Parse a raw JSON message into a `JetstreamEvent` |
| `Jetstream.buildUri` | Build the WebSocket URI from options |
| `Jetstream.defaultOptions` | Default options pointing at `jetstream1.us-east.bsky.network` |

### Subscribing to Posts

```fsharp
let cts = new CancellationTokenSource()

let options =
    { Jetstream.defaultOptions with
        WantedCollections = [ "app.bsky.feed.post" ] }

let events = Jetstream.subscribe options cts.Token

task {
    let enumerator = events.GetAsyncEnumerator(cts.Token)

    while! enumerator.MoveNextAsync() do
        match enumerator.Current with
        | Ok (CommitEvent (did, _timeUs, info)) when info.Operation = Create ->
            printfn "New post from %s (rkey: %s)" (Did.value did) info.Rkey
        | Ok (IdentityEvent (did, _timeUs, identity)) ->
            printfn "Identity update: %s" (Did.value identity.Did)
        | Ok (AccountEvent (did, _timeUs, account)) ->
            printfn "Account %s active=%b" (Did.value did) account.Active
        | Error StreamError.Closed ->
            printfn "Stream closed"
        | Error e ->
            printfn "Error: %A" e
        | _ -> ()

    // Cancel to disconnect
    cts.Cancel()
}
```

### Filtering by DID

To receive events only for specific accounts, set `WantedDids`:

```fsharp
let options =
    { Jetstream.defaultOptions with
        WantedDids = [ "did:plc:abc123"; "did:plc:def456" ] }
```

### Resuming from a Cursor

Jetstream cursors are microsecond timestamps. To resume from where you left off:

```fsharp
let options =
    { Jetstream.defaultOptions with
        Cursor = Some 1709312400000000L }
```

## Firehose

The Firehose provides the full-fidelity CBOR event stream from `com.atproto.sync.subscribeRepos`. It includes raw CAR blocks and is suitable for indexers and relay operators.

### Key Types

| Type | Description |
|---|---|
| `FirehoseEvent` | `FirehoseCommitEvent`, `FirehoseIdentityEvent`, `FirehoseAccountEvent`, `FirehoseInfoEvent`, `FirehoseErrorEvent`, `FirehoseUnknownEvent` |
| `FirehoseCommit` | Full commit: `Seq`, `Repo`, `Rev`, `Ops`, `Blocks : CarFile`, `Time` |
| `RepoOp` | Operation within a commit: `Action`, `Path`, `Cid` |
| `CarFile` | Parsed CAR v1: `Roots : Cid list`, `Blocks : Map<string, byte[]>` |
| `FirehoseOptions` | Connection config: `Endpoint`, `Cursor`, `MaxMessageSizeBytes` |

### Functions

| Function | Description |
|---|---|
| `Firehose.subscribe` | Subscribe to events; returns `IAsyncEnumerable` |
| `Firehose.parseFrame` | Parse a raw CBOR frame into a `FirehoseEvent` |
| `Firehose.buildUri` | Build the WebSocket URI from options |
| `Firehose.defaultOptions` | Default options pointing at `bsky.network` |

### Subscribing to the Firehose

```fsharp
let cts = new CancellationTokenSource()

let options =
    { Firehose.defaultOptions with
        Cursor = None }

let events = Firehose.subscribe options cts.Token

task {
    let enumerator = events.GetAsyncEnumerator(cts.Token)

    while! enumerator.MoveNextAsync() do
        match enumerator.Current with
        | Ok (FirehoseCommitEvent commit) ->
            for op in commit.Ops do
                printfn "[%d] %s %A %s" commit.Seq (Did.value commit.Repo) op.Action op.Path
        | Ok (FirehoseInfoEvent info) ->
            printfn "Info: %s" info.Name
        | Ok (FirehoseErrorEvent (error, message)) ->
            printfn "Error: %s %A" error message
        | Error e ->
            printfn "Stream error: %A" e
        | _ -> ()

    cts.Cancel()
}
```

## Graceful Shutdown

Both `subscribe` functions respect `CancellationToken`. Cancel the token to close the WebSocket connection cleanly. After cancellation, `MoveNextAsync` returns `false` and the enumerator disposes the underlying WebSocket.

```fsharp
// Run for 30 seconds then stop
cts.CancelAfter(30_000)
```
