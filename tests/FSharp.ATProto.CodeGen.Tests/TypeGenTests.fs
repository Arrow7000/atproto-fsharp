module TypeGenTests

open Expecto
open FSharp.ATProto.Lexicon
open FSharp.ATProto.CodeGen.TypeGen

// Helpers for creating empty constraint records
let emptyStringConstraints: LexString =
    { Description = None; Default = None; Const = None; Enum = None
      KnownValues = None; Format = None; MinLength = None; MaxLength = None
      MinGraphemes = None; MaxGraphemes = None }

let emptyBooleanConstraints: LexBoolean =
    { Description = None; Default = None; Const = None }

let emptyIntegerConstraints: LexInteger =
    { Description = None; Default = None; Const = None; Enum = None; Minimum = None; Maximum = None }

let emptyBytesConstraints: LexBytes =
    { Description = None; MinLength = None; MaxLength = None }

let emptyBlobConstraints: LexBlob =
    { Description = None; Accept = None; MaxSize = None }

[<Tests>]
let generateRecordTests =
    testList "generateRecord" [
        testCase "simple record with required string fields" <| fun () ->
            let obj: LexObject =
                { Description = None
                  Properties = Map.ofList [
                      "text", LexType.String { emptyStringConstraints with Format = None }
                      "createdAt", LexType.String { emptyStringConstraints with Format = Some LexStringFormat.Datetime }
                  ]
                  Required = [ "text"; "createdAt" ]
                  Nullable = [] }
            let source = generateRecord "AppBskyFeed" "Post" None obj
            Expect.stringContains source "type Post =" "should have type name"
            Expect.stringContains source "Text: string" "should have Text field"
            Expect.stringContains source "CreatedAt: string" "should have CreatedAt field"
            // Both fields are required, neither should be option
            Expect.isFalse (source.Contains("option")) "required fields should not be option"

        testCase "record with optional field" <| fun () ->
            let obj: LexObject =
                { Description = None
                  Properties = Map.ofList [
                      "name", LexType.String emptyStringConstraints
                      "bio", LexType.String emptyStringConstraints
                  ]
                  Required = [ "name" ]
                  Nullable = [] }
            let source = generateRecord "AppBskyActor" "Profile" None obj
            Expect.stringContains source "Name: string" "required field not option"
            Expect.stringContains source "string option" "optional field should be option"

        testCase "record with description generates XML doc" <| fun () ->
            let obj: LexObject =
                { Description = None
                  Properties = Map.ofList [
                      "name", LexType.String emptyStringConstraints
                  ]
                  Required = [ "name" ]
                  Nullable = [] }
            let source = generateRecord "AppBskyActor" "Profile" (Some "A user profile") obj
            Expect.stringContains source "A user profile" "should have description"

        testCase "record with nullable field" <| fun () ->
            let obj: LexObject =
                { Description = None
                  Properties = Map.ofList [
                      "value", LexType.String emptyStringConstraints
                  ]
                  Required = [ "value" ]
                  Nullable = [ "value" ] }
            let source = generateRecord "AppBskyFeed" "Test" None obj
            Expect.stringContains source "option" "nullable field should be option even if required"

        testCase "record field has JsonPropertyName attribute with original name" <| fun () ->
            let obj: LexObject =
                { Description = None
                  Properties = Map.ofList [
                      "createdAt", LexType.String emptyStringConstraints
                  ]
                  Required = [ "createdAt" ]
                  Nullable = [] }
            let source = generateRecord "AppBskyFeed" "Post" None obj
            Expect.stringContains source "JsonPropertyName" "should have JsonPropertyName attribute"
            Expect.stringContains source "\"createdAt\"" "should use original camelCase name"
            Expect.stringContains source "CreatedAt" "field should be PascalCase"

        testCase "record with boolean field" <| fun () ->
            let obj: LexObject =
                { Description = None
                  Properties = Map.ofList [
                      "active", LexType.Boolean emptyBooleanConstraints
                  ]
                  Required = [ "active" ]
                  Nullable = [] }
            let source = generateRecord "AppBskyActor" "Status" None obj
            Expect.stringContains source "Active: bool" "should have bool field"

        testCase "record with integer field" <| fun () ->
            let obj: LexObject =
                { Description = None
                  Properties = Map.ofList [
                      "count", LexType.Integer emptyIntegerConstraints
                  ]
                  Required = [ "count" ]
                  Nullable = [] }
            let source = generateRecord "AppBskyFeed" "Stats" None obj
            Expect.stringContains source "Count: int64" "should have int64 field"

        testCase "record with bytes field" <| fun () ->
            let obj: LexObject =
                { Description = None
                  Properties = Map.ofList [
                      "data", LexType.Bytes emptyBytesConstraints
                  ]
                  Required = [ "data" ]
                  Nullable = [] }
            let source = generateRecord "ComAtprotoRepo" "Upload" None obj
            Expect.stringContains source "byte[]" "should have byte[] field"

        testCase "record with array of strings field" <| fun () ->
            let obj: LexObject =
                { Description = None
                  Properties = Map.ofList [
                      "tags", LexType.Array { Description = None; Items = LexType.String emptyStringConstraints; MinLength = None; MaxLength = None }
                  ]
                  Required = [ "tags" ]
                  Nullable = [] }
            let source = generateRecord "AppBskyFeed" "Post" None obj
            Expect.stringContains source "string list" "should have string list field"

        testCase "record with ref field" <| fun () ->
            let obj: LexObject =
                { Description = None
                  Properties = Map.ofList [
                      "subject", LexType.Ref { Description = None; Ref = "com.atproto.repo.strongRef" }
                  ]
                  Required = [ "subject" ]
                  Nullable = [] }
            let source = generateRecord "AppBskyFeed" "Like" None obj
            Expect.stringContains source "StrongRef.StrongRef" "should have qualified ref type"

        testCase "record with optional ref field is option" <| fun () ->
            let obj: LexObject =
                { Description = None
                  Properties = Map.ofList [
                      "reply", LexType.Ref { Description = None; Ref = "app.bsky.feed.defs#feedViewPost" }
                  ]
                  Required = []
                  Nullable = [] }
            let source = generateRecord "AppBskyFeed" "Post" None obj
            Expect.stringContains source "Defs.FeedViewPost option" "optional ref should be option"

        testCase "record fields are in deterministic order" <| fun () ->
            let obj: LexObject =
                { Description = None
                  Properties = Map.ofList [
                      "zField", LexType.String emptyStringConstraints
                      "aField", LexType.String emptyStringConstraints
                      "mField", LexType.String emptyStringConstraints
                  ]
                  Required = [ "zField"; "aField"; "mField" ]
                  Nullable = [] }
            let source = generateRecord "AppBskyFeed" "Test" None obj
            let aPos = source.IndexOf("AField")
            let mPos = source.IndexOf("MField")
            let zPos = source.IndexOf("ZField")
            Expect.isTrue (aPos < mPos && mPos < zPos) "fields should be in alphabetical order"

        testCase "record with mixed required and optional fields" <| fun () ->
            let obj: LexObject =
                { Description = None
                  Properties = Map.ofList [
                      "uri", LexType.String emptyStringConstraints
                      "cid", LexType.String emptyStringConstraints
                      "displayName", LexType.String emptyStringConstraints
                      "description", LexType.String emptyStringConstraints
                  ]
                  Required = [ "uri"; "cid" ]
                  Nullable = [] }
            let source = generateRecord "AppBskyFeed" "GeneratorView" None obj
            // uri and cid are required -> no option
            Expect.stringContains source "Uri: string" "required uri field"
            Expect.stringContains source "Cid: string" "required cid field"
            // displayName and description are optional -> option
            Expect.stringContains source "DisplayName: string option" "optional displayName field"
            Expect.stringContains source "Description: string option" "optional description field"

        testCase "record with no description does not have XML doc" <| fun () ->
            let obj: LexObject =
                { Description = None
                  Properties = Map.ofList [
                      "text", LexType.String emptyStringConstraints
                  ]
                  Required = [ "text" ]
                  Nullable = [] }
            let source = generateRecord "AppBskyFeed" "Post" None obj
            Expect.isFalse (source.Contains("///")) "should not have XML doc comment"
    ]

