module MstTests

open Expecto
open FSharp.ATProto.Syntax
open FSharp.ATProto.Repo

// ── Key height interop tests ────────────────────────────────────────────────

let private keyHeightVectors =
    [ "", 0
      "asdf", 0
      "blue", 1
      "2653ae71", 0
      "88bfafc7", 2
      "2a92d355", 4
      "884976f5", 6
      "app.bsky.feed.post/454397e440ec", 4
      "app.bsky.feed.post/9adeb165882c", 8 ]

[<Tests>]
let keyHeightTests =
    testList
        "Mst.heightForKey (interop)"
        (keyHeightVectors
         |> List.mapi (fun i (key, expected) ->
             testCase (sprintf "key height vector %d: %s" i (if key = "" then "(empty)" else key))
             <| fun () ->
                 let actual = Mst.heightForKey key
                 Expect.equal actual expected (sprintf "height for '%s'" key)))

// ── Common prefix interop tests ─────────────────────────────────────────────

let private commonPrefixVectors =
    [ "", "", 0
      "abc", "abc", 3
      "", "abc", 0
      "abc", "", 0
      "ab", "abc", 2
      "abc", "ab", 2
      "abcde", "abc", 3
      "abc", "abcde", 3
      "abcde", "abc1", 3
      "abcde", "abb", 2
      "abcde", "qbb", 0
      "abc", "abc\x00", 3
      "abc\x00", "abc", 3 ]

[<Tests>]
let commonPrefixTests =
    testList
        "Mst.commonPrefixLen (interop)"
        (commonPrefixVectors
         |> List.mapi (fun i (left, right, expected) ->
             testCase (sprintf "common prefix vector %d" i)
             <| fun () ->
                 let actual = Mst.commonPrefixLen left right
                 Expect.equal actual expected (sprintf "commonPrefixLen('%s', '%s')" left right)))

// ── Commit-proof fixture tests (MST root CID) ──────────────────────────────

let private leafValue = "bafyreie5cvv4h45feadgeuwhbcutmh6t2ceseocckahdoe6uat64zmz454"

let private parseCid (s : string) =
    match Cid.parse s with
    | Ok cid -> cid
    | Error e -> failwith (sprintf "Invalid CID: %s" e)

let private leafCid = parseCid leafValue

type private CommitFixture =
    { Comment : string
      Keys : string list
      Adds : string list
      Dels : string list
      RootBefore : string
      RootAfter : string }

let private commitFixtures =
    [ { Comment = "two deep split"
        Keys = [ "A0/374913"; "B1/986427"; "C0/451630"; "E0/670489"; "F1/085263"; "G0/765327" ]
        Adds = [ "D2/269196" ]
        Dels = []
        RootBefore = "bafyreicraprx2xwnico4tuqir3ozsxpz46qkcpox3obf5bagicqwurghpy"
        RootAfter = "bafyreihvay6pazw3dfa47u5d2tn3rd6pa57sr37bo5bqyvjuqc73ib65my" }

      { Comment = "two deep leafless split"
        Keys = [ "A0/374913"; "B0/601692"; "D0/952776"; "E0/670489" ]
        Adds = [ "C2/014073" ]
        Dels = []
        RootBefore = "bafyreialm5sgf7pijawbschsjpdevid5rss5ip3d4n4w6cc4mhu53sfl4i"
        RootAfter = "bafyreibxh4iztp5l2yshz3ectg2qjpeyprpw2gogao3pvceowpq3k3thya" }

      { Comment = "add on edge with neighbor two layers down"
        Keys = [ "A0/374913"; "B2/827649"; "C0/451630" ]
        Adds = [ "D2/269196" ]
        Dels = []
        RootBefore = "bafyreigc6ay2qwfk7kuevvrczummpd64nknfo4yxpaooknfymzyb7u3ntq"
        RootAfter = "bafyreign6kxoll35r5f2ske6hjx7vg56aw3jn6r5hcopgrepzafpvohr2a" }

      { Comment = "merge and split in multi-op commit"
        Keys = [ "A0/374913"; "B2/827649"; "D2/269196"; "E0/670489" ]
        Adds = [ "C2/014073" ]
        Dels = [ "B2/827649"; "D2/269196" ]
        RootBefore = "bafyreiceld4icym4qjmdcn3dfgtxt7t66hdgyhvigessgmkvb56dx6amgi"
        RootAfter = "bafyreigkalika3taqauapfha556lo36zzcjoiifny5xeru6yis3nxw5ruq" }

      { Comment = "complex multi-op commit"
        Keys = [ "B0/601692"; "C2/014073"; "D0/952776"; "E2/819540"; "F0/697858"; "H0/131238" ]
        Adds = [ "A2/827942"; "G2/611528" ]
        Dels = [ "C2/014073" ]
        RootBefore = "bafyreigr3plnts7dax6yokvinbhcqpyicdfgg6npvvyx6okc5jo55slfqi"
        RootAfter = "bafyreiftrcrbhrwmi37u4egedlg56gk3jeh3tvmqvwgowoifuklfysyx54" }

      { Comment = "split with earlier leaves on same layer"
        Keys =
            [ "app.bsky.feed.post/3lo3kqqljmfe2"
              "app.bsky.feed.post/3log4547dm6h2"
              "app.bsky.feed.post/3log45inogon2"
              "app.bsky.feed.post/3logaodrh74d2"
              "app.bsky.feed.post/3logteazog2n2"
              "app.bsky.feed.post/3lon5cqsbwrj2"
              "app.bsky.feed.repost/3l6sjhvqonco2" ]
        Adds = [ "app.bsky.feed.post/3lon5dzeaihj2" ]
        Dels = []
        RootBefore = "bafyreigfcsro2up7qi7l3rxdpg7n6gjtteotkmgrrqztl5oy2tf4ncl4ji"
        RootAfter = "bafyreig33hsjiplaixvmccy65n7rn3in5nsbtcittzx6k3w5wjfhk2sg3a" } ]

