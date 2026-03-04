namespace FSharp.ATProto.Streaming

open System.Text.Json
open FSharp.ATProto.Syntax

/// Errors that can occur during Jetstream event streaming.
type StreamError =
    | ConnectionFailed of message : string
    | WebSocketError of message : string
    | DeserializationError of message : string
    | Closed

/// The operation type within a Jetstream commit event.
type CommitOperation =
    | Create
    | Update
    | Delete

/// Details of a repository commit from a Jetstream event.
type CommitInfo =
    { Rev : string
      Operation : CommitOperation
      Collection : string
      Rkey : string
      Record : JsonElement option
      Cid : string option }

/// Identity change information from a Jetstream event.
type IdentityInfo =
    { Did : Did
      Handle : Handle option }

/// Account status change information from a Jetstream event.
type AccountInfo =
    { Active : bool
      Status : string option }

/// A parsed Jetstream event.
type JetstreamEvent =
    | CommitEvent of did : Did * timeUs : int64 * commit : CommitInfo
    | IdentityEvent of did : Did * timeUs : int64 * identity : IdentityInfo
    | AccountEvent of did : Did * timeUs : int64 * account : AccountInfo
    | UnknownEvent of did : Did * timeUs : int64 * kind : string

/// Options for configuring a Jetstream subscription.
type JetstreamOptions =
    { Endpoint : string
      WantedCollections : string list
      WantedDids : string list
      Cursor : int64 option
      MaxMessageSizeBytes : int }

// ── CAR file types ───────────────────────────────────────────────────────

/// A parsed CAR v1 file.
type CarFile =
    { Roots : Cid list
      Blocks : Map<string, byte[]> }

// ── Firehose types ───────────────────────────────────────────────────────

/// A record-level operation within a firehose commit.
type RepoOp =
    { Action : CommitOperation
      Path : string
      Cid : Cid option }

/// A firehose commit event with repository operations and CAR block data.
type FirehoseCommit =
    { Seq : int64
      Repo : Did
      Rev : string
      Since : string option
      Ops : RepoOp list
      Blocks : CarFile
      Time : string }

/// A firehose identity event.
type FirehoseIdentity =
    { Seq : int64
      Did : Did
      Handle : Handle option
      Time : string }

/// A firehose account status event.
type FirehoseAccount =
    { Seq : int64
      Did : Did
      Active : bool
      Status : string option
      Time : string }

/// An informational firehose message (e.g., OutdatedCursor).
type FirehoseInfo =
    { Name : string
      Message : string option }

/// A parsed firehose event from com.atproto.sync.subscribeRepos.
type FirehoseEvent =
    | FirehoseCommitEvent of FirehoseCommit
    | FirehoseIdentityEvent of FirehoseIdentity
    | FirehoseAccountEvent of FirehoseAccount
    | FirehoseInfoEvent of FirehoseInfo
    | FirehoseErrorEvent of error : string * message : string option
    | FirehoseUnknownEvent of kind : string

/// Options for configuring a firehose subscription.
type FirehoseOptions =
    { Endpoint : string
      Cursor : int64 option
      MaxMessageSizeBytes : int }
