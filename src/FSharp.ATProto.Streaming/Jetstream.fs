namespace FSharp.ATProto.Streaming

open System
open System.Buffers
open System.Collections.Generic
open System.Net.WebSockets
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FSharp.ATProto.Syntax

module Jetstream =

    /// Default Jetstream options pointing at the US-East relay.
    let defaultOptions =
        { Endpoint = "wss://jetstream1.us-east.bsky.network/subscribe"
          WantedCollections = []
          WantedDids = []
          Cursor = None
          MaxMessageSizeBytes = 1_048_576 }

    /// Build the WebSocket URI from options.
    let buildUri (options : JetstreamOptions) : System.Uri =
        let sb = StringBuilder (options.Endpoint)
        let mutable hasParam = false

        let addParam (key : string) (value : string) =
            if hasParam then
                sb.Append ('&') |> ignore
            else
                sb.Append ('?') |> ignore
                hasParam <- true

            sb.Append (System.Uri.EscapeDataString key) |> ignore
            sb.Append ('=') |> ignore
            sb.Append (System.Uri.EscapeDataString value) |> ignore

        for col in options.WantedCollections do
            addParam "wantedCollections" col

        for did in options.WantedDids do
            addParam "wantedDids" did

        match options.Cursor with
        | Some cursor -> addParam "cursor" (string cursor)
        | None -> ()

        System.Uri (sb.ToString ())

    let private parseOperation (s : string) =
        match s with
        | "create" -> Ok Create
        | "update" -> Ok Update
        | "delete" -> Ok Delete
        | other -> Error (DeserializationError $"Unknown commit operation: '{other}'")

    let private tryGetString (prop : string) (element : JsonElement) =
        match element.TryGetProperty (prop) with
        | true, v when v.ValueKind = JsonValueKind.String -> Some (v.GetString ())
        | _ -> None

    let private tryGetInt64 (prop : string) (element : JsonElement) =
        match element.TryGetProperty (prop) with
        | true, v when v.ValueKind = JsonValueKind.Number -> Some (v.GetInt64 ())
        | _ -> None

    let private tryGetBool (prop : string) (element : JsonElement) =
        match element.TryGetProperty (prop) with
        | true, v when v.ValueKind = JsonValueKind.True -> Some true
        | true, v when v.ValueKind = JsonValueKind.False -> Some false
        | _ -> None

    let private tryGetElement (prop : string) (element : JsonElement) =
        match element.TryGetProperty (prop) with
        | true, v -> Some v
        | false, _ -> None

    let private parseCommit (root : JsonElement) (did : Did) (timeUs : int64) =
        match tryGetElement "commit" root with
        | None -> Error (DeserializationError "Commit event missing 'commit' field")
        | Some c ->
            match tryGetString "operation" c with
            | None -> Error (DeserializationError "Commit missing 'operation' field")
            | Some opStr ->
                match parseOperation opStr with
                | Error e -> Error e
                | Ok op ->
                    let rev = tryGetString "rev" c |> Option.defaultValue ""
                    let collection = tryGetString "collection" c |> Option.defaultValue ""
                    let rkey = tryGetString "rkey" c |> Option.defaultValue ""
                    let cid = tryGetString "cid" c

                    let record =
                        match tryGetElement "record" c with
                        | Some r when r.ValueKind <> JsonValueKind.Null -> Some (r.Clone ())
                        | _ -> None

                    Ok (
                        CommitEvent (
                            did,
                            timeUs,
                            { Rev = rev
                              Operation = op
                              Collection = collection
                              Rkey = rkey
                              Record = record
                              Cid = cid }
                        )
                    )

    let private parseIdentity (root : JsonElement) (did : Did) (timeUs : int64) =
        match tryGetElement "identity" root with
        | None -> Error (DeserializationError "Identity event missing 'identity' field")
        | Some i ->
            let identityDid =
                tryGetString "did" i
                |> Option.bind (fun s ->
                    match Did.parse s with
                    | Ok d -> Some d
                    | Error _ -> None)
                |> Option.defaultValue did

            let handle =
                tryGetString "handle" i
                |> Option.bind (fun s ->
                    match Handle.parse s with
                    | Ok h -> Some h
                    | Error _ -> None)

            Ok (
                IdentityEvent (
                    did,
                    timeUs,
                    { Did = identityDid
                      Handle = handle }
                )
            )

    let private parseAccount (root : JsonElement) (did : Did) (timeUs : int64) =
        match tryGetElement "account" root with
        | None -> Error (DeserializationError "Account event missing 'account' field")
        | Some a ->
            let active = tryGetBool "active" a |> Option.defaultValue true
            let status = tryGetString "status" a
            Ok (AccountEvent (did, timeUs, { Active = active ; Status = status }))

    /// Parse a raw Jetstream JSON message into a JetstreamEvent.
    let parseEvent (json : string) : Result<JetstreamEvent, StreamError> =
        try
            let doc = JsonDocument.Parse json
            let root = doc.RootElement

            let didStr =
                tryGetString "did" root
                |> Option.defaultValue ""

            match Did.parse didStr with
            | Error _ -> Error (DeserializationError $"Invalid DID in event: '{didStr}'")
            | Ok did ->
                let timeUs =
                    tryGetInt64 "time_us" root |> Option.defaultValue 0L

                match tryGetString "kind" root with
                | Some "commit" -> parseCommit root did timeUs
                | Some "identity" -> parseIdentity root did timeUs
                | Some "account" -> parseAccount root did timeUs
                | Some kind -> Ok (UnknownEvent (did, timeUs, kind))
                | None -> Error (DeserializationError "Event missing 'kind' field")
        with ex ->
            Error (DeserializationError $"Failed to parse JSON: {ex.Message}")

    let private receiveOne
        (ws : ClientWebSocket)
        (maxSize : int)
        (token : CancellationToken)
        : Task<Result<JetstreamEvent, StreamError> option> =
        task {
            let buffer = ArrayPool<byte>.Shared.Rent maxSize

            try
                let segment = ArraySegment<byte> (buffer)
                let! wsResult = ws.ReceiveAsync (segment, token)

                if wsResult.MessageType = WebSocketMessageType.Close then
                    return Some (Error Closed)
                elif wsResult.MessageType = WebSocketMessageType.Text then
                    let text = Encoding.UTF8.GetString (buffer, 0, wsResult.Count)
                    return Some (parseEvent text)
                else
                    return None
            finally
                ArrayPool<byte>.Shared.Return buffer
        }

    /// Subscribe to a Jetstream relay. Returns an IAsyncEnumerable that yields
    /// events until the connection closes or the CancellationToken is triggered.
    /// On disconnect the final element will be Error Closed.
    let subscribe
        (options : JetstreamOptions)
        (ct : CancellationToken)
        : IAsyncEnumerable<Result<JetstreamEvent, StreamError>> =
        { new IAsyncEnumerable<Result<JetstreamEvent, StreamError>> with
            member _.GetAsyncEnumerator (_ct) =
                let combinedCts =
                    CancellationTokenSource.CreateLinkedTokenSource (ct, _ct)

                let token = combinedCts.Token
                let ws = new ClientWebSocket ()
                let mutable current = Unchecked.defaultof<Result<JetstreamEvent, StreamError>>
                let mutable finished = false
                let mutable connected = false

                { new IAsyncEnumerator<Result<JetstreamEvent, StreamError>> with
                    member _.Current = current

                    member _.MoveNextAsync () =
                        if finished then
                            ValueTask<bool> false
                        else
                            ValueTask<bool> (
                                task {
                                    try
                                        if not connected then
                                            let uri = buildUri options
                                            do! ws.ConnectAsync (uri, token)
                                            connected <- true

                                        let! msg = receiveOne ws options.MaxMessageSizeBytes token

                                        match msg with
                                        | Some (Error Closed) ->
                                            current <- Error Closed
                                            finished <- true
                                            return true
                                        | Some result ->
                                            current <- result
                                            return true
                                        | None ->
                                            // Binary/unexpected frame — skip
                                            current <- Error (DeserializationError "Unexpected binary frame")
                                            return true
                                    with
                                    | :? OperationCanceledException ->
                                        finished <- true
                                        return false
                                    | :? WebSocketException as ex ->
                                        current <- Error (StreamError.WebSocketError ex.Message)
                                        finished <- true
                                        return true
                                    | ex ->
                                        current <- Error (ConnectionFailed ex.Message)
                                        finished <- true
                                        return true
                                }
                            )

                    member _.DisposeAsync () =
                        task {
                            try
                                if connected && ws.State = WebSocketState.Open then
                                    do!
                                        ws.CloseAsync (
                                            WebSocketCloseStatus.NormalClosure,
                                            "done",
                                            CancellationToken.None
                                        )
                            with _ ->
                                ()

                            ws.Dispose ()
                            combinedCts.Dispose ()
                        }
                        |> ValueTask } }
