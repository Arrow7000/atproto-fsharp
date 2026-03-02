namespace FSharp.ATProto.DRISL

/// Unsigned LEB128 varint encoding/decoding for CID binary format.
module Varint =

    /// Encode a uint64 as unsigned LEB128 bytes.
    let encode (value : uint64) : byte[] =
        if value < 0x80UL then
            [| byte value |]
        else
            let result = System.Collections.Generic.List<byte> ()
            let mutable v = value

            while v >= 0x80UL do
                result.Add (byte (v &&& 0x7FUL) ||| 0x80uy)
                v <- v >>> 7

            result.Add (byte v)
            result.ToArray ()

    /// Decode an unsigned LEB128 varint from data at the given offset.
    /// Returns (value, bytesConsumed).
    let decode (data : byte[]) (offset : int) : uint64 * int =
        let mutable result = 0UL
        let mutable shift = 0
        let mutable i = offset
        let mutable cont = true

        while cont do
            if i >= data.Length then
                failwith "Unexpected end of varint data"

            let b = data.[i]
            result <- result ||| ((uint64 (b &&& 0x7Fuy)) <<< shift)
            i <- i + 1

            if b &&& 0x80uy = 0uy then
                cont <- false
            else
                shift <- shift + 7

                if shift > 63 then
                    failwith "Varint too long"

        (result, i - offset)
