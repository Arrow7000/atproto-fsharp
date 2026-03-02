module FSharp.ATProto.CodeGen.NamespaceGen

open FSharp.ATProto.Lexicon
open FSharp.ATProto.Syntax
open Fabulous.AST
open type Fabulous.AST.Ast

// ---------------------------------------------------------------------------
// Grouping
// ---------------------------------------------------------------------------

/// Group LexiconDocs by their namespace (all-but-last NSID segments, PascalCased).
let groupByNamespace (docs : LexiconDoc list) : Map<string, LexiconDoc list> =
    docs
    |> List.groupBy (fun doc -> Naming.nsidToNamespace (Nsid.value doc.Id))
    |> Map.ofList

// ---------------------------------------------------------------------------
// Topological sort
// ---------------------------------------------------------------------------

/// Sort namespaces by dependencies (dependencies first).
/// Handles cycles gracefully by breaking them.
let topologicalSort (deps : Map<string, Set<string>>) : string list =
    let allNodes =
        deps |> Map.fold (fun acc k vs -> Set.add k acc |> Set.union vs) Set.empty

    let mutable visited = Set.empty
    let mutable inStack = Set.empty
    let mutable result = []

    let rec visit (node : string) =
        if Set.contains node visited then
            ()
        elif Set.contains node inStack then
            // Cycle detected -- break it by treating as visited
            ()
        else
            inStack <- Set.add node inStack

            match Map.tryFind node deps with
            | Some nodeDeps ->
                for dep in nodeDeps do
                    visit dep
            | None -> ()

            inStack <- Set.remove node inStack
            visited <- Set.add node visited
            result <- node :: result

    for node in allNodes do
        visit node

    List.rev result

// ---------------------------------------------------------------------------
// Dependency collection
// ---------------------------------------------------------------------------

/// Collect all LexType trees from a LexDef, including nested schemas in
/// Query/Procedure/Subscription bodies and parameters.
let private collectLexTypesFromDef (def : LexDef) : LexType list =
    match def with
    | LexDef.Record r -> [ LexType.Object r.Record ]
    | LexDef.Query q ->
        [ match q.Parameters with
          | Some p -> LexType.Params p
          | None -> ()
          match q.Output with
          | Some body ->
              match body.Schema with
              | Some schema -> schema
              | None -> ()
          | None -> () ]
    | LexDef.Procedure p ->
        [ match p.Parameters with
          | Some prms -> LexType.Params prms
          | None -> ()
          match p.Input with
          | Some body ->
              match body.Schema with
              | Some schema -> schema
              | None -> ()
          | None -> ()
          match p.Output with
          | Some body ->
              match body.Schema with
              | Some schema -> schema
              | None -> ()
          | None -> () ]
    | LexDef.Subscription s ->
        [ match s.Parameters with
          | Some p -> LexType.Params p
          | None -> ()
          match s.Message with
          | Some msg -> LexType.Union msg.Schema
          | None -> () ]
    | LexDef.DefType lt -> [ lt ]
    | LexDef.Token _ -> []
    | LexDef.PermissionSet _ -> []

/// Collect cross-namespace dependencies for a group of docs.
let collectDependencies (nsName : string) (docs : LexiconDoc list) : Set<string> =
    docs
    |> List.collect (fun doc -> doc.Defs |> Map.values |> Seq.toList |> List.collect collectLexTypesFromDef)
    |> List.map (TypeMapping.collectNamespaceDeps nsName)
    |> List.fold Set.union Set.empty

// ---------------------------------------------------------------------------
// Wrapper function types and post-processing
// ---------------------------------------------------------------------------

/// Kind of XRPC wrapper function to generate.
type WrapperKind =
    | QueryWithParams // has Params + Output → Xrpc.query<Params, Output>
    | QueryNoParams // no Params, has Output → Xrpc.query<{||}, Output>
    | ProcedureWithIO // has Input + Output → Xrpc.procedure<Input, Output>
    | ProcedureInputOnly // has Input, no Output → Xrpc.procedureVoid<Input>

/// Metadata about a wrapper function to inject into generated code.
type WrapperInfo = { Nsid : string; Kind : WrapperKind }

