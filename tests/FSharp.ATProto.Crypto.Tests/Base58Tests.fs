module Base58Tests

open System
open Expecto
open FSharp.ATProto.Crypto

[<Tests>]
let tests =
    testList
        "Base58"
        [ testCase "encode empty bytes"
          <| fun () -> Expect.equal (Base58.encode [||]) "" "empty"

          testCase "encode single zero byte"
          <| fun () -> Expect.equal (Base58.encode [| 0uy |]) "1" "leading zero"

          testCase "encode two zero bytes"
          <| fun () -> Expect.equal (Base58.encode [| 0uy; 0uy |]) "11" "two leading zeros"

          testCase "encode known value"
          <| fun () ->
              // "Hello World!" in base58btc = "2NEpo7TZRRrLZSi2U"
              let input = Text.Encoding.UTF8.GetBytes "Hello World!"
              Expect.equal (Base58.encode input) "2NEpo7TZRRrLZSi2U" "Hello World!"

          testCase "decode empty string"
          <| fun () -> Expect.equal (Base58.decode "") (Ok [||]) "empty"

          testCase "decode leading 1s"
          <| fun () ->
              Expect.equal (Base58.decode "1") (Ok [| 0uy |]) "single zero"
              Expect.equal (Base58.decode "11") (Ok [| 0uy; 0uy |]) "double zero"

          testCase "decode known value"
          <| fun () ->
              match Base58.decode "2NEpo7TZRRrLZSi2U" with
              | Ok bytes ->
                  Expect.equal (Text.Encoding.UTF8.GetString bytes) "Hello World!" "Hello World!"
              | Error e -> failtest e

          testCase "decode invalid character"
          <| fun () ->
              match Base58.decode "0OIl" with
              | Error _ -> ()
              | Ok _ -> failtest "Expected Error for invalid chars"

          testCase "round-trip random bytes"
          <| fun () ->
              let rng = Random 42
              for _ in 1..100 do
                  let len = rng.Next (1, 50)
                  let bytes = Array.zeroCreate len
                  rng.NextBytes bytes
                  let encoded = Base58.encode bytes
                  match Base58.decode encoded with
                  | Ok decoded -> Expect.equal decoded bytes (sprintf "round-trip %d bytes" len)
                  | Error e -> failtest (sprintf "round-trip failed: %s" e)

          testCase "round-trip with leading zeros"
          <| fun () ->
              let bytes = [| 0uy; 0uy; 0uy; 1uy; 2uy; 3uy |]
              let encoded = Base58.encode bytes
              match Base58.decode encoded with
              | Ok decoded -> Expect.equal decoded bytes "preserved leading zeros"
              | Error e -> failtest e ]