[<Tests>]
let generateTokenTests =
    testList "generateToken" [
        testCase "token generates literal constant" <| fun () ->
            let token: LexToken = { Description = Some "A feed view post" }
            let source = generateToken "app.bsky.feed.defs" "feedViewPost" token
            Expect.stringContains source "[<Literal>]" "should have Literal attribute"
            Expect.stringContains source "FeedViewPost" "should have PascalCase name"
            Expect.stringContains source "\"app.bsky.feed.defs#feedViewPost\"" "should have NSID#def value"

        testCase "token with description generates XML doc" <| fun () ->
            let token: LexToken = { Description = Some "A feed view post" }
            let source = generateToken "app.bsky.feed.defs" "feedViewPost" token
            Expect.stringContains source "A feed view post" "should have description"

        testCase "token without description has no XML doc" <| fun () ->
            let token: LexToken = { Description = None }
            let source = generateToken "app.bsky.feed.defs" "feedViewPost" token
            Expect.isFalse (source.Contains("///")) "should not have XML doc"

        testCase "token name is PascalCased" <| fun () ->
            let token: LexToken = { Description = None }
            let source = generateToken "com.atproto.label.defs" "selfLabel" token
            Expect.stringContains source "SelfLabel" "should PascalCase the defName"
            Expect.stringContains source "\"com.atproto.label.defs#selfLabel\"" "value should use original names"

        testCase "token generates let binding" <| fun () ->
            let token: LexToken = { Description = None }
            let source = generateToken "app.bsky.feed.defs" "feedViewPost" token
            Expect.stringContains source "let FeedViewPost" "should have let binding"
    ]

