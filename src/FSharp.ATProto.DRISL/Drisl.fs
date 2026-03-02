namespace FSharp.ATProto.DRISL

open System
open System.Formats.Cbor
open FSharp.ATProto.Syntax

/// DRISL-CBOR encoding and decoding for AT Protocol data model.
module Drisl =

    let private cidTag : CborTag = LanguagePrimitives.EnumOfValue 42UL

    /// Compare two map keys in DRISL sort order (length-first, then lexicographic on UTF-8 bytes).
    let private compareKeys (a : string) (b : string) =
        let aBytes = Text.Encoding.UTF8.GetBytes (a)
        let bBytes = Text.Encoding.UTF8.GetBytes (b)
        let lenCmp = compare aBytes.Length bBytes.Length
        if lenCmp <> 0 then lenCmp else compare aBytes bBytes

    /// Sort map keys in DRISL canonical order.
    let private sortKeys (keys : string seq) = keys |> Seq.sortWith compareKeys

    let rec private writeValue (writer : CborWriter) (value : AtpValue) =
        match value with
        | AtpValue.Null -> writer.WriteNull ()
        | AtpValue.Bool b -> writer.WriteBoolean (b)
        | AtpValue.Integer n -> writer.WriteInt64 (n)
        | AtpValue.String s -> writer.WriteTextString (s)
        | AtpValue.Bytes b -> writer.WriteByteString (b)
        | AtpValue.Link cid ->
            writer.WriteTag (cidTag)
            let cidBytes = CidBinary.toBytes cid
            let withPrefix = Array.append [| 0x00uy |] cidBytes
            writer.WriteByteString (withPrefix)
        | AtpValue.Array items ->
            writer.WriteStartArray (Nullable items.Length)

            for item in items do
                writeValue writer item

            writer.WriteEndArray ()
        | AtpValue.Object map ->
            writer.WriteStartMap (Nullable map.Count)
            let sorted = sortKeys (map |> Map.keys) |> Seq.toArray

            for key in sorted do
                writer.WriteTextString (key)
                writeValue writer (Map.find key map)

            writer.WriteEndMap ()

    /// Encode an AtpValue to canonical DRISL-CBOR bytes.
    let encode (value : AtpValue) : byte[] =
        let writer = CborWriter (CborConformanceMode.Canonical)
        writeValue writer value
        writer.Encode ()

    let rec private readValue (reader : CborReader) : AtpValue =
        match reader.PeekState () with
        | CborReaderState.Null ->
            reader.ReadNull ()
            AtpValue.Null
        | CborReaderState.Boolean -> AtpValue.Bool (reader.ReadBoolean ())
        | CborReaderState.UnsignedInteger
        | CborReaderState.NegativeInteger -> AtpValue.Integer (reader.ReadInt64 ())
        | CborReaderState.TextString -> AtpValue.String (reader.ReadTextString ())
        | CborReaderState.ByteString -> AtpValue.Bytes (reader.ReadByteString ())
        | CborReaderState.Tag ->
            let tag = reader.ReadTag ()

            if tag <> cidTag then
                failwithf "Unsupported CBOR tag: %d" (uint64 tag)

            let bytes = reader.ReadByteString ()

            if bytes.Length < 2 || bytes.[0] <> 0x00uy then
                failwith "Invalid CID in tag 42: missing 0x00 prefix"

            let cidBytes = bytes.[1..]

            match CidBinary.fromBytes cidBytes with
            | Ok cid -> AtpValue.Link cid
            | Error e -> failwithf "Invalid CID in tag 42: %s" e
        | CborReaderState.StartArray ->
            let _count = reader.ReadStartArray ()
            let items = System.Collections.Generic.List<AtpValue> ()

            while reader.PeekState () <> CborReaderState.EndArray do
                items.Add (readValue reader)

            reader.ReadEndArray ()
            AtpValue.Array (items |> Seq.toList)
        | CborReaderState.StartMap ->
            let _count = reader.ReadStartMap ()
            let mutable map = Map.empty

            while reader.PeekState () <> CborReaderState.EndMap do
                if reader.PeekState () <> CborReaderState.TextString then
                    failwith "DRISL map keys must be text strings"

                let key = reader.ReadTextString ()
                let value = readValue reader
                map <- Map.add key value map

            reader.ReadEndMap ()
            AtpValue.Object map
        | CborReaderState.HalfPrecisionFloat
        | CborReaderState.SinglePrecisionFloat
        | CborReaderState.DoublePrecisionFloat -> failwith "Floats are not allowed in DRISL"
        | state -> failwithf "Unexpected CBOR state: %A" state

    /// Decode DRISL-CBOR bytes to an AtpValue.
    let decode (data : byte[]) : Result<AtpValue, string> =
        try
            let reader = CborReader (ReadOnlyMemory (data), CborConformanceMode.Canonical)
            let result = readValue reader

            if reader.BytesRemaining > 0 then
                Error "Trailing bytes after CBOR value"
            else
                Ok result
        with ex ->
            Error ex.Message
