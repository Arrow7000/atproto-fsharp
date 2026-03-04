namespace FSharp.ATProto.Core

open System
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks
open FSharp.ATProto.Syntax

/// <summary>
/// Client for the PLC Directory -- the public ledger for <c>did:plc:*</c> DIDs.
/// Provides read operations (resolve, audit log, export) that require no authentication,
/// and operation construction/signing helpers for DID document updates.
/// </summary>
/// <remarks>
/// The PLC Directory is a centralized but auditable registry. All read endpoints are public
/// and require only an <see cref="System.Net.Http.HttpClient"/>. Write operations (creating
/// or updating DID documents) require signing with a rotation key, but this module does NOT
/// depend on <c>FSharp.ATProto.Crypto</c> directly -- signing is injected via a
/// <c>byte[] -> byte[]</c> function parameter.
/// </remarks>
module Plc =

    /// Default PLC Directory base URL.
    let [<Literal>] DefaultBaseUrl = "https://plc.directory"

    // -----------------------------------------------------------------------
    // Types
    // -----------------------------------------------------------------------

    /// <summary>
    /// A service endpoint entry in a PLC operation.
    /// Maps a service identifier (e.g. <c>"atproto_pds"</c>) to its type and endpoint URL.
    /// </summary>
    type PlcService =
        { /// <summary>The service type (e.g. <c>"AtprotoPersonalDataServer"</c>).</summary>
          Type : string
          /// <summary>The service endpoint URL (e.g. <c>"https://bsky.social"</c>).</summary>
          Endpoint : string }

    /// <summary>
    /// A resolved DID document from the PLC Directory.
    /// This is the W3C DID Document format returned by <c>GET /{did}</c>.
    /// </summary>
    type PlcDocument =
        { /// <summary>The DID this document describes.</summary>
          Did : Did
          /// <summary>Alternative identifiers (e.g. <c>["at://handle.bsky.social"]</c>).</summary>
          AlsoKnownAs : string list
          /// <summary>Verification methods keyed by fragment ID (e.g. <c>"atproto"</c> -> <c>"did:key:z..."</c>).</summary>
          VerificationMethods : Map<string, string>
          /// <summary>Rotation keys that control this DID, as <c>did:key</c> multibase strings.</summary>
          RotationKeys : string list
          /// <summary>Service endpoints keyed by service ID (e.g. <c>"atproto_pds"</c>).</summary>
          Services : Map<string, PlcService> }

    /// <summary>
    /// The type of a PLC operation.
    /// </summary>
    [<RequireQualifiedAccess>]
    type PlcOperationType =
        /// <summary>A creation or update operation.</summary>
        | PlcOperation
        /// <summary>A tombstone that deactivates the DID.</summary>
        | PlcTombstone
        /// <summary>A legacy genesis operation (deprecated format).</summary>
        | Create

    /// <summary>
    /// A PLC operation that creates, updates, or tombstones a DID document.
    /// Operations are signed with a rotation key and submitted to the PLC Directory.
    /// </summary>
    type PlcOperation =
        { /// <summary>The operation type.</summary>
          Type : PlcOperationType
          /// <summary>Rotation keys (1-5 <c>did:key</c> values). Empty for tombstone operations.</summary>
          RotationKeys : string list
          /// <summary>Verification methods as a map of key ID to <c>did:key</c> value. Empty for tombstone operations.</summary>
          VerificationMethods : Map<string, string>
          /// <summary>Alternative identifiers (e.g. <c>["at://handle.bsky.social"]</c>). Empty for tombstone operations.</summary>
          AlsoKnownAs : string list
          /// <summary>Service endpoints keyed by service ID. Empty for tombstone operations.</summary>
          Services : Map<string, PlcService>
          /// <summary>CID of the previous operation (dag-cbor, sha-256), or <c>None</c> for genesis.</summary>
          Prev : string option
          /// <summary>Base64url-encoded ECDSA signature, or <c>None</c> if unsigned.</summary>
          Sig : string option }

    /// <summary>
    /// An entry in the PLC audit log, representing one historical operation on a DID.
    /// </summary>
    type AuditEntry =
        { /// <summary>The DID this operation applies to.</summary>
          Did : Did
          /// <summary>The signed operation.</summary>
          Operation : PlcOperation
          /// <summary>The CID of this operation (dag-cbor, sha-256, base32).</summary>
          Cid : string
          /// <summary>Whether this operation has been nullified.</summary>
          Nullified : bool
          /// <summary>When the PLC Directory accepted this operation.</summary>
          CreatedAt : DateTimeOffset }

    /// <summary>
    /// An entry in the PLC export stream. Same as <see cref="AuditEntry"/> but sourced
    /// from the global export endpoint rather than a per-DID audit log.
    /// </summary>
    type ExportEntry =
        { /// <summary>The DID this operation applies to.</summary>
          Did : Did
          /// <summary>The signed operation.</summary>
          Operation : PlcOperation
          /// <summary>The CID of this operation.</summary>
          Cid : string
          /// <summary>Whether this operation has been nullified.</summary>
          Nullified : bool
          /// <summary>When the PLC Directory accepted this operation.</summary>
          CreatedAt : DateTimeOffset }

    /// <summary>
    /// Error type for PLC Directory operations.
    /// </summary>
    [<RequireQualifiedAccess>]
    type PlcError =
        /// <summary>HTTP request failed with the given status code and body.</summary>
        | HttpError of statusCode : int * body : string
        /// <summary>The response body could not be parsed.</summary>
        | ParseError of message : string
        /// <summary>The DID was not found (404).</summary>
        | NotFound of did : string

    // -----------------------------------------------------------------------
    // Internal JSON parsing helpers
    // -----------------------------------------------------------------------

    let private base64UrlEncode (bytes : byte[]) : string =
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')

    let private parseOperationType (s : string) : PlcOperationType =
        match s with
        | "plc_operation" -> PlcOperationType.PlcOperation
        | "plc_tombstone" -> PlcOperationType.PlcTombstone
        | "create" -> PlcOperationType.Create
        | other -> failwithf "Unknown PLC operation type: %s" other

    let private operationTypeToString (t : PlcOperationType) : string =
        match t with
        | PlcOperationType.PlcOperation -> "plc_operation"
        | PlcOperationType.PlcTombstone -> "plc_tombstone"
        | PlcOperationType.Create -> "create"

    let private tryGetString (element : JsonElement) (prop : string) : string option =
        match element.TryGetProperty prop with
        | true, v when v.ValueKind = JsonValueKind.String -> Some (v.GetString ())
        | true, v when v.ValueKind = JsonValueKind.Null -> None
        | _ -> None

    let private getStringList (element : JsonElement) (prop : string) : string list =
        match element.TryGetProperty prop with
        | true, v when v.ValueKind = JsonValueKind.Array ->
            [ for item in v.EnumerateArray () -> item.GetString () ]
        | _ -> []

    let private getStringMap (element : JsonElement) (prop : string) : Map<string, string> =
        match element.TryGetProperty prop with
        | true, v when v.ValueKind = JsonValueKind.Object ->
            [ for kv in v.EnumerateObject () -> kv.Name, kv.Value.GetString () ]
            |> Map.ofList
        | _ -> Map.empty

    let private getServicesMap (element : JsonElement) (prop : string) : Map<string, PlcService> =
        match element.TryGetProperty prop with
        | true, v when v.ValueKind = JsonValueKind.Object ->
            [ for kv in v.EnumerateObject () ->
                  let svcType =
                      match kv.Value.TryGetProperty "type" with
                      | true, t -> t.GetString ()
                      | _ -> ""

                  let endpoint =
                      match kv.Value.TryGetProperty "endpoint" with
                      | true, e -> e.GetString ()
                      | _ -> ""

                  kv.Name,
                  { Type = svcType
                    Endpoint = endpoint } ]
            |> Map.ofList
        | _ -> Map.empty

    let private parseOperation (element : JsonElement) : PlcOperation =
        let opType =
            match element.TryGetProperty "type" with
            | true, v -> parseOperationType (v.GetString ())
            | _ -> PlcOperationType.PlcOperation

        { Type = opType
          RotationKeys = getStringList element "rotationKeys"
          VerificationMethods = getStringMap element "verificationMethods"
          AlsoKnownAs = getStringList element "alsoKnownAs"
          Services = getServicesMap element "services"
          Prev = tryGetString element "prev"
          Sig = tryGetString element "sig" }

    let private parseAuditEntry (element : JsonElement) : Result<AuditEntry, string> =
        try
            let didStr =
                match element.TryGetProperty "did" with
                | true, v -> v.GetString ()
                | _ -> failwith "Missing 'did' field"

            let did =
                match Did.parse didStr with
                | Ok d -> d
                | Error e -> failwith (sprintf "Invalid DID '%s': %s" didStr e)

            let operation =
                match element.TryGetProperty "operation" with
                | true, v -> parseOperation v
                | _ -> failwith "Missing 'operation' field"

            let cid =
                match element.TryGetProperty "cid" with
                | true, v -> v.GetString ()
                | _ -> failwith "Missing 'cid' field"

            let nullified =
                match element.TryGetProperty "nullified" with
                | true, v -> v.GetBoolean ()
                | _ -> false

            let createdAt =
                match element.TryGetProperty "createdAt" with
                | true, v -> DateTimeOffset.Parse (v.GetString ())
                | _ -> failwith "Missing 'createdAt' field"

            Ok
                { Did = did
                  Operation = operation
                  Cid = cid
                  Nullified = nullified
                  CreatedAt = createdAt }
        with ex ->
            Error (sprintf "Failed to parse audit entry: %s" ex.Message)

    /// Parse a DID document from the PLC Directory JSON response.
    let private parseDidDocument (element : JsonElement) : Result<PlcDocument, string> =
        try
            let id =
                match element.TryGetProperty "id" with
                | true, v -> v.GetString ()
                | _ -> failwith "Missing 'id' field"

            let did =
                match Did.parse id with
                | Ok d -> d
                | Error e -> failwith (sprintf "Invalid DID '%s': %s" id e)

            let alsoKnownAs = getStringList element "alsoKnownAs"

            // verificationMethod is an array of objects with id, type, controller, publicKeyMultibase
            let verificationMethods =
                match element.TryGetProperty "verificationMethod" with
                | true, v when v.ValueKind = JsonValueKind.Array ->
                    [ for item in v.EnumerateArray () ->
                          let vmId =
                              match item.TryGetProperty "id" with
                              | true, idVal ->
                                  let s = idVal.GetString ()
                                  // id is like "did:plc:xxx#atproto" or "#atproto" -- extract fragment
                                  match s.IndexOf '#' with
                                  | -1 -> s
                                  | i -> s.Substring (i + 1)
                              | _ -> ""

                          let pubKey =
                              match item.TryGetProperty "publicKeyMultibase" with
                              | true, pk -> pk.GetString ()
                              | _ -> ""

                          vmId, pubKey ]
                    |> Map.ofList
                | _ -> Map.empty

            // service is an array of objects with id, type, serviceEndpoint
            let services =
                match element.TryGetProperty "service" with
                | true, v when v.ValueKind = JsonValueKind.Array ->
                    [ for item in v.EnumerateArray () ->
                          let svcId =
                              match item.TryGetProperty "id" with
                              | true, idVal ->
                                  let s = idVal.GetString ()
                                  match s.IndexOf '#' with
                                  | -1 -> s
                                  | i -> s.Substring (i + 1)
                              | _ -> ""

                          let svcType =
                              match item.TryGetProperty "type" with
                              | true, t -> t.GetString ()
                              | _ -> ""

                          let endpoint =
                              match item.TryGetProperty "serviceEndpoint" with
                              | true, e -> e.GetString ()
                              | _ -> ""

                          svcId,
                          { Type = svcType
                            Endpoint = endpoint } ]
                    |> Map.ofList
                | _ -> Map.empty

            // rotationKeys are not in the DID document response; they come from the audit log
            Ok
                { Did = did
                  AlsoKnownAs = alsoKnownAs
                  VerificationMethods = verificationMethods
                  RotationKeys = []
                  Services = services }
        with ex ->
            Error (sprintf "Failed to parse DID document: %s" ex.Message)

    // -----------------------------------------------------------------------
    // Operation construction
    // -----------------------------------------------------------------------

    /// <summary>
    /// Create an unsigned genesis operation (the first operation for a new DID).
    /// The <c>prev</c> field is <c>None</c> because there is no prior operation.
    /// </summary>
    /// <param name="rotationKeys">The rotation keys (1-5 <c>did:key</c> values) that will control this DID.</param>
    /// <param name="verificationMethods">Verification methods as a map of key ID to <c>did:key</c> value.</param>
    /// <param name="alsoKnownAs">Alternative identifiers (e.g. <c>["at://handle.bsky.social"]</c>).</param>
    /// <param name="services">Service endpoints keyed by service ID.</param>
    /// <returns>An unsigned <see cref="PlcOperation"/> with <c>prev = None</c> and <c>sig = None</c>.</returns>
    let createGenesisOp
        (rotationKeys : string list)
        (verificationMethods : Map<string, string>)
        (alsoKnownAs : string list)
        (services : Map<string, PlcService>)
        : PlcOperation =
        { Type = PlcOperationType.PlcOperation
          RotationKeys = rotationKeys
          VerificationMethods = verificationMethods
          AlsoKnownAs = alsoKnownAs
          Services = services
          Prev = None
          Sig = None }

    /// <summary>
    /// Create an unsigned rotation (update) operation that modifies the DID document.
    /// </summary>
    /// <param name="prev">The CID of the previous operation in the log.</param>
    /// <param name="rotationKeys">The new rotation keys.</param>
    /// <param name="verificationMethods">The new verification methods.</param>
    /// <param name="alsoKnownAs">The new alternative identifiers.</param>
    /// <param name="services">The new service endpoints.</param>
    /// <returns>An unsigned <see cref="PlcOperation"/> with the given <c>prev</c> CID.</returns>
    let createRotationOp
        (prev : string)
        (rotationKeys : string list)
        (verificationMethods : Map<string, string>)
        (alsoKnownAs : string list)
        (services : Map<string, PlcService>)
        : PlcOperation =
        { Type = PlcOperationType.PlcOperation
          RotationKeys = rotationKeys
          VerificationMethods = verificationMethods
          AlsoKnownAs = alsoKnownAs
          Services = services
          Prev = Some prev
          Sig = None }

    /// <summary>
    /// Create an unsigned tombstone operation that deactivates the DID.
    /// </summary>
    /// <param name="prev">The CID of the previous operation in the log.</param>
    /// <returns>An unsigned tombstone <see cref="PlcOperation"/>.</returns>
    let createTombstoneOp (prev : string) : PlcOperation =
        { Type = PlcOperationType.PlcTombstone
          RotationKeys = []
          VerificationMethods = Map.empty
          AlsoKnownAs = []
          Services = Map.empty
          Prev = Some prev
          Sig = None }

    // -----------------------------------------------------------------------
    // Operation serialization (for signing)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Serialize a PLC operation to its canonical JSON bytes for signing.
    /// The <c>sig</c> field is omitted from the signing input per the PLC spec.
    /// </summary>
    let serializeForSigning (op : PlcOperation) : byte[] =
        use ms = new IO.MemoryStream ()
        use writer = new Utf8JsonWriter (ms, JsonWriterOptions (Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping))
        writer.WriteStartObject ()
        writer.WriteString ("type", operationTypeToString op.Type)

        match op.Type with
        | PlcOperationType.PlcOperation ->
            // rotationKeys
            writer.WriteStartArray "rotationKeys"

            for key in op.RotationKeys do
                writer.WriteStringValue key

            writer.WriteEndArray ()
            // verificationMethods
            writer.WriteStartObject "verificationMethods"

            for kv in op.VerificationMethods do
                writer.WriteString (kv.Key, kv.Value)

            writer.WriteEndObject ()
            // alsoKnownAs
            writer.WriteStartArray "alsoKnownAs"

            for aka in op.AlsoKnownAs do
                writer.WriteStringValue aka

            writer.WriteEndArray ()
            // services
            writer.WriteStartObject "services"

            for kv in op.Services do
                writer.WriteStartObject kv.Key
                writer.WriteString ("type", kv.Value.Type)
                writer.WriteString ("endpoint", kv.Value.Endpoint)
                writer.WriteEndObject ()

            writer.WriteEndObject ()
        | PlcOperationType.PlcTombstone -> ()
        | PlcOperationType.Create -> ()

        // prev
        match op.Prev with
        | Some prev -> writer.WriteString ("prev", prev)
        | None -> writer.WriteNull "prev"

        // sig is NOT included in the signing input
        writer.WriteEndObject ()
        writer.Flush ()
        ms.ToArray ()

    /// <summary>
    /// Serialize a PLC operation to JSON bytes including the signature.
    /// Used for submitting to the PLC Directory.
    /// </summary>
    let serializeWithSig (op : PlcOperation) : byte[] =
        use ms = new IO.MemoryStream ()
        use writer = new Utf8JsonWriter (ms, JsonWriterOptions (Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping))
        writer.WriteStartObject ()
        writer.WriteString ("type", operationTypeToString op.Type)

        match op.Type with
        | PlcOperationType.PlcOperation ->
            writer.WriteStartArray "rotationKeys"

            for key in op.RotationKeys do
                writer.WriteStringValue key

            writer.WriteEndArray ()

            writer.WriteStartObject "verificationMethods"

            for kv in op.VerificationMethods do
                writer.WriteString (kv.Key, kv.Value)

            writer.WriteEndObject ()

            writer.WriteStartArray "alsoKnownAs"

            for aka in op.AlsoKnownAs do
                writer.WriteStringValue aka

            writer.WriteEndArray ()

            writer.WriteStartObject "services"

            for kv in op.Services do
                writer.WriteStartObject kv.Key
                writer.WriteString ("type", kv.Value.Type)
                writer.WriteString ("endpoint", kv.Value.Endpoint)
                writer.WriteEndObject ()

            writer.WriteEndObject ()
        | PlcOperationType.PlcTombstone -> ()
        | PlcOperationType.Create -> ()

        match op.Prev with
        | Some prev -> writer.WriteString ("prev", prev)
        | None -> writer.WriteNull "prev"

        match op.Sig with
        | Some s -> writer.WriteString ("sig", s)
        | None -> ()

        writer.WriteEndObject ()
        writer.Flush ()
        ms.ToArray ()

    // -----------------------------------------------------------------------
    // Operation signing
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sign a PLC operation with a rotation key.
    /// The sign function should produce a 64-byte compact ECDSA signature (r || s)
    /// over the input bytes. The data is the canonical JSON encoding of the operation
    /// (without the <c>sig</c> field).
    /// </summary>
    /// <param name="sign">A signing function that takes raw bytes and returns a 64-byte signature. Typically <c>Signing.sign keyPair</c> from the Crypto project.</param>
    /// <param name="op">The unsigned operation to sign.</param>
    /// <returns>The operation with the <c>sig</c> field populated.</returns>
    let signOperation (sign : byte[] -> byte[]) (op : PlcOperation) : PlcOperation =
        let signingInput = serializeForSigning op
        let signature = sign signingInput |> base64UrlEncode
        { op with Sig = Some signature }

    // -----------------------------------------------------------------------
    // Read operations (public, no auth)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolve a <c>did:plc:*</c> DID to its current DID document.
    /// Calls <c>GET {baseUrl}/{did}</c> on the PLC Directory.
    /// </summary>
    /// <param name="client">An <see cref="System.Net.Http.HttpClient"/> for making the request.</param>
    /// <param name="did">The DID to resolve.</param>
    /// <param name="baseUrl">The PLC Directory base URL. Defaults to <c>https://plc.directory</c>.</param>
    /// <returns>The resolved <see cref="PlcDocument"/> on success, or a <see cref="PlcError"/> on failure.</returns>
    let resolve
        (client : HttpClient)
        (did : Did)
        (baseUrl : string option)
        : Task<Result<PlcDocument, PlcError>> =
        task {
            let url = sprintf "%s/%s" (defaultArg baseUrl DefaultBaseUrl) (Did.value did)

            try
                let! response = client.GetAsync (url : string)

                if response.IsSuccessStatusCode then
                    let! body = response.Content.ReadAsStringAsync ()

                    try
                        let doc = JsonDocument.Parse body
                        let result = parseDidDocument doc.RootElement

                        match result with
                        | Ok plcDoc -> return Ok plcDoc
                        | Error msg -> return Error (PlcError.ParseError msg)
                    with ex ->
                        return Error (PlcError.ParseError (sprintf "JSON parse error: %s" ex.Message))
                elif int response.StatusCode = 404 then
                    return Error (PlcError.NotFound (Did.value did))
                else
                    let! body = response.Content.ReadAsStringAsync ()
                    return Error (PlcError.HttpError (int response.StatusCode, body))
            with ex ->
                return Error (PlcError.HttpError (0, sprintf "Request failed: %s" ex.Message))
        }

    /// <summary>
    /// Get the audit log for a <c>did:plc:*</c> DID.
    /// Calls <c>GET {baseUrl}/{did}/log/audit</c> on the PLC Directory.
    /// Returns the complete history of signed operations for the DID.
    /// </summary>
    /// <param name="client">An <see cref="System.Net.Http.HttpClient"/> for making the request.</param>
    /// <param name="did">The DID to get the audit log for.</param>
    /// <param name="baseUrl">The PLC Directory base URL. Defaults to <c>https://plc.directory</c>.</param>
    /// <returns>A list of <see cref="AuditEntry"/> on success, or a <see cref="PlcError"/> on failure.</returns>
    let getAuditLog
        (client : HttpClient)
        (did : Did)
        (baseUrl : string option)
        : Task<Result<AuditEntry list, PlcError>> =
        task {
            let url = sprintf "%s/%s/log/audit" (defaultArg baseUrl DefaultBaseUrl) (Did.value did)

            try
                let! response = client.GetAsync (url : string)

                if response.IsSuccessStatusCode then
                    let! body = response.Content.ReadAsStringAsync ()

                    try
                        let doc = JsonDocument.Parse body

                        if doc.RootElement.ValueKind <> JsonValueKind.Array then
                            return Error (PlcError.ParseError "Expected JSON array")
                        else
                            let results =
                                [ for item in doc.RootElement.EnumerateArray () -> parseAuditEntry item ]

                            let errors =
                                results |> List.choose (function Error e -> Some e | _ -> None)

                            if not (List.isEmpty errors) then
                                return Error (PlcError.ParseError (String.concat "; " errors))
                            else
                                let entries =
                                    results |> List.choose (function Ok e -> Some e | _ -> None)

                                return Ok entries
                    with ex ->
                        return Error (PlcError.ParseError (sprintf "JSON parse error: %s" ex.Message))
                elif int response.StatusCode = 404 then
                    return Error (PlcError.NotFound (Did.value did))
                else
                    let! body = response.Content.ReadAsStringAsync ()
                    return Error (PlcError.HttpError (int response.StatusCode, body))
            with ex ->
                return Error (PlcError.HttpError (0, sprintf "Request failed: %s" ex.Message))
        }

    /// <summary>
    /// Export operations from the PLC Directory.
    /// Calls <c>GET {baseUrl}/export?after={after}&amp;count={count}</c>.
    /// The export endpoint returns newline-delimited JSON (NDJSON).
    /// </summary>
    /// <param name="client">An <see cref="System.Net.Http.HttpClient"/> for making the request.</param>
    /// <param name="after">Optional cursor (ISO 8601 timestamp) to resume export from.</param>
    /// <param name="count">Optional maximum number of entries to return.</param>
    /// <param name="baseUrl">The PLC Directory base URL. Defaults to <c>https://plc.directory</c>.</param>
    /// <returns>A list of <see cref="ExportEntry"/> on success, or a <see cref="PlcError"/> on failure.</returns>
    let export
        (client : HttpClient)
        (after : string option)
        (count : int option)
        (baseUrl : string option)
        : Task<Result<ExportEntry list, PlcError>> =
        task {
            let baseUrlStr = defaultArg baseUrl DefaultBaseUrl
            let mutable queryParts = []

            match after with
            | Some a -> queryParts <- sprintf "after=%s" (Uri.EscapeDataString a) :: queryParts
            | None -> ()

            match count with
            | Some c -> queryParts <- sprintf "count=%d" c :: queryParts
            | None -> ()

            let queryString =
                if List.isEmpty queryParts then ""
                else "?" + (queryParts |> List.rev |> String.concat "&")

            let url = sprintf "%s/export%s" baseUrlStr queryString

            try
                let! response = client.GetAsync (url : string)

                if response.IsSuccessStatusCode then
                    let! body = response.Content.ReadAsStringAsync ()

                    try
                        let lines =
                            body.Split ([| '\n' |], StringSplitOptions.RemoveEmptyEntries)

                        let results =
                            lines
                            |> Array.map (fun line ->
                                let doc = JsonDocument.Parse line

                                match parseAuditEntry doc.RootElement with
                                | Ok entry ->
                                    Ok
                                        { Did = entry.Did
                                          Operation = entry.Operation
                                          Cid = entry.Cid
                                          Nullified = entry.Nullified
                                          CreatedAt = entry.CreatedAt }
                                | Error e -> Error e)
                            |> Array.toList

                        let errors =
                            results |> List.choose (function Error e -> Some e | _ -> None)

                        if not (List.isEmpty errors) then
                            return Error (PlcError.ParseError (String.concat "; " errors))
                        else
                            let entries =
                                results |> List.choose (function Ok e -> Some e | _ -> None)

                            return Ok entries
                    with ex ->
                        return Error (PlcError.ParseError (sprintf "NDJSON parse error: %s" ex.Message))
                else
                    let! body = response.Content.ReadAsStringAsync ()
                    return Error (PlcError.HttpError (int response.StatusCode, body))
            with ex ->
                return Error (PlcError.HttpError (0, sprintf "Request failed: %s" ex.Message))
        }

    /// <summary>
    /// Submit a signed PLC operation to the PLC Directory.
    /// Calls <c>POST {baseUrl}/{did}</c> with the serialized operation as the JSON body.
    /// </summary>
    /// <param name="client">An <see cref="System.Net.Http.HttpClient"/> for making the request.</param>
    /// <param name="did">The DID to submit the operation for.</param>
    /// <param name="op">The signed operation to submit. Must have a <c>Sig</c> value.</param>
    /// <param name="baseUrl">The PLC Directory base URL. Defaults to <c>https://plc.directory</c>.</param>
    /// <returns><c>Ok ()</c> on success, or a <see cref="PlcError"/> on failure.</returns>
    let submitOperation
        (client : HttpClient)
        (did : Did)
        (op : PlcOperation)
        (baseUrl : string option)
        : Task<Result<unit, PlcError>> =
        task {
            match op.Sig with
            | None ->
                return Error (PlcError.ParseError "Operation must be signed before submitting")
            | Some _ ->
                let url = sprintf "%s/%s" (defaultArg baseUrl DefaultBaseUrl) (Did.value did)
                let body = serializeWithSig op
                let content = new StringContent (Encoding.UTF8.GetString body, Encoding.UTF8, "application/json")

                try
                    let! response = client.PostAsync (url, content)

                    if response.IsSuccessStatusCode then
                        return Ok ()
                    else
                        let! responseBody = response.Content.ReadAsStringAsync ()
                        return Error (PlcError.HttpError (int response.StatusCode, responseBody))
                with ex ->
                    return Error (PlcError.HttpError (0, sprintf "Request failed: %s" ex.Message))
        }
