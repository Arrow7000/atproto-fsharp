namespace FSharp.ATProto.Bluesky

open System.Text.Json
open System.Threading.Tasks
open FSharp.ATProto.Core
open FSharp.ATProto.Syntax

/// <summary>
/// Errors that can occur during identity resolution.
/// </summary>
type IdentityError =
    /// <summary>An XRPC call failed (e.g., handle resolution).</summary>
    | XrpcError of XrpcError
    /// <summary>Bidirectional verification failed (e.g., handle-DID mismatch).</summary>
    | VerificationFailed of string
    /// <summary>A DID document could not be fetched or parsed.</summary>
    | DocumentParseError of string

/// <summary>
/// AT Protocol identity resolution: DID documents, handle resolution, and bidirectional verification.
/// Supports both <c>did:plc</c> (via PLC directory) and <c>did:web</c> (via .well-known) methods.
/// </summary>
module Identity =

    /// <summary>
    /// A resolved AT Protocol identity containing the DID and optional metadata extracted from the DID document.
    /// </summary>
    type AtprotoIdentity =
        { /// <summary>The decentralized identifier (e.g., <c>did:plc:z72i7hdynmk6r22z27h6tvur</c>).</summary>
          Did: string
          /// <summary>The handle claimed in the DID document's <c>alsoKnownAs</c> field, if present and verified.</summary>
          Handle: string option
          /// <summary>The PDS (Personal Data Server) endpoint URL from the DID document's service entries.</summary>
          PdsEndpoint: string option
          /// <summary>The atproto signing key in multibase encoding from the DID document's verification methods.</summary>
          SigningKey: string option }

    let private tryGetString (element: JsonElement) (prop: string) =
        match element.TryGetProperty(prop) with
        | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
        | _ -> None

    let private tryGetArray (element: JsonElement) (prop: string) =
        match element.TryGetProperty(prop) with
        | true, v when v.ValueKind = JsonValueKind.Array -> Some(v.EnumerateArray() |> Seq.toList)
        | _ -> None

    let private extractHandle (doc: JsonElement) =
        tryGetArray doc "alsoKnownAs"
        |> Option.bind (fun entries ->
            entries
            |> List.tryPick (fun e ->
                if e.ValueKind = JsonValueKind.String then
                    let s = e.GetString()
                    if s.StartsWith("at://") then Some(s.Substring(5))
                    else None
                else None))

    let private extractPdsEndpoint (doc: JsonElement) =
        tryGetArray doc "service"
        |> Option.bind (fun services ->
            services
            |> List.tryPick (fun svc ->
                let id = tryGetString svc "id"
                let typ = tryGetString svc "type"
                let endpoint = tryGetString svc "serviceEndpoint"
                match id, typ, endpoint with
                | Some id, Some "AtprotoPersonalDataServer", Some ep
                    when id.EndsWith("#atproto_pds") -> Some ep
                | _ -> None))

    let private extractSigningKey (doc: JsonElement) =
        tryGetArray doc "verificationMethod"
        |> Option.bind (fun methods ->
            methods
            |> List.tryPick (fun vm ->
                let id = tryGetString vm "id"
                let key = tryGetString vm "publicKeyMultibase"
                match id, key with
                | Some id, Some k when id.EndsWith("#atproto") -> Some k
                | _ -> None))

    /// <summary>
    /// Parse a DID document JSON into an <see cref="AtprotoIdentity"/>.
    /// Extracts the DID, handle (from <c>alsoKnownAs</c>), PDS endpoint (from <c>service</c>),
    /// and signing key (from <c>verificationMethod</c>).
    /// </summary>
    /// <param name="doc">A JSON element representing the DID document.</param>
    /// <returns>
    /// <c>Ok</c> with the parsed identity, or <c>Error</c> if the document is missing the required <c>id</c> field.
    /// Optional fields (handle, PDS endpoint, signing key) are <c>None</c> if absent from the document.
    /// </returns>
    let parseDidDocument (doc: JsonElement) : Result<AtprotoIdentity, string> =
        match tryGetString doc "id" with
        | None -> Error "DID document missing 'id' field"
        | Some did ->
            Ok { Did = did
                 Handle = extractHandle doc
                 PdsEndpoint = extractPdsEndpoint doc
                 SigningKey = extractSigningKey doc }

    let private plcDirectoryUrl = "https://plc.directory"

    /// <summary>
    /// Resolve a DID to an <see cref="AtprotoIdentity"/> by fetching its DID document.
    /// </summary>
    /// <param name="agent">An <see cref="AtpAgent"/> whose <c>HttpClient</c> is used for the HTTP request.</param>
    /// <param name="did">
    /// The DID to resolve. Must start with <c>did:plc:</c> (resolved via PLC directory)
    /// or <c>did:web:</c> (resolved via <c>.well-known/did.json</c>).
    /// </param>
    /// <returns>
    /// <c>Ok</c> with the parsed identity on success, or <c>Error</c> with an <see cref="IdentityError"/>
    /// on HTTP failure or unsupported DID method.
    /// </returns>
    let resolveDid (agent: AtpAgent) (did: Did) : Task<Result<AtprotoIdentity, IdentityError>> =
        task {
            let didStr = Did.value did
            if didStr.StartsWith("did:plc:") then
                let url = $"{plcDirectoryUrl}/{didStr}"
                let! response = agent.HttpClient.GetAsync(url)
                if response.IsSuccessStatusCode then
                    let! json = response.Content.ReadAsStringAsync()
                    let doc = JsonSerializer.Deserialize<JsonElement>(json)
                    return parseDidDocument doc |> Result.mapError DocumentParseError
                else
                    return Error (DocumentParseError $"PLC directory returned {int response.StatusCode} for {didStr}")
            elif didStr.StartsWith("did:web:") then
                let domain = didStr.Substring(8)
                let url = $"https://{domain}/.well-known/did.json"
                let! response = agent.HttpClient.GetAsync(url)
                if response.IsSuccessStatusCode then
                    let! json = response.Content.ReadAsStringAsync()
                    let doc = JsonSerializer.Deserialize<JsonElement>(json)
                    return parseDidDocument doc |> Result.mapError DocumentParseError
                else
                    return Error (DocumentParseError $"did:web resolution returned {int response.StatusCode} for {didStr}")
            else
                return Error (DocumentParseError $"Unsupported DID method: {didStr}")
        }

    /// <summary>
    /// Resolve a handle to its DID via <c>com.atproto.identity.resolveHandle</c>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="handle">The handle to resolve (e.g., <c>alice.bsky.social</c>).</param>
    /// <returns>
    /// <c>Ok</c> with the resolved <see cref="Did"/> on success, or <c>Error</c> with an <see cref="IdentityError"/>
    /// if the handle cannot be resolved.
    /// </returns>
    let resolveHandle (agent: AtpAgent) (handle: Handle) : Task<Result<Did, IdentityError>> =
        task {
            let! result = ComAtprotoIdentity.ResolveHandle.query agent { Handle = handle }
            return result |> Result.map (fun o -> o.Did) |> Result.mapError XrpcError
        }

    /// <summary>
    /// Fully resolve an AT Protocol identity with bidirectional verification.
    /// Accepts either a DID or a handle and performs the forward + reverse resolution
    /// needed to confirm the handle-DID binding.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="identifier">
    /// A DID (starting with <c>did:</c>) or a handle (e.g., <c>alice.bsky.social</c>).
    /// </param>
    /// <returns>
    /// <c>Ok</c> with the resolved <see cref="AtprotoIdentity"/>. If bidirectional verification
    /// fails (the reverse lookup does not match), the <c>Handle</c> field is set to <c>None</c>
    /// but the identity is still returned. Returns <c>Error</c> on resolution failure.
    /// </returns>
    /// <remarks>
    /// Bidirectional verification ensures that both directions of the DID-handle binding agree:
    /// the DID document must list the handle in <c>alsoKnownAs</c>, and resolving that handle
    /// must return the same DID. If either direction fails, the handle is cleared but the
    /// identity (DID, PDS endpoint, signing key) is still returned.
    /// </remarks>
    /// <example>
    /// <code>
    /// let! identity = Identity.resolveIdentity agent "alice.bsky.social"
    /// match identity with
    /// | Ok id -> printfn "DID: %s, Handle verified: %b" id.Did id.Handle.IsSome
    /// | Error msg -> printfn "Resolution failed: %A" msg
    /// </code>
    /// </example>
    let resolveIdentity (agent: AtpAgent) (identifier: string) : Task<Result<AtprotoIdentity, IdentityError>> =
        task {
            let isDid = identifier.StartsWith("did:")
            if isDid then
                let did = Did.parse identifier |> Result.defaultWith (fun e -> failwith $"Invalid DID: {e}")
                let! identity = resolveDid agent did
                match identity with
                | Error e -> return Error e
                | Ok id ->
                    match id.Handle with
                    | None -> return Ok id
                    | Some handle ->
                        let handleTyped = Handle.parse handle |> Result.defaultWith (fun _ -> failwith $"Invalid handle in DID doc: {handle}")
                        let! reverseResult = resolveHandle agent handleTyped
                        match reverseResult with
                        | Ok reverseDid when Did.value reverseDid = identifier -> return Ok id
                        | _ -> return Ok { id with Handle = None }
            else
                // identifier is a handle
                let handleTyped = Handle.parse identifier |> Result.defaultWith (fun _ -> failwith $"Invalid handle: {identifier}")
                let! handleResult = resolveHandle agent handleTyped
                match handleResult with
                | Error e -> return Error e
                | Ok did ->
                    let! identity = resolveDid agent did
                    match identity with
                    | Error e -> return Error e
                    | Ok id ->
                        // Bidirectional: check DID doc's handle matches
                        if id.Handle = Some identifier then return Ok id
                        else return Ok { id with Handle = None }
        }