[<Tests>]
let commitProofRootBeforeTests =
    testList
        "MST root CID before commit (interop)"
        (commitFixtures
         |> List.map (fun f ->
             testCase f.Comment
             <| fun () ->
                 let entries = f.Keys |> List.map (fun k -> k, leafCid)
                 let tree = Mst.create entries
                 let (rootCid, _) = Mst.serialize tree
                 Expect.equal (Cid.value rootCid) f.RootBefore (sprintf "root before: %s" f.Comment)))

[<Tests>]
let commitProofRootAfterTests =
    testList
        "MST root CID after commit (interop)"
        (commitFixtures
         |> List.map (fun f ->
             testCase f.Comment
             <| fun () ->
                 let entries = f.Keys |> List.map (fun k -> k, leafCid)
                 let mutable tree = Mst.create entries

                 for key in f.Adds do
                     tree <- Mst.insert key leafCid tree

                 for key in f.Dels do
                     tree <- Mst.delete key tree

                 let (rootCid, _) = Mst.serialize tree
                 Expect.equal (Cid.value rootCid) f.RootAfter (sprintf "root after: %s" f.Comment)))

// ── MST unit tests ──────────────────────────────────────────────────────────

[<Tests>]
let mstUnitTests =
    testList
        "MST operations"
        [ testCase "empty tree has no entries"
          <| fun () ->
              let entries = Mst.allEntries Mst.empty
              Expect.isEmpty entries "empty tree"

          testCase "empty tree serializes"
          <| fun () ->
              let (cid, blocks) = Mst.serialize Mst.empty
              Expect.isTrue (blocks.Count > 0) "has blocks"
              Expect.isTrue ((Cid.value cid).Length > 0) "has CID"

          testCase "insert and lookup"
          <| fun () ->
              let tree =
                  Mst.empty
                  |> Mst.insert "test/key1" leafCid
                  |> Mst.insert "test/key2" leafCid

              Expect.equal (Mst.lookup "test/key1" tree) (Some leafCid) "key1"
              Expect.equal (Mst.lookup "test/key2" tree) (Some leafCid) "key2"
              Expect.equal (Mst.lookup "test/key3" tree) None "key3"

          testCase "delete removes entry"
          <| fun () ->
              let tree =
                  Mst.empty
                  |> Mst.insert "test/key1" leafCid
                  |> Mst.insert "test/key2" leafCid
                  |> Mst.delete "test/key1"

              Expect.equal (Mst.lookup "test/key1" tree) None "deleted"
              Expect.equal (Mst.lookup "test/key2" tree) (Some leafCid) "remaining"

          testCase "allEntries returns sorted"
          <| fun () ->
              let tree =
                  Mst.empty
                  |> Mst.insert "z/3" leafCid
                  |> Mst.insert "a/1" leafCid
                  |> Mst.insert "m/2" leafCid

              let entries = Mst.allEntries tree
              let keys = entries |> List.map fst
              Expect.equal keys [ "a/1"; "m/2"; "z/3" ] "sorted order"

          testCase "serialize then deserialize round-trips"
          <| fun () ->
              let entries =
                  [ "app.bsky.feed.post/abc", leafCid
                    "app.bsky.feed.like/xyz", leafCid
                    "app.bsky.graph.follow/def", leafCid ]

              let tree = Mst.create entries
              let (rootCid, blocks) = Mst.serialize tree

              match Mst.deserialize blocks rootCid with
              | Ok deserialized ->
                  let original = Mst.allEntries tree |> List.sortBy fst
                  let roundTripped = Mst.allEntries deserialized |> List.sortBy fst
                  Expect.equal roundTripped original "round-trip preserves entries"
              | Error e -> failtest e

          testCase "diff detects adds, updates, deletes"
          <| fun () ->
              let cid2 = parseCid "bafyreigdyrzt5sfp7udm7hu76uh7y26nf3efuylqabf3oclgtqy55fbzdi"

              let oldTree =
                  Mst.create [ "a/1", leafCid; "b/2", leafCid; "c/3", leafCid ]

              let newTree =
                  Mst.create [ "a/1", leafCid; "b/2", cid2; "d/4", leafCid ]

              let (added, updated, deleted) = Mst.diff oldTree newTree
              Expect.equal (added |> List.map fst) [ "d/4" ] "added"
              Expect.equal (updated |> List.map fst) [ "b/2" ] "updated"
              Expect.equal deleted [ "c/3" ] "deleted"

          testCase "create with many keys produces valid tree"
          <| fun () ->
              let keys =
                  [ for i in 0..99 -> sprintf "app.bsky.feed.post/%06d" i, leafCid ]

              let tree = Mst.create keys
              let all = Mst.allEntries tree
              Expect.equal all.Length 100 "100 entries"

              let (_, blocks) = Mst.serialize tree
              Expect.isTrue (blocks.Count > 0) "has blocks" ]
