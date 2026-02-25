module FSharp.ATProto.Lexicon.LexiconParser

open System.Text.Json
open FSharp.ATProto.Syntax

// ---------------------------------------------------------------------------
// JSON helper functions
// ---------------------------------------------------------------------------

let private tryGetString (el: JsonElement) (prop: string) : string option =
    match el.TryGetProperty(prop) with
    | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
    | _ -> None

let private tryGetInt64 (el: JsonElement) (prop: string) : int64 option =
    match el.TryGetProperty(prop) with
    | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt64())
    | _ -> None

let private tryGetInt (el: JsonElement) (prop: string) : int option =
    match el.TryGetProperty(prop) with
    | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt32())
    | _ -> None

let private tryGetBool (el: JsonElement) (prop: string) : bool option =
    match el.TryGetProperty(prop) with
    | true, v when v.ValueKind = JsonValueKind.True -> Some true
    | true, v when v.ValueKind = JsonValueKind.False -> Some false
    | _ -> None

let private getStringList (el: JsonElement) (prop: string) : string list =
    match el.TryGetProperty(prop) with
    | true, v when v.ValueKind = JsonValueKind.Array ->
        [ for item in v.EnumerateArray() -> item.GetString() ]
    | _ -> []

let private tryGetStringList (el: JsonElement) (prop: string) : string list option =
    match el.TryGetProperty(prop) with
    | true, v when v.ValueKind = JsonValueKind.Array ->
        Some [ for item in v.EnumerateArray() -> item.GetString() ]
    | _ -> None

let private tryGetInt64List (el: JsonElement) (prop: string) : int64 list option =
    match el.TryGetProperty(prop) with
    | true, v when v.ValueKind = JsonValueKind.Array ->
        Some [ for item in v.EnumerateArray() -> item.GetInt64() ]
    | _ -> None

let private getStringMap (el: JsonElement) (prop: string) : Map<string, string> =
    match el.TryGetProperty(prop) with
    | true, v when v.ValueKind = JsonValueKind.Object ->
        v.EnumerateObject()
        |> Seq.fold (fun acc kv -> Map.add (kv.Name) (kv.Value.GetString()) acc) Map.empty
    | _ -> Map.empty

// ---------------------------------------------------------------------------
// Ref resolution
// ---------------------------------------------------------------------------

let private resolveRef (docId: string) (ref: string) : string =
    if ref.StartsWith("#") then
        docId + ref
    else
        ref

// ---------------------------------------------------------------------------
// String format parsing
// ---------------------------------------------------------------------------

let private parseStringFormat (s: string) : Result<LexStringFormat, string> =
    match s with
    | "did" -> Ok LexStringFormat.Did
    | "handle" -> Ok LexStringFormat.Handle
    | "at-identifier" -> Ok LexStringFormat.AtIdentifier
    | "at-uri" -> Ok LexStringFormat.AtUri
    | "nsid" -> Ok LexStringFormat.Nsid
    | "cid" -> Ok LexStringFormat.Cid
    | "datetime" -> Ok LexStringFormat.Datetime
    | "language" -> Ok LexStringFormat.Language
    | "uri" -> Ok LexStringFormat.Uri
    | "tid" -> Ok LexStringFormat.Tid
    | "record-key" -> Ok LexStringFormat.RecordKey
    | other -> Error(sprintf "Unknown string format: %s" other)

// ---------------------------------------------------------------------------
// Type parsing (recursive)
// ---------------------------------------------------------------------------

