module QueryParamsTests

open Expecto
open FSharp.ATProto.Core

type SimpleParams = { Actor: string }
type OptionalParams = { Limit: int64 option; Cursor: string option }
type ListParams = { Tag: string list }
type BoolParams = { IncludeReplies: bool }
type AllOptionalNone = { A: string option; B: int64 option }

[<Tests>]
let tests =
    testList "QueryParams" [
        testCase "serializes string field" <| fun () ->
            let result = QueryParams.toQueryString { Actor = "did:plc:abc" }
            Expect.equal result "?actor=did%3Aplc%3Aabc" "string field"

        testCase "serializes int64 field" <| fun () ->
            let result = QueryParams.toQueryString { Limit = Some 50L; Cursor = None }
            Expect.equal result "?limit=50" "int64 option Some"

        testCase "omits None option fields" <| fun () ->
            let result = QueryParams.toQueryString { A = None; B = None }
            Expect.equal result "" "all None = empty string"

        testCase "serializes bool field" <| fun () ->
            let result = QueryParams.toQueryString { IncludeReplies = true }
            Expect.equal result "?includeReplies=true" "bool true"

        testCase "serializes string list as repeated params" <| fun () ->
            let result = QueryParams.toQueryString { Tag = [ "cat"; "dog" ] }
            Expect.equal result "?tag=cat&tag=dog" "repeated params"

        testCase "serializes empty list as no params" <| fun () ->
            let result = QueryParams.toQueryString { Tag = [] }
            Expect.equal result "" "empty list = nothing"

        testCase "combines multiple fields" <| fun () ->
            let result = QueryParams.toQueryString { Limit = Some 25L; Cursor = Some "abc123" }
            Expect.equal result "?limit=25&cursor=abc123" "multiple fields"
    ]