/// Generate the wrapper function text for a given module.
/// The indent parameter is detected from the TypeId line.
let private generateWrapper (indent : string) (info : WrapperInfo) : string =
    match info.Kind with
    | QueryWithParams ->
        $"\n\n{indent}let query (agent: FSharp.ATProto.Core.AtpAgent) (parameters: Params) : System.Threading.Tasks.Task<Result<Output, FSharp.ATProto.Core.XrpcError>> =\n{indent}    FSharp.ATProto.Core.Xrpc.query<Params, Output> TypeId parameters agent"
    | QueryNoParams ->
        $"\n\n{indent}let query (agent: FSharp.ATProto.Core.AtpAgent) : System.Threading.Tasks.Task<Result<Output, FSharp.ATProto.Core.XrpcError>> =\n{indent}    FSharp.ATProto.Core.Xrpc.queryNoParams<Output> TypeId agent"
    | ProcedureWithIO ->
        $"\n\n{indent}let call (agent: FSharp.ATProto.Core.AtpAgent) (input: Input) : System.Threading.Tasks.Task<Result<Output, FSharp.ATProto.Core.XrpcError>> =\n{indent}    FSharp.ATProto.Core.Xrpc.procedure<Input, Output> TypeId input agent"
    | ProcedureInputOnly ->
        $"\n\n{indent}let call (agent: FSharp.ATProto.Core.AtpAgent) (input: Input) : System.Threading.Tasks.Task<Result<unit, FSharp.ATProto.Core.XrpcError>> =\n{indent}    FSharp.ATProto.Core.Xrpc.procedureVoid<Input> TypeId input agent"

/// Inject wrapper functions into the generated code by finding TypeId markers.
let private injectWrappers (content : string) (wrappers : WrapperInfo list) : string =
    let mutable result = content

    for wrapper in wrappers do
        let marker = sprintf "let TypeId = \"%s\"" wrapper.Nsid
        let idx = result.IndexOf (marker)

        if idx >= 0 then
            // Detect indentation from the TypeId line
            let lineStart =
                let prev = result.LastIndexOf ('\n', idx)
                if prev >= 0 then prev + 1 else 0

            let indent = result.Substring (lineStart, idx - lineStart)
            let lineEnd = result.IndexOf ('\n', idx)
            let wrapperText = generateWrapper indent wrapper

            if lineEnd >= 0 then
                result <- result.Insert (lineEnd, wrapperText)
            else
                result <- result + wrapperText

    result

// ---------------------------------------------------------------------------
// Code generation helpers
// ---------------------------------------------------------------------------

/// Generate a record type as a Fabulous.AST widget for use inside a Module builder.
let private generateRecordWidget
    (currentNamespace : string)
    (typeName : string)
    (description : string option)
    (lexObj : LexObject)
    (unionOverrides : Map<string, string>)
    =
    let fields =
        lexObj.Properties
        |> Map.toList
        |> List.map (fun (propName, lexType) ->
            let baseType =
                match Map.tryFind propName unionOverrides with
                | Some overrideType -> overrideType
                | None -> TypeMapping.lexTypeToFSharpType currentNamespace lexType

            let fieldName = Naming.toPascalCase propName |> Naming.escapeReservedWord

            let isOpt =
                not (List.contains propName lexObj.Required)
                || List.contains propName lexObj.Nullable

            let fieldType = if isOpt then sprintf "%s option" baseType else baseType

            (propName, fieldName, fieldType))

    let r =
        Ast.Record (typeName) {
            for (origName, fieldName, fieldType) in fields do
                Field(fieldName, LongIdent (fieldType))
                    .attribute (Attribute ("JsonPropertyName", ParenExpr (ConstantExpr (Ast.String (origName)))))
        }

    match description with
    | Some desc -> r.xmlDocs ([ desc ])
    | None -> r

/// Generate a record type from a LexParams (query/procedure parameters).
let private generateParamsWidget
    (currentNamespace : string)
    (typeName : string)
    (lexParams : LexParams)
    (unionOverrides : Map<string, string>)
    =
    let fields =
        lexParams.Properties
        |> Map.toList
        |> List.map (fun (propName, lexType) ->
            let baseType =
                match Map.tryFind propName unionOverrides with
                | Some overrideType -> overrideType
                | None -> TypeMapping.lexTypeToFSharpType currentNamespace lexType

            let fieldName = Naming.toPascalCase propName |> Naming.escapeReservedWord

            let isOpt = not (List.contains propName lexParams.Required)

            let fieldType = if isOpt then sprintf "%s option" baseType else baseType

            (propName, fieldName, fieldType))

    Ast.Record (typeName) {
        for (origName, fieldName, fieldType) in fields do
            Field(fieldName, LongIdent (fieldType))
                .attribute (Attribute ("JsonPropertyName", ParenExpr (ConstantExpr (Ast.String (origName)))))
    }

