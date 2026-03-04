module FSharp.ATProto.Streaming.Tests.FirehoseTests

open System.Formats.Cbor
open Expecto
open FSharp.ATProto.Streaming
open FSharp.ATProto.DRISL
open FSharp.ATProto.Syntax

let private cidTag : CborTag = LanguagePrimitives.EnumOfValue 42UL

/// Build a CBOR frame header + payload pair.
let private buildFrame (op : int) (t : string option) (payloadWriter : CborWriter -> unit) : byte[] =
    // Header
    let hw = CborWriter (CborConformanceMode.Lax)
    let fieldCount = if t.IsSome then 2 else 1
    hw.WriteStartMap (System.Nullable fieldCount)
    hw.WriteTextString "op"
    hw.WriteInt64 (int64 op)

    match t with
    | Some typ ->
        hw.WriteTextString "t"
        hw.WriteTextString typ
    | None -> ()

    hw.WriteEndMap ()
    let headerBytes = hw.Encode ()

    // Payload
    let pw = CborWriter (CborConformanceMode.Lax)
    payloadWriter pw
    let payloadBytes = pw.Encode ()

    Array.concat [| headerBytes ; payloadBytes |]

/// Build a fake CID bytes for use in test data.
let private fakeCidBytes () : byte[] =
    let hash = Array.zeroCreate 32 // All zeros
    hash.[0] <- 0xABuy

    Array.concat
        [| Varint.encode 1UL
           Varint.encode 0x71UL
           Varint.encode 0x12UL
           Varint.encode 0x20UL
           hash |]

/// Build a minimal CAR bytes for testing.
let private buildTestCar () : byte[] =
    let cidBytes = fakeCidBytes ()
    let blockData = [| 0x01uy ; 0x02uy |]

    // Header CBOR
    let hw = CborWriter (CborConformanceMode.Lax)
    hw.WriteStartMap (System.Nullable 2)
    hw.WriteTextString "version"
    hw.WriteInt32 1
    hw.WriteTextString "roots"
    hw.WriteStartArray (System.Nullable 1)
    hw.WriteTag cidTag
    hw.WriteByteString (Array.concat [| [| 0uy |] ; cidBytes |])
    hw.WriteEndArray ()
    hw.WriteEndMap ()
    let headerCbor = hw.Encode ()
    let headerVarint = Varint.encode (uint64 headerCbor.Length)

    // Block
    let blockContent = Array.concat [| cidBytes ; blockData |]
    let blockVarint = Varint.encode (uint64 blockContent.Length)

    Array.concat [| headerVarint ; headerCbor ; blockVarint ; blockContent |]

let private writeCidLink (writer : CborWriter) (cidBytes : byte[]) =
    writer.WriteTag cidTag
    writer.WriteByteString (Array.concat [| [| 0uy |] ; cidBytes |])