[<Tests>]
let generateKnownValuesTests =
    testList "generateKnownValues" [
        testCase "knownValues generates module with constants" <| fun () ->
            let source = generateKnownValues "sort" ["app.bsky.feed.defs#sortHot"; "app.bsky.feed.defs#sortNew"]
            Expect.stringContains source "module Sort" "should have module name"
            Expect.stringContains source "SortHot" "should have PascalCase constant for sortHot"
            Expect.stringContains source "SortNew" "should have PascalCase constant for sortNew"

        testCase "knownValues constants have Literal attribute" <| fun () ->
            let source = generateKnownValues "sort" ["app.bsky.feed.defs#sortHot"]
            Expect.stringContains source "[<Literal>]" "should have Literal attribute"

        testCase "knownValues constants have full value string" <| fun () ->
            let source = generateKnownValues "sort" ["app.bsky.feed.defs#sortHot"]
            Expect.stringContains source "\"app.bsky.feed.defs#sortHot\"" "should have full value string"

        testCase "knownValues with dot-only values (no fragment)" <| fun () ->
            let source = generateKnownValues "format" ["image.png"; "image.jpeg"]
            Expect.stringContains source "module Format" "should have module name"
            Expect.stringContains source "ImagePng" "should clean up dots and PascalCase"
            Expect.stringContains source "ImageJpeg" "should clean up dots and PascalCase"

        testCase "knownValues extracts name after hash" <| fun () ->
            let source = generateKnownValues "type" ["app.bsky.actor.defs#contentLabelPref"]
            Expect.stringContains source "ContentLabelPref" "should PascalCase part after #"

        testCase "knownValues module name is PascalCased" <| fun () ->
            let source = generateKnownValues "viewerState" ["app.bsky.feed.defs#liked"]
            Expect.stringContains source "module ViewerState" "should PascalCase module name"

        testCase "knownValues with multiple values generates multiple lets" <| fun () ->
            let source = generateKnownValues "sort" [
                "app.bsky.feed.defs#sortHot"
                "app.bsky.feed.defs#sortNew"
                "app.bsky.feed.defs#sortOld"
            ]
            Expect.stringContains source "SortHot" "should have first constant"
            Expect.stringContains source "SortNew" "should have second constant"
            Expect.stringContains source "SortOld" "should have third constant"
    ]