/// Generate a union type widget for use inside a Module builder.
let private generateUnionWidget (currentNamespace : string) (typeName : string) (union : LexUnion) =
    let cases =
        union.Refs
        |> List.map (fun ref ->
            let caseName = TypeGen.unionCaseName ref

            let (_targetNamespace, qualifiedType) =
                Naming.refToQualifiedType currentNamespace ref

            let tag =
                if ref.EndsWith ("#main") then
                    ref.Substring (0, ref.Length - 5)
                else
                    ref

            (caseName, qualifiedType, tag))
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

/// Collect inline union DU info from an object's properties.
/// Returns (widgets to emit, overrides map: property name -> F# type string).
/// Deduplicates when multiple properties share the same sorted refs.
/// parentTypeName is used as a prefix for DU names to avoid collisions across objects in the same module.
let private collectInlineUnionsAndOverrides
    (currentNamespace : string)
    (parentTypeName : string)
    (properties : Map<string, LexType>)
    =
    let raw =
        properties
        |> Map.toList
        |> List.choose (fun (propName, lexType) ->
            match lexType with
            | LexType.Union u -> Some (propName, u, false)
            | LexType.Array { Items = LexType.Union u } -> Some (propName, u, true)
            | _ -> None)

    let groups = raw |> List.groupBy (fun (_, u, _) -> (u.Refs |> List.sort, u.Closed))

    let mutable widgets = []
    let mutable overrides = Map.empty

    for (_, group) in groups do
        let (firstName, firstUnion, firstIsArray) = group |> List.head

        let duName =
            if firstIsArray then
                parentTypeName + (Naming.toPascalCase firstName) + "Item"
            else
                parentTypeName + (Naming.toPascalCase firstName) + "Union"

        let widget = generateUnionWidget currentNamespace duName firstUnion
        widgets <- widget :: widgets

        for (propName, _, isArray) in group do
            let typeName = if isArray then sprintf "%s list" duName else duName
            overrides <- Map.add propName typeName overrides

    (List.rev widgets, overrides)

/// Collect known-value DU info from an object's properties.
/// Returns (widgets to emit, overrides map: property name -> F# type string).
let private collectKnownValueOverrides (parentTypeName : string) (properties : Map<string, LexType>) =
    let mutable widgets = []
    let mutable overrides = Map.empty

    for (propName, lexType) in Map.toList properties do
        match lexType with
        | LexType.String s when s.KnownValues.IsSome && s.KnownValues.Value.Length > 0 ->
            let duName = parentTypeName + (Naming.toPascalCase propName)
            let widget = TypeGen.generateKnownValueDU duName s.KnownValues.Value
            widgets <- widget :: widgets
            overrides <- Map.add propName duName overrides
        | LexType.Array { Items = LexType.String s } when s.KnownValues.IsSome && s.KnownValues.Value.Length > 0 ->
            let duName = parentTypeName + (Naming.toPascalCase propName) + "Item"
            let widget = TypeGen.generateKnownValueDU duName s.KnownValues.Value
            widgets <- widget :: widgets
            overrides <- Map.add propName (sprintf "%s list" duName) overrides
        | _ -> ()

    (List.rev widgets, overrides)

/// Check if a LexBody has JSON schema (application/json encoding with a non-empty object schema).
let private hasJsonObjectSchema (body : LexBody) : LexObject option =
    if body.Encoding = "application/json" then
        match body.Schema with
        | Some (LexType.Object obj) when not obj.Properties.IsEmpty -> Some obj
        | _ -> None
    else
        None

/// Check if a LexBody has a ref schema (application/json encoding with a ref to another type).
let private hasJsonRefSchema (body : LexBody) : string option =
    if body.Encoding = "application/json" then
        match body.Schema with
        | Some (LexType.Ref r) -> Some r.Ref
        | _ -> None
    else
        None

