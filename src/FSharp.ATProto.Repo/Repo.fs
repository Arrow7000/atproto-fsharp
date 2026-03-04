namespace FSharp.ATProto.Repo

open System
open System.IO
open FSharp.ATProto.Syntax
open FSharp.ATProto.DRISL

/// In-memory repository with MST, commit creation, and CAR export/import.
module Repo =

    /// An in-memory repository.
    type Repository =
        { Did : Did
          Tree : Mst.Node
          Rev : string option
          Prev : Cid option }

    /// Create an empty repository for a DID.
    let empty (did : Did) : Repository =
        { Did = did
          Tree = Mst.empty
          Rev = None
          Prev = None }

    /// Get a record value by its key (collection/rkey path).
    let getRecord (key : string) (repo : Repository) : Cid option =
        Mst.lookup key repo.Tree

    /// Put a record into the repository.
    let putRecord (key : string) (value : Cid) (repo : Repository) : Repository =
        { repo with Tree = Mst.insert key value repo.Tree }

    /// Delete a record from the repository.
    let deleteRecord (key : string) (repo : Repository) : Repository =
        { repo with Tree = Mst.delete key repo.Tree }

    /// List all records in the repository.
    let listRecords (repo : Repository) : (string * Cid) list =
        Mst.allEntries repo.Tree

    /// Compute the MST root CID and collect all blocks.
    let computeRoot (repo : Repository) : Cid * Map<string, byte[]> =
        Mst.serialize repo.Tree

    /// Serialize a CAR v1 file from roots and blocks.
    let exportCar (roots : Cid list) (blocks : Map<string, byte[]>) : byte[] =
        use ms = new MemoryStream ()

        // Encode header as DAG-CBOR
        let rootLinks = roots |> List.map AtpValue.Link
        let headerValue =
            AtpValue.Object (
                Map.ofList
                    [ "roots", AtpValue.Array rootLinks
                      "version", AtpValue.Integer 1L ]
            )

        let headerBytes = Drisl.encode headerValue

        // Write header length varint
        let headerLenVarint = Varint.encode (uint64 headerBytes.Length)
        ms.Write (headerLenVarint, 0, headerLenVarint.Length)
        ms.Write (headerBytes, 0, headerBytes.Length)

        // Write blocks
        for kv in blocks do
            let cid =
                match Cid.parse kv.Key with
                | Ok c -> c
                | Error e -> failwith (sprintf "Invalid CID key: %s" e)

            let cidBytes = CidBinary.toBytes cid
            let blockLen = cidBytes.Length + kv.Value.Length
            let blockLenVarint = Varint.encode (uint64 blockLen)
            ms.Write (blockLenVarint, 0, blockLenVarint.Length)
            ms.Write (cidBytes, 0, cidBytes.Length)
            ms.Write (kv.Value, 0, kv.Value.Length)

        ms.ToArray ()
