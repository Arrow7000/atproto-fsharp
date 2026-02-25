module WrapperGenTests

open Expecto
open FSharp.ATProto.Lexicon
open FSharp.ATProto.Syntax

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

let private mkNsid (s: string) =
    match Nsid.parse s with
    | Ok nsid -> nsid
    | Error e -> failwithf "Invalid test NSID %s: %s" s e

let private emptyStringConstraints: LexString =
    { Description = None; Default = None; Const = None; Enum = None
      KnownValues = None; Format = None; MinLength = None; MaxLength = None
      MinGraphemes = None; MaxGraphemes = None }

let private emptyIntegerConstraints: LexInteger =
    { Description = None; Default = None; Const = None; Enum = None
      Minimum = None; Maximum = None }

/// A query with both Params and Output.
let private queryWithParamsDoc =
    { Lexicon = 1
      Id = mkNsid "app.bsky.feed.getTimeline"
      Revision = None
      Description = None
      Defs =
          Map.ofList [
              "main",
              LexDef.Query
                  { Description = None
                    Parameters =
                      Some
                          { Description = None
                            Properties = Map.ofList [ "limit", LexType.Integer emptyIntegerConstraints ]
                            Required = [] }
                    Output =
                      Some
                          { Description = None
                            Encoding = "application/json"
                            Schema =
                              Some
                                  (LexType.Object
                                      { Description = None
                                        Properties = Map.ofList [ "feed", LexType.Array { Description = None; Items = LexType.String emptyStringConstraints; MinLength = None; MaxLength = None } ]
                                        Required = [ "feed" ]
                                        Nullable = [] }) }
                    Errors = [] }
          ] }

/// A query with Output but no Params.
let private queryNoParamsDoc =
    { Lexicon = 1
      Id = mkNsid "app.bsky.actor.getProfile"
      Revision = None
      Description = None
      Defs =
          Map.ofList [
              "main",
              LexDef.Query
                  { Description = None
                    Parameters = None
                    Output =
                      Some
                          { Description = None
                            Encoding = "application/json"
                            Schema =
                              Some
                                  (LexType.Object
                                      { Description = None
                                        Properties = Map.ofList [ "handle", LexType.String emptyStringConstraints ]
                                        Required = [ "handle" ]
                                        Nullable = [] }) }
                    Errors = [] }
          ] }

/// A procedure with Input and Output.
let private procedureWithIODoc =
    { Lexicon = 1
      Id = mkNsid "com.atproto.repo.createRecord"
      Revision = None
      Description = None
      Defs =
          Map.ofList [
              "main",
              LexDef.Procedure
                  { Description = None
                    Parameters = None
                    Input =
                      Some
                          { Description = None
                            Encoding = "application/json"
                            Schema =
                              Some
                                  (LexType.Object
                                      { Description = None
                                        Properties = Map.ofList [ "repo", LexType.String emptyStringConstraints ]
                                        Required = [ "repo" ]
                                        Nullable = [] }) }
                    Output =
                      Some
                          { Description = None
                            Encoding = "application/json"
                            Schema =
                              Some
                                  (LexType.Object
                                      { Description = None
                                        Properties = Map.ofList [ "uri", LexType.String emptyStringConstraints ]
                                        Required = [ "uri" ]
                                        Nullable = [] }) }
                    Errors = [] }
          ] }

/// A procedure with Input but no Output.
let private procedureInputOnlyDoc =
    { Lexicon = 1
      Id = mkNsid "com.atproto.repo.deleteRecord"
      Revision = None
      Description = None
      Defs =
          Map.ofList [
              "main",
              LexDef.Procedure
                  { Description = None
                    Parameters = None
                    Input =
                      Some
                          { Description = None
                            Encoding = "application/json"
                            Schema =
                              Some
                                  (LexType.Object
                                      { Description = None
                                        Properties = Map.ofList [ "repo", LexType.String emptyStringConstraints ]
                                        Required = [ "repo" ]
                                        Nullable = [] }) }
                    Output = None
                    Errors = [] }
          ] }

/// A query with ref output (e.g. getProfile -> defs#profileViewDetailed).
let private queryWithRefOutputDoc =
    { Lexicon = 1
      Id = mkNsid "app.bsky.actor.getProfile"
      Revision = None
      Description = None
      Defs =
          Map.ofList [
              "main",
              LexDef.Query
                  { Description = None
                    Parameters =
                      Some
                          { Description = None
                            Properties = Map.ofList [ "actor", LexType.String emptyStringConstraints ]
                            Required = [ "actor" ] }
                    Output =
                      Some
                          { Description = None
                            Encoding = "application/json"
                            Schema =
                              Some (LexType.Ref { Description = None; Ref = "app.bsky.actor.defs#profileViewDetailed" }) }
                    Errors = [] }
          ] }

