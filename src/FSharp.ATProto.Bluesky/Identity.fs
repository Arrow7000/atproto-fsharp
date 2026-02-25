namespace FSharp.ATProto.Bluesky

open System.Text.Json

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
