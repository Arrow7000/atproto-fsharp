module FSharp.ATProto.CodeGen.TypeGen

open FSharp.ATProto.Lexicon
open Fabulous.AST
open type Fabulous.AST.Ast

/// Determine if a property should be wrapped in option.
/// A property is option if it is NOT in required OR IS in nullable.
let private isOptionField (required : string list) (nullable : string list) (propName : string) : bool =
    not (List.contains propName required) || List.contains propName nullable

/// Generate F# source for a record type from a LexObject.
let generateRecord
    (currentNamespace : string)
    (typeName : string)
    (description : string option)
    (lexObj : LexObject)
    : string =
    let fields =
        lexObj.Properties
        |> Map.toList
        |> List.map (fun (propName, lexType) ->
            let baseType = TypeMapping.lexTypeToFSharpType currentNamespace lexType
            let fieldName = Naming.toPascalCase propName |> Naming.escapeReservedWord
            let isOpt = isOptionField lexObj.Required lexObj.Nullable propName
            let fieldType = if isOpt then sprintf "%s option" baseType else baseType
            (propName, fieldName, fieldType))

    let recordWidget =
        let r =
            Ast.Record (typeName) {
                for (origName, fieldName, fieldType) in fields do
                    Field(fieldName, LongIdent (fieldType))
                        .attribute (Attribute ("JsonPropertyName", ParenExpr (ConstantExpr (Ast.String (origName)))))
            }

        match description with
        | Some desc -> r.xmlDocs ([ desc ])
        | None -> r

    Oak () { AnonymousModule () { recordWidget } } |> Gen.mkOak |> Gen.run

/// Generate a [<Literal>] let binding for a token.
let generateToken (nsid : string) (defName : string) (token : LexToken) : string =
    let name = Naming.toPascalCase defName |> Naming.escapeReservedWord
    let tokenValue = sprintf "%s#%s" nsid defName

    let valueWidget =
        let v =
            Value(name, ConstantExpr (Ast.String (tokenValue))).attribute (Attribute ("Literal"))

        match token.Description with
        | Some desc -> v.xmlDocs ([ desc ])
        | None -> v

    Oak () { AnonymousModule () { valueWidget } } |> Gen.mkOak |> Gen.run

/// Clean a raw string into a valid PascalCase F# identifier.
/// Splits on non-alphanumeric characters, PascalCases each segment, strips leading digits.
let private cleanIdentifier (raw : string) : string =
    raw.Split ([| '.'; '-'; '_'; '!'; ':'; '/'; ' ' |], System.StringSplitOptions.RemoveEmptyEntries)
    |> Array.map Naming.toPascalCase
    |> System.String.Concat
    |> fun s ->
        // Strip leading digits (invalid F# identifier start)
        let trimmed = s.TrimStart ([| '0'; '1'; '2'; '3'; '4'; '5'; '6'; '7'; '8'; '9' |])

        if System.String.IsNullOrEmpty (trimmed) then
            "Value" + s
        else
            trimmed

/// Extract a clean name from a known value string.
/// "app.bsky.feed.defs#sortHot" -> "SortHot"
/// "app.bsky.something" -> "AppBskySomething"
/// "!hide" -> "Hide"
/// "dmca-violation" -> "DmcaViolation"
let private knownValueToName (value : string) : string =
    if value.Contains ('#') then
        let afterHash = value.Substring (value.IndexOf ('#') + 1)
        cleanIdentifier afterHash
    else
        cleanIdentifier value

/// Generate a module with string constant [<Literal>] let bindings.
let generateKnownValues (fieldName : string) (values : string list) : string =
    let moduleName = Naming.toPascalCase fieldName

    let moduleWidget =
        Module (moduleName) {
            for v in values do
                let name = knownValueToName v |> Naming.escapeReservedWord
                Value(name, ConstantExpr (Ast.String (v))).attribute (Attribute ("Literal"))
        }

    Oak () { AnonymousModule () { moduleWidget } } |> Gen.mkOak |> Gen.run