/// Check if a LexBody has any usable output schema (inline object or ref).
let private hasJsonOutputSchema (body : LexBody) : bool =
    hasJsonObjectSchema body |> Option.isSome
    || hasJsonRefSchema body |> Option.isSome

// ---------------------------------------------------------------------------
// Module generation for each LexDef type
// ---------------------------------------------------------------------------

/// Generate module content for a Record main def.
let private generateRecordModule
    (currentNamespace : string)
    (nsid : string)
    (moduleName : string)
    (record : LexRecord)
    (otherDefs : (string * LexDef) list)
    =
    let (mainDuWidgets, mainUnionOverrides) =
        collectInlineUnionsAndOverrides currentNamespace moduleName record.Record.Properties

    let (mainKvWidgets, mainKvOverrides) =
        collectKnownValueOverrides moduleName record.Record.Properties

    let mainOverrides =
        Map.fold (fun acc k v -> Map.add k v acc) mainUnionOverrides mainKvOverrides

    Module (moduleName) {
        Value("TypeId", ConstantExpr (Ast.String (nsid))).attribute (Attribute ("Literal"))

        for w in mainKvWidgets do
            w

        for w in mainDuWidgets do
            w

        if not record.Record.Properties.IsEmpty then
            generateRecordWidget currentNamespace moduleName record.Description record.Record mainOverrides

        for (defName, def) in otherDefs do
            let typeName = Naming.defToTypeName moduleName defName

            match def with
            | LexDef.DefType (LexType.Object obj) when not obj.Properties.IsEmpty ->
                let (duWidgets, unionOverrides) =
                    collectInlineUnionsAndOverrides currentNamespace typeName obj.Properties

                let (kvWidgets, kvOverrides) = collectKnownValueOverrides typeName obj.Properties
                let overrides = Map.fold (fun acc k v -> Map.add k v acc) unionOverrides kvOverrides

                for w in kvWidgets do
                    w

                for w in duWidgets do
                    w

                generateRecordWidget currentNamespace typeName obj.Description obj overrides
            | LexDef.DefType (LexType.Object _) ->
                // Empty object: generate as JsonElement alias so union DU cases can reference it
                Abbrev (typeName, LongIdent ("JsonElement"))
            | LexDef.DefType (LexType.Union u) -> generateUnionWidget currentNamespace typeName u
            | LexDef.Token t ->
                Value(typeName, ConstantExpr (Ast.String (sprintf "%s#%s" nsid defName)))
                    .attribute (Attribute ("Literal"))
            | LexDef.DefType (LexType.String s) when s.KnownValues.IsSome && s.KnownValues.Value.Length > 0 ->
                TypeGen.generateKnownValueDU typeName s.KnownValues.Value
            | LexDef.DefType (LexType.String _) -> Abbrev (typeName, LongIdent ("string"))
            | _ -> ()
    }

