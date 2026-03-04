module FSharp.ATProto.Streaming.Tests.JetstreamTests

open Expecto
open FSharp.ATProto.Streaming
open FSharp.ATProto.Syntax

[<Tests>]
let parseEventTests =
    testList
        "Jetstream.parseEvent"
        [
          // ── Commit events ──────────────────────────────────────────────

          test "parses create commit event" {
              let json =
                  """
                  {
                      "did": "did:plc:eygmaihciaxprqvxpfvl6flk",
                      "time_us": 1725911162329308,
                      "kind": "commit",
                      "commit": {
                          "rev": "3l3qo2vutsw2b",
                          "operation": "create",
                          "collection": "app.bsky.feed.like",
                          "rkey": "3l3qo2vuowo2b",
                          "record": {
                              "$type": "app.bsky.feed.like",
                              "subject": {
                                  "cid": "bafyreidc6sydkkbchcyg62v77wbhzvb2mvytlmsychqgwf2xojjtirmyq",
                                  "uri": "at://did:plc:wa7b35aakoll7hugkrjtf3xf/app.bsky.feed.post/3l3pte3p2e32y"
                              },
                              "createdAt": "2024-09-09T19:46:02.102Z"
                          },
                          "cid": "bafyreiawxqjb5ld3aqdmzjpfsmctfnbw7qr2yjbzplbz3oqk2wr2yjwpe"
                      }
                  }
                  """

              match Jetstream.parseEvent json with
              | Ok (CommitEvent (did, timeUs, commit)) ->
                  Expect.equal (Did.value did) "did:plc:eygmaihciaxprqvxpfvl6flk" "DID"
                  Expect.equal timeUs 1725911162329308L "time_us"
                  Expect.equal commit.Rev "3l3qo2vutsw2b" "rev"
                  Expect.equal commit.Operation Create "operation"
                  Expect.equal commit.Collection "app.bsky.feed.like" "collection"
                  Expect.equal commit.Rkey "3l3qo2vuowo2b" "rkey"
                  Expect.isSome commit.Record "record should be present"
                  Expect.equal commit.Cid (Some "bafyreiawxqjb5ld3aqdmzjpfsmctfnbw7qr2yjbzplbz3oqk2wr2yjwpe") "cid"
              | other -> failtest $"Expected CommitEvent, got {other}"
          }

          test "parses update commit event" {
              let json =
                  """
                  {
                      "did": "did:plc:abc123",
                      "time_us": 1000000,
                      "kind": "commit",
                      "commit": {
                          "rev": "rev1",
                          "operation": "update",
                          "collection": "app.bsky.actor.profile",
                          "rkey": "self",
                          "record": { "$type": "app.bsky.actor.profile", "displayName": "Alice" },
                          "cid": "bafyreicid"
                      }
                  }
                  """

              match Jetstream.parseEvent json with
              | Ok (CommitEvent (_, _, commit)) ->
                  Expect.equal commit.Operation Update "operation"
                  Expect.equal commit.Collection "app.bsky.actor.profile" "collection"
                  Expect.equal commit.Rkey "self" "rkey"
              | other -> failtest $"Expected CommitEvent, got {other}"
          }

          test "parses delete commit event (no record)" {
              let json =
                  """
                  {
                      "did": "did:plc:abc123",
                      "time_us": 2000000,
                      "kind": "commit",
                      "commit": {
                          "rev": "rev2",
                          "operation": "delete",
                          "collection": "app.bsky.feed.post",
                          "rkey": "3l3qo2vuowo2b"
                      }
                  }
                  """

              match Jetstream.parseEvent json with
              | Ok (CommitEvent (_, _, commit)) ->
                  Expect.equal commit.Operation Delete "operation"
                  Expect.isNone commit.Record "delete has no record"
                  Expect.isNone commit.Cid "delete has no cid"
              | other -> failtest $"Expected CommitEvent, got {other}"
          }

          test "commit event record is a cloned JsonElement" {
              let json =
                  """
                  {
                      "did": "did:plc:abc123",
                      "time_us": 1000000,
                      "kind": "commit",
                      "commit": {
                          "rev": "rev1",
                          "operation": "create",
                          "collection": "app.bsky.feed.post",
                          "rkey": "abc",
                          "record": { "$type": "app.bsky.feed.post", "text": "hello" }
                      }
                  }
                  """

              match Jetstream.parseEvent json with
              | Ok (CommitEvent (_, _, commit)) ->
                  let record = commit.Record.Value
                  let text = record.GetProperty("text").GetString ()
                  Expect.equal text "hello" "record field accessible after parse"
              | other -> failtest $"Expected CommitEvent, got {other}"
          }

          // ── Identity events ────────────────────────────────────────────

          test "parses identity event with handle" {
              let json =
                  """
                  {
                      "did": "did:plc:eygmaihciaxprqvxpfvl6flk",
                      "time_us": 1725911000000000,
                      "kind": "identity",
                      "identity": {
                          "did": "did:plc:eygmaihciaxprqvxpfvl6flk",
                          "handle": "user.bsky.social"
                      }
                  }
                  """

              match Jetstream.parseEvent json with
              | Ok (IdentityEvent (did, timeUs, identity)) ->
                  Expect.equal (Did.value did) "did:plc:eygmaihciaxprqvxpfvl6flk" "DID"
                  Expect.equal timeUs 1725911000000000L "time_us"
                  Expect.equal (Did.value identity.Did) "did:plc:eygmaihciaxprqvxpfvl6flk" "identity DID"
                  Expect.isSome identity.Handle "handle present"

                  Expect.equal
                      (Handle.value identity.Handle.Value)
                      "user.bsky.social"
                      "handle value"
              | other -> failtest $"Expected IdentityEvent, got {other}"
          }

          test "parses identity event without handle" {
              let json =
                  """
                  {
                      "did": "did:plc:abc123",
                      "time_us": 5000000,
                      "kind": "identity",
                      "identity": {
                          "did": "did:plc:abc123"
                      }
                  }
                  """

              match Jetstream.parseEvent json with
              | Ok (IdentityEvent (_, _, identity)) ->
                  Expect.isNone identity.Handle "no handle"
              | other -> failtest $"Expected IdentityEvent, got {other}"
          }

          // ── Account events ─────────────────────────────────────────────

          test "parses account event active" {
              let json =
                  """
                  {
                      "did": "did:plc:abc123",
                      "time_us": 3000000,
                      "kind": "account",
                      "account": {
                          "active": true,
                          "status": "active"
                      }
                  }
                  """

              match Jetstream.parseEvent json with
              | Ok (AccountEvent (did, timeUs, account)) ->
                  Expect.equal (Did.value did) "did:plc:abc123" "DID"
                  Expect.equal timeUs 3000000L "time_us"
                  Expect.isTrue account.Active "active"
                  Expect.equal account.Status (Some "active") "status"
              | other -> failtest $"Expected AccountEvent, got {other}"
          }

          test "parses account event deactivated" {
              let json =
                  """
                  {
                      "did": "did:plc:abc123",
                      "time_us": 4000000,
                      "kind": "account",
                      "account": {
                          "active": false,
                          "status": "deactivated"
                      }
                  }
                  """

              match Jetstream.parseEvent json with
              | Ok (AccountEvent (_, _, account)) ->
                  Expect.isFalse account.Active "not active"
                  Expect.equal account.Status (Some "deactivated") "status"
              | other -> failtest $"Expected AccountEvent, got {other}"
          }

          test "parses account event without status" {
              let json =
                  """
                  {
                      "did": "did:plc:abc123",
                      "time_us": 4500000,
                      "kind": "account",
                      "account": {
                          "active": true
                      }
                  }
                  """

              match Jetstream.parseEvent json with
              | Ok (AccountEvent (_, _, account)) ->
                  Expect.isTrue account.Active "active"
                  Expect.isNone account.Status "no status"
              | other -> failtest $"Expected AccountEvent, got {other}"
          }

          // ── Unknown events ─────────────────────────────────────────────

          test "parses unknown kind as UnknownEvent" {
              let json =
                  """
                  {
                      "did": "did:plc:abc123",
                      "time_us": 6000000,
                      "kind": "something_new"
                  }
                  """

              match Jetstream.parseEvent json with
              | Ok (UnknownEvent (did, timeUs, kind)) ->
                  Expect.equal (Did.value did) "did:plc:abc123" "DID"
                  Expect.equal timeUs 6000000L "time_us"
                  Expect.equal kind "something_new" "kind"
              | other -> failtest $"Expected UnknownEvent, got {other}"
          }

          // ── Error cases ────────────────────────────────────────────────

          test "returns error for invalid JSON" {
              match Jetstream.parseEvent "not json" with
              | Error (DeserializationError _) -> ()
              | other -> failtest $"Expected DeserializationError, got {other}"
          }

          test "returns error for invalid DID" {
              let json =
                  """
                  {
                      "did": "invalid",
                      "time_us": 1000000,
                      "kind": "commit"
                  }
                  """

              match Jetstream.parseEvent json with
              | Error (DeserializationError msg) ->
                  Expect.stringContains msg "Invalid DID" "error mentions DID"
              | other -> failtest $"Expected DeserializationError, got {other}"
          }

          test "returns error for missing kind" {
              let json =
                  """
                  {
                      "did": "did:plc:abc123",
                      "time_us": 1000000
                  }
                  """

              match Jetstream.parseEvent json with
              | Error (DeserializationError msg) ->
                  Expect.stringContains msg "kind" "error mentions kind"
              | other -> failtest $"Expected DeserializationError, got {other}"
          }

          test "returns error for commit with missing commit field" {
              let json =
                  """
                  {
                      "did": "did:plc:abc123",
                      "time_us": 1000000,
                      "kind": "commit"
                  }
                  """

              match Jetstream.parseEvent json with
              | Error (DeserializationError msg) ->
                  Expect.stringContains msg "commit" "error mentions commit"
              | other -> failtest $"Expected DeserializationError, got {other}"
          }

          test "returns error for unknown commit operation" {
              let json =
                  """
                  {
                      "did": "did:plc:abc123",
                      "time_us": 1000000,
                      "kind": "commit",
                      "commit": {
                          "rev": "r1",
                          "operation": "weird",
                          "collection": "app.bsky.feed.post",
                          "rkey": "abc"
                      }
                  }
                  """

              match Jetstream.parseEvent json with
              | Error (DeserializationError msg) ->
                  Expect.stringContains msg "weird" "error mentions the unknown operation"
              | other -> failtest $"Expected DeserializationError, got {other}"
          }

          test "returns error for identity with missing identity field" {
              let json =
                  """
                  {
                      "did": "did:plc:abc123",
                      "time_us": 1000000,
                      "kind": "identity"
                  }
                  """

              match Jetstream.parseEvent json with
              | Error (DeserializationError msg) ->
                  Expect.stringContains msg "identity" "error mentions identity"
              | other -> failtest $"Expected DeserializationError, got {other}"
          }

          test "returns error for account with missing account field" {
              let json =
                  """
                  {
                      "did": "did:plc:abc123",
                      "time_us": 1000000,
                      "kind": "account"
                  }
                  """

              match Jetstream.parseEvent json with
              | Error (DeserializationError msg) ->
                  Expect.stringContains msg "account" "error mentions account"
              | other -> failtest $"Expected DeserializationError, got {other}"
          }
        ]