/// Generate a known-values DU as a Fabulous.AST widget for use inside a Module builder.
/// Each fieldless case gets [<JsonName("original-value")>], plus an Unknown of string fallback.
/// The DU gets [<JsonConverter(typeof<KnownValueConverter<TypeName>>)>].
let generateKnownValueDU (typeName : string) (values : string list) =
    let cases =
        values
        |> List.map (fun v ->
            let caseName = knownValueToName v |> Naming.escapeReservedWord
            (caseName, v))
        // Rename any case named "Unknown" to "UnknownValue" to avoid collision with fallback
        |> List.map (fun (caseName, v) ->
            if caseName = "Unknown" then
                ("UnknownValue", v)
            else
                (caseName, v))
        |> List.fold
            (fun (acc, seen : Map<string, int>) (caseName, origValue) ->
                match Map.tryFind caseName seen with
                | Some count ->
                    let newName = sprintf "%s%d" caseName (count + 1)
                    ((newName, origValue) :: acc, Map.add caseName (count + 1) seen)
                | None -> ((caseName, origValue) :: acc, Map.add caseName 1 seen))
            ([], Map.empty)
        |> fst
        |> List.rev

    (Union (typeName) {
        for (caseName, origValue) in cases do
            UnionCase(caseName).attribute (Attribute ("JsonName", ParenExpr (ConstantExpr (Ast.String (origValue)))))

        UnionCase ("Unknown", "string")
    })
        .attribute (Attribute (sprintf "JsonConverter(typeof<FSharp.ATProto.Core.KnownValueConverter<%s>>)" typeName))

/// Extract a DU case name from a ref string.
/// "app.bsky.embed.images" -> "Images" (last NSID segment, PascalCased)
/// "app.bsky.embed.images#main" -> "Images" (main def -> use doc name)
/// "app.bsky.feed.defs#feedViewPost" -> "FeedViewPost" (non-main -> PascalCase def name)
let unionCaseName (ref : string) : string =
    if ref.Contains ('#') then
        let parts = ref.Split ('#')
        let defName = parts.[1]

        if defName = "main" then
            Naming.nsidToModuleName parts.[0]
        else
            Naming.toPascalCase defName
    else
        Naming.nsidToModuleName ref

/// Compute the $type tag value for a ref.
/// Bare NSID "app.bsky.embed.images" -> "app.bsky.embed.images"
/// With #main "app.bsky.embed.images#main" -> "app.bsky.embed.images" (strip #main)
/// With non-main fragment "app.bsky.feed.defs#feedViewPost" -> "app.bsky.feed.defs#feedViewPost" (keep as-is)
let private tagValue (ref : string) : string =
    if ref.EndsWith ("#main") then
        ref.Substring (0, ref.Length - 5)
    else
        ref

/// Deduplicate case names by appending a suffix when collisions occur.
let private deduplicateCaseNames (cases : (string * string * string) list) : (string * string * string) list =
    cases
    |> List.fold
        (fun (acc, seen : Map<string, int>) (caseName, qualType, tag) ->
            match Map.tryFind caseName seen with
            | Some count ->
                let newName = sprintf "%s%d" caseName (count + 1)
                ((newName, qualType, tag) :: acc, Map.add caseName (count + 1) seen)
            | None -> ((caseName, qualType, tag) :: acc, Map.add caseName 1 seen))
        ([], Map.empty)
    |> fst
    |> List.rev

/// Generate F# DU source from a LexUnion.
let generateUnion (currentNamespace : string) (typeName : string) (union : LexUnion) : string =
    let cases =
        union.Refs
        |> List.map (fun ref ->
            let caseName = unionCaseName ref

            let (_targetNamespace, qualifiedType) =
                Naming.refToQualifiedType currentNamespace ref

            let tag = tagValue ref
            (caseName, qualifiedType, tag))
        |> deduplicateCaseNames

    let unionWidget =
        let u =
            (Union (typeName) {
                for (caseName, qualType, tag) in cases do
                    UnionCase(caseName, qualType)
                        .attribute (Attribute ("JsonName", ParenExpr (ConstantExpr (Ast.String (tag)))))

                if not union.Closed then
                    UnionCase ("Unknown", [ "string"; "System.Text.Json.JsonElement" ])
            })
                .attribute (
                    Attribute (
                        "JsonFSharpConverter(JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.UnwrapSingleFieldCases, unionTagName = \"$type\")"
                    )
                )

        match union.Description with
        | Some desc -> u.xmlDocs ([ desc ])
        | None -> u

    Oak () { AnonymousModule () { unionWidget } } |> Gen.mkOak |> Gen.run
