namespace FSharp.ATProto.DRISL

open System
open System.Formats.Cbor
open FSharp.ATProto.Syntax

/// DRISL-CBOR encoding and decoding for AT Protocol data model.
module Drisl =

    let private cidTag : CborTag = LanguagePrimitives.EnumOfValue 42UL

    /// Compare two map keys in DRISL sort order (length-first, then lexicographic on UTF-8 bytes).
    let private compareKeys (a: string) (b: string) =
        let aBytes = Text.Encoding.UTF8.GetBytes(a)
        let bBytes = Text.Encoding.UTF8.GetBytes(b)
        let lenCmp = compare aBytes.Length bBytes.Length
        if lenCmp <> 0 then lenCmp
        else compare aBytes bBytes

    /// Sort map keys in DRISL canonical order.
    let private sortKeys (keys: string seq) =
        keys |> Seq.sortWith compareKeys

    let rec private writeValue (writer: CborWriter) (value: AtpValue) =
        match value with
        | AtpValue.Null -> writer.WriteNull()
        | AtpValue.Bool b -> writer.WriteBoolean(b)
        | AtpValue.Integer n -> writer.WriteInt64(n)
        | AtpValue.String s -> writer.WriteTextString(s)
        | AtpValue.Bytes b -> writer.WriteByteString(b)
        | AtpValue.Link cid ->
            writer.WriteTag(cidTag)
            let cidBytes = CidBinary.toBytes cid
            let withPrefix = Array.append [| 0x00uy |] cidBytes
            writer.WriteByteString(withPrefix)
        | AtpValue.Array items ->
            writer.WriteStartArray(Nullable items.Length)
            for item in items do writeValue writer item
            writer.WriteEndArray()
        | AtpValue.Object map ->
            writer.WriteStartMap(Nullable map.Count)
            let sorted = sortKeys (map |> Map.keys) |> Seq.toArray
            for key in sorted do
                writer.WriteTextString(key)
                writeValue writer (Map.find key map)
            writer.WriteEndMap()

    /// Encode an AtpValue to canonical DRISL-CBOR bytes.
    let encode (value: AtpValue) : byte[] =
        let writer = CborWriter(CborConformanceMode.Canonical)
        writeValue writer value
        writer.Encode()
