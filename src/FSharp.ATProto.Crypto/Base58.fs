namespace FSharp.ATProto.Crypto

open System

/// Base58btc encoding and decoding using the Bitcoin alphabet.
/// Used by multibase (prefix 'z') for encoding public keys in did:key and multikey formats.
module Base58 =

    let private alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz"

    let private alphabetMap =
        let map = Array.create 128 -1
        for i in 0 .. alphabet.Length - 1 do
            map.[int alphabet.[i]] <- i
        map

    /// Encode bytes to base58btc string.
    let encode (input : byte[]) : string =
        if input.Length = 0 then
            ""
        else
            // Count leading zeros
            let mutable leadingZeros = 0
            while leadingZeros < input.Length && input.[leadingZeros] = 0uy do
                leadingZeros <- leadingZeros + 1

            // Allocate enough space (log(256)/log(58) ≈ 1.366)
            let size = (input.Length - leadingZeros) * 138 / 100 + 1
            let b58 = Array.zeroCreate<int> size
            let mutable length = 0

            for i in leadingZeros .. input.Length - 1 do
                let mutable carry = int input.[i]
                let mutable j = 0
                let mutable k = size - 1
                while (carry <> 0 || j < length) && k >= 0 do
                    carry <- carry + 256 * b58.[k]
                    b58.[k] <- carry % 58
                    carry <- carry / 58
                    j <- j + 1
                    k <- k - 1
                length <- j

            // Skip leading zeros in base58 output
            let mutable start = size - length
            while start < size && b58.[start] = 0 do
                start <- start + 1

            let chars = Array.zeroCreate<char> (leadingZeros + size - start)
            for i in 0 .. leadingZeros - 1 do
                chars.[i] <- '1'
            for i in start .. size - 1 do
                chars.[leadingZeros + i - start] <- alphabet.[b58.[i]]

            String chars

    /// Decode a base58btc string to bytes. Returns Error on invalid input.
    let decode (input : string) : Result<byte[], string> =
        if String.IsNullOrEmpty input then
            Ok Array.empty
        else
            // Count leading '1's (represent zero bytes)
            let mutable leadingOnes = 0
            while leadingOnes < input.Length && input.[leadingOnes] = '1' do
                leadingOnes <- leadingOnes + 1

            // Allocate enough space (log(58)/log(256) ≈ 0.733)
            let size = (input.Length - leadingOnes) * 733 / 1000 + 1
            let b256 = Array.zeroCreate<int> size
            let mutable length = 0

            let mutable error = false
            let mutable i = leadingOnes
            while i < input.Length && not error do
                let c = int input.[i]
                if c >= 128 || alphabetMap.[c] = -1 then
                    error <- true
                else
                    let mutable carry = alphabetMap.[c]
                    let mutable j = 0
                    let mutable k = size - 1
                    while (carry <> 0 || j < length) && k >= 0 do
                        carry <- carry + 58 * b256.[k]
                        b256.[k] <- carry % 256
                        carry <- carry / 256
                        j <- j + 1
                        k <- k - 1
                    length <- j
                i <- i + 1

            if error then
                Error (sprintf "Invalid base58 character at position %d" (i - 1))
            else
                // Skip leading zeros in b256 output
                let mutable start = size - length
                while start < size && b256.[start] = 0 do
                    start <- start + 1

                let result = Array.zeroCreate<byte> (leadingOnes + size - start)
                // Leading '1's are already zero bytes
                for j in start .. size - 1 do
                    result.[leadingOnes + j - start] <- byte b256.[j]

                Ok result