/// Generate module content for a Query main def.
let private generateQueryModule
    (currentNamespace : string)
    (nsid : string)
    (moduleName : string)
    (query : LexQuery)
    (otherDefs : (string * LexDef) list)
    =
    Module (moduleName) {
        Value("TypeId", ConstantExpr (Ast.String (nsid))).attribute (Attribute ("Literal"))

        match query.Parameters with
        | Some p when p.Properties.Count > 0 ->
            let (duWidgets, unionOverrides) =
                collectInlineUnionsAndOverrides currentNamespace "Params" p.Properties

            let (kvWidgets, kvOverrides) = collectKnownValueOverrides "Params" p.Properties
            let overrides = Map.fold (fun acc k v -> Map.add k v acc) unionOverrides kvOverrides

            for w in kvWidgets do
                w

            for w in duWidgets do
                w

            generateParamsWidget currentNamespace "Params" p overrides
        | _ -> ()

        match query.Output with
        | Some body ->
            match hasJsonObjectSchema body with
            | Some obj ->
                let (duWidgets, unionOverrides) =
                    collectInlineUnionsAndOverrides currentNamespace "Output" obj.Properties

                let (kvWidgets, kvOverrides) = collectKnownValueOverrides "Output" obj.Properties
                let overrides = Map.fold (fun acc k v -> Map.add k v acc) unionOverrides kvOverrides

                for w in kvWidgets do
                    w

                for w in duWidgets do
                    w

                generateRecordWidget currentNamespace "Output" body.Description obj overrides
            | None ->
                match hasJsonRefSchema body with
                | Some ref ->
                    let (_ns, qualType) = Naming.refToQualifiedType currentNamespace ref
                    Abbrev ("Output", LongIdent (qualType))
                | None -> ()
        | None -> ()

        if not query.Errors.IsEmpty then
            Module ("Errors") {
                for err in query.Errors do
                    let name = Naming.toPascalCase err.Name |> Naming.escapeReservedWord

                    Value(name, ConstantExpr (Ast.String (err.Name))).attribute (Attribute ("Literal"))
            }

        for (defName, def) in otherDefs do
            let typeName = Naming.defToTypeName moduleName defName

            match def with
            | LexDef.DefType (LexType.Object obj) when not obj.Properties.IsEmpty ->
                let (duWidgets, unionOverrides) =
                    collectInlineUnionsAndOverrides currentNamespace typeName obj.Properties

                let (kvWidgets, kvOverrides) = collectKnownValueOverrides typeName obj.Properties
                let overrides = Map.fold (fun acc k v -> Map.add k v acc) unionOverrides kvOverrides

                for w in kvWidgets do
                    w

                for w in duWidgets do
                    w

                generateRecordWidget currentNamespace typeName obj.Description obj overrides
            | LexDef.DefType (LexType.Object _) ->
                // Empty object: generate as JsonElement alias so union DU cases can reference it
                Abbrev (typeName, LongIdent ("JsonElement"))
            | LexDef.DefType (LexType.Union u) -> generateUnionWidget currentNamespace typeName u
            | LexDef.Token t ->
                Value(typeName, ConstantExpr (Ast.String (sprintf "%s#%s" nsid defName)))
                    .attribute (Attribute ("Literal"))
            | LexDef.DefType (LexType.String s) when s.KnownValues.IsSome && s.KnownValues.Value.Length > 0 ->
                TypeGen.generateKnownValueDU typeName s.KnownValues.Value
            | LexDef.DefType (LexType.String _) -> Abbrev (typeName, LongIdent ("string"))
            | _ -> ()
    }

/// Generate module content for a Procedure main def.
let private generateProcedureModule
    (currentNamespace : string)
    (nsid : string)
    (moduleName : string)
    (proc : LexProcedure)
    (otherDefs : (string * LexDef) list)
    =
    Module (moduleName) {
        Value("TypeId", ConstantExpr (Ast.String (nsid))).attribute (Attribute ("Literal"))

        match proc.Parameters with
        | Some p when p.Properties.Count > 0 ->
            let (duWidgets, unionOverrides) =
                collectInlineUnionsAndOverrides currentNamespace "Params" p.Properties

            let (kvWidgets, kvOverrides) = collectKnownValueOverrides "Params" p.Properties
            let overrides = Map.fold (fun acc k v -> Map.add k v acc) unionOverrides kvOverrides

            for w in kvWidgets do
                w

            for w in duWidgets do
                w

            generateParamsWidget currentNamespace "Params" p overrides
        | _ -> ()

        match proc.Input with
        | Some body ->
            match hasJsonObjectSchema body with
            | Some obj ->
                let (duWidgets, unionOverrides) =
                    collectInlineUnionsAndOverrides currentNamespace "Input" obj.Properties

                let (kvWidgets, kvOverrides) = collectKnownValueOverrides "Input" obj.Properties
                let overrides = Map.fold (fun acc k v -> Map.add k v acc) unionOverrides kvOverrides

                for w in kvWidgets do
                    w

                for w in duWidgets do
                    w

                generateRecordWidget currentNamespace "Input" body.Description obj overrides
            | None ->
                match hasJsonRefSchema body with
                | Some ref ->
                    let (_ns, qualType) = Naming.refToQualifiedType currentNamespace ref
                    Abbrev ("Input", LongIdent (qualType))
                | None -> ()
        | None -> ()

        match proc.Output with
        | Some body ->
            match hasJsonObjectSchema body with
            | Some obj ->
                let (duWidgets, unionOverrides) =
                    collectInlineUnionsAndOverrides currentNamespace "Output" obj.Properties

                let (kvWidgets, kvOverrides) = collectKnownValueOverrides "Output" obj.Properties
                let overrides = Map.fold (fun acc k v -> Map.add k v acc) unionOverrides kvOverrides

                for w in kvWidgets do
                    w

                for w in duWidgets do
                    w

                generateRecordWidget currentNamespace "Output" body.Description obj overrides
            | None ->
                match hasJsonRefSchema body with
                | Some ref ->
                    let (_ns, qualType) = Naming.refToQualifiedType currentNamespace ref
                    Abbrev ("Output", LongIdent (qualType))
                | None -> ()
        | None -> ()

        if not proc.Errors.IsEmpty then
            Module ("Errors") {
                for err in proc.Errors do
                    let name = Naming.toPascalCase err.Name |> Naming.escapeReservedWord

                    Value(name, ConstantExpr (Ast.String (err.Name))).attribute (Attribute ("Literal"))
            }

        for (defName, def) in otherDefs do
            let typeName = Naming.defToTypeName moduleName defName

            match def with
            | LexDef.DefType (LexType.Object obj) when not obj.Properties.IsEmpty ->
                let (duWidgets, unionOverrides) =
                    collectInlineUnionsAndOverrides currentNamespace typeName obj.Properties

                let (kvWidgets, kvOverrides) = collectKnownValueOverrides typeName obj.Properties
                let overrides = Map.fold (fun acc k v -> Map.add k v acc) unionOverrides kvOverrides

                for w in kvWidgets do
                    w

                for w in duWidgets do
                    w

                generateRecordWidget currentNamespace typeName obj.Description obj overrides
            | LexDef.DefType (LexType.Object _) ->
                // Empty object: generate as JsonElement alias so union DU cases can reference it
                Abbrev (typeName, LongIdent ("JsonElement"))
            | LexDef.DefType (LexType.Union u) -> generateUnionWidget currentNamespace typeName u
            | LexDef.Token t ->
                Value(typeName, ConstantExpr (Ast.String (sprintf "%s#%s" nsid defName)))
                    .attribute (Attribute ("Literal"))
            | LexDef.DefType (LexType.String s) when s.KnownValues.IsSome && s.KnownValues.Value.Length > 0 ->
                TypeGen.generateKnownValueDU typeName s.KnownValues.Value
            | LexDef.DefType (LexType.String _) -> Abbrev (typeName, LongIdent ("string"))
            | _ -> ()
    }

