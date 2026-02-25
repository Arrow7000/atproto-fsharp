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
    ]
