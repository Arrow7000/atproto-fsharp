namespace FSharp.ATProto.Repo

open System.Security.Cryptography
open System.Text
open FSharp.ATProto.Syntax
open FSharp.ATProto.DRISL

/// Merkle Search Tree (MST) data structure for AT Protocol repositories.
module Mst =

    /// An entry in an MST node.
    type Entry =
        { Key : string
          Value : Cid
          Tree : Node option }

    /// An MST node.
    and Node =
        { Left : Node option
          Entries : Entry list }

    /// An empty MST.
    let empty : Node = { Left = None; Entries = [] }

    /// Compute the MST height (layer) for a key.
    /// Counts leading zero 2-bit pairs in the SHA-256 hash of the key's UTF-8 bytes.
    let heightForKey (key : string) : int =
        let hash = SHA256.HashData (Encoding.UTF8.GetBytes key)
        let mutable zeros = 0
        let mutable i = 0
        let mutable stop = false

        while i < hash.Length && not stop do
            let b = hash.[i]
            if b < 64uy then zeros <- zeros + 1
            if b < 16uy then zeros <- zeros + 1
            if b < 4uy then zeros <- zeros + 1

            if b = 0uy then
                zeros <- zeros + 1
                i <- i + 1
            else
                stop <- true

        zeros

    /// Compute the common prefix length between two strings.
    let commonPrefixLen (a : string) (b : string) : int =
        let minLen = min a.Length b.Length
        let mutable i = 0
        while i < minLen && a.[i] = b.[i] do
            i <- i + 1
        i

    /// Build an MST from a sorted list of (key, valueCid) pairs.
    /// Higher heightForKey values = closer to root. Layer 0 = leaf level.
    let create (entries : (string * Cid) list) : Node =
        let sorted = entries |> List.sortBy fst

        if sorted.IsEmpty then
            empty
        else
            let maxLayer =
                sorted |> List.map (fun (k, _) -> heightForKey k) |> List.max

            let rec buildLayer (items : (string * Cid) list) (layer : int) : Node option =
                if items.IsEmpty then
                    None
                elif layer < 0 then
                    None
                else
                    let hasLayerEntries =
                        items |> List.exists (fun (k, _) -> heightForKey k = layer)

                    if not hasLayerEntries then
                        // No entries at this layer - create pass-through node to lower layer
                        match buildLayer items (layer - 1) with
                        | Some leftNode -> Some { Left = Some leftNode; Entries = [] }
                        | None -> None
                    else
                        // Split items into segments around layer entries
                        let mutable segments = [] // (before_items, key, value)
                        let mutable current = []

                        for (key, value) in items do
                            if heightForKey key = layer then
                                segments <- (List.rev current, key, value) :: segments
                                current <- []
                            else
                                current <- (key, value) :: current

                        let trailing = List.rev current
                        let segments = List.rev segments

                        // First segment's items → node's left
                        let (firstItems, _, _) = segments.Head
                        let leftNode = buildLayer firstItems (layer - 1)

                        // Each entry's tree → next segment's items or trailing
                        let nodeEntries =
                            segments
                            |> List.mapi (fun i (_, key, value) ->
                                let treeItems =
                                    if i + 1 < segments.Length then
                                        let (items, _, _) = segments.[i + 1]
                                        items
                                    else
                                        trailing

                                { Key = key
                                  Value = value
                                  Tree = buildLayer treeItems (layer - 1) })

                        Some { Left = leftNode; Entries = nodeEntries }

            match buildLayer sorted maxLayer with
            | Some node -> node
            | None -> empty

    /// Serialize an MST node to DAG-CBOR and return (CID, block map).
    let rec serialize (node : Node) : Cid * Map<string, byte[]> =
        let mutable blocks = Map.empty

        let leftLink =
            match node.Left with
            | Some left ->
                let (cid, childBlocks) = serialize left
                blocks <- Map.foldBack Map.add childBlocks blocks
                AtpValue.Link cid
            | None -> AtpValue.Null

        let mutable prevKey = ""

        let cborEntries =
            node.Entries
            |> List.map (fun entry ->
                let prefixLen = commonPrefixLen prevKey entry.Key
                let suffix = Encoding.UTF8.GetBytes (entry.Key.Substring prefixLen)

                let treeLink =
                    match entry.Tree with
                    | Some tree ->
                        let (cid, childBlocks) = serialize tree
                        blocks <- Map.foldBack Map.add childBlocks blocks
                        AtpValue.Link cid
                    | None -> AtpValue.Null

                prevKey <- entry.Key

                AtpValue.Object (
                    Map.ofList
                        [ "k", AtpValue.Bytes suffix
                          "p", AtpValue.Integer (int64 prefixLen)
                          "t", treeLink
                          "v", AtpValue.Link entry.Value ]
                ))

        let nodeValue =
            AtpValue.Object (
                Map.ofList [ "e", AtpValue.Array cborEntries; "l", leftLink ]
            )

        let nodeBytes = Drisl.encode nodeValue
        let cid = CidBinary.compute nodeBytes
        blocks <- blocks |> Map.add (Cid.value cid) nodeBytes
        (cid, blocks)

    /// Deserialize an MST from a block store starting from a root CID.
    let rec deserialize (blocks : Map<string, byte[]>) (cid : Cid) : Result<Node, string> =
        match blocks |> Map.tryFind (Cid.value cid) with
        | None -> Error (sprintf "Block not found: %s" (Cid.value cid))
        | Some data ->
            match Drisl.decode data with
            | Error e -> Error (sprintf "CBOR decode error: %s" e)
            | Ok (AtpValue.Object map) ->
                let left =
                    match map |> Map.tryFind "l" with
                    | Some (AtpValue.Link leftCid) ->
                        match deserialize blocks leftCid with
                        | Ok node -> Some node
                        | Error e -> failwith e
                    | _ -> None

                let entries =
                    match map |> Map.tryFind "e" with
                    | Some (AtpValue.Array items) ->
                        let mutable prevKey = ""

                        items
                        |> List.map (fun item ->
                            match item with
                            | AtpValue.Object entryMap ->
                                let p =
                                    match entryMap |> Map.tryFind "p" with
                                    | Some (AtpValue.Integer n) -> int n
                                    | _ -> 0

                                let k =
                                    match entryMap |> Map.tryFind "k" with
                                    | Some (AtpValue.Bytes b) -> Encoding.UTF8.GetString b
                                    | _ -> ""

                                let key = prevKey.Substring (0, p) + k
                                prevKey <- key

                                let v =
                                    match entryMap |> Map.tryFind "v" with
                                    | Some (AtpValue.Link c) -> c
                                    | _ -> failwith "Entry missing value CID"

                                let tree =
                                    match entryMap |> Map.tryFind "t" with
                                    | Some (AtpValue.Link treeCid) ->
                                        match deserialize blocks treeCid with
                                        | Ok node -> Some node
                                        | Error e -> failwith e
                                    | _ -> None

                                { Key = key; Value = v; Tree = tree }
                            | _ -> failwith "Expected entry object")
                    | _ -> []

                Ok { Left = left; Entries = entries }
            | _ -> Error "Expected CBOR map for MST node"

    /// Collect all (key, value) pairs from an MST in sorted order.
    let rec allEntries (node : Node) : (string * Cid) list =
        let leftEntries =
            match node.Left with
            | Some left -> allEntries left
            | None -> []

        let rest =
            node.Entries
            |> List.collect (fun entry ->
                let treeEntries =
                    match entry.Tree with
                    | Some tree -> allEntries tree
                    | None -> []

                (entry.Key, entry.Value) :: treeEntries)

        leftEntries @ rest

    /// Insert a key-value pair into the MST. Rebuilds the tree.
    let insert (key : string) (value : Cid) (node : Node) : Node =
        let existing = allEntries node
        let filtered = existing |> List.filter (fun (k, _) -> k <> key)
        create ((key, value) :: filtered)

    /// Delete a key from the MST. Rebuilds the tree.
    let delete (key : string) (node : Node) : Node =
        let existing = allEntries node
        let filtered = existing |> List.filter (fun (k, _) -> k <> key)
        create filtered

    /// Look up a key in the MST.
    let rec lookup (key : string) (node : Node) : Cid option =
        let rec searchEntries (entries : Entry list) =
            match entries with
            | [] -> None
            | e :: rest ->
                if key = e.Key then
                    Some e.Value
                elif key < e.Key then
                    None
                elif rest.IsEmpty then
                    match e.Tree with
                    | Some tree -> lookup key tree
                    | None -> None
                else
                    let nextE = rest.Head

                    if key < nextE.Key then
                        match e.Tree with
                        | Some tree -> lookup key tree
                        | None -> None
                    else
                        searchEntries rest

        match node.Entries with
        | e :: _ when key < e.Key ->
            match node.Left with
            | Some left -> lookup key left
            | None -> None
        | _ -> searchEntries node.Entries

    /// Diff two MSTs, returning (added, updated, deleted) key-value pairs.
    let diff (oldNode : Node) (newNode : Node) : (string * Cid) list * (string * Cid) list * string list =
        let oldEntries = allEntries oldNode |> Map.ofList
        let newEntries = allEntries newNode |> Map.ofList

        let added =
            newEntries
            |> Map.toList
            |> List.filter (fun (k, _) -> not (oldEntries |> Map.containsKey k))

        let updated =
            newEntries
            |> Map.toList
            |> List.filter (fun (k, v) ->
                match oldEntries |> Map.tryFind k with
                | Some oldV -> oldV <> v
                | None -> false)

        let deleted =
            oldEntries
            |> Map.toList
            |> List.filter (fun (k, _) -> not (newEntries |> Map.containsKey k))
            |> List.map fst

        (added, updated, deleted)
