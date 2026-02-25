module FSharp.ATProto.Lexicon.RecordValidator

open System
open System.Text.Json
open FSharp.ATProto.Syntax

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private countGraphemes (s: string) =
    Globalization.StringInfo(s).LengthInTextElements

/// Split a type reference string on '#' to get (nsid, defName).
/// If no '#', defName defaults to "main".
let private splitRef (refStr: string) : string * string =
    match refStr.IndexOf('#') with
    | -1 -> refStr, "main"
    | i -> refStr.Substring(0, i), refStr.Substring(i + 1)

/// Resolve a ref to its LexDef in the catalog.
let private resolveRef
    (catalog: Map<string, LexiconDoc>)
    (refStr: string)
    : Result<LexDef, string> =
    let nsid, defName = splitRef refStr
    match Map.tryFind nsid catalog with
    | None -> Error(sprintf "NSID '%s' not found in catalog" nsid)
    | Some doc ->
        match Map.tryFind defName doc.Defs with
        | None -> Error(sprintf "Definition '%s' not found in '%s'" defName nsid)
        | Some def -> Ok def

// ---------------------------------------------------------------------------
// Format validation
// ---------------------------------------------------------------------------

let private validateFormat (format: LexStringFormat) (value: string) : Result<unit, string> =
    let mapResult r =
        r |> Result.map (fun _ -> ())

    match format with
    | LexStringFormat.Did -> Did.parse value |> mapResult
    | LexStringFormat.Handle -> Handle.parse value |> mapResult
    | LexStringFormat.AtIdentifier -> AtIdentifier.parse value |> mapResult
    | LexStringFormat.AtUri -> AtUri.parse value |> mapResult
    | LexStringFormat.Nsid -> Nsid.parse value |> mapResult
    | LexStringFormat.Cid -> Cid.parse value |> mapResult
    | LexStringFormat.Datetime -> AtDateTime.parse value |> mapResult
    | LexStringFormat.Language -> Language.parse value |> mapResult
    | LexStringFormat.Uri -> Uri.parse value |> mapResult
    | LexStringFormat.Tid -> Tid.parse value |> mapResult
    | LexStringFormat.RecordKey -> RecordKey.parse value |> mapResult

// ---------------------------------------------------------------------------
// Validation chaining helper
// ---------------------------------------------------------------------------

let private (>>=) (r: Result<unit, string>) (f: unit -> Result<unit, string>) : Result<unit, string> =
    match r with
    | Error e -> Error e
    | Ok () -> f ()

// ---------------------------------------------------------------------------
// Blob validation (extracted due to nesting complexity)
// ---------------------------------------------------------------------------

let private validateBlob
    (catalog: Map<string, LexiconDoc>)
    (constraints: LexBlob)
    (element: JsonElement)
    : Result<unit, string> =
    if element.ValueKind <> JsonValueKind.Object then
        Error "Expected blob object"
    else
        // Must have $type == "blob"
        let checkType () =
            match element.TryGetProperty("$type") with
            | false, _ -> Error "Blob must have '$type' field"
            | true, typeVal ->
                if typeVal.ValueKind <> JsonValueKind.String || typeVal.GetString() <> "blob" then
                    Error "Blob '$type' must be \"blob\""
                else
                    Ok()

        // Must have mimeType (string)
        let getMimeType () =
            match element.TryGetProperty("mimeType") with
            | false, _ -> Error "Blob must have 'mimeType' field"
            | true, mimeVal ->
                if mimeVal.ValueKind <> JsonValueKind.String then
                    Error "Blob 'mimeType' must be a string"
                else
                    Ok(mimeVal.GetString())

        // Must have size (number)
        let getSize () =
            match element.TryGetProperty("size") with
            | false, _ -> Error "Blob must have 'size' field"
            | true, sizeVal ->
                if sizeVal.ValueKind <> JsonValueKind.Number then
                    Error "Blob 'size' must be a number"
                else
                    Ok(sizeVal.GetInt64())

        // Must have ref with $link
        let checkRef () =
            match element.TryGetProperty("ref") with
            | false, _ -> Error "Blob must have 'ref' field"
            | true, refVal ->
                if refVal.ValueKind <> JsonValueKind.Object then
                    Error "Blob 'ref' must be an object"
                else
                    match refVal.TryGetProperty("$link") with
                    | false, _ -> Error "Blob 'ref' must have '$link' key"
                    | true, linkVal ->
                        if linkVal.ValueKind <> JsonValueKind.String then
                            Error "Blob 'ref.$link' must be a string"
                        else
                            Ok()

        match checkType () with
        | Error e -> Error e
        | Ok () ->
            match getMimeType () with
            | Error e -> Error e
            | Ok mimeType ->
                match getSize () with
                | Error e -> Error e
                | Ok size ->
                    match checkRef () with
                    | Error e -> Error e
                    | Ok () ->
                        // Check maxSize constraint
                        match constraints.MaxSize with
                        | Some maxSize when size > maxSize ->
                            Error(sprintf "Blob size %d exceeds maxSize %d" size maxSize)
                        | _ ->
                            // Check accept constraint
                            match constraints.Accept with
                            | Some acceptList ->
                                let matches =
                                    acceptList
                                    |> List.exists (fun pattern ->
                                        if pattern.EndsWith("/*") then
                                            let prefix = pattern.Substring(0, pattern.Length - 1)
                                            mimeType.StartsWith(prefix)
                                        else
                                            mimeType = pattern)
                                if not matches then
                                    Error(sprintf "Blob mimeType '%s' not accepted" mimeType)
                                else
                                    Ok()
                            | None -> Ok()