/// Generate module content for a Subscription main def.
let private generateSubscriptionModule
    (currentNamespace : string)
    (nsid : string)
    (moduleName : string)
    (sub : LexSubscription)
    (otherDefs : (string * LexDef) list)
    =
    Module (moduleName) {
        Value("TypeId", ConstantExpr (Ast.String (nsid))).attribute (Attribute ("Literal"))

        match sub.Parameters with
        | Some p when p.Properties.Count > 0 ->
            let (duWidgets, unionOverrides) =
                collectInlineUnionsAndOverrides currentNamespace "Params" p.Properties

            let (kvWidgets, kvOverrides) = collectKnownValueOverrides "Params" p.Properties
            let overrides = Map.fold (fun acc k v -> Map.add k v acc) unionOverrides kvOverrides

            for w in kvWidgets do
                w

            for w in duWidgets do
                w

            generateParamsWidget currentNamespace "Params" p overrides
        | _ -> ()

        match sub.Message with
        | Some msg -> generateUnionWidget currentNamespace "Message" msg.Schema
        | None -> ()

        if not sub.Errors.IsEmpty then
            Module ("Errors") {
                for err in sub.Errors do
                    let name = Naming.toPascalCase err.Name |> Naming.escapeReservedWord

                    Value(name, ConstantExpr (Ast.String (err.Name))).attribute (Attribute ("Literal"))
            }

        for (defName, def) in otherDefs do
            let typeName = Naming.defToTypeName moduleName defName

            match def with
            | LexDef.DefType (LexType.Object obj) when not obj.Properties.IsEmpty ->
                let (duWidgets, unionOverrides) =
                    collectInlineUnionsAndOverrides currentNamespace typeName obj.Properties

                let (kvWidgets, kvOverrides) = collectKnownValueOverrides typeName obj.Properties
                let overrides = Map.fold (fun acc k v -> Map.add k v acc) unionOverrides kvOverrides

                for w in kvWidgets do
                    w

                for w in duWidgets do
                    w

                generateRecordWidget currentNamespace typeName obj.Description obj overrides
            | LexDef.DefType (LexType.Object _) ->
                // Empty object: generate as JsonElement alias so union DU cases can reference it
                Abbrev (typeName, LongIdent ("JsonElement"))
            | LexDef.DefType (LexType.Union u) -> generateUnionWidget currentNamespace typeName u
            | LexDef.Token t ->
                Value(typeName, ConstantExpr (Ast.String (sprintf "%s#%s" nsid defName)))
                    .attribute (Attribute ("Literal"))
            | LexDef.DefType (LexType.String s) when s.KnownValues.IsSome && s.KnownValues.Value.Length > 0 ->
                TypeGen.generateKnownValueDU typeName s.KnownValues.Value
            | LexDef.DefType (LexType.String _) -> Abbrev (typeName, LongIdent ("string"))
            | _ -> ()
    }

