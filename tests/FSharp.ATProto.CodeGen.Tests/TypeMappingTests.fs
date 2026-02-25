module TypeMappingTests

open Expecto
open FSharp.ATProto.Lexicon
open FSharp.ATProto.CodeGen.TypeMapping

// Helpers for creating empty constraint records
let emptyBooleanConstraints: LexBoolean =
    { Description = None; Default = None; Const = None }

let emptyIntegerConstraints: LexInteger =
    { Description = None; Default = None; Const = None; Enum = None; Minimum = None; Maximum = None }

let emptyStringConstraints: LexString =
    { Description = None; Default = None; Const = None; Enum = None
      KnownValues = None; Format = None; MinLength = None; MaxLength = None
      MinGraphemes = None; MaxGraphemes = None }

let emptyBytesConstraints: LexBytes =
    { Description = None; MinLength = None; MaxLength = None }

let emptyBlobConstraints: LexBlob =
    { Description = None; Accept = None; MaxSize = None }

let emptyArrayOf items: LexArray =
    { Description = None; Items = items; MinLength = None; MaxLength = None }

[<Tests>]
let lexTypeToFSharpTypeTests =
    testList "lexTypeToFSharpType" [
        testCase "Boolean maps to bool" <| fun () ->
            let result = lexTypeToFSharpType "AppBskyFeed" (Boolean emptyBooleanConstraints)
            Expect.equal result "bool" "Boolean should map to bool"

        testCase "Integer maps to int64" <| fun () ->
            let result = lexTypeToFSharpType "AppBskyFeed" (Integer emptyIntegerConstraints)
            Expect.equal result "int64" "Integer should map to int64"

        testCase "String maps to string" <| fun () ->
            let result = lexTypeToFSharpType "AppBskyFeed" (String emptyStringConstraints)
            Expect.equal result "string" "String should map to string"

        testCase "String with DID format maps to string" <| fun () ->
            let result = lexTypeToFSharpType "AppBskyFeed" (String { emptyStringConstraints with Format = Some LexStringFormat.Did })
            Expect.equal result "string" "String with DID format should still map to string"

        testCase "String with datetime format maps to string" <| fun () ->
            let result = lexTypeToFSharpType "AppBskyFeed" (String { emptyStringConstraints with Format = Some LexStringFormat.Datetime })
            Expect.equal result "string" "String with datetime format should still map to string"

        testCase "Bytes maps to byte[]" <| fun () ->
            let result = lexTypeToFSharpType "AppBskyFeed" (Bytes emptyBytesConstraints)
            Expect.equal result "byte[]" "Bytes should map to byte[]"

        testCase "CidLink maps to string" <| fun () ->
            let result = lexTypeToFSharpType "AppBskyFeed" CidLink
            Expect.equal result "string" "CidLink should map to string"

        testCase "Unknown maps to JsonElement" <| fun () ->
            let result = lexTypeToFSharpType "AppBskyFeed" Unknown
            Expect.equal result "JsonElement" "Unknown should map to JsonElement"

        testCase "Blob maps to JsonElement" <| fun () ->
            let result = lexTypeToFSharpType "AppBskyFeed" (Blob emptyBlobConstraints)
            Expect.equal result "JsonElement" "Blob should map to JsonElement"

        testCase "Array of strings maps to string list" <| fun () ->
            let arr = emptyArrayOf (LexType.String emptyStringConstraints)
            let result = lexTypeToFSharpType "AppBskyFeed" (Array arr)
            Expect.equal result "string list" "Array of strings should map to string list"

        testCase "Array of integers maps to int64 list" <| fun () ->
            let arr = emptyArrayOf (LexType.Integer emptyIntegerConstraints)
            let result = lexTypeToFSharpType "AppBskyFeed" (Array arr)
            Expect.equal result "int64 list" "Array of integers should map to int64 list"

        testCase "Array of refs maps to qualified type list" <| fun () ->
            let arr = emptyArrayOf (Ref { Description = None; Ref = "app.bsky.feed.defs#feedViewPost" })
            let result = lexTypeToFSharpType "AppBskyFeed" (Array arr)
            Expect.equal result "Defs.FeedViewPost list" "Array of refs should map to qualified type list"

        testCase "Ref in same namespace uses Module.TypeName" <| fun () ->
            let result = lexTypeToFSharpType "AppBskyFeed" (Ref { Description = None; Ref = "app.bsky.feed.defs#feedViewPost" })
            Expect.equal result "Defs.FeedViewPost" "Ref in same namespace should use Module.TypeName"

        testCase "Ref to different namespace uses Namespace.Module.TypeName" <| fun () ->
            let result = lexTypeToFSharpType "AppBskyFeed" (Ref { Description = None; Ref = "com.atproto.repo.strongRef" })
            Expect.equal result "ComAtprotoRepo.StrongRef.StrongRef" "Ref to different namespace should use Namespace.Module.TypeName"

        testCase "Ref with main def uses module name as type name" <| fun () ->
            let result = lexTypeToFSharpType "AppBskyFeed" (Ref { Description = None; Ref = "app.bsky.feed.post" })
            Expect.equal result "Post.Post" "Ref to main def should use Module.Module"

        testCase "Union maps to JsonElement" <| fun () ->
            let result = lexTypeToFSharpType "AppBskyFeed" (Union { Description = None; Refs = ["app.bsky.feed.defs#feedViewPost"]; Closed = false })
            Expect.equal result "JsonElement" "Union should map to JsonElement"

        testCase "Object maps to JsonElement" <| fun () ->
            let obj = Object { Description = None; Properties = Map.empty; Required = []; Nullable = [] }
            let result = lexTypeToFSharpType "AppBskyFeed" obj
            Expect.equal result "JsonElement" "Object should map to JsonElement"

        testCase "Params maps to JsonElement" <| fun () ->
            let p = Params { Description = None; Properties = Map.empty; Required = [] }
            let result = lexTypeToFSharpType "AppBskyFeed" p
            Expect.equal result "JsonElement" "Params should map to JsonElement"
    ]

