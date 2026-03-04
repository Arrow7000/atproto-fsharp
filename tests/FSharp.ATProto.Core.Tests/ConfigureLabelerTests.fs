module ConfigureLabelerTests

open Expecto
open FSharp.ATProto.Core

[<Tests>]
let configureLabelerTests =
    testList
        "AtpAgent.configureLabelers"
        [ testCase "adds atproto-accept-labelers header"
          <| fun () ->
              let agent = AtpAgent.create "https://bsky.social"

              let configured =
                  agent
                  |> AtpAgent.configureLabelers [ ("did:plc:abc123", false) ]

              let header =
                  configured.ExtraHeaders
                  |> List.tryFind (fun (k, _) -> k = "atproto-accept-labelers")

              Expect.isSome header "Should have atproto-accept-labelers header"
              Expect.equal (snd header.Value) "did:plc:abc123" "Header value should be the DID"

          testCase "formats single labeler with redact flag"
          <| fun () ->
              let agent = AtpAgent.create "https://bsky.social"

              let configured =
                  agent
                  |> AtpAgent.configureLabelers [ ("did:plc:abc123", true) ]

              let header =
                  configured.ExtraHeaders
                  |> List.tryFind (fun (k, _) -> k = "atproto-accept-labelers")

              Expect.equal (snd header.Value) "did:plc:abc123;redact" "Should include ;redact"

          testCase "formats multiple labelers"
          <| fun () ->
              let agent = AtpAgent.create "https://bsky.social"

              let configured =
                  agent
                  |> AtpAgent.configureLabelers
                      [ ("did:plc:abc123", true)
                        ("did:plc:def456", false)
                        ("did:plc:ghi789", true) ]

              let header =
                  configured.ExtraHeaders
                  |> List.tryFind (fun (k, _) -> k = "atproto-accept-labelers")

              Expect.equal
                  (snd header.Value)
                  "did:plc:abc123;redact, did:plc:def456, did:plc:ghi789;redact"
                  "Should format multiple labelers correctly"

          testCase "replaces existing atproto-accept-labelers header"
          <| fun () ->
              let agent = AtpAgent.create "https://bsky.social"

              let first =
                  agent
                  |> AtpAgent.configureLabelers [ ("did:plc:old", false) ]

              let second =
                  first
                  |> AtpAgent.configureLabelers [ ("did:plc:new", true) ]

              let headers =
                  second.ExtraHeaders
                  |> List.filter (fun (k, _) -> k = "atproto-accept-labelers")

              Expect.hasLength headers 1 "Should have exactly one atproto-accept-labelers header"
              Expect.equal (snd headers.[0]) "did:plc:new;redact" "Should be the new value"

          testCase "preserves other extra headers"
          <| fun () ->
              let agent =
                  AtpAgent.create "https://bsky.social"
                  |> AtpAgent.withChatProxy

              let configured =
                  agent
                  |> AtpAgent.configureLabelers [ ("did:plc:abc123", false) ]

              let proxyHeader =
                  configured.ExtraHeaders
                  |> List.tryFind (fun (k, _) -> k = "atproto-proxy")

              let labelerHeader =
                  configured.ExtraHeaders
                  |> List.tryFind (fun (k, _) -> k = "atproto-accept-labelers")

              Expect.isSome proxyHeader "Should preserve atproto-proxy header"
              Expect.isSome labelerHeader "Should have atproto-accept-labelers header"

          testCase "empty labeler list produces empty header value"
          <| fun () ->
              let agent = AtpAgent.create "https://bsky.social"

              let configured =
                  agent
                  |> AtpAgent.configureLabelers []

              let header =
                  configured.ExtraHeaders
                  |> List.tryFind (fun (k, _) -> k = "atproto-accept-labelers")

              Expect.isSome header "Should have header even with empty list"
              Expect.equal (snd header.Value) "" "Empty list should produce empty header value"

          testCase "does not mutate original agent"
          <| fun () ->
              let agent = AtpAgent.create "https://bsky.social"

              let _ =
                  agent
                  |> AtpAgent.configureLabelers [ ("did:plc:abc123", false) ]

              Expect.isEmpty agent.ExtraHeaders "Original agent should not be modified" ]