let rec private parseType (docId: string) (el: JsonElement) : Result<LexType, string> =
    match tryGetString el "type" with
    | None -> Error "Missing 'type' field"
    | Some typ ->
        match typ with
        | "boolean" ->
            Ok(
                LexType.Boolean
                    { Description = tryGetString el "description"
                      Default = tryGetBool el "default"
                      Const = tryGetBool el "const" }
            )

        | "integer" ->
            Ok(
                LexType.Integer
                    { Description = tryGetString el "description"
                      Default = tryGetInt64 el "default"
                      Const = tryGetInt64 el "const"
                      Enum = tryGetInt64List el "enum"
                      Minimum = tryGetInt64 el "minimum"
                      Maximum = tryGetInt64 el "maximum" }
            )

        | "string" ->
            let fmt =
                match tryGetString el "format" with
                | None -> Ok None
                | Some f -> parseStringFormat f |> Result.map Some

            match fmt with
            | Error e -> Error e
            | Ok fmtVal ->
                Ok(
                    LexType.String
                        { Description = tryGetString el "description"
                          Default = tryGetString el "default"
                          Const = tryGetString el "const"
                          Enum = tryGetStringList el "enum"
                          KnownValues = tryGetStringList el "knownValues"
                          Format = fmtVal
                          MinLength = tryGetInt el "minLength"
                          MaxLength = tryGetInt el "maxLength"
                          MinGraphemes = tryGetInt el "minGraphemes"
                          MaxGraphemes = tryGetInt el "maxGraphemes" }
                )

        | "bytes" ->
            Ok(
                LexType.Bytes
                    { Description = tryGetString el "description"
                      MinLength = tryGetInt el "minLength"
                      MaxLength = tryGetInt el "maxLength" }
            )

        | "cid-link" -> Ok LexType.CidLink

        | "blob" ->
            Ok(
                LexType.Blob
                    { Description = tryGetString el "description"
                      Accept = tryGetStringList el "accept"
                      MaxSize = tryGetInt64 el "maxSize" }
            )

        | "array" ->
            match el.TryGetProperty("items") with
            | false, _ -> Error "Array type missing 'items' field"
            | true, itemsEl ->
                parseType docId itemsEl
                |> Result.map (fun items ->
                    LexType.Array
                        { Description = tryGetString el "description"
                          Items = items
                          MinLength = tryGetInt el "minLength"
                          MaxLength = tryGetInt el "maxLength" })

        | "object" -> parseObject docId el |> Result.map LexType.Object

        | "params" -> parseParams docId el |> Result.map LexType.Params

        | "ref" ->
            match tryGetString el "ref" with
            | None -> Error "Ref type missing 'ref' field"
            | Some r ->
                Ok(
                    LexType.Ref
                        { Description = tryGetString el "description"
                          Ref = resolveRef docId r }
                )

        | "union" ->
            let refs =
                getStringList el "refs" |> List.map (resolveRef docId)

            let closed = tryGetBool el "closed" |> Option.defaultValue false

            Ok(
                LexType.Union
                    { Description = tryGetString el "description"
                      Refs = refs
                      Closed = closed }
            )

        | "unknown" -> Ok LexType.Unknown

        | "token" ->
            // Tokens used as a field type act as strings with no constraints
            Ok(
                LexType.String
                    { Description = tryGetString el "description"
                      Default = None
                      Const = None
                      Enum = None
                      KnownValues = None
                      Format = None
                      MinLength = None
                      MaxLength = None
                      MinGraphemes = None
                      MaxGraphemes = None }
            )

        | other -> Error(sprintf "Unknown type: %s" other)

// ---------------------------------------------------------------------------
// Object / Params parsing
// ---------------------------------------------------------------------------

and private parseObject (docId: string) (el: JsonElement) : Result<LexObject, string> =
    let propsResult =
        match el.TryGetProperty("properties") with
        | false, _ -> Ok Map.empty
        | true, propsEl ->
            let mutable err = None
            let mutable props = Map.empty

            for kv in propsEl.EnumerateObject() do
                if err.IsNone then
                    match parseType docId kv.Value with
                    | Ok t -> props <- Map.add kv.Name t props
                    | Error e -> err <- Some e

            match err with
            | Some e -> Error e
            | None -> Ok props

    match propsResult with
    | Error e -> Error e
    | Ok props ->
        Ok
            { Description = tryGetString el "description"
              Properties = props
              Required = getStringList el "required"
              Nullable = getStringList el "nullable" }

and private parseParams (docId: string) (el: JsonElement) : Result<LexParams, string> =
    let propsResult =
        match el.TryGetProperty("properties") with
        | false, _ -> Ok Map.empty
        | true, propsEl ->
            let mutable err = None
            let mutable props = Map.empty

            for kv in propsEl.EnumerateObject() do
                if err.IsNone then
                    match parseType docId kv.Value with
                    | Ok t -> props <- Map.add kv.Name t props
                    | Error e -> err <- Some e

            match err with
            | Some e -> Error e
            | None -> Ok props

    match propsResult with
    | Error e -> Error e
    | Ok props ->
        Ok
            { Description = tryGetString el "description"
              Properties = props
              Required = getStringList el "required" }

// ---------------------------------------------------------------------------
// Record, Query, Procedure, Subscription parsing
// ---------------------------------------------------------------------------

let private parseRecord (docId: string) (el: JsonElement) : Result<LexRecord, string> =
    match tryGetString el "key" with
    | None -> Error "Record missing 'key' field"
    | Some key ->
        match el.TryGetProperty("record") with
        | false, _ -> Error "Record missing 'record' field"
        | true, recordEl ->
            match tryGetString recordEl "type" with
            | Some "object" ->
                parseObject docId recordEl
                |> Result.map (fun obj ->
                    { Key = key
                      Description = tryGetString el "description"
                      Record = obj })
            | _ -> Error "Record's 'record' field must have \"type\": \"object\""

