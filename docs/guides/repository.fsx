(**
---
title: Repository
category: Infrastructure
categoryindex: 5
index: 26
description: Merkle Search Tree, signed commits, and CAR file export
keywords: fsharp, atproto, repository, mst, merkle, commit, car, sync
---

# Repository

AT Protocol repositories are Merkle Search Trees (MSTs) with signed commits. The `FSharp.ATProto.Repo` package provides data structures for reading, writing, and verifying repository contents -- including MST operations, commit signing, and CAR v1 export.

## Merkle Search Tree (MST)

The MST is the core data structure that stores records in a repository. Keys are strings in the form `collection/rkey` (e.g. `app.bsky.feed.post/3jui7kd2z2y2e`), and values are CIDs pointing to the record's DAG-CBOR block.

### Types

```fsharp
type Entry =
    { Key : string
      Value : Cid
      Tree : Node option }

and Node =
    { Left : Node option
      Entries : Entry list }
```

### Building and Querying
*)

(*** hide ***)
#nowarn "20"
#r "../../src/FSharp.ATProto.Syntax/bin/Release/net10.0/FSharp.ATProto.Syntax.dll"
#r "../../src/FSharp.ATProto.DRISL/bin/Release/net10.0/FSharp.ATProto.DRISL.dll"
#r "../../src/FSharp.ATProto.Crypto/bin/Release/net10.0/FSharp.ATProto.Crypto.dll"
#r "../../src/FSharp.ATProto.Repo/bin/Release/net10.0/FSharp.ATProto.Repo.dll"
open FSharp.ATProto.Syntax
open FSharp.ATProto.DRISL
open FSharp.ATProto.Crypto
open FSharp.ATProto.Repo

let cidA = Unchecked.defaultof<Cid>
let cidB = Unchecked.defaultof<Cid>
let cidC = Unchecked.defaultof<Cid>
(***)

open FSharp.ATProto.Repo

// Start with an empty tree
let tree = Mst.empty

// Build from a sorted list of entries
let tree2 =
    Mst.create
        [ ("app.bsky.feed.post/abc", cidA)
          ("app.bsky.feed.post/def", cidB) ]

// Insert and delete (both rebuild the tree)
let tree3 = tree2 |> Mst.insert "app.bsky.feed.post/ghi" cidC
let tree4 = tree3 |> Mst.delete "app.bsky.feed.post/abc"

// Look up a key
match Mst.lookup "app.bsky.feed.post/def" tree4 with
| Some cid -> printfn "Found: %s" (Cid.value cid)
| None -> printfn "Not found"

// List all entries in sorted order
let allPairs = Mst.allEntries tree4

(**
### Diffing

Compare two MST states to find what changed:
*)

(*** hide ***)
let oldTree = Unchecked.defaultof<Mst.Node>
let newTree = Unchecked.defaultof<Mst.Node>
(***)

let (added, updated, deleted) = Mst.diff oldTree newTree
// added: (key, cid) list -- new keys
// updated: (key, cid) list -- keys with changed CIDs
// deleted: string list -- removed keys

(**
### Serialization

Serialize an MST to DAG-CBOR blocks and compute its root CID:
*)

(*** hide ***)
let tree5 = Unchecked.defaultof<Mst.Node>
(***)

let (rootCid, blocks) = Mst.serialize tree5
// rootCid: Cid -- the MST root
// blocks: Map<string, byte[]> -- CID string -> DAG-CBOR bytes

// Deserialize from a block store
match Mst.deserialize blocks rootCid with
| Ok node -> printfn "Loaded %d entries" (Mst.allEntries node).Length
| Error msg -> printfn "Error: %s" msg

(**
## Signed Commits

Every repository state is anchored by a signed commit that references the MST root.

```fsharp
type SignedCommit =
    { Did : Did
      Version : int       // always 3
      Data : Cid          // MST root CID
      Rev : string        // TID-based revision
      Prev : Cid option   // previous commit CID
      Sig : byte[] }      // 64-byte ECDSA signature
```

### Creating and Verifying Commits
*)

(*** hide ***)
let did = Did.parse "did:plc:example" |> Result.defaultWith failwith
let rootCid2 = Unchecked.defaultof<Cid>
(***)

open FSharp.ATProto.Repo
open FSharp.ATProto.Crypto

let keyPair = Keys.generate Algorithm.P256
let rev = Commit.createRev ()

// Create a signed commit
let commit = Commit.create did rootCid2 rev None keyPair

// Encode to DAG-CBOR
let commitBytes = Commit.encode commit

// Decode from DAG-CBOR
match Commit.decode commitBytes with
| Ok decoded -> printfn "Rev: %s" decoded.Rev
| Error msg -> printfn "Decode error: %s" msg

// Verify signature
match Commit.verify (Keys.publicKey keyPair) commitBytes with
| Ok true -> printfn "Valid signature"
| Ok false -> printfn "Invalid signature"
| Error msg -> printfn "Verification error: %s" msg

(**
## In-Memory Repository

The `Repo` module provides a higher-level wrapper combining the MST with repository metadata:
*)

open FSharp.ATProto.Repo
open FSharp.ATProto.Syntax

(*** hide ***)
let recordCid = Unchecked.defaultof<Cid>
(***)

let did2 = Did.parse "did:plc:example" |> Result.defaultWith failwith
let repo = Repo.empty did2

// Add and remove records
let repo2 = repo |> Repo.putRecord "app.bsky.feed.post/abc" recordCid
let repo3 = repo2 |> Repo.deleteRecord "app.bsky.feed.post/abc"

// Query
let maybeCid = Repo.getRecord "app.bsky.feed.post/abc" repo3
let allRecords = Repo.listRecords repo3

// Compute root CID and blocks
let (rootCid3, blocks3) = Repo.computeRoot repo3

(**
## CAR Export

Export a repository's blocks as a CAR v1 file:
*)

(*** hide ***)
let repo4 = Unchecked.defaultof<Repo.Repository>
(***)

let (rootCid4, blocks4) = Repo.computeRoot repo4
let carBytes = Repo.exportCar [ rootCid4 ] blocks4
// carBytes is a complete CAR v1 file ready for transport

(**
The CAR format is used by AT Protocol sync endpoints (`com.atproto.sync.getRepo`, etc.) to transfer repository data.
*)
