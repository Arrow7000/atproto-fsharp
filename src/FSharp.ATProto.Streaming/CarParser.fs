namespace FSharp.ATProto.Streaming

open System.Formats.Cbor
open FSharp.ATProto.Syntax
open FSharp.ATProto.DRISL

/// Parser for CAR v1 (Content Addressable aRchive) files used by the AT Protocol firehose.
module CarParser =

    let private sha256Code = 0x12uy
    let private sha256DigestLen = 0x20uy
    let private cidTag : CborTag = LanguagePrimitives.EnumOfValue 42UL

    /// Read a CID from raw bytes at the given offset.
    /// Returns (CID string key, bytesConsumed).
    let readCidBytes (data : byte[]) (offset : int) : Result<string * int, string> =
        try
            // CIDv0 detection: starts with 0x12 0x20 (SHA2-256 multihash)
            if
                offset + 1 < data.Length
                && data.[offset] = sha256Code
                && data.[offset + 1] = sha256DigestLen
            then
                let cidLen = 34 // 2 prefix + 32 digest

                if offset + cidLen > data.Length then
                    Error "Unexpected end of CIDv0 data"
                else
                    let cidBytes = data.[offset .. offset + cidLen - 1]
                    let encoded = "b" + Base32.encode cidBytes
                    Ok (encoded, cidLen)
            else
                // CIDv1: version varint + codec varint + multihash
                let mutable pos = offset
                let (version, vLen) = Varint.decode data pos
                pos <- pos + vLen

                if version <> 1UL then
                    Error $"Unsupported CID version: {version}"
                else
                    let (_codec, cLen) = Varint.decode data pos
                    pos <- pos + cLen
                    // multihash: hash-code varint + digest-length varint + digest
                    let (_hashCode, hLen) = Varint.decode data pos
                    pos <- pos + hLen
                    let (digestLen, dLenLen) = Varint.decode data pos
                    pos <- pos + dLenLen

                    let endPos = pos + int digestLen

                    if endPos > data.Length then
                        Error "Unexpected end of CID digest data"
                    else
                        let totalLen = endPos - offset
                        let cidBytes = data.[offset .. endPos - 1]
                        let encoded = "b" + Base32.encode cidBytes
                        Ok (encoded, totalLen)
        with ex ->
            Error $"Failed to read CID: {ex.Message}"

    /// Read a CID from a CBOR reader (DAG-CBOR tag 42 link).
    /// Returns the CID string key.
    let readCborCidString (reader : CborReader) : Result<string, string> =
        try
            let tag = reader.ReadTag ()

            if tag <> cidTag then
                Error $"Expected CBOR tag 42, got {tag}"
            else
                let bytes = reader.ReadByteString ()
                // DAG-CBOR CID links: byte string starts with 0x00 (identity multibase prefix)
                if bytes.Length < 2 || bytes.[0] <> 0uy then
                    Error "Invalid DAG-CBOR CID link: missing 0x00 prefix"
                else
                    let cidBytes = bytes.[1..]
                    readCidBytes cidBytes 0 |> Result.map fst
        with ex ->
            Error $"Failed to read CBOR CID: {ex.Message}"

    /// Read a CID from a CBOR reader and parse it as a Cid value.
    let readCborCid (reader : CborReader) : Result<Cid, string> =
        readCborCidString reader
        |> Result.bind (fun s ->
            match Cid.parse s with
            | Ok cid -> Ok cid
            | Error e -> Error $"Invalid CID string '{s}': {e}")

    /// Parse a CAR v1 byte array into a CarFile.
    let parse (data : byte[]) : Result<CarFile, string> =
        try
            let mutable offset = 0

            // Read header varint (length of CBOR header)
            let (headerLen, hVarLen) = Varint.decode data offset
            offset <- offset + hVarLen

            // Decode CBOR header
            let headerBytes = data.[offset .. offset + int headerLen - 1]
            offset <- offset + int headerLen

            let headerReader =
                CborReader (System.ReadOnlyMemory<byte> headerBytes, CborConformanceMode.Lax)

            let mutable version = 0
            let roots = System.Collections.Generic.List<Cid> ()

            let mapLen = headerReader.ReadStartMap ()

            let mapEntries =
                match mapLen |> System.Nullable.op_Explicit with
                | 0 -> 0
                | n -> n

            for _ in 0 .. mapEntries - 1 do
                let key = headerReader.ReadTextString ()

                match key with
                | "version" -> version <- headerReader.ReadInt32 ()
                | "roots" ->
                    let arrayLen = headerReader.ReadStartArray ()

                    let rootCount =
                        match arrayLen |> System.Nullable.op_Explicit with
                        | 0 -> 0
                        | n -> n

                    for _ in 0 .. rootCount - 1 do
                        match readCborCid headerReader with
                        | Ok cid -> roots.Add cid
                        | Error e -> failwith $"Failed to read root CID: {e}"

                    headerReader.ReadEndArray ()
                | _ -> headerReader.SkipValue ()

            headerReader.ReadEndMap ()

            if version <> 1 then
                Error $"Unsupported CAR version: {version}"
            else
                // Parse blocks
                let blocks = System.Collections.Generic.Dictionary<string, byte[]> ()

                while offset < data.Length do
                    let (blockLen, bVarLen) = Varint.decode data offset
                    offset <- offset + bVarLen

                    if int blockLen = 0 then
                        ()
                    else
                        let blockEnd = offset + int blockLen

                        if blockEnd > data.Length then
                            failwith $"Block extends past end of data: {blockEnd} > {data.Length}"

                        match readCidBytes data offset with
                        | Ok (cidKey, cidLen) ->
                            let dataStart = offset + cidLen
                            let dataBytes = data.[dataStart .. blockEnd - 1]
                            blocks.[cidKey] <- dataBytes
                            offset <- blockEnd
                        | Error e -> failwith $"Failed to read block CID: {e}"

                Ok
                    { Roots = roots |> Seq.toList
                      Blocks = blocks |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq }
        with ex ->
            Error $"Failed to parse CAR file: {ex.Message}"
