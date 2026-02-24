namespace FSharp.ATProto.DRISL

open FSharp.ATProto.Syntax

/// Binary CID operations: parse, construct, compute.
/// Supports CIDv1 with dag-cbor (0x71) or raw (0x55) codec and SHA-256 hash.
module CidBinary =

    let private dagCborCodec = 0x71UL
    let private rawCodec = 0x55UL
    let private sha256Code = 0x12UL
    let private sha256DigestLen = 0x20UL

    /// Compute a CIDv1 (dag-cbor + SHA-256) from raw data bytes.
    let compute (data: byte[]) : Cid =
        use sha = System.Security.Cryptography.SHA256.Create()
        let hash = sha.ComputeHash(data)
        let binary = Array.concat [|
            Varint.encode 1UL
            Varint.encode dagCborCodec
            Varint.encode sha256Code
            Varint.encode sha256DigestLen
            hash
        |]
        let encoded = "b" + Base32.encode binary
        Cid.fromValidated encoded

    /// Convert a CID string to its raw binary representation.
    let toBytes (cid: Cid) : byte[] =
        let s = Cid.value cid
        if s.Length < 2 || s.[0] <> 'b' then
            failwith "Expected multibase 'b' prefix on CID string"
        Base32.decode (s.Substring(1))

    /// Parse raw binary bytes as a CID, validating structure.
    let fromBytes (data: byte[]) : Result<Cid, string> =
        try
            let mutable offset = 0
            let readVarint () =
                let (v, len) = Varint.decode data offset
                offset <- offset + len
                v
            let version = readVarint ()
            if version <> 1UL then
                Error (sprintf "Unsupported CID version: %d" version)
            else
                let codec = readVarint ()
                if codec <> dagCborCodec && codec <> rawCodec then
                    Error (sprintf "Unsupported CID codec: 0x%x" codec)
                else
                    let hashFn = readVarint ()
                    if hashFn <> sha256Code then
                        Error (sprintf "Unsupported hash function: 0x%x" hashFn)
                    else
                        let digestLen = readVarint ()
                        if digestLen <> sha256DigestLen then
                            Error (sprintf "Invalid SHA-256 digest length: %d" digestLen)
                        else
                            let remaining = data.Length - offset
                            if remaining <> int digestLen then
                                Error (sprintf "Expected %d digest bytes, got %d" digestLen remaining)
                            else
                                let encoded = "b" + Base32.encode data
                                Ok (Cid.fromValidated encoded)
        with ex ->
            Error ex.Message