/// Generate module content for a doc that has no main def (only non-main defs, e.g. "defs" docs).
let private generateDefsOnlyModule
    (currentNamespace : string)
    (nsid : string)
    (moduleName : string)
    (allDefs : (string * LexDef) list)
    =
    Module (moduleName) {
        for (defName, def) in allDefs do
            let typeName = Naming.defToTypeName moduleName defName

            match def with
            | LexDef.DefType (LexType.Object obj) when not obj.Properties.IsEmpty ->
                let (duWidgets, unionOverrides) =
                    collectInlineUnionsAndOverrides currentNamespace typeName obj.Properties

                let (kvWidgets, kvOverrides) = collectKnownValueOverrides typeName obj.Properties
                let overrides = Map.fold (fun acc k v -> Map.add k v acc) unionOverrides kvOverrides

                for w in kvWidgets do
                    w

                for w in duWidgets do
                    w

                generateRecordWidget currentNamespace typeName obj.Description obj overrides
            | LexDef.DefType (LexType.Object _) ->
                // Empty object: generate as JsonElement alias so union DU cases can reference it
                Abbrev (typeName, LongIdent ("JsonElement"))
            | LexDef.DefType (LexType.Union u) -> generateUnionWidget currentNamespace typeName u
            | LexDef.Token t ->
                Value(typeName, ConstantExpr (Ast.String (sprintf "%s#%s" nsid defName)))
                    .attribute (Attribute ("Literal"))
            | LexDef.DefType (LexType.String s) when s.KnownValues.IsSome && s.KnownValues.Value.Length > 0 ->
                TypeGen.generateKnownValueDU typeName s.KnownValues.Value
            | LexDef.DefType (LexType.String _) -> Abbrev (typeName, LongIdent ("string"))
            | LexDef.DefType (LexType.Array arr) ->
                let inner = TypeMapping.lexTypeToFSharpType currentNamespace arr.Items
                Abbrev (typeName, LongIdent (sprintf "%s list" inner))
            | LexDef.DefType (LexType.Boolean _) -> Abbrev (typeName, LongIdent ("bool"))
            | LexDef.DefType (LexType.Integer _) -> Abbrev (typeName, LongIdent ("int64"))
            | LexDef.DefType (LexType.Bytes _) -> Abbrev (typeName, LongIdent ("byte[]"))
            | LexDef.PermissionSet _ ->
                // PermissionSets are skipped in code generation
                ()
            | _ -> ()
    }

// ---------------------------------------------------------------------------
// Group module generation
// ---------------------------------------------------------------------------

/// Generate a top-level module widget for a namespace group.
/// Each namespace group becomes a module within the single shared namespace.
let private generateGroupModule (nsName : string) (docs : LexiconDoc list) =
    // Sort docs by NSID for deterministic output
    let sortedDocs = docs |> List.sortBy (fun doc -> Nsid.value doc.Id)

    Module (nsName) {
        for doc in sortedDocs do
            let nsid = Nsid.value doc.Id
            let moduleName = Naming.nsidToModuleName nsid
            let currentNamespace = nsName

            // Separate main def from other defs
            let mainDef = Map.tryFind "main" doc.Defs

            let otherDefs =
                doc.Defs
                |> Map.toList
                |> List.filter (fun (name, _) -> name <> "main")
                |> List.sortBy fst

            match mainDef with
            | Some (LexDef.Record r) -> generateRecordModule currentNamespace nsid moduleName r otherDefs
            | Some (LexDef.Query q) -> generateQueryModule currentNamespace nsid moduleName q otherDefs
            | Some (LexDef.Procedure p) -> generateProcedureModule currentNamespace nsid moduleName p otherDefs
            | Some (LexDef.Subscription s) -> generateSubscriptionModule currentNamespace nsid moduleName s otherDefs
            | Some (LexDef.PermissionSet _) ->
                // PermissionSets: generate module with non-main defs only (if any)
                if not otherDefs.IsEmpty then
                    generateDefsOnlyModule currentNamespace nsid moduleName otherDefs
            | Some (LexDef.DefType mainLexType) ->
                // Main def is a type (object, union, array, string, etc.)
                // Generate it alongside other defs in a module
                let allDefs = ("main", LexDef.DefType mainLexType) :: otherDefs
                generateDefsOnlyModule currentNamespace nsid moduleName allDefs
            | Some _ ->
                if not otherDefs.IsEmpty then
                    generateDefsOnlyModule currentNamespace nsid moduleName otherDefs
            | None ->
                // No main def: generate all defs in a module
                let allDefs = doc.Defs |> Map.toList |> List.sortBy fst

                if not allDefs.IsEmpty then
                    generateDefsOnlyModule currentNamespace nsid moduleName allDefs
    }

