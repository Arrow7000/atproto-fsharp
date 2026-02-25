namespace FSharp.ATProto.Lexicon

open FSharp.ATProto.Syntax

/// String format constraint for string-typed fields.
type LexStringFormat =
    | Did
    | Handle
    | AtIdentifier
    | AtUri
    | Nsid
    | Cid
    | Datetime
    | Language
    | Uri
    | Tid
    | RecordKey

/// Boolean field constraints.
type LexBoolean =
    { Description: string option
      Default: bool option
      Const: bool option }

/// Integer field constraints.
type LexInteger =
    { Description: string option
      Default: int64 option
      Const: int64 option
      Enum: int64 list option
      Minimum: int64 option
      Maximum: int64 option }

/// String field constraints.
type LexString =
    { Description: string option
      Default: string option
      Const: string option
      Enum: string list option
      KnownValues: string list option
      Format: LexStringFormat option
      MinLength: int option
      MaxLength: int option
      MinGraphemes: int option
      MaxGraphemes: int option }

/// Bytes field constraints.
type LexBytes =
    { Description: string option
      MinLength: int option
      MaxLength: int option }

/// Blob field constraints.
type LexBlob =
    { Description: string option
      Accept: string list option
      MaxSize: int64 option }

/// A field type in a Lexicon schema. Recursive (arrays/objects contain nested types).
type LexType =
    | Boolean of LexBoolean
    | Integer of LexInteger
    | String of LexString
    | Bytes of LexBytes
    | CidLink
    | Blob of LexBlob
    | Array of LexArray
    | Object of LexObject
    | Params of LexParams
    | Ref of LexRef
    | Union of LexUnion
    | Unknown

/// Array field: homogeneous typed elements with length constraints.
and LexArray =
    { Description: string option
      Items: LexType
      MinLength: int option
      MaxLength: int option }

/// Object type: named properties with required/nullable lists.
and LexObject =
    { Description: string option
      Properties: Map<string, LexType>
      Required: string list
      Nullable: string list }

/// Query/procedure parameter object (restricted to boolean, integer, string, unknown, array).
and LexParams =
    { Description: string option
      Properties: Map<string, LexType>
      Required: string list }

/// Reference to another type definition.
and LexRef =
    { Description: string option
      Ref: string }

/// Union of multiple referenced types, discriminated by $type at runtime.
and LexUnion =
    { Description: string option
      Refs: string list
      Closed: bool }

/// An XRPC error response.
type LexError =
    { Name: string
      Description: string option }

/// A token definition (string constant).
type LexToken =
    { Description: string option }

/// HTTP body schema for query output / procedure input+output.
type LexBody =
    { Description: string option
      Encoding: string
      Schema: LexType option }

/// Subscription message schema (must be a union).
type LexSubscriptionMessage =
    { Description: string option
      Schema: LexUnion }

/// Record definition: data stored in AT Protocol repos.
type LexRecord =
    { Key: string
      Description: string option
      Record: LexObject }

/// Query definition: GET XRPC endpoint.
type LexQuery =
    { Description: string option
      Parameters: LexParams option
      Output: LexBody option
      Errors: LexError list }

/// Procedure definition: POST XRPC endpoint.
type LexProcedure =
    { Description: string option
      Parameters: LexParams option
      Input: LexBody option
      Output: LexBody option
      Errors: LexError list }

/// Subscription definition: WebSocket event stream.
type LexSubscription =
    { Description: string option
      Parameters: LexParams option
      Message: LexSubscriptionMessage option
      Errors: LexError list }

/// A single permission entry in a permission set.
type LexPermission =
    { Resource: string
      Collection: string list
      Action: string list
      Lxm: string list
      Aud: string option
      InheritAud: bool option }

/// Permission set definition: OAuth scope bundle.
type LexPermissionSet =
    { Title: string
      TitleLang: Map<string, string>
      Detail: string option
      DetailLang: Map<string, string>
      Permissions: LexPermission list }

/// A named definition within a Lexicon document.
type LexDef =
    | Record of LexRecord
    | Query of LexQuery
    | Procedure of LexProcedure
    | Subscription of LexSubscription
    | Token of LexToken
    | PermissionSet of LexPermissionSet
    | DefType of LexType

/// A complete Lexicon document (one per NSID).
type LexiconDoc =
    { Lexicon: int
      Id: Nsid
      Revision: int option
      Description: string option
      Defs: Map<string, LexDef> }