let private parseBody (docId: string) (el: JsonElement) : Result<LexBody, string> =
    match tryGetString el "encoding" with
    | None -> Error "Body missing 'encoding' field"
    | Some encoding ->
        let schemaResult =
            match el.TryGetProperty("schema") with
            | false, _ -> Ok None
            | true, schemaEl -> parseType docId schemaEl |> Result.map Some

        match schemaResult with
        | Error e -> Error e
        | Ok schema ->
            Ok
                { Description = tryGetString el "description"
                  Encoding = encoding
                  Schema = schema }

let private parseErrors (el: JsonElement) : LexError list =
    match el.TryGetProperty("errors") with
    | false, _ -> []
    | true, errArr ->
        [ for item in errArr.EnumerateArray() ->
              { Name = item.GetProperty("name").GetString()
                Description = tryGetString item "description" } ]

let private parseQuery (docId: string) (el: JsonElement) : Result<LexQuery, string> =
    let paramsResult =
        match el.TryGetProperty("parameters") with
        | false, _ -> Ok None
        | true, pEl -> parseParams docId pEl |> Result.map Some

    let outputResult =
        match el.TryGetProperty("output") with
        | false, _ -> Ok None
        | true, oEl -> parseBody docId oEl |> Result.map Some

    match paramsResult, outputResult with
    | Error e, _ -> Error e
    | _, Error e -> Error e
    | Ok p, Ok o ->
        Ok
            { Description = tryGetString el "description"
              Parameters = p
              Output = o
              Errors = parseErrors el }

let private parseProcedure (docId: string) (el: JsonElement) : Result<LexProcedure, string> =
    let paramsResult =
        match el.TryGetProperty("parameters") with
        | false, _ -> Ok None
        | true, pEl -> parseParams docId pEl |> Result.map Some

    let inputResult =
        match el.TryGetProperty("input") with
        | false, _ -> Ok None
        | true, iEl -> parseBody docId iEl |> Result.map Some

    let outputResult =
        match el.TryGetProperty("output") with
        | false, _ -> Ok None
        | true, oEl -> parseBody docId oEl |> Result.map Some

    match paramsResult, inputResult, outputResult with
    | Error e, _, _ -> Error e
    | _, Error e, _ -> Error e
    | _, _, Error e -> Error e
    | Ok p, Ok i, Ok o ->
        Ok
            { Description = tryGetString el "description"
              Parameters = p
              Input = i
              Output = o
              Errors = parseErrors el }

let private parseSubscriptionMessage
    (docId: string)
    (el: JsonElement)
    : Result<LexSubscriptionMessage, string> =
    match el.TryGetProperty("schema") with
    | false, _ -> Error "Subscription message missing 'schema' field"
    | true, schemaEl ->
        parseType docId schemaEl
        |> Result.bind (fun t ->
            match t with
            | LexType.Union u ->
                Ok
                    { Description = tryGetString el "description"
                      Schema = u }
            | _ -> Error "Subscription message schema must be a union type")

let private parseSubscription (docId: string) (el: JsonElement) : Result<LexSubscription, string> =
    let paramsResult =
        match el.TryGetProperty("parameters") with
        | false, _ -> Ok None
        | true, pEl -> parseParams docId pEl |> Result.map Some

    let messageResult =
        match el.TryGetProperty("message") with
        | false, _ -> Ok None
        | true, mEl -> parseSubscriptionMessage docId mEl |> Result.map Some

    match paramsResult, messageResult with
    | Error e, _ -> Error e
    | _, Error e -> Error e
    | Ok p, Ok m ->
        Ok
            { Description = tryGetString el "description"
              Parameters = p
              Message = m
              Errors = parseErrors el }

// ---------------------------------------------------------------------------
// Permission set parsing
// ---------------------------------------------------------------------------

let private parsePermission (el: JsonElement) : Result<LexPermission, string> =
    match tryGetString el "resource" with
    | None -> Error "Permission missing 'resource' field"
    | Some resource ->
        Ok
            { Resource = resource
              Collection = getStringList el "collection"
              Action = getStringList el "action"
              Lxm = getStringList el "lxm"
              Aud = tryGetString el "aud"
              InheritAud = tryGetBool el "inheritAud" }

let private parsePermissionSet (el: JsonElement) : Result<LexPermissionSet, string> =
    match tryGetString el "title" with
    | None -> Error "Permission set missing 'title' field"
    | Some title ->
        let permsResult =
            match el.TryGetProperty("permissions") with
            | false, _ -> Ok []
            | true, permsArr ->
                let mutable err = None
                let mutable perms = []

                for item in permsArr.EnumerateArray() do
                    if err.IsNone then
                        match parsePermission item with
                        | Ok p -> perms <- perms @ [ p ]
                        | Error e -> err <- Some e

                match err with
                | Some e -> Error e
                | None -> Ok perms

        match permsResult with
        | Error e -> Error e
        | Ok perms ->
            Ok
                { Title = title
                  TitleLang = getStringMap el "title:lang"
                  Detail = tryGetString el "detail"
                  DetailLang = getStringMap el "detail:lang"
                  Permissions = perms }