[<Tests>]
let firehoseParseTests =
    testList
        "Firehose.parseFrame"
        [
          // ── Commit events ──────────────────────────────────────────────

          test "parses commit frame" {
              let carBytes = buildTestCar ()
              let fakeCid = fakeCidBytes ()

              let frame =
                  buildFrame 1 (Some "#commit") (fun pw ->
                      pw.WriteStartMap (System.Nullable 7)
                      pw.WriteTextString "seq"
                      pw.WriteInt64 42L
                      pw.WriteTextString "repo"
                      pw.WriteTextString "did:plc:abc123"
                      pw.WriteTextString "rev"
                      pw.WriteTextString "3l3qo2vutsw2b"
                      pw.WriteTextString "since"
                      pw.WriteNull ()
                      pw.WriteTextString "time"
                      pw.WriteTextString "2024-09-09T19:46:02.102Z"
                      pw.WriteTextString "blocks"
                      pw.WriteByteString carBytes
                      pw.WriteTextString "ops"
                      pw.WriteStartArray (System.Nullable 1)
                      // RepoOp
                      pw.WriteStartMap (System.Nullable 3)
                      pw.WriteTextString "action"
                      pw.WriteTextString "create"
                      pw.WriteTextString "path"
                      pw.WriteTextString "app.bsky.feed.post/3l3qo2vuowo2b"
                      pw.WriteTextString "cid"
                      writeCidLink pw fakeCid
                      pw.WriteEndMap ()
                      pw.WriteEndArray ()
                      pw.WriteEndMap ())

              match Firehose.parseFrame frame frame.Length with
              | Ok (FirehoseCommitEvent commit) ->
                  Expect.equal commit.Seq 42L "seq"
                  Expect.equal (Did.value commit.Repo) "did:plc:abc123" "repo"
                  Expect.equal commit.Rev "3l3qo2vutsw2b" "rev"
                  Expect.isNone commit.Since "since is null"
                  Expect.equal commit.Time "2024-09-09T19:46:02.102Z" "time"
                  Expect.equal commit.Ops.Length 1 "one op"
                  Expect.equal commit.Ops.[0].Action Create "op action"
                  Expect.equal commit.Ops.[0].Path "app.bsky.feed.post/3l3qo2vuowo2b" "op path"
                  Expect.isSome commit.Ops.[0].Cid "op has CID"
                  Expect.isTrue (commit.Blocks.Blocks.Count > 0) "has CAR blocks"
              | other -> failtest $"Expected FirehoseCommitEvent, got {other}"
          }

          test "parses commit with delete op (null CID)" {
              let carBytes = buildTestCar ()

              let frame =
                  buildFrame 1 (Some "#commit") (fun pw ->
                      pw.WriteStartMap (System.Nullable 7)
                      pw.WriteTextString "seq"
                      pw.WriteInt64 100L
                      pw.WriteTextString "repo"
                      pw.WriteTextString "did:plc:xyz789"
                      pw.WriteTextString "rev"
                      pw.WriteTextString "rev1"
                      pw.WriteTextString "since"
                      pw.WriteTextString "rev0"
                      pw.WriteTextString "time"
                      pw.WriteTextString "2024-01-01T00:00:00Z"
                      pw.WriteTextString "blocks"
                      pw.WriteByteString carBytes
                      pw.WriteTextString "ops"
                      pw.WriteStartArray (System.Nullable 1)
                      pw.WriteStartMap (System.Nullable 3)
                      pw.WriteTextString "action"
                      pw.WriteTextString "delete"
                      pw.WriteTextString "path"
                      pw.WriteTextString "app.bsky.feed.like/abc"
                      pw.WriteTextString "cid"
                      pw.WriteNull ()
                      pw.WriteEndMap ()
                      pw.WriteEndArray ()
                      pw.WriteEndMap ())

              match Firehose.parseFrame frame frame.Length with
              | Ok (FirehoseCommitEvent commit) ->
                  Expect.equal commit.Since (Some "rev0") "since present"
                  Expect.equal commit.Ops.[0].Action Delete "delete op"
                  Expect.isNone commit.Ops.[0].Cid "null CID for delete"
              | other -> failtest $"Expected FirehoseCommitEvent, got {other}"
          }

          // ── Identity events ────────────────────────────────────────────

          test "parses identity frame with handle" {
              let frame =
                  buildFrame 1 (Some "#identity") (fun pw ->
                      pw.WriteStartMap (System.Nullable 4)
                      pw.WriteTextString "seq"
                      pw.WriteInt64 200L
                      pw.WriteTextString "did"
                      pw.WriteTextString "did:plc:abc123"
                      pw.WriteTextString "handle"
                      pw.WriteTextString "user.bsky.social"
                      pw.WriteTextString "time"
                      pw.WriteTextString "2024-06-01T12:00:00Z"
                      pw.WriteEndMap ())

              match Firehose.parseFrame frame frame.Length with
              | Ok (FirehoseIdentityEvent identity) ->
                  Expect.equal identity.Seq 200L "seq"
                  Expect.equal (Did.value identity.Did) "did:plc:abc123" "did"
                  Expect.isSome identity.Handle "handle present"
                  Expect.equal (Handle.value identity.Handle.Value) "user.bsky.social" "handle"
                  Expect.equal identity.Time "2024-06-01T12:00:00Z" "time"
              | other -> failtest $"Expected FirehoseIdentityEvent, got {other}"
          }

          test "parses identity frame without handle" {
              let frame =
                  buildFrame 1 (Some "#identity") (fun pw ->
                      pw.WriteStartMap (System.Nullable 3)
                      pw.WriteTextString "seq"
                      pw.WriteInt64 201L
                      pw.WriteTextString "did"
                      pw.WriteTextString "did:plc:abc123"
                      pw.WriteTextString "time"
                      pw.WriteTextString "2024-06-01T12:00:00Z"
                      pw.WriteEndMap ())

              match Firehose.parseFrame frame frame.Length with
              | Ok (FirehoseIdentityEvent identity) ->
                  Expect.isNone identity.Handle "no handle"
              | other -> failtest $"Expected FirehoseIdentityEvent, got {other}"
          }

          // ── Account events ─────────────────────────────────────────────

          test "parses account frame active" {
              let frame =
                  buildFrame 1 (Some "#account") (fun pw ->
                      pw.WriteStartMap (System.Nullable 5)
                      pw.WriteTextString "seq"
                      pw.WriteInt64 300L
                      pw.WriteTextString "did"
                      pw.WriteTextString "did:plc:abc123"
                      pw.WriteTextString "active"
                      pw.WriteBoolean true
                      pw.WriteTextString "status"
                      pw.WriteTextString "active"
                      pw.WriteTextString "time"
                      pw.WriteTextString "2024-07-01T00:00:00Z"
                      pw.WriteEndMap ())

              match Firehose.parseFrame frame frame.Length with
              | Ok (FirehoseAccountEvent account) ->
                  Expect.equal account.Seq 300L "seq"
                  Expect.equal (Did.value account.Did) "did:plc:abc123" "did"
                  Expect.isTrue account.Active "active"
                  Expect.equal account.Status (Some "active") "status"
                  Expect.equal account.Time "2024-07-01T00:00:00Z" "time"
              | other -> failtest $"Expected FirehoseAccountEvent, got {other}"
          }

          test "parses account frame deactivated" {
              let frame =
                  buildFrame 1 (Some "#account") (fun pw ->
                      pw.WriteStartMap (System.Nullable 4)
                      pw.WriteTextString "seq"
                      pw.WriteInt64 301L
                      pw.WriteTextString "did"
                      pw.WriteTextString "did:plc:abc123"
                      pw.WriteTextString "active"
                      pw.WriteBoolean false
                      pw.WriteTextString "time"
                      pw.WriteTextString "2024-07-01T00:00:00Z"
                      pw.WriteEndMap ())

              match Firehose.parseFrame frame frame.Length with
              | Ok (FirehoseAccountEvent account) ->
                  Expect.isFalse account.Active "not active"
                  Expect.isNone account.Status "no status"
              | other -> failtest $"Expected FirehoseAccountEvent, got {other}"
          }

          // ── Info events ────────────────────────────────────────────────

          test "parses info frame" {
              let frame =
                  buildFrame 1 (Some "#info") (fun pw ->
                      pw.WriteStartMap (System.Nullable 2)
                      pw.WriteTextString "name"
                      pw.WriteTextString "OutdatedCursor"
                      pw.WriteTextString "message"
                      pw.WriteTextString "Cursor is too old"
                      pw.WriteEndMap ())

              match Firehose.parseFrame frame frame.Length with
              | Ok (FirehoseInfoEvent info) ->
                  Expect.equal info.Name "OutdatedCursor" "name"
                  Expect.equal info.Message (Some "Cursor is too old") "message"
              | other -> failtest $"Expected FirehoseInfoEvent, got {other}"
          }

          test "parses info frame without message" {
              let frame =
                  buildFrame 1 (Some "#info") (fun pw ->
                      pw.WriteStartMap (System.Nullable 1)
                      pw.WriteTextString "name"
                      pw.WriteTextString "OutdatedCursor"
                      pw.WriteEndMap ())

              match Firehose.parseFrame frame frame.Length with
              | Ok (FirehoseInfoEvent info) ->
                  Expect.equal info.Name "OutdatedCursor" "name"
                  Expect.isNone info.Message "no message"
              | other -> failtest $"Expected FirehoseInfoEvent, got {other}"
          }

          // ── Error frames ───────────────────────────────────────────────

          test "parses error frame" {
              let frame =
                  buildFrame -1 None (fun pw ->
                      pw.WriteStartMap (System.Nullable 2)
                      pw.WriteTextString "error"
                      pw.WriteTextString "FutureCursor"
                      pw.WriteTextString "message"
                      pw.WriteTextString "Cursor is in the future"
                      pw.WriteEndMap ())

              match Firehose.parseFrame frame frame.Length with
              | Ok (FirehoseErrorEvent (error, message)) ->
                  Expect.equal error "FutureCursor" "error"
                  Expect.equal message (Some "Cursor is in the future") "message"
              | other -> failtest $"Expected FirehoseErrorEvent, got {other}"
          }

          // ── Unknown types ──────────────────────────────────────────────

          test "parses unknown message type" {
              let frame =
                  buildFrame 1 (Some "#newtype") (fun pw ->
                      pw.WriteStartMap (System.Nullable 0)
                      pw.WriteEndMap ())

              match Firehose.parseFrame frame frame.Length with
              | Ok (FirehoseUnknownEvent kind) ->
                  Expect.equal kind "#newtype" "unknown kind"
              | other -> failtest $"Expected FirehoseUnknownEvent, got {other}"
          }

          // ── Error cases ────────────────────────────────────────────────

          test "returns error for truncated frame" {
              match Firehose.parseFrame [| 0xA1uy |] 1 with
              | Error (DeserializationError _) -> ()
              | other -> failtest $"Expected DeserializationError, got {other}"
          }

          test "returns error for missing type field" {
              // op=1 but no t field
              let frame =
                  buildFrame 1 None (fun pw ->
                      pw.WriteStartMap (System.Nullable 0)
                      pw.WriteEndMap ())

              match Firehose.parseFrame frame frame.Length with
              | Error (DeserializationError msg) ->
                  Expect.stringContains msg "type" "mentions type"
              | other -> failtest $"Expected DeserializationError, got {other}"
          }
        ]

[<Tests>]
let firehoseBuildUriTests =
    testList
        "Firehose.buildUri"
        [
          test "default options produce base endpoint" {
              let uri = Firehose.buildUri Firehose.defaultOptions
              Expect.equal (uri.ToString ()) "wss://bsky.network/xrpc/com.atproto.sync.subscribeRepos" "base uri"
          }

          test "cursor appended as query param" {
              let opts =
                  { Firehose.defaultOptions with
                      Cursor = Some 12345L }

              let uri = Firehose.buildUri opts
              Expect.stringContains (uri.ToString ()) "cursor=12345" "cursor param"
          }
        ]