// ---------------------------------------------------------------------------
// Type validation (recursive)
// ---------------------------------------------------------------------------

let rec private validateType
    (catalog: Map<string, LexiconDoc>)
    (lexType: LexType)
    (element: JsonElement)
    : Result<unit, string> =

    match lexType with
    | LexType.Boolean _ ->
        if element.ValueKind = JsonValueKind.True || element.ValueKind = JsonValueKind.False then
            Ok()
        else
            Error(sprintf "Expected boolean, got %A" element.ValueKind)

    | LexType.Integer constraints ->
        if element.ValueKind <> JsonValueKind.Number then
            Error(sprintf "Expected integer (number), got %A" element.ValueKind)
        else
            let value = element.GetInt64()
            let checkConst () =
                match constraints.Const with
                | Some c when value <> c ->
                    Error(sprintf "Integer value %d does not match const %d" value c)
                | _ -> Ok()
            let checkEnum () =
                match constraints.Enum with
                | Some values when not (List.contains value values) ->
                    Error(sprintf "Integer value %d not in enum" value)
                | _ -> Ok()
            let checkMin () =
                match constraints.Minimum with
                | Some min when value < min ->
                    Error(sprintf "Integer value %d below minimum %d" value min)
                | _ -> Ok()
            let checkMax () =
                match constraints.Maximum with
                | Some max when value > max ->
                    Error(sprintf "Integer value %d above maximum %d" value max)
                | _ -> Ok()
            checkConst () >>= checkEnum >>= checkMin >>= checkMax

    | LexType.String constraints ->
        if element.ValueKind <> JsonValueKind.String then
            Error(sprintf "Expected string, got %A" element.ValueKind)
        else
            let value = element.GetString()
            let checkConst () =
                match constraints.Const with
                | Some c when value <> c ->
                    Error(sprintf "String value '%s' does not match const '%s'" value c)
                | _ -> Ok()
            let checkEnum () =
                match constraints.Enum with
                | Some values when not (List.contains value values) ->
                    Error(sprintf "String value '%s' not in enum" value)
                | _ -> Ok()
            let checkMinLen () =
                match constraints.MinLength with
                | Some min when value.Length < min ->
                    Error(sprintf "String length %d below minLength %d" value.Length min)
                | _ -> Ok()
            let checkMaxLen () =
                match constraints.MaxLength with
                | Some max when value.Length > max ->
                    Error(sprintf "String length %d above maxLength %d" value.Length max)
                | _ -> Ok()
            let checkMinGraphemes () =
                match constraints.MinGraphemes with
                | Some min ->
                    let gc = countGraphemes value
                    if gc < min then
                        Error(sprintf "String grapheme count %d below minGraphemes %d" gc min)
                    else
                        Ok()
                | None -> Ok()
            let checkMaxGraphemes () =
                match constraints.MaxGraphemes with
                | Some max ->
                    let gc = countGraphemes value
                    if gc > max then
                        Error(sprintf "String grapheme count %d above maxGraphemes %d" gc max)
                    else
                        Ok()
                | None -> Ok()
            let checkFormat () =
                match constraints.Format with
                | Some fmt -> validateFormat fmt value
                | None -> Ok()
            checkConst ()
            >>= checkEnum
            >>= checkMinLen
            >>= checkMaxLen
            >>= checkMinGraphemes
            >>= checkMaxGraphemes
            >>= checkFormat

    | LexType.Bytes constraints ->
        if element.ValueKind <> JsonValueKind.Object then
            Error "Expected bytes object (with $bytes key)"
        else
            match element.TryGetProperty("$bytes") with
            | false, _ -> Error "Bytes value must have '$bytes' key"
            | true, bytesVal ->
                if bytesVal.ValueKind <> JsonValueKind.String then
                    Error "Bytes '$bytes' value must be a string"
                else
                    let b64 = bytesVal.GetString()
                    match constraints.MinLength, constraints.MaxLength with
                    | None, None -> Ok()
                    | _ ->
                        try
                            let decoded = Convert.FromBase64String(b64)
                            let len = decoded.Length
                            match constraints.MinLength with
                            | Some min when len < min ->
                                Error(sprintf "Bytes length %d below minLength %d" len min)
                            | _ ->
                                match constraints.MaxLength with
                                | Some max when len > max ->
                                    Error(sprintf "Bytes length %d above maxLength %d" len max)
                                | _ -> Ok()
                        with _ ->
                            Error "Invalid base64 in bytes value"

    | LexType.CidLink ->
        if element.ValueKind <> JsonValueKind.Object then
            Error "Expected cid-link object (with $link key)"
        else
            match element.TryGetProperty("$link") with
            | false, _ -> Error "CID-link must have '$link' key"
            | true, linkVal ->
                if linkVal.ValueKind <> JsonValueKind.String then
                    Error "CID-link '$link' value must be a string"
                else
                    Ok()

    | LexType.Blob constraints ->
        validateBlob catalog constraints element

    | LexType.Array arr ->
        if element.ValueKind <> JsonValueKind.Array then
            Error(sprintf "Expected array, got %A" element.ValueKind)
        else
            let len = element.GetArrayLength()
            let checkMinLen () =
                match arr.MinLength with
                | Some min when len < min ->
                    Error(sprintf "Array length %d below minLength %d" len min)
                | _ -> Ok()
            let checkMaxLen () =
                match arr.MaxLength with
                | Some max when len > max ->
                    Error(sprintf "Array length %d above maxLength %d" len max)
                | _ -> Ok()
            let checkElements () =
                let mutable err = None
                let mutable idx = 0
                for item in element.EnumerateArray() do
                    if err.IsNone then
                        match validateType catalog arr.Items item with
                        | Ok() -> ()
                        | Error e ->
                            err <- Some(sprintf "Array element [%d]: %s" idx e)
                    idx <- idx + 1
                match err with
                | Some e -> Error e
                | None -> Ok()
            checkMinLen () >>= checkMaxLen >>= checkElements

    | LexType.Object obj ->
        validateObject catalog obj element

    | LexType.Ref { Ref = refStr } ->
        match resolveRef catalog refStr with
        | Error e -> Error e
        | Ok def ->
            match def with
            | LexDef.DefType lexType -> validateType catalog lexType element
            | LexDef.Record record -> validateObject catalog record.Record element
            | LexDef.Token _ ->
                if element.ValueKind = JsonValueKind.String then Ok()
                else Error(sprintf "Expected string for token ref, got %A" element.ValueKind)
            | _ -> Error(sprintf "Unexpected def type for ref '%s'" refStr)

    | LexType.Union union ->
        if element.ValueKind <> JsonValueKind.Object then
            Error(sprintf "Expected object for union, got %A" element.ValueKind)
        else
            match element.TryGetProperty("$type") with
            | false, _ -> Error "Union value must have '$type' field"
            | true, typeVal ->
                if typeVal.ValueKind <> JsonValueKind.String then
                    Error "Union '$type' must be a string"
                else
                    let typeName = typeVal.GetString()
                    let isInRefs = List.contains typeName union.Refs
                    if union.Closed && not isInRefs then
                        Error(sprintf "Type '%s' not in closed union" typeName)
                    elif isInRefs then
                        match resolveRef catalog typeName with
                        | Error e -> Error e
                        | Ok def ->
                            match def with
                            | LexDef.DefType (LexType.Object obj) ->
                                validateObject catalog obj element
                            | LexDef.DefType lexType ->
                                validateType catalog lexType element
                            | LexDef.Record record ->
                                validateObject catalog record.Record element
                            | _ -> Ok()
                    else
                        Ok()

    | LexType.Unknown ->
        if element.ValueKind <> JsonValueKind.Object then
            Error(sprintf "Expected object for unknown type, got %A" element.ValueKind)
        else
            match element.TryGetProperty("$bytes") with
            | true, _ -> Error "Unknown field must not be a bytes value (has $bytes)"
            | false, _ ->
                match element.TryGetProperty("$type") with
                | true, typeVal when typeVal.ValueKind = JsonValueKind.String && typeVal.GetString() = "blob" ->
                    Error "Unknown field must not be a blob value ($type = blob)"
                | _ -> Ok()

    | LexType.Params _ ->
        Ok()