[<Tests>]
let buildUriTests =
    testList
        "Jetstream.buildUri"
        [
          test "default options produce base endpoint" {
              let uri = Jetstream.buildUri Jetstream.defaultOptions
              Expect.equal (uri.ToString ()) "wss://jetstream1.us-east.bsky.network/subscribe" "base uri"
          }

          test "single wantedCollections" {
              let opts =
                  { Jetstream.defaultOptions with
                      WantedCollections = [ "app.bsky.feed.post" ] }

              let uri = Jetstream.buildUri opts

              Expect.stringContains
                  (uri.ToString ())
                  "wantedCollections=app.bsky.feed.post"
                  "collection param"
          }

          test "multiple wantedCollections" {
              let opts =
                  { Jetstream.defaultOptions with
                      WantedCollections =
                          [ "app.bsky.feed.post"
                            "app.bsky.feed.like" ] }

              let uri = Jetstream.buildUri opts
              let s = uri.ToString ()
              Expect.stringContains s "wantedCollections=app.bsky.feed.post" "first collection"
              Expect.stringContains s "wantedCollections=app.bsky.feed.like" "second collection"
          }

          test "wantedDids" {
              let opts =
                  { Jetstream.defaultOptions with
                      WantedDids = [ "did:plc:abc123" ] }

              let uri = Jetstream.buildUri opts
              Expect.stringContains (uri.ToString ()) "wantedDids=did" "did param present"
          }

          test "cursor" {
              let opts =
                  { Jetstream.defaultOptions with
                      Cursor = Some 1725911162329308L }

              let uri = Jetstream.buildUri opts
              Expect.stringContains (uri.ToString ()) "cursor=1725911162329308" "cursor param"
          }

          test "all options combined" {
              let opts =
                  { Jetstream.defaultOptions with
                      WantedCollections = [ "app.bsky.feed.post" ]
                      WantedDids = [ "did:plc:abc123" ]
                      Cursor = Some 100L }

              let uri = Jetstream.buildUri opts
              let s = uri.ToString ()
              Expect.stringContains s "wantedCollections=app.bsky.feed.post" "collection"
              Expect.stringContains s "wantedDids=" "did"
              Expect.stringContains s "cursor=100" "cursor"
          }

          test "custom endpoint" {
              let opts =
                  { Jetstream.defaultOptions with
                      Endpoint = "wss://jetstream2.us-west.bsky.network/subscribe" }

              let uri = Jetstream.buildUri opts
              Expect.stringStarts (uri.ToString ()) "wss://jetstream2.us-west.bsky.network/subscribe" "custom endpoint"
          }
        ]
