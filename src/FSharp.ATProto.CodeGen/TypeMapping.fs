module FSharp.ATProto.CodeGen.TypeMapping

open FSharp.ATProto.Lexicon

/// Map a LexType to its F# type name string for use in code generation.
/// currentNamespace is used to resolve Ref types relative to the current module.
let rec lexTypeToFSharpType (currentNamespace: string) (lexType: LexType) : string =
    match lexType with
    | Boolean _ -> "bool"
    | Integer _ -> "int64"
    | String s ->
        match s.Format with
        | Some LexStringFormat.Did -> "Did"
        | Some LexStringFormat.Handle -> "Handle"
        | Some LexStringFormat.AtUri -> "AtUri"
        | Some LexStringFormat.Cid -> "Cid"
        | Some LexStringFormat.Nsid -> "Nsid"
        | Some LexStringFormat.Tid -> "Tid"
        | Some LexStringFormat.RecordKey -> "RecordKey"
        | Some LexStringFormat.Datetime -> "AtDateTime"
        | Some LexStringFormat.Language -> "Language"
        | Some LexStringFormat.Uri -> "Uri"
        | Some LexStringFormat.AtIdentifier -> "string"
        | None -> "string"
    | Bytes _ -> "byte[]"
    | CidLink -> "Cid"
    | Blob _ -> "JsonElement"
    | Unknown -> "JsonElement"
    | Array arr ->
        let inner = lexTypeToFSharpType currentNamespace arr.Items
        sprintf "%s list" inner
    | Ref r ->
        let (_targetNamespace, qualifiedName) = Naming.refToQualifiedType currentNamespace r.Ref
        qualifiedName
    | Union _ -> "JsonElement"
    | Object _ -> "JsonElement"
    | Params _ -> "JsonElement"

/// Collect cross-namespace dependencies from a LexType.
/// Returns the set of namespace names that differ from currentNamespace.
let rec collectNamespaceDeps (currentNamespace: string) (lexType: LexType) : Set<string> =
    match lexType with
    | Ref r ->
        let (targetNamespace, _qualifiedName) = Naming.refToQualifiedType currentNamespace r.Ref
        if targetNamespace <> currentNamespace then
            Set.singleton targetNamespace
        else
            Set.empty
    | Array arr ->
        collectNamespaceDeps currentNamespace arr.Items
    | Union u ->
        u.Refs
        |> List.map (fun ref ->
            let (targetNamespace, _) = Naming.refToQualifiedType currentNamespace ref
            if targetNamespace <> currentNamespace then
                Set.singleton targetNamespace
            else
                Set.empty)
        |> List.fold Set.union Set.empty
    | Object obj ->
        obj.Properties
        |> Map.values
        |> Seq.map (collectNamespaceDeps currentNamespace)
        |> Seq.fold Set.union Set.empty
    | _ -> Set.empty
