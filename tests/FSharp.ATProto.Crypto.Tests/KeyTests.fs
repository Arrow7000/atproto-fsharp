module KeyTests

open Expecto
open FSharp.ATProto.Crypto

[<Tests>]
let tests =
    testList
        "Keys"
        [ testCase "generate P-256 key pair"
          <| fun () ->
              let kp = Keys.generate Algorithm.P256
              Expect.equal kp.Algorithm Algorithm.P256 "algorithm"
              Expect.equal kp.PrivateKeyBytes.Length 32 "private key 32 bytes"
              Expect.equal kp.CompressedPublicKey.Length 33 "compressed public key 33 bytes"
              Expect.isTrue (kp.CompressedPublicKey.[0] = 0x02uy || kp.CompressedPublicKey.[0] = 0x03uy) "valid prefix"

          testCase "generate K-256 key pair"
          <| fun () ->
              let kp = Keys.generate Algorithm.K256
              Expect.equal kp.Algorithm Algorithm.K256 "algorithm"
              Expect.equal kp.PrivateKeyBytes.Length 32 "private key 32 bytes"
              Expect.equal kp.CompressedPublicKey.Length 33 "compressed public key 33 bytes"
              Expect.isTrue (kp.CompressedPublicKey.[0] = 0x02uy || kp.CompressedPublicKey.[0] = 0x03uy) "valid prefix"

          testCase "import K-256 private key from hex"
          <| fun () ->
              let hexKey = "9085d2bef69286a6cbb51623c8fa258629945cd55ca705cc4e66700396894e0c"
              let privBytes = System.Convert.FromHexString hexKey
              let kp = Keys.importPrivateKey Algorithm.K256 privBytes
              Expect.equal kp.Algorithm Algorithm.K256 "algorithm"
              Expect.equal kp.CompressedPublicKey.Length 33 "compressed key length"

          testCase "import public key validates length"
          <| fun () ->
              match Keys.importPublicKey Algorithm.P256 [| 0x02uy |] with
              | Error _ -> ()
              | Ok _ -> failtest "Expected error for 1-byte key"

          testCase "import public key validates prefix"
          <| fun () ->
              match Keys.importPublicKey Algorithm.P256 (Array.create 33 0x04uy) with
              | Error _ -> ()
              | Ok _ -> failtest "Expected error for 0x04 prefix"

          testCase "decompress P-256 key round-trips"
          <| fun () ->
              let kp = Keys.generate Algorithm.P256
              let pub = Keys.publicKey kp
              let uncompressed = Keys.decompress pub
              Expect.equal uncompressed.Length 65 "uncompressed is 65 bytes"
              Expect.equal uncompressed.[0] 0x04uy "starts with 0x04"
              // Re-compress should give same result
              let x = uncompressed.[1..32]
              let y = uncompressed.[33..64]
              let rePrefix = if y.[y.Length - 1] % 2uy = 0uy then 0x02uy else 0x03uy
              let reCompressed = Array.append [| rePrefix |] x
              Expect.equal reCompressed pub.CompressedBytes "re-compress matches"

          testCase "decompress K-256 key round-trips"
          <| fun () ->
              let kp = Keys.generate Algorithm.K256
              let pub = Keys.publicKey kp
              let uncompressed = Keys.decompress pub
              Expect.equal uncompressed.Length 65 "uncompressed is 65 bytes"
              Expect.equal uncompressed.[0] 0x04uy "starts with 0x04" ]