// ---------------------------------------------------------------------------
// Wrapper metadata collection
// ---------------------------------------------------------------------------

/// Determine whether a Query def should get a wrapper, and if so what kind.
let private queryWrapperKind (query : LexQuery) : WrapperKind option =
    let hasOutput =
        query.Output |> Option.map hasJsonOutputSchema |> Option.defaultValue false

    let hasParams =
        match query.Parameters with
        | Some p when p.Properties.Count > 0 -> true
        | _ -> false

    match hasOutput, hasParams with
    | true, true -> Some QueryWithParams
    | true, false -> Some QueryNoParams
    | false, _ -> None

/// Determine whether a Procedure def should get a wrapper, and if so what kind.
let private procedureWrapperKind (proc : LexProcedure) : WrapperKind option =
    let hasInput =
        proc.Input |> Option.map hasJsonOutputSchema |> Option.defaultValue false

    let hasOutput =
        proc.Output |> Option.map hasJsonOutputSchema |> Option.defaultValue false

    if not hasInput then None
    else if hasOutput then Some ProcedureWithIO
    else Some ProcedureInputOnly

/// Collect WrapperInfo for all docs that need XRPC wrapper functions.
let private collectWrappers (docs : LexiconDoc list) : WrapperInfo list =
    docs
    |> List.choose (fun doc ->
        let nsid = Nsid.value doc.Id
        let mainDef = Map.tryFind "main" doc.Defs

        match mainDef with
        | Some (LexDef.Query q) -> queryWrapperKind q |> Option.map (fun kind -> { Nsid = nsid; Kind = kind })
        | Some (LexDef.Procedure p) -> procedureWrapperKind p |> Option.map (fun kind -> { Nsid = nsid; Kind = kind })
        | _ -> None)

// ---------------------------------------------------------------------------
// Complete pipeline
// ---------------------------------------------------------------------------

/// Generate all types as a single file under one namespace rec.
/// Returns a single (fileName, content) pair.
let generateAll (docs : LexiconDoc list) : (string * string) list =
    // 1. Group by namespace
    let groups = groupByNamespace docs

    // 2. Collect dependencies for each group
    let deps =
        groups |> Map.map (fun nsName nsDocs -> collectDependencies nsName nsDocs)

    // 3. Topological sort for module ordering within the file
    let order = topologicalSort deps

    // 4. Collect wrapper metadata before code generation
    let wrappers = collectWrappers docs

    // 5. Generate a single file with all modules under one namespace rec
    let namespaceWidget =
        (Namespace ("FSharp.ATProto.Bluesky") {
            Open ("System.Text.Json")
            Open ("System.Text.Json.Serialization")
            Open ("System.Threading.Tasks")
            Open ("FSharp.ATProto.Core")
            Open ("FSharp.ATProto.Syntax")

            for nsName in order do
                match Map.tryFind nsName groups with
                | Some nsDocs -> generateGroupModule nsName nsDocs
                | None -> ()
        })
            .toRecursive ()

    let content = Oak () { namespaceWidget } |> Gen.mkOak |> Gen.run

    // 6. Post-process: inject XRPC wrapper functions
    let contentWithWrappers = injectWrappers content wrappers

    [ ("Generated.fs", contentWithWrappers) ]
