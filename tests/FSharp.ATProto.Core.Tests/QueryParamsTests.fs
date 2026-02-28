module QueryParamsTests

open Expecto
open FSharp.ATProto.Core
open FSharp.ATProto.Syntax

type SimpleParams = { Actor: string }
type OptionalParams = { Limit: int64 option; Cursor: string option }
type ListParams = { Tag: string list }
type BoolParams = { IncludeReplies: bool }
type AllOptionalNone = { A: string option; B: int64 option }

// Record types with typed Syntax identifiers
type DidParams = { Actor: Did }
type HandleParams = { Actor: Handle }
type AtUriParams = { Uri: AtUri }
type CidParams = { Cid: Cid }
type NsidParams = { Collection: Nsid }
type OptionalDidParams = { Actor: Did option }
type DidListParams = { Actors: Did list }

open System.Text.Json.Serialization

type TestReason =
    | [<JsonName("like")>] Like
    | [<JsonName("repost")>] Repost
    | Unknown of string

type KnownValueParams = { Reason: TestReason }
type OptionalKnownValueParams = { Reason: TestReason option }
type KnownValueListParams = { Reasons: TestReason list }

[<Tests>]
let tests =
    testList "QueryParams" [
        testCase "serializes string field" <| fun () ->
            let result = QueryParams.toQueryString { SimpleParams.Actor = "did:plc:abc" }
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

        testCase "serializes Did field via ToString" <| fun () ->
            let did = Did.parse "did:plc:z72i7hdynmk6r22z27h6tvur" |> Result.defaultWith failwith
            let result = QueryParams.toQueryString { DidParams.Actor = did }
            Expect.equal result "?actor=did%3Aplc%3Az72i7hdynmk6r22z27h6tvur" "Did field"

        testCase "serializes Handle field via ToString" <| fun () ->
            let handle = Handle.parse "my-handle.bsky.social" |> Result.defaultWith failwith
            let result = QueryParams.toQueryString { HandleParams.Actor = handle }
            Expect.equal result "?actor=my-handle.bsky.social" "Handle field"

        testCase "serializes AtUri field via ToString" <| fun () ->
            let uri = AtUri.parse "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/3k2la3b" |> Result.defaultWith failwith
            let result = QueryParams.toQueryString { AtUriParams.Uri = uri }
            Expect.equal result "?uri=at%3A%2F%2Fdid%3Aplc%3Az72i7hdynmk6r22z27h6tvur%2Fapp.bsky.feed.post%2F3k2la3b" "AtUri field"

        testCase "serializes Cid field via ToString" <| fun () ->
            let cid = Cid.parse "bafyreib2rxk3rybpej3j" |> Result.defaultWith failwith
            let result = QueryParams.toQueryString { CidParams.Cid = cid }
            Expect.equal result "?cid=bafyreib2rxk3rybpej3j" "Cid field"

        testCase "serializes Nsid field via ToString" <| fun () ->
            let nsid = Nsid.parse "app.bsky.feed.post" |> Result.defaultWith failwith
            let result = QueryParams.toQueryString { NsidParams.Collection = nsid }
            Expect.equal result "?collection=app.bsky.feed.post" "Nsid field"

        testCase "serializes optional Did field" <| fun () ->
            let did = Did.parse "did:plc:z72i7hdynmk6r22z27h6tvur" |> Result.defaultWith failwith
            let result = QueryParams.toQueryString { OptionalDidParams.Actor = Some did }
            Expect.equal result "?actor=did%3Aplc%3Az72i7hdynmk6r22z27h6tvur" "optional Did Some"

        testCase "omits None Did field" <| fun () ->
            let result = QueryParams.toQueryString { OptionalDidParams.Actor = None }
            Expect.equal result "" "optional Did None"

        testCase "serializes Did list as repeated params" <| fun () ->
            let did1 = Did.parse "did:plc:abc123def456ghi" |> Result.defaultWith failwith
            let did2 = Did.parse "did:web:example.com" |> Result.defaultWith failwith
            let result = QueryParams.toQueryString { DidListParams.Actors = [ did1; did2 ] }
            Expect.equal result "?actors=did%3Aplc%3Aabc123def456ghi&actors=did%3Aweb%3Aexample.com" "Did list"

        testCase "serializes known-value DU fieldless case" <| fun () ->
            let result = QueryParams.toQueryString { KnownValueParams.Reason = TestReason.Like }
            Expect.equal result "?reason=like" "fieldless case uses JsonName value"

        testCase "serializes known-value DU Unknown case" <| fun () ->
            let result = QueryParams.toQueryString { KnownValueParams.Reason = TestReason.Unknown "custom-reason" }
            Expect.equal result "?reason=custom-reason" "Unknown case extracts string"

        testCase "serializes optional known-value DU" <| fun () ->
            let result = QueryParams.toQueryString { OptionalKnownValueParams.Reason = Some TestReason.Repost }
            Expect.equal result "?reason=repost" "optional known value Some"

        testCase "serializes known-value DU list" <| fun () ->
            let result = QueryParams.toQueryString { KnownValueListParams.Reasons = [TestReason.Like; TestReason.Repost] }
            Expect.equal result "?reasons=like&reasons=repost" "DU list as repeated params"
    ]
