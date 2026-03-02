namespace FSharp.ATProto.DRISL

open System
open System.Text.Json
open System.Text.Json.Nodes
open FSharp.ATProto.Syntax

/// JSON <-> AtpValue conversion with AT Protocol conventions ($link, $bytes, $type, blobs).
module AtpJson =

    let private padBase64 (s : string) =
        match s.Length % 4 with
        | 2 -> s + "=="
        | 3 -> s + "="
        | _ -> s

    /// Convert a JSON element to AtpValue. No top-level type check.
    let rec private convertElement (element : JsonElement) : Result<AtpValue, string> =
        match element.ValueKind with
        | JsonValueKind.Null -> Ok AtpValue.Null
        | JsonValueKind.True -> Ok (AtpValue.Bool true)
        | JsonValueKind.False -> Ok (AtpValue.Bool false)
        | JsonValueKind.Number ->
            match element.TryGetInt64 () with
            | true, n -> Ok (AtpValue.Integer n)
            | false, _ ->
                let d = element.GetDouble ()

                if d = Math.Floor (d) && d >= float Int64.MinValue && d <= float Int64.MaxValue then
                    Ok (AtpValue.Integer (int64 d))
                else
                    Error (sprintf "Non-integer floats are not allowed in AT Protocol data model: %g" d)
        | JsonValueKind.String -> Ok (AtpValue.String (element.GetString ()))
        | JsonValueKind.Array ->
            let mutable result = Ok []

            for item in element.EnumerateArray () do
                match result with
                | Error _ -> ()
                | Ok acc ->
                    match convertElement item with
                    | Error e -> result <- Error e
                    | Ok v -> result <- Ok (acc @ [ v ])

            result |> Result.map AtpValue.Array
        | JsonValueKind.Object -> convertObject element
        | kind -> Error (sprintf "Unsupported JSON value kind: %A" kind)

    and private convertObject (element : JsonElement) : Result<AtpValue, string> =
        // Check for $link
        let mutable linkProp = Unchecked.defaultof<JsonElement>

        if element.TryGetProperty ("$link", &linkProp) then
            let count = element.EnumerateObject () |> Seq.length

            if count > 1 then
                Error "$link object must have exactly one key"
            elif linkProp.ValueKind <> JsonValueKind.String then
                Error "$link value must be a string"
            else
                match Cid.parse (linkProp.GetString ()) with
                | Ok cid -> Ok (AtpValue.Link cid)
                | Error e -> Error (sprintf "Invalid CID in $link: %s" e)
        else
            // Check for $bytes
            let mutable bytesProp = Unchecked.defaultof<JsonElement>

            if element.TryGetProperty ("$bytes", &bytesProp) then
                let count = element.EnumerateObject () |> Seq.length

                if count > 1 then
                    Error "$bytes object must have exactly one key"
                elif bytesProp.ValueKind <> JsonValueKind.String then
                    Error "$bytes value must be a string"
                else
                    try
                        let bytes = Convert.FromBase64String (padBase64 (bytesProp.GetString ()))
                        Ok (AtpValue.Bytes bytes)
                    with _ ->
                        Error "Invalid base64 in $bytes"
            else
                // Check $type validation
                let mutable typeProp = Unchecked.defaultof<JsonElement>

                if element.TryGetProperty ("$type", &typeProp) then
                    if typeProp.ValueKind = JsonValueKind.Null then
                        Error "$type must not be null"
                    elif typeProp.ValueKind <> JsonValueKind.String then
                        Error "$type must be a string"
                    elif typeProp.GetString().Length = 0 then
                        Error "$type must not be empty"
                    elif typeProp.GetString () = "blob" then
                        validateBlob element
                    else
                        convertRegularObject element
                else
                    convertRegularObject element

    and private validateBlob (element : JsonElement) : Result<AtpValue, string> =
        let mutable refProp = Unchecked.defaultof<JsonElement>

        if not (element.TryGetProperty ("ref", &refProp)) then
            Error "Blob must have a 'ref' field"
        elif refProp.ValueKind <> JsonValueKind.Object then
            Error "Blob 'ref' must be an object"
        else
            let mutable mimeProp = Unchecked.defaultof<JsonElement>

            if not (element.TryGetProperty ("mimeType", &mimeProp)) then
                Error "Blob must have a 'mimeType' field"
            elif mimeProp.ValueKind <> JsonValueKind.String then
                Error "Blob 'mimeType' must be a string"
            else
                let mutable sizeProp = Unchecked.defaultof<JsonElement>

                if not (element.TryGetProperty ("size", &sizeProp)) then
                    Error "Blob must have a 'size' field"
                elif sizeProp.ValueKind <> JsonValueKind.Number then
                    Error "Blob 'size' must be a number"
                else
                    match sizeProp.TryGetInt64 () with
                    | false, _ -> Error "Blob 'size' must be an integer"
                    | true, _ -> convertRegularObject element

    and private convertRegularObject (element : JsonElement) : Result<AtpValue, string> =
        let mutable result = Ok Map.empty

        for prop in element.EnumerateObject () do
            match result with
            | Error _ -> ()
            | Ok map ->
                match convertElement prop.Value with
                | Error e -> result <- Error e
                | Ok v -> result <- Ok (Map.add prop.Name v map)

        result |> Result.map AtpValue.Object

    /// Convert a JSON element to AtpValue with data model validation.
    /// Top-level must be an object. Validates $type, $link, $bytes, blob structure.
    let fromJson (element : JsonElement) : Result<AtpValue, string> =
        if element.ValueKind <> JsonValueKind.Object then
            Error "Top-level value must be an object"
        else
            convertElement element

    /// Convert an AtpValue to a JSON node.
    let rec toJsonNode (value : AtpValue) : JsonNode =
        match value with
        | AtpValue.Null -> null
        | AtpValue.Bool b -> JsonValue.Create (b)
        | AtpValue.Integer n -> JsonValue.Create (n)
        | AtpValue.String s -> JsonValue.Create (s)
        | AtpValue.Bytes b ->
            let obj = JsonObject ()
            obj.Add ("$bytes", JsonValue.Create (Convert.ToBase64String (b)))
            obj
        | AtpValue.Link cid ->
            let obj = JsonObject ()
            obj.Add ("$link", JsonValue.Create (Cid.value cid))
            obj
        | AtpValue.Array items ->
            let arr = JsonArray ()

            for item in items do
                arr.Add (toJsonNode item)

            arr
        | AtpValue.Object map ->
            let obj = JsonObject ()

            for KeyValue (k, v) in map do
                obj.Add (k, toJsonNode v)

            obj