/// A procedure with inline input and ref output (e.g. sendMessage -> defs#messageView).
let private procedureWithRefOutputDoc =
    { Lexicon = 1
      Id = mkNsid "chat.bsky.convo.sendMessage"
      Revision = None
      Description = None
      Defs =
          Map.ofList [
              "main",
              LexDef.Procedure
                  { Description = None
                    Parameters = None
                    Input =
                      Some
                          { Description = None
                            Encoding = "application/json"
                            Schema =
                              Some
                                  (LexType.Object
                                      { Description = None
                                        Properties = Map.ofList [ "convoId", LexType.String emptyStringConstraints ]
                                        Required = [ "convoId" ]
                                        Nullable = [] }) }
                    Output =
                      Some
                          { Description = None
                            Encoding = "application/json"
                            Schema =
                              Some (LexType.Ref { Description = None; Ref = "chat.bsky.convo.defs#messageView" }) }
                    Errors = [] }
          ] }

/// A record doc (should NOT get a wrapper).
let private recordDoc =
    { Lexicon = 1
      Id = mkNsid "app.bsky.feed.post"
      Revision = None
      Description = None
      Defs =
          Map.ofList [
              "main",
              LexDef.Record
                  { Key = "tid"
                    Description = None
                    Record =
                      { Description = None
                        Properties = Map.ofList [ "text", LexType.String emptyStringConstraints ]
                        Required = [ "text" ]
                        Nullable = [] } }
          ] }

/// A record doc with typed string format fields and CidLink.
let private typedStringFormatsDoc =
    let didString = { emptyStringConstraints with Format = Some LexStringFormat.Did }
    let handleString = { emptyStringConstraints with Format = Some LexStringFormat.Handle }
    let atUriString = { emptyStringConstraints with Format = Some LexStringFormat.AtUri }
    let atIdentifierString = { emptyStringConstraints with Format = Some LexStringFormat.AtIdentifier }

    { Lexicon = 1
      Id = mkNsid "app.bsky.test.typedFormats"
      Revision = None
      Description = None
      Defs =
          Map.ofList [
              "main",
              LexDef.Record
                  { Key = "tid"
                    Description = None
                    Record =
                      { Description = None
                        Properties =
                            Map.ofList [
                                "did", LexType.String didString
                                "handle", LexType.String handleString
                                "uri", LexType.String atUriString
                                "cid", LexType.CidLink
                                "name", LexType.String emptyStringConstraints
                                "actor", LexType.String atIdentifierString
                            ]
                        Required = [ "did"; "handle"; "uri"; "cid"; "name"; "actor" ]
                        Nullable = [] } }
          ] }

// ---------------------------------------------------------------------------
// Inline union test fixtures
// ---------------------------------------------------------------------------

/// A record with an open inline union field.
let private recordWithInlineUnionDoc =
    { Lexicon = 1
      Id = mkNsid "com.example.inlineUnion"
      Revision = None
      Description = None
      Defs =
        Map.ofList [
            "main", LexDef.Record
                { Description = None
                  Key = "tid"
                  Record =
                    { Description = None
                      Properties = Map.ofList [
                          "text", LexType.String emptyStringConstraints
                          "embed", LexType.Union
                              { Description = None
                                Refs = [ "com.example.embedA#main"; "com.example.embedB#main" ]
                                Closed = false }
                      ]
                      Required = [ "text" ]
                      Nullable = [] } }
        ] }

/// A record with a closed inline union field.
let private recordWithClosedUnionDoc =
    { Lexicon = 1
      Id = mkNsid "com.example.closedUnion"
      Revision = None
      Description = None
      Defs =
        Map.ofList [
            "main", LexDef.Record
                { Description = None
                  Key = "tid"
                  Record =
                    { Description = None
                      Properties = Map.ofList [
                          "action", LexType.Union
                              { Description = None
                                Refs = [ "com.example.create#main"; "com.example.delete#main" ]
                                Closed = true }
                      ]
                      Required = [ "action" ]
                      Nullable = [] } }
        ] }

/// A record with an array of union items.
let private recordWithArrayUnionDoc =
    { Lexicon = 1
      Id = mkNsid "com.example.arrayUnion"
      Revision = None
      Description = None
      Defs =
        Map.ofList [
            "main", LexDef.Record
                { Description = None
                  Key = "tid"
                  Record =
                    { Description = None
                      Properties = Map.ofList [
                          "items", LexType.Array
                              { Description = None
                                Items = LexType.Union
                                    { Description = None
                                      Refs = [ "com.example.itemA#main"; "com.example.itemB#main" ]
                                      Closed = false }
                                MinLength = None
                                MaxLength = None }
                      ]
                      Required = [ "items" ]
                      Nullable = [] } }
        ] }

