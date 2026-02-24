namespace FSharp.ATProto.DRISL

open FSharp.ATProto.Syntax

/// The AT Protocol data model. Represents all values that can be encoded in DRISL-CBOR.
[<RequireQualifiedAccess>]
type AtpValue =
    | Null
    | Bool of bool
    | Integer of int64
    | String of string
    | Bytes of byte[]
    | Link of Cid
    | Array of AtpValue list
    | Object of Map<string, AtpValue>