[<Tests>]
let collectNamespaceDepsTests =
    testList "collectNamespaceDeps" [
        testCase "Ref to same namespace returns empty set" <| fun () ->
            let result = collectNamespaceDeps "AppBskyFeed" (Ref { Description = None; Ref = "app.bsky.feed.defs#feedViewPost" })
            Expect.equal result Set.empty "Same-namespace ref should return empty set"

        testCase "Ref to different namespace returns that namespace" <| fun () ->
            let result = collectNamespaceDeps "AppBskyFeed" (Ref { Description = None; Ref = "com.atproto.repo.strongRef" })
            Expect.equal result (Set.singleton "ComAtprotoRepo") "Cross-namespace ref should return target namespace"

        testCase "Array of cross-namespace ref returns that namespace" <| fun () ->
            let arr = emptyArrayOf (Ref { Description = None; Ref = "com.atproto.repo.strongRef" })
            let result = collectNamespaceDeps "AppBskyFeed" (Array arr)
            Expect.equal result (Set.singleton "ComAtprotoRepo") "Array of cross-ns ref should return target namespace"

        testCase "Array of same-namespace ref returns empty set" <| fun () ->
            let arr = emptyArrayOf (Ref { Description = None; Ref = "app.bsky.feed.defs#feedViewPost" })
            let result = collectNamespaceDeps "AppBskyFeed" (Array arr)
            Expect.equal result Set.empty "Array of same-ns ref should return empty set"

        testCase "Union with mixed refs returns cross-namespace deps" <| fun () ->
            let u = Union {
                Description = None
                Refs = [
                    "app.bsky.feed.defs#feedViewPost"       // same namespace
                    "com.atproto.repo.strongRef"             // different namespace
                    "com.atproto.label.defs#label"           // different namespace
                ]
                Closed = false
            }
            let result = collectNamespaceDeps "AppBskyFeed" u
            let expected = set ["ComAtprotoRepo"; "ComAtprotoLabel"]
            Expect.equal result expected "Union should return all cross-namespace deps"

        testCase "Union with all same-namespace refs returns empty set" <| fun () ->
            let u = Union {
                Description = None
                Refs = [
                    "app.bsky.feed.defs#feedViewPost"
                    "app.bsky.feed.post"
                ]
                Closed = false
            }
            let result = collectNamespaceDeps "AppBskyFeed" u
            Expect.equal result Set.empty "Union with all same-ns refs should return empty set"

        testCase "Object with cross-namespace property returns that namespace" <| fun () ->
            let obj = Object {
                Description = None
                Properties = Map.ofList [
                    ("subject", Ref { Description = None; Ref = "com.atproto.repo.strongRef" })
                    ("text", LexType.String emptyStringConstraints)
                ]
                Required = ["subject"; "text"]
                Nullable = []
            }
            let result = collectNamespaceDeps "AppBskyFeed" obj
            Expect.equal result (Set.singleton "ComAtprotoRepo") "Object with cross-ns ref should return that namespace"

        testCase "Object with multiple cross-namespace properties returns all" <| fun () ->
            let obj = Object {
                Description = None
                Properties = Map.ofList [
                    ("subject", Ref { Description = None; Ref = "com.atproto.repo.strongRef" })
                    ("labels", Ref { Description = None; Ref = "com.atproto.label.defs#label" })
                ]
                Required = []
                Nullable = []
            }
            let result = collectNamespaceDeps "AppBskyFeed" obj
            let expected = set ["ComAtprotoRepo"; "ComAtprotoLabel"]
            Expect.equal result expected "Object with multiple cross-ns refs should return all"

        testCase "Primitive types return empty set" <| fun () ->
            let types = [
                Boolean emptyBooleanConstraints
                Integer emptyIntegerConstraints
                LexType.String emptyStringConstraints
                Bytes emptyBytesConstraints
                CidLink
                Blob emptyBlobConstraints
                Unknown
            ]
            for t in types do
                let result = collectNamespaceDeps "AppBskyFeed" t
                Expect.equal result Set.empty (sprintf "Primitive type %A should return empty set" t)

        testCase "Nested array of cross-namespace ref" <| fun () ->
            // Array of array items that are refs (via Array containing a Ref)
            let arr = emptyArrayOf (Ref { Description = None; Ref = "com.atproto.label.defs#label" })
            let result = collectNamespaceDeps "AppBskyFeed" (Array arr)
            Expect.equal result (Set.singleton "ComAtprotoLabel") "Nested cross-ns ref should be collected"
    ]