// ---------------------------------------------------------------------------
// Definition parsing
// ---------------------------------------------------------------------------

let private parseDef
    (docId: string)
    (defName: string)
    (el: JsonElement)
    : Result<string * LexDef, string> =
    match tryGetString el "type" with
    | None -> Error(sprintf "Definition '%s' missing 'type' field" defName)
    | Some typ ->
        let requireMain () =
            if defName <> "main" then
                Error(sprintf "'%s' type must be the 'main' definition, found in '%s'" typ defName)
            else
                Ok()

        match typ with
        | "record" ->
            match requireMain () with
            | Error e -> Error e
            | Ok() -> parseRecord docId el |> Result.map (fun r -> defName, LexDef.Record r)

        | "query" ->
            match requireMain () with
            | Error e -> Error e
            | Ok() -> parseQuery docId el |> Result.map (fun q -> defName, LexDef.Query q)

        | "procedure" ->
            match requireMain () with
            | Error e -> Error e
            | Ok() -> parseProcedure docId el |> Result.map (fun p -> defName, LexDef.Procedure p)

        | "subscription" ->
            match requireMain () with
            | Error e -> Error e
            | Ok() ->
                parseSubscription docId el
                |> Result.map (fun s -> defName, LexDef.Subscription s)

        | "permission-set" ->
            match requireMain () with
            | Error e -> Error e
            | Ok() ->
                parsePermissionSet el
                |> Result.map (fun ps -> defName, LexDef.PermissionSet ps)

        | "token" ->
            Ok(defName, LexDef.Token { Description = tryGetString el "description" })

        | "ref" -> Error "ref cannot be a top-level definition"

        | "unknown" -> Error "unknown cannot be a top-level definition"

        | _ ->
            // All other types (boolean, integer, string, bytes, cid-link, blob, array, object, union)
            // are parsed as LexType and wrapped in DefType
            parseType docId el |> Result.map (fun t -> defName, LexDef.DefType t)

// ---------------------------------------------------------------------------
// Document parsing (public API)
// ---------------------------------------------------------------------------

let parseElement (el: JsonElement) : Result<LexiconDoc, string> =
    // Validate 'lexicon' field: must exist, must be Number, must be 1
    match el.TryGetProperty("lexicon") with
    | false, _ -> Error "Missing 'lexicon' field"
    | true, lexVal ->
        if lexVal.ValueKind <> JsonValueKind.Number then
            Error "Field 'lexicon' must be a number"
        else
            let lexVersion = lexVal.GetInt32()

            if lexVersion <> 1 then
                Error(sprintf "Unsupported lexicon version: %d" lexVersion)
            else
                // Validate 'id' field: must exist, must be String, must be valid NSID
                match el.TryGetProperty("id") with
                | false, _ -> Error "Missing 'id' field"
                | true, idVal ->
                    if idVal.ValueKind <> JsonValueKind.String then
                        Error "Field 'id' must be a string"
                    else
                        let idStr = idVal.GetString()

                        match Nsid.parse idStr with
                        | Error e -> Error(sprintf "Invalid NSID in 'id': %s" e)
                        | Ok nsid ->
                            let docId = Nsid.value nsid

                            // Parse optional fields
                            let revision = tryGetInt el "revision"
                            let description = tryGetString el "description"

                            // Parse defs
                            let defsResult =
                                match el.TryGetProperty("defs") with
                                | false, _ -> Ok Map.empty
                                | true, defsEl ->
                                    let mutable err = None
                                    let mutable defs = Map.empty

                                    for kv in defsEl.EnumerateObject() do
                                        if err.IsNone then
                                            match parseDef docId kv.Name kv.Value with
                                            | Ok(name, def) -> defs <- Map.add name def defs
                                            | Error e -> err <- Some e

                                    match err with
                                    | Some e -> Error e
                                    | None -> Ok defs

                            match defsResult with
                            | Error e -> Error e
                            | Ok defs ->
                                Ok
                                    { Lexicon = lexVersion
                                      Id = nsid
                                      Revision = revision
                                      Description = description
                                      Defs = defs }

let parse (json: string) : Result<LexiconDoc, string> =
    try
        let doc = JsonDocument.Parse(json)
        parseElement doc.RootElement
    with ex ->
        Error(sprintf "Failed to parse JSON: %s" ex.Message)
