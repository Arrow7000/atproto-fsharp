namespace FSharp.ATProto.Repo

open System
open System.Text
open FSharp.ATProto.Syntax
open FSharp.ATProto.DRISL
open FSharp.ATProto.Crypto

/// Signed commit objects for AT Protocol repositories.
module Commit =

    /// A signed commit object.
    type SignedCommit =
        { Did : Did
          Version : int
          Data : Cid
          Rev : string
          Prev : Cid option
          Sig : byte[] }

    /// Create a TID-based revision string for the current time.
    let createRev () : string =
        let now = DateTimeOffset.UtcNow
        let micros = now.ToUnixTimeMilliseconds () * 1000L + int64 (now.Millisecond % 1000)
        // TID: 13-char base32-sortable encoding of microseconds since epoch
        let chars = "234567abcdefghijklmnopqrstuvwxyz"
        let mutable value = uint64 micros
        let result = Array.create 13 '2'

        for i in 12 .. -1 .. 0 do
            result.[i] <- chars.[int (value &&& 31UL)]
            value <- value >>> 5

        String result

    /// Encode a commit to DAG-CBOR bytes (without the signature field, for signing).
    let private encodeUnsigned (commit : SignedCommit) : byte[] =
        let prev =
            match commit.Prev with
            | Some cid -> AtpValue.Link cid
            | None -> AtpValue.Null

        let map =
            Map.ofList
                [ "did", AtpValue.String (Did.value commit.Did)
                  "version", AtpValue.Integer (int64 commit.Version)
                  "data", AtpValue.Link commit.Data
                  "rev", AtpValue.String commit.Rev
                  "prev", prev ]

        Drisl.encode (AtpValue.Object map)

    /// Encode a signed commit to DAG-CBOR bytes (with signature).
    let encode (commit : SignedCommit) : byte[] =
        let prev =
            match commit.Prev with
            | Some cid -> AtpValue.Link cid
            | None -> AtpValue.Null

        let map =
            Map.ofList
                [ "did", AtpValue.String (Did.value commit.Did)
                  "version", AtpValue.Integer (int64 commit.Version)
                  "data", AtpValue.Link commit.Data
                  "rev", AtpValue.String commit.Rev
                  "prev", prev
                  "sig", AtpValue.Bytes commit.Sig ]

        Drisl.encode (AtpValue.Object map)

    /// Create and sign a commit.
    let create
        (did : Did)
        (data : Cid)
        (rev : string)
        (prev : Cid option)
        (keyPair : KeyPair)
        : SignedCommit =
        let unsigned =
            { Did = did
              Version = 3
              Data = data
              Rev = rev
              Prev = prev
              Sig = Array.empty }

        let unsignedBytes = encodeUnsigned unsigned
        let signature = Signing.sign keyPair unsignedBytes
        { unsigned with Sig = signature }

    /// Decode a signed commit from DAG-CBOR bytes.
    let decode (data : byte[]) : Result<SignedCommit, string> =
        match Drisl.decode data with
        | Error e -> Error (sprintf "CBOR decode error: %s" e)
        | Ok (AtpValue.Object map) ->
            let did =
                match map |> Map.tryFind "did" with
                | Some (AtpValue.String s) ->
                    match Did.parse s with
                    | Ok d -> d
                    | Error e -> failwith (sprintf "Invalid DID: %s" e)
                | _ -> failwith "Missing 'did' field"

            let version =
                match map |> Map.tryFind "version" with
                | Some (AtpValue.Integer n) -> int n
                | _ -> 3

            let dataCid =
                match map |> Map.tryFind "data" with
                | Some (AtpValue.Link cid) -> cid
                | _ -> failwith "Missing 'data' field"

            let rev =
                match map |> Map.tryFind "rev" with
                | Some (AtpValue.String s) -> s
                | _ -> failwith "Missing 'rev' field"

            let prev =
                match map |> Map.tryFind "prev" with
                | Some (AtpValue.Link cid) -> Some cid
                | _ -> None

            let sig' =
                match map |> Map.tryFind "sig" with
                | Some (AtpValue.Bytes b) -> b
                | _ -> failwith "Missing 'sig' field"

            Ok
                { Did = did
                  Version = version
                  Data = dataCid
                  Rev = rev
                  Prev = prev
                  Sig = sig' }
        | _ -> Error "Expected CBOR map for commit"

    /// Verify a signed commit's signature against a public key.
    let verify (publicKey : PublicKey) (commitBytes : byte[]) : Result<bool, string> =
        match decode commitBytes with
        | Error e -> Error e
        | Ok commit ->
            let unsigned =
                { commit with Sig = Array.empty }

            let unsignedBytes = encodeUnsigned unsigned
            Ok (Signing.verify publicKey unsignedBytes commit.Sig)