// ---------------------------------------------------------------------------
// Object validation
// ---------------------------------------------------------------------------

and private validateObject
    (catalog: Map<string, LexiconDoc>)
    (obj: LexObject)
    (element: JsonElement)
    : Result<unit, string> =

    if element.ValueKind <> JsonValueKind.Object then
        Error(sprintf "Expected object, got %A" element.ValueKind)
    else
        // Check required properties are present and not null (unless nullable)
        let checkRequired () =
            let mutable err = None
            for reqProp in obj.Required do
                if err.IsNone then
                    match element.TryGetProperty(reqProp) with
                    | false, _ ->
                        err <- Some(sprintf "Required property '%s' is missing" reqProp)
                    | true, propVal ->
                        if propVal.ValueKind = JsonValueKind.Null && not (List.contains reqProp obj.Nullable) then
                            err <- Some(sprintf "Required property '%s' is null but not nullable" reqProp)
            match err with
            | Some e -> Error e
            | None -> Ok()

        // Validate each property that exists in the schema
        let checkProperties () =
            let mutable propErr = None
            for kv in element.EnumerateObject() do
                if propErr.IsNone then
                    let propName = kv.Name
                    if propName <> "$type" then
                        match Map.tryFind propName obj.Properties with
                        | Some propSchema ->
                            if kv.Value.ValueKind = JsonValueKind.Null then
                                if not (List.contains propName obj.Nullable) then
                                    propErr <- Some(sprintf "Property '%s' is null but not nullable" propName)
                            else
                                match validateType catalog propSchema kv.Value with
                                | Ok() -> ()
                                | Error e ->
                                    propErr <- Some(sprintf "Property '%s': %s" propName e)
                        | None -> ()
            match propErr with
            | Some e -> Error e
            | None -> Ok()

        checkRequired () >>= checkProperties

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/// Validate a JSON record data instance against Lexicon schemas in the catalog.
let validate (catalog: Map<string, LexiconDoc>) (data: JsonElement) : Result<unit, string> =
    match data.TryGetProperty("$type") with
    | false, _ -> Error "Record data missing '$type' field"
    | true, typeVal ->
        if typeVal.ValueKind <> JsonValueKind.String then
            Error "'$type' field must be a string"
        else
            let typeStr = typeVal.GetString()
            let nsid, defName = splitRef typeStr

            match Map.tryFind nsid catalog with
            | None -> Error(sprintf "NSID '%s' not found in catalog" nsid)
            | Some doc ->
                match Map.tryFind defName doc.Defs with
                | None -> Error(sprintf "Definition '%s' not found in '%s'" defName nsid)
                | Some def ->
                    match def with
                    | LexDef.Record record ->
                        validateObject catalog record.Record data
                    | _ -> Error(sprintf "Definition '%s' in '%s' is not a record" defName nsid)
