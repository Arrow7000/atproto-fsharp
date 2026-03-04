namespace FSharp.ATProto.Streaming

open System
open System.Buffers
open System.Collections.Generic
open System.Formats.Cbor
open System.Net.WebSockets
open System.Threading
open System.Threading.Tasks
open FSharp.ATProto.Syntax

module Firehose =

    /// Default firehose options pointing at the main Bluesky relay.
    let defaultOptions =
        { Endpoint = "wss://bsky.network/xrpc/com.atproto.sync.subscribeRepos"
          Cursor = None
          MaxMessageSizeBytes = 5_242_880 }

    /// Build the WebSocket URI from options.
    let buildUri (options : FirehoseOptions) : System.Uri =
        match options.Cursor with
        | Some cursor -> System.Uri $"{options.Endpoint}?cursor={cursor}"
        | None -> System.Uri options.Endpoint

    let private readCborString (reader : CborReader) =
        reader.ReadTextString ()

    let private readCborInt (reader : CborReader) =
        reader.ReadInt64 ()

    let private readCborBool (reader : CborReader) =
        reader.ReadBoolean ()

    let private readCborBytes (reader : CborReader) =
        reader.ReadByteString ()

    let private tryReadCborStringOpt (reader : CborReader) =
        if reader.PeekState () = CborReaderState.Null then
            reader.ReadNull ()
            None
        else
            Some (reader.ReadTextString ())

    let private readCborCidLink (reader : CborReader) : Cid option =
        if reader.PeekState () = CborReaderState.Null then
            reader.ReadNull ()
            None
        else
            match CarParser.readCborCid reader with
            | Ok cid -> Some cid
            | Error _ -> None

    /// Parse the CBOR frame header from a WebSocket message.
    /// Returns (op, type_string_or_none, remaining_bytes).
    let private parseFrameHeader (data : byte[]) (count : int) : Result<int * string option * byte[], string> =
        try
            let reader =
                CborReader (ReadOnlyMemory<byte> (data, 0, count), CborConformanceMode.Lax)

            let mapLen = reader.ReadStartMap ()
            let mutable op = 0
            let mutable t : string option = None

            let entries =
                match mapLen |> Nullable.op_Explicit with
                | n when n > 0 -> n
                | _ -> 2 // Expect at most 2 fields

            for _ in 0 .. entries - 1 do
                if reader.PeekState () <> CborReaderState.EndMap then
                    let key = reader.ReadTextString ()

                    match key with
                    | "op" -> op <- int (reader.ReadInt64 ())
                    | "t" -> t <- Some (reader.ReadTextString ())
                    | _ -> reader.SkipValue ()

            reader.ReadEndMap ()

            let consumed = count - reader.BytesRemaining
            let remaining = data.[consumed .. count - 1]
            Ok (op, t, remaining)
        with ex ->
            Error $"Failed to parse frame header: {ex.Message}"

    let private parseRepoOp (reader : CborReader) : RepoOp =
        let _ = reader.ReadStartMap ()
        let mutable action = Create
        let mutable path = ""
        let mutable cid : Cid option = None

        let mutable reading = true

        while reading do
            if reader.PeekState () = CborReaderState.EndMap then
                reading <- false
            else
                let key = reader.ReadTextString ()

                match key with
                | "action" ->
                    action <-
                        match reader.ReadTextString () with
                        | "create" -> Create
                        | "update" -> Update
                        | "delete" -> Delete
                        | _ -> Delete
                | "path" -> path <- reader.ReadTextString ()
                | "cid" -> cid <- readCborCidLink reader
                | _ -> reader.SkipValue ()

        reader.ReadEndMap ()

        { Action = action
          Path = path
          Cid = cid }

    let private parseCommitPayload (data : byte[]) : Result<FirehoseCommit, string> =
        try
            let reader =
                CborReader (ReadOnlyMemory<byte> data, CborConformanceMode.Lax)

            let _ = reader.ReadStartMap ()
            let mutable seq = 0L
            let mutable repo = ""
            let mutable rev = ""
            let mutable since : string option = None
            let mutable time = ""
            let mutable blocks : byte[] = [||]
            let ops = System.Collections.Generic.List<RepoOp> ()

            let mutable reading = true

            while reading do
                if reader.PeekState () = CborReaderState.EndMap then
                    reading <- false
                else
                    let key = reader.ReadTextString ()

                    match key with
                    | "seq" -> seq <- reader.ReadInt64 ()
                    | "repo" -> repo <- reader.ReadTextString ()
                    | "rev" -> rev <- reader.ReadTextString ()
                    | "since" -> since <- tryReadCborStringOpt reader
                    | "time" -> time <- reader.ReadTextString ()
                    | "blocks" -> blocks <- reader.ReadByteString ()
                    | "ops" ->
                        let _ = reader.ReadStartArray ()
                        let mutable readingOps = true

                        while readingOps do
                            if reader.PeekState () = CborReaderState.EndArray then
                                readingOps <- false
                            else
                                ops.Add (parseRepoOp reader)

                        reader.ReadEndArray ()
                    | _ -> reader.SkipValue ()

            reader.ReadEndMap ()

            match Did.parse repo with
            | Error e -> Error $"Invalid repo DID: {e}"
            | Ok repoDid ->
                let carResult =
                    if blocks.Length > 0 then
                        CarParser.parse blocks
                    else
                        Ok { Roots = [] ; Blocks = Map.empty }

                match carResult with
                | Error e -> Error $"Failed to parse CAR blocks: {e}"
                | Ok carFile ->
                    Ok
                        { Seq = seq
                          Repo = repoDid
                          Rev = rev
                          Since = since
                          Ops = ops |> Seq.toList
                          Blocks = carFile
                          Time = time }
        with ex ->
            Error $"Failed to parse commit payload: {ex.Message}"

    let private parseIdentityPayload (data : byte[]) : Result<FirehoseIdentity, string> =
        try
            let reader =
                CborReader (ReadOnlyMemory<byte> data, CborConformanceMode.Lax)

            let _ = reader.ReadStartMap ()
            let mutable seq = 0L
            let mutable did = ""
            let mutable handle : string option = None
            let mutable time = ""

            let mutable reading = true

            while reading do
                if reader.PeekState () = CborReaderState.EndMap then
                    reading <- false
                else
                    let key = reader.ReadTextString ()

                    match key with
                    | "seq" -> seq <- reader.ReadInt64 ()
                    | "did" -> did <- reader.ReadTextString ()
                    | "handle" -> handle <- tryReadCborStringOpt reader
                    | "time" -> time <- reader.ReadTextString ()
                    | _ -> reader.SkipValue ()

            reader.ReadEndMap ()

            match Did.parse did with
            | Error e -> Error $"Invalid DID: {e}"
            | Ok parsedDid ->
                let parsedHandle =
                    handle
                    |> Option.bind (fun h ->
                        match Handle.parse h with
                        | Ok hh -> Some hh
                        | Error _ -> None)

                Ok
                    { Seq = seq
                      Did = parsedDid
                      Handle = parsedHandle
                      Time = time }
        with ex ->
            Error $"Failed to parse identity payload: {ex.Message}"

    let private parseAccountPayload (data : byte[]) : Result<FirehoseAccount, string> =
        try
            let reader =
                CborReader (ReadOnlyMemory<byte> data, CborConformanceMode.Lax)

            let _ = reader.ReadStartMap ()
            let mutable seq = 0L
            let mutable did = ""
            let mutable active = true
            let mutable status : string option = None
            let mutable time = ""

            let mutable reading = true

            while reading do
                if reader.PeekState () = CborReaderState.EndMap then
                    reading <- false
                else
                    let key = reader.ReadTextString ()

                    match key with
                    | "seq" -> seq <- reader.ReadInt64 ()
                    | "did" -> did <- reader.ReadTextString ()
                    | "active" -> active <- reader.ReadBoolean ()
                    | "status" -> status <- tryReadCborStringOpt reader
                    | "time" -> time <- reader.ReadTextString ()
                    | _ -> reader.SkipValue ()

            reader.ReadEndMap ()

            match Did.parse did with
            | Error e -> Error $"Invalid DID: {e}"
            | Ok parsedDid ->
                Ok
                    { Seq = seq
                      Did = parsedDid
                      Active = active
                      Status = status
                      Time = time }
        with ex ->
            Error $"Failed to parse account payload: {ex.Message}"

    let private parseInfoPayload (data : byte[]) : Result<FirehoseInfo, string> =
        try
            let reader =
                CborReader (ReadOnlyMemory<byte> data, CborConformanceMode.Lax)

            let _ = reader.ReadStartMap ()
            let mutable name = ""
            let mutable message : string option = None

            let mutable reading = true

            while reading do
                if reader.PeekState () = CborReaderState.EndMap then
                    reading <- false
                else
                    let key = reader.ReadTextString ()

                    match key with
                    | "name" -> name <- reader.ReadTextString ()
                    | "message" -> message <- tryReadCborStringOpt reader
                    | _ -> reader.SkipValue ()

            reader.ReadEndMap ()

            Ok { Name = name ; Message = message }
        with ex ->
            Error $"Failed to parse info payload: {ex.Message}"

    let private parseErrorPayload (data : byte[]) : string * string option =
        try
            let reader =
                CborReader (ReadOnlyMemory<byte> data, CborConformanceMode.Lax)

            let _ = reader.ReadStartMap ()
            let mutable error = ""
            let mutable message : string option = None

            let mutable reading = true

            while reading do
                if reader.PeekState () = CborReaderState.EndMap then
                    reading <- false
                else
                    let key = reader.ReadTextString ()

                    match key with
                    | "error" -> error <- reader.ReadTextString ()
                    | "message" -> message <- tryReadCborStringOpt reader
                    | _ -> reader.SkipValue ()

            reader.ReadEndMap ()
            (error, message)
        with _ ->
            ("Unknown", None)

    /// Parse a raw firehose WebSocket binary frame into a FirehoseEvent.
    let parseFrame (data : byte[]) (count : int) : Result<FirehoseEvent, StreamError> =
        match parseFrameHeader data count with
        | Error e -> Error (DeserializationError e)
        | Ok (op, t, payload) ->
            if op = -1 then
                let (error, message) = parseErrorPayload payload
                Ok (FirehoseErrorEvent (error, message))
            elif op = 1 then
                match t with
                | Some "#commit" ->
                    match parseCommitPayload payload with
                    | Ok commit -> Ok (FirehoseCommitEvent commit)
                    | Error e -> Error (DeserializationError e)
                | Some "#identity" ->
                    match parseIdentityPayload payload with
                    | Ok identity -> Ok (FirehoseIdentityEvent identity)
                    | Error e -> Error (DeserializationError e)
                | Some "#account" ->
                    match parseAccountPayload payload with
                    | Ok account -> Ok (FirehoseAccountEvent account)
                    | Error e -> Error (DeserializationError e)
                | Some "#info" ->
                    match parseInfoPayload payload with
                    | Ok info -> Ok (FirehoseInfoEvent info)
                    | Error e -> Error (DeserializationError e)
                | Some kind -> Ok (FirehoseUnknownEvent kind)
                | None -> Error (DeserializationError "Message frame missing type field")
            else
                Error (DeserializationError $"Unknown frame op: {op}")

    let private receiveFullMessage
        (ws : ClientWebSocket)
        (initialBuffer : int)
        (token : CancellationToken)
        : Task<byte[] * int> =
        task {
            let mutable buffer = ArrayPool<byte>.Shared.Rent initialBuffer
            let mutable totalReceived = 0
            let mutable endOfMessage = false
            let mutable closed = false

            while not endOfMessage && not closed do
                if totalReceived >= buffer.Length then
                    let newBuffer = ArrayPool<byte>.Shared.Rent (buffer.Length * 2)
                    Buffer.BlockCopy (buffer, 0, newBuffer, 0, totalReceived)
                    ArrayPool<byte>.Shared.Return buffer
                    buffer <- newBuffer

                let segment =
                    ArraySegment<byte> (buffer, totalReceived, buffer.Length - totalReceived)

                let! result = ws.ReceiveAsync (segment, token)

                if result.MessageType = WebSocketMessageType.Close then
                    closed <- true
                else
                    totalReceived <- totalReceived + result.Count
                    endOfMessage <- result.EndOfMessage

            if closed then
                return (buffer, -1)
            else
                return (buffer, totalReceived)
        }

    /// Subscribe to the AT Protocol firehose. Returns an IAsyncEnumerable that yields
    /// parsed events until the connection closes or the CancellationToken is triggered.
    let subscribe
        (options : FirehoseOptions)
        (ct : CancellationToken)
        : IAsyncEnumerable<Result<FirehoseEvent, StreamError>> =
        { new IAsyncEnumerable<Result<FirehoseEvent, StreamError>> with
            member _.GetAsyncEnumerator (_ct) =
                let combinedCts =
                    CancellationTokenSource.CreateLinkedTokenSource (ct, _ct)

                let token = combinedCts.Token
                let ws = new ClientWebSocket ()
                let mutable current = Unchecked.defaultof<Result<FirehoseEvent, StreamError>>
                let mutable finished = false
                let mutable connected = false

                { new IAsyncEnumerator<Result<FirehoseEvent, StreamError>> with
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

                                        let! (buffer, count) =
                                            receiveFullMessage ws options.MaxMessageSizeBytes token

                                        try
                                            if count = -1 then
                                                current <- Error Closed
                                                finished <- true
                                                return true
                                            else
                                                current <- parseFrame buffer count
                                                return true
                                        finally
                                            ArrayPool<byte>.Shared.Return buffer
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
