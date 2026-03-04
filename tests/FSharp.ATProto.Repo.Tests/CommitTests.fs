module CommitTests

open Expecto
open FSharp.ATProto.Syntax
open FSharp.ATProto.DRISL
open FSharp.ATProto.Crypto
open FSharp.ATProto.Repo

let private testDid =
    match Did.parse "did:plc:testuser123456" with
    | Ok d -> d
    | Error e -> failwith e

let private leafCid =
    match Cid.parse "bafyreie5cvv4h45feadgeuwhbcutmh6t2ceseocckahdoe6uat64zmz454" with
    | Ok c -> c
    | Error e -> failwith e

[<Tests>]
let commitTests =
    testList
        "Commit"
        [ testCase "create and verify P-256 signed commit"
          <| fun () ->
              let kp = Keys.generate Algorithm.P256
              let tree = Mst.create [ "app.bsky.feed.post/abc", leafCid ]
              let (rootCid, _) = Mst.serialize tree
              let rev = Commit.createRev ()
              let commit = Commit.create testDid rootCid rev None kp
              Expect.equal commit.Did testDid "DID"
              Expect.equal commit.Version 3 "version"
              Expect.equal commit.Data rootCid "data"
              Expect.equal commit.Sig.Length 64 "signature is 64 bytes"

              let commitBytes = Commit.encode commit
              match Commit.verify (Keys.publicKey kp) commitBytes with
              | Ok valid -> Expect.isTrue valid "signature verifies"
              | Error e -> failtest e

          testCase "create and verify K-256 signed commit"
          <| fun () ->
              let kp = Keys.generate Algorithm.K256
              let tree = Mst.create [ "app.bsky.feed.post/abc", leafCid ]
              let (rootCid, _) = Mst.serialize tree
              let rev = Commit.createRev ()
              let commit = Commit.create testDid rootCid rev None kp

              let commitBytes = Commit.encode commit
              match Commit.verify (Keys.publicKey kp) commitBytes with
              | Ok valid -> Expect.isTrue valid "signature verifies"
              | Error e -> failtest e

          testCase "wrong key rejects commit"
          <| fun () ->
              let kp1 = Keys.generate Algorithm.P256
              let kp2 = Keys.generate Algorithm.P256
              let tree = Mst.create [ "app.bsky.feed.post/abc", leafCid ]
              let (rootCid, _) = Mst.serialize tree
              let commit = Commit.create testDid rootCid (Commit.createRev ()) None kp1

              let commitBytes = Commit.encode commit
              match Commit.verify (Keys.publicKey kp2) commitBytes with
              | Ok valid -> Expect.isFalse valid "wrong key rejects"
              | Error e -> failtest e

          testCase "encode and decode round-trip"
          <| fun () ->
              let kp = Keys.generate Algorithm.P256
              let tree = Mst.create [ "test/key", leafCid ]
              let (rootCid, _) = Mst.serialize tree
              let rev = Commit.createRev ()
              let commit = Commit.create testDid rootCid rev None kp

              let encoded = Commit.encode commit
              match Commit.decode encoded with
              | Ok decoded ->
                  Expect.equal decoded.Did commit.Did "DID round-trips"
                  Expect.equal decoded.Version commit.Version "version round-trips"
                  Expect.equal decoded.Data commit.Data "data round-trips"
                  Expect.equal decoded.Rev commit.Rev "rev round-trips"
                  Expect.equal decoded.Prev commit.Prev "prev round-trips"
                  Expect.equal decoded.Sig commit.Sig "sig round-trips"
              | Error e -> failtest e

          testCase "commit with prev CID"
          <| fun () ->
              let kp = Keys.generate Algorithm.P256
              let tree = Mst.create [ "test/key", leafCid ]
              let (rootCid, _) = Mst.serialize tree
              let rev = Commit.createRev ()
              let prevCid = leafCid
              let commit = Commit.create testDid rootCid rev (Some prevCid) kp

              Expect.equal commit.Prev (Some prevCid) "prev CID set"

              let encoded = Commit.encode commit
              match Commit.decode encoded with
              | Ok decoded -> Expect.equal decoded.Prev (Some prevCid) "prev round-trips"
              | Error e -> failtest e

          testCase "createRev produces 13-char string"
          <| fun () ->
              let rev = Commit.createRev ()
              Expect.equal rev.Length 13 "rev is 13 chars" ]

[<Tests>]
let repoTests =
    testList
        "Repo"
        [ testCase "empty repo has no records"
          <| fun () ->
              let repo = Repo.empty testDid
              Expect.isEmpty (Repo.listRecords repo) "no records"

          testCase "put and get record"
          <| fun () ->
              let repo =
                  Repo.empty testDid
                  |> Repo.putRecord "app.bsky.feed.post/abc" leafCid

              Expect.equal (Repo.getRecord "app.bsky.feed.post/abc" repo) (Some leafCid) "found"
              Expect.equal (Repo.getRecord "app.bsky.feed.post/xyz" repo) None "not found"

          testCase "delete record"
          <| fun () ->
              let repo =
                  Repo.empty testDid
                  |> Repo.putRecord "app.bsky.feed.post/abc" leafCid
                  |> Repo.deleteRecord "app.bsky.feed.post/abc"

              Expect.equal (Repo.getRecord "app.bsky.feed.post/abc" repo) None "deleted"

          testCase "computeRoot produces valid CID"
          <| fun () ->
              let repo =
                  Repo.empty testDid
                  |> Repo.putRecord "app.bsky.feed.post/abc" leafCid

              let (rootCid, blocks) = Repo.computeRoot repo
              Expect.isTrue ((Cid.value rootCid).Length > 0) "has root CID"
              Expect.isTrue (blocks.Count > 0) "has blocks"

          testCase "exportCar produces valid bytes"
          <| fun () ->
              let repo =
                  Repo.empty testDid
                  |> Repo.putRecord "app.bsky.feed.post/abc" leafCid

              let (rootCid, blocks) = Repo.computeRoot repo
              let carBytes = Repo.exportCar [ rootCid ] blocks
              Expect.isTrue (carBytes.Length > 0) "non-empty CAR" ]