/// A query with an inline union in the output object.
let private queryWithInlineUnionOutputDoc =
    { Lexicon = 1
      Id = mkNsid "com.example.queryUnion"
      Revision = None
      Description = None
      Defs =
        Map.ofList [
            "main", LexDef.Query
                { Description = None
                  Parameters = None
                  Output =
                    Some
                        { Description = None
                          Encoding = "application/json"
                          Schema =
                            Some
                                (LexType.Object
                                    { Description = None
                                      Properties = Map.ofList [
                                          "result", LexType.Union
                                              { Description = None
                                                Refs = [ "com.example.success#main"; "com.example.failure#main" ]
                                                Closed = false }
                                      ]
                                      Required = [ "result" ]
                                      Nullable = [] }) }
                  Errors = [] }
        ] }

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[<Tests>]
let wrapperTests =
    testList "wrapper generation" [
        testCase "query with params generates query function" <| fun () ->
            let result = FSharp.ATProto.CodeGen.NamespaceGen.generateAll [ queryWithParamsDoc ]
            let (_, content) = result.[0]
            Expect.stringContains content "let query" "has query function"
            Expect.stringContains content "Xrpc.query<Params, Output>" "calls Xrpc.query with type params"
            Expect.stringContains content "AtpAgent" "references AtpAgent"
            Expect.stringContains content "XrpcError" "references XrpcError"
            Expect.stringContains content "parameters: Params" "takes Params parameter"
            Expect.stringContains content "TypeId" "uses TypeId constant"

        testCase "query without params generates query function with just agent" <| fun () ->
            let result = FSharp.ATProto.CodeGen.NamespaceGen.generateAll [ queryNoParamsDoc ]
            let (_, content) = result.[0]
            Expect.stringContains content "let query" "has query function"
            Expect.stringContains content "Xrpc.query" "calls Xrpc.query"
            Expect.stringContains content "AtpAgent" "references AtpAgent"
            // Should NOT have a parameters param
            Expect.isFalse (content.Contains "parameters: Params") "no Params parameter"

        testCase "procedure with input+output generates call function" <| fun () ->
            let result = FSharp.ATProto.CodeGen.NamespaceGen.generateAll [ procedureWithIODoc ]
            let (_, content) = result.[0]
            Expect.stringContains content "let call" "has call function"
            Expect.stringContains content "Xrpc.procedure<Input, Output>" "calls Xrpc.procedure"
            Expect.stringContains content "input: Input" "takes Input parameter"
            Expect.stringContains content "AtpAgent" "references AtpAgent"

        testCase "procedure with input only generates call function with unit result" <| fun () ->
            let result = FSharp.ATProto.CodeGen.NamespaceGen.generateAll [ procedureInputOnlyDoc ]
            let (_, content) = result.[0]
            Expect.stringContains content "let call" "has call function"
            Expect.stringContains content "Xrpc.procedureVoid<Input>" "calls Xrpc.procedureVoid"
            Expect.stringContains content "Result<unit" "returns Result<unit, ...>"

        testCase "query with ref output generates query function with Output type alias" <| fun () ->
            let result = FSharp.ATProto.CodeGen.NamespaceGen.generateAll [ queryWithRefOutputDoc ]
            let (_, content) = result.[0]
            Expect.stringContains content "let query" "has query function"
            Expect.stringContains content "Xrpc.query<Params, Output>" "calls Xrpc.query with type params"
            Expect.stringContains content "type Output = " "has Output type alias"
            Expect.stringContains content "parameters: Params" "takes Params parameter"

        testCase "procedure with ref output generates call function with Output type alias" <| fun () ->
            let result = FSharp.ATProto.CodeGen.NamespaceGen.generateAll [ procedureWithRefOutputDoc ]
            let (_, content) = result.[0]
            Expect.stringContains content "let call" "has call function"
            Expect.stringContains content "Xrpc.procedure<Input, Output>" "calls Xrpc.procedure (not procedureVoid)"
            Expect.stringContains content "type Output = " "has Output type alias"
            Expect.stringContains content "input: Input" "takes Input parameter"

        testCase "record doc does not get a wrapper function" <| fun () ->
            let result = FSharp.ATProto.CodeGen.NamespaceGen.generateAll [ recordDoc ]
            let (_, content) = result.[0]
            Expect.isFalse (content.Contains "let query") "no query function"
            Expect.isFalse (content.Contains "let call") "no call function"
            Expect.isFalse (content.Contains "Xrpc.") "no Xrpc references"

        testCase "generated code includes open statements" <| fun () ->
            let result = FSharp.ATProto.CodeGen.NamespaceGen.generateAll [ queryWithParamsDoc ]
            let (_, content) = result.[0]
            Expect.stringContains content "open System.Threading.Tasks" "has Tasks open"
            Expect.stringContains content "open FSharp.ATProto.Core" "has Core open"

        testCase "wrapper is placed after TypeId line" <| fun () ->
            let result = FSharp.ATProto.CodeGen.NamespaceGen.generateAll [ queryWithParamsDoc ]
            let (_, content) = result.[0]
            let typeIdIdx = content.IndexOf("let TypeId = \"app.bsky.feed.getTimeline\"")
            let queryIdx = content.IndexOf("let query")
            Expect.isTrue (typeIdIdx >= 0) "TypeId present"
            Expect.isTrue (queryIdx >= 0) "query function present"
            Expect.isTrue (queryIdx > typeIdIdx) "query appears after TypeId"

        testCase "multiple docs generate wrappers for queries and procedures" <| fun () ->
            let result = FSharp.ATProto.CodeGen.NamespaceGen.generateAll [ queryWithParamsDoc; procedureWithIODoc ]
            let (_, content) = result.[0]
            Expect.stringContains content "let query" "has query function"
            Expect.stringContains content "let call" "has call function"

        testCase "typed string formats emit Syntax types instead of string" <| fun () ->
            let result = FSharp.ATProto.CodeGen.NamespaceGen.generateAll [ typedStringFormatsDoc ]
            let (_, content) = result.[0]
            // DID format -> Did type
            Expect.stringContains content "Did: Did" "did field is typed Did"
            // Handle format -> Handle type
            Expect.stringContains content "Handle: Handle" "handle field is typed Handle"
            // AT-URI format -> AtUri type
            Expect.stringContains content "Uri: AtUri" "uri field is typed AtUri"
            // CidLink -> Cid type
            Expect.stringContains content "Cid: Cid" "cid field is typed Cid"
            // Unformatted string stays as string
            Expect.stringContains content "Name: string" "unformatted string stays string"
            // at-identifier stays as string (ambiguous DID/Handle)
            Expect.stringContains content "Actor: string" "at-identifier stays string"
            // Open statement for Syntax types
            Expect.stringContains content "open FSharp.ATProto.Syntax" "has Syntax open"

        testCase "inline union generates DU type" <| fun () ->
            let result = FSharp.ATProto.CodeGen.NamespaceGen.generateAll [ recordWithInlineUnionDoc ]
            let (_, content) = result.[0]
            // Should have JsonFSharpConverter attribute for the union
            Expect.stringContains content "JsonFSharpConverter" "has union converter attribute"
            // Should have union cases
            Expect.stringContains content "EmbedA" "has first union case"
            Expect.stringContains content "EmbedB" "has second union case"
            // Open union should have Unknown fallback
            Expect.stringContains content "Unknown" "has Unknown fallback"
            // The embed field should NOT be JsonElement
            Expect.isFalse (content.Contains "Embed: JsonElement") "embed is not JsonElement"
            Expect.isFalse (content.Contains "Embed: System.Text.Json.JsonElement") "embed is not System.Text.Json.JsonElement"

        testCase "closed inline union has no Unknown case" <| fun () ->
            let result = FSharp.ATProto.CodeGen.NamespaceGen.generateAll [ recordWithClosedUnionDoc ]
            let (_, content) = result.[0]
            Expect.stringContains content "Create" "has first case"
            Expect.stringContains content "Delete" "has second case"
            Expect.isFalse (content.Contains "Unknown") "no Unknown case for closed union"

        testCase "array of union generates DU with list type" <| fun () ->
            let result = FSharp.ATProto.CodeGen.NamespaceGen.generateAll [ recordWithArrayUnionDoc ]
            let (_, content) = result.[0]
            // Should have DU cases
            Expect.stringContains content "ItemA" "has first union case"
            Expect.stringContains content "ItemB" "has second union case"
            // The field should be a list of the DU type, not JsonElement list
            Expect.isFalse (content.Contains "JsonElement list") "items is not JsonElement list"
            Expect.stringContains content "list" "has list in the field type"

        testCase "query output with inline union generates DU type" <| fun () ->
            let result = FSharp.ATProto.CodeGen.NamespaceGen.generateAll [ queryWithInlineUnionOutputDoc ]
            let (_, content) = result.[0]
            Expect.stringContains content "Success" "has first union case"
            Expect.stringContains content "Failure" "has second union case"
            Expect.stringContains content "JsonFSharpConverter" "has union converter attribute"
            Expect.isFalse (content.Contains "Result: JsonElement") "result is not JsonElement"
    ]
