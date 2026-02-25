namespace FSharp.ATProto.Bluesky

open System.Text.Json
open System.Threading.Tasks
open FSharp.ATProto.Core

module Identity =

    type AtprotoIdentity =
        { Did: string
          Handle: string option
          PdsEndpoint: string option
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

    let parseDidDocument (doc: JsonElement) : Result<AtprotoIdentity, string> =
        match tryGetString doc "id" with
        | None -> Error "DID document missing 'id' field"
        | Some did ->
            Ok { Did = did
                 Handle = extractHandle doc
                 PdsEndpoint = extractPdsEndpoint doc
                 SigningKey = extractSigningKey doc }

    let private plcDirectoryUrl = "https://plc.directory"

    let resolveDid (agent: AtpAgent) (did: string) : Task<Result<AtprotoIdentity, string>> =
        task {
            if did.StartsWith("did:plc:") then
                let url = $"{plcDirectoryUrl}/{did}"
                let! response = agent.HttpClient.GetAsync(url)
                if response.IsSuccessStatusCode then
                    let! json = response.Content.ReadAsStringAsync()
                    let doc = JsonSerializer.Deserialize<JsonElement>(json)
                    return parseDidDocument doc
                else
                    return Error $"PLC directory returned {int response.StatusCode} for {did}"
            elif did.StartsWith("did:web:") then
                let domain = did.Substring(8)
                let url = $"https://{domain}/.well-known/did.json"
                let! response = agent.HttpClient.GetAsync(url)
                if response.IsSuccessStatusCode then
                    let! json = response.Content.ReadAsStringAsync()
                    let doc = JsonSerializer.Deserialize<JsonElement>(json)
                    return parseDidDocument doc
                else
                    return Error $"did:web resolution returned {int response.StatusCode} for {did}"
            else
                return Error $"Unsupported DID method: {did}"
        }

    let resolveHandle (agent: AtpAgent) (handle: string) : Task<Result<string, XrpcError>> =
        task {
            let! result = ComAtprotoIdentity.ResolveHandle.query agent { Handle = handle }
            return result |> Result.map (fun o -> o.Did)
        }

    let resolveIdentity (agent: AtpAgent) (identifier: string) : Task<Result<AtprotoIdentity, string>> =
        task {
            let isDid = identifier.StartsWith("did:")
            if isDid then
                let! identity = resolveDid agent identifier
                match identity with
                | Error e -> return Error e
                | Ok id ->
                    match id.Handle with
                    | None -> return Ok id
                    | Some handle ->
                        let! reverseResult = resolveHandle agent handle
                        match reverseResult with
                        | Ok reverseDid when reverseDid = identifier -> return Ok id
                        | _ -> return Ok { id with Handle = None }
            else
                // identifier is a handle
                let! handleResult = resolveHandle agent identifier
                match handleResult with
                | Error e ->
                    let errorMsg = e.Error |> Option.defaultValue "unknown"
                    return Error $"Handle resolution failed: {errorMsg}"
                | Ok did ->
                    let! identity = resolveDid agent did
                    match identity with
                    | Error e -> return Error e
                    | Ok id ->
                        // Bidirectional: check DID doc's handle matches
                        if id.Handle = Some identifier then return Ok id
                        else return Ok { id with Handle = None }
        }
