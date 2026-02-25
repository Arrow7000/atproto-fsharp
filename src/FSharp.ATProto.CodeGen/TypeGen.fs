module FSharp.ATProto.CodeGen.TypeGen

open FSharp.ATProto.Lexicon
open Fabulous.AST
open type Fabulous.AST.Ast

/// Determine if a property should be wrapped in option.
/// A property is option if it is NOT in required OR IS in nullable.
let private isOptionField (required: string list) (nullable: string list) (propName: string) : bool =
    not (List.contains propName required) || List.contains propName nullable

/// Generate F# source for a record type from a LexObject.
let generateRecord (currentNamespace: string) (typeName: string) (description: string option) (lexObj: LexObject) : string =
    let fields =
        lexObj.Properties
        |> Map.toList
        |> List.map (fun (propName, lexType) ->
            let baseType = TypeMapping.lexTypeToFSharpType currentNamespace lexType
            let fieldName = Naming.toPascalCase propName |> Naming.escapeReservedWord
            let isOpt = isOptionField lexObj.Required lexObj.Nullable propName
            let fieldType =
                if isOpt then
                    sprintf "%s option" baseType
                else
                    baseType
            (propName, fieldName, fieldType))

    let recordWidget =
        let r =
            Ast.Record(typeName) {
                for (origName, fieldName, fieldType) in fields do
                    Field(fieldName, LongIdent(fieldType))
                        .attribute(
                            Attribute(
                                "JsonPropertyName",
                                ParenExpr(ConstantExpr(Ast.String(origName)))))
            }
        match description with
        | Some desc -> r.xmlDocs([ desc ])
        | None -> r

    Oak() { AnonymousModule() { recordWidget } }
    |> Gen.mkOak
    |> Gen.run

/// Generate a [<Literal>] let binding for a token.
let generateToken (nsid: string) (defName: string) (token: LexToken) : string =
    let name = Naming.toPascalCase defName |> Naming.escapeReservedWord
    let tokenValue = sprintf "%s#%s" nsid defName

    let valueWidget =
        let v =
            Value(name, ConstantExpr(Ast.String(tokenValue)))
                .attribute(Attribute("Literal"))
        match token.Description with
        | Some desc -> v.xmlDocs([ desc ])
        | None -> v

    Oak() { AnonymousModule() { valueWidget } }
    |> Gen.mkOak
    |> Gen.run

/// Extract a clean name from a known value string.
/// "app.bsky.feed.defs#sortHot" -> "SortHot"
/// "app.bsky.something" -> "AppBskySomething"
let private knownValueToName (value: string) : string =
    if value.Contains('#') then
        let afterHash = value.Substring(value.IndexOf('#') + 1)
        Naming.toPascalCase afterHash
    else
        // No fragment - clean up dots and PascalCase
        value.Split('.')
        |> Array.map Naming.toPascalCase
        |> System.String.Concat

/// Generate a module with string constant [<Literal>] let bindings.
let generateKnownValues (fieldName: string) (values: string list) : string =
    let moduleName = Naming.toPascalCase fieldName

    let moduleWidget =
        Module(moduleName) {
            for v in values do
                let name = knownValueToName v |> Naming.escapeReservedWord
                Value(name, ConstantExpr(Ast.String(v)))
                    .attribute(Attribute("Literal"))
        }

    Oak() { AnonymousModule() { moduleWidget } }
    |> Gen.mkOak
    |> Gen.run
