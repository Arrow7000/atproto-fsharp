namespace FSharp.ATProto.DRISL

/// RFC 4648 Base32 encoding, lowercase alphabet, no padding.
module Base32 =
    let private alphabet = "abcdefghijklmnopqrstuvwxyz234567"

    /// Encode bytes to base32 lowercase string (no padding).
    let encode (data : byte[]) : string =
        if data.Length = 0 then
            ""
        else
            let sb = System.Text.StringBuilder ()
            let mutable buffer = 0u
            let mutable bits = 0

            for b in data do
                buffer <- (buffer <<< 8) ||| uint32 b
                bits <- bits + 8

                while bits >= 5 do
                    bits <- bits - 5
                    sb.Append (alphabet.[int ((buffer >>> bits) &&& 0x1Fu)]) |> ignore
                    buffer <- buffer &&& ((1u <<< bits) - 1u)

            if bits > 0 then
                sb.Append (alphabet.[int ((buffer <<< (5 - bits)) &&& 0x1Fu)]) |> ignore

            sb.ToString ()

    /// Decode a base32 lowercase string (no padding) to bytes.
    let decode (s : string) : byte[] =
        if s.Length = 0 then
            [||]
        else
            let output = System.Collections.Generic.List<byte> ()
            let mutable buffer = 0u
            let mutable bits = 0

            for c in s do
                let value =
                    if c >= 'a' && c <= 'z' then int c - int 'a'
                    elif c >= '2' && c <= '7' then int c - int '2' + 26
                    else failwithf "Invalid base32 character: %c" c

                buffer <- (buffer <<< 5) ||| uint32 value
                bits <- bits + 5

                if bits >= 8 then
                    bits <- bits - 8
                    output.Add (byte ((buffer >>> bits) &&& 0xFFu))
                    buffer <- buffer &&& ((1u <<< bits) - 1u)

            output.ToArray ()
