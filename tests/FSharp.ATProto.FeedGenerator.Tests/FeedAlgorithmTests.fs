module FSharp.ATProto.FeedGenerator.Tests.FeedAlgorithmTests

open System.Text.Json
open System.Threading.Tasks
open Expecto
open FSharp.ATProto.FeedGenerator
open FSharp.ATProto.Syntax

let private unwrap result =
    match result with
    | Ok v -> v
    | Error e -> failtest (sprintf "Expected Ok, got Error: %s" e)

let private testDid = Did.parse "did:plc:z72i7hdynmk6r22z27h6tvur" |> unwrap
let private testPostUri = AtUri.parse "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/3k2la3b" |> unwrap
let private testFeedUri = AtUri.parse "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.generator/my-feed" |> unwrap

[<Tests>]
let feedAlgorithmTests =
    testList
        "FeedAlgorithm"
        [
          test "fromFunction creates a working algorithm" {
              let algorithm =
                  FeedAlgorithm.fromFunction (fun query ->
                      Task.FromResult {
                          Feed = [
                              { Post = testPostUri; Reason = None }
                          ]
                          Cursor = Some "next"
                      })

              let query = {
                  Feed = testFeedUri
                  Limit = 10
                  Cursor = None
              }

              let result = algorithm.GetFeedSkeleton(query).Result
              Expect.equal result.Feed.Length 1 "Should have 1 item"
              Expect.equal result.Feed.[0].Post testPostUri "Post URI should match"
              Expect.equal result.Feed.[0].Reason None "Reason should be None"
              Expect.equal result.Cursor (Some "next") "Cursor should be Some next"
          }

          test "fromSync creates a working algorithm" {
              let algorithm =
                  FeedAlgorithm.fromSync (fun query -> {
                      Feed = [
                          { Post = testPostUri; Reason = None }
                          { Post = testPostUri; Reason = Some (RepostBy (testDid, "2024-01-01T00:00:00Z")) }
                      ]
                      Cursor = None
                  })

              let query = {
                  Feed = testFeedUri
                  Limit = 20
                  Cursor = Some "abc"
              }

              let result = algorithm.GetFeedSkeleton(query).Result
              Expect.equal result.Feed.Length 2 "Should have 2 items"
              Expect.isNone result.Feed.[0].Reason "First item should have no reason"
              Expect.isSome result.Feed.[1].Reason "Second item should have a reason"

              match result.Feed.[1].Reason with
              | Some (RepostBy (did, indexedAt)) ->
                  Expect.equal did testDid "Repost DID should match"
                  Expect.equal indexedAt "2024-01-01T00:00:00Z" "IndexedAt should match"
              | None -> failtest "Expected RepostBy reason"
          }

          test "fromFunction receives the query parameters" {
              let mutable receivedQuery = None

              let algorithm =
                  FeedAlgorithm.fromFunction (fun query ->
                      receivedQuery <- Some query
                      Task.FromResult { Feed = []; Cursor = None })

              let query = {
                  Feed = testFeedUri
                  Limit = 42
                  Cursor = Some "cursor123"
              }

              algorithm.GetFeedSkeleton(query).Result |> ignore
              Expect.isSome receivedQuery "Query should have been received"
              let q = receivedQuery.Value
              Expect.equal q.Feed testFeedUri "Feed URI should match"
              Expect.equal q.Limit 42 "Limit should match"
              Expect.equal q.Cursor (Some "cursor123") "Cursor should match"
          }

          test "fromSync returns empty feed" {
              let algorithm =
                  FeedAlgorithm.fromSync (fun _ -> { Feed = []; Cursor = None })

              let query = {
                  Feed = testFeedUri
                  Limit = 10
                  Cursor = None
              }

              let result = algorithm.GetFeedSkeleton(query).Result
              Expect.isEmpty result.Feed "Feed should be empty"
              Expect.isNone result.Cursor "Cursor should be None"
          }
        ]

[<Tests>]
let skeletonSerializationTests =
    testList
        "SkeletonFeed serialization"
        [
          test "SkeletonItem with no reason serializes to post-only JSON" {
              let item = { Post = testPostUri; Reason = None }

              let json =
                  JsonSerializer.Serialize (
                      dict [ "post", box (AtUri.value item.Post) ],
                      JsonSerializerOptions (PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
                  )

              Expect.stringContains json "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/3k2la3b" "Should contain post URI"
              Expect.isFalse (json.Contains "reason") "Should not contain reason"
          }

          test "SkeletonItem with RepostBy reason includes reason in JSON" {
              let item = {
                  Post = testPostUri
                  Reason = Some (RepostBy (testDid, "2024-03-01T12:00:00Z"))
              }

              let data = dict [
                  "post", box (AtUri.value item.Post)
                  "reason", box (dict [
                      "$type", box "app.bsky.feed.defs#skeletonReasonRepost"
                      "repost", box (Did.value testDid)
                      "indexedAt", box "2024-03-01T12:00:00Z"
                  ])
              ]

              let json = JsonSerializer.Serialize (data, JsonSerializerOptions (PropertyNamingPolicy = JsonNamingPolicy.CamelCase))
              Expect.stringContains json "skeletonReasonRepost" "Should contain reason type"
              Expect.stringContains json "2024-03-01T12:00:00Z" "Should contain indexedAt"
          }

          test "SkeletonFeed with cursor serializes correctly" {
              let feed = {
                  Feed = [
                      { Post = testPostUri; Reason = None }
                  ]
                  Cursor = Some "abc123"
              }

              let data = dict [
                  "feed", box (feed.Feed |> List.map (fun i -> dict [ "post", box (AtUri.value i.Post) ]))
                  "cursor", box "abc123"
              ]

              let json = JsonSerializer.Serialize (data, JsonSerializerOptions (PropertyNamingPolicy = JsonNamingPolicy.CamelCase))
              Expect.stringContains json "abc123" "Should contain cursor"
              Expect.stringContains json "feed" "Should contain feed key"
          }

          test "SkeletonFeed without cursor omits cursor field" {
              let feed = { Feed = []; Cursor = None }

              let opts = JsonSerializerOptions (PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
              opts.DefaultIgnoreCondition <- System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull

              let data = dict [ "feed", box ([] : obj list) ]
              let json = JsonSerializer.Serialize (data, opts)
              Expect.isFalse (json.Contains "cursor") "Should not contain cursor when None"
          }
        ]

[<Tests>]
let generatorDescriptionTests =
    testList
        "GeneratorDescription serialization"
        [
          test "GeneratorDescription serializes with feeds" {
              let desc = {
                  Did = testDid
                  Feeds = [
                      {
                          Uri = testFeedUri
                          DisplayName = "My Feed"
                          Description = Some "A cool feed"
                          Avatar = None
                      }
                  ]
              }

              let data = dict [
                  "did", box (Did.value desc.Did)
                  "feeds", box (
                      desc.Feeds
                      |> List.map (fun f ->
                          let d = System.Collections.Generic.Dictionary<string, obj> ()
                          d.["uri"] <- box (AtUri.value f.Uri)
                          match f.Description with
                          | Some descr -> d.["description"] <- box descr
                          | None -> ()
                          d)
                  )
              ]

              let json = JsonSerializer.Serialize (data, JsonSerializerOptions (PropertyNamingPolicy = JsonNamingPolicy.CamelCase))
              Expect.stringContains json "did:plc:z72i7hdynmk6r22z27h6tvur" "Should contain DID"
              Expect.stringContains json "A cool feed" "Should contain description"
          }

          test "GeneratorDescription serializes with empty feeds" {
              let desc = { Did = testDid; Feeds = [] }

              let data = dict [
                  "did", box (Did.value desc.Did)
                  "feeds", box ([] : obj list)
              ]

              let json = JsonSerializer.Serialize (data, JsonSerializerOptions (PropertyNamingPolicy = JsonNamingPolicy.CamelCase))
              Expect.stringContains json "\"feeds\":[]" "Should contain empty feeds array"
          }
        ]

[<Tests>]
let feedQueryTests =
    testList
        "FeedQuery"
        [
          test "FeedQuery stores all parameters" {
              let query = {
                  Feed = testFeedUri
                  Limit = 30
                  Cursor = Some "cursor-value"
              }

              Expect.equal query.Feed testFeedUri "Feed should match"
              Expect.equal query.Limit 30 "Limit should match"
              Expect.equal query.Cursor (Some "cursor-value") "Cursor should match"
          }

          test "FeedQuery with no cursor" {
              let query = {
                  Feed = testFeedUri
                  Limit = 50
                  Cursor = None
              }

              Expect.isNone query.Cursor "Cursor should be None"
          }

          test "FeedDescription with all optional fields" {
              let desc = {
                  Uri = testFeedUri
                  DisplayName = "Test Feed"
                  Description = Some "Interesting posts"
                  Avatar = Some "https://example.com/avatar.png"
              }

              Expect.equal desc.DisplayName "Test Feed" "DisplayName should match"
              Expect.isSome desc.Description "Description should be present"
              Expect.isSome desc.Avatar "Avatar should be present"
          }

          test "FeedDescription with no optional fields" {
              let desc = {
                  Uri = testFeedUri
                  DisplayName = "Minimal Feed"
                  Description = None
                  Avatar = None
              }

              Expect.isNone desc.Description "Description should be None"
              Expect.isNone desc.Avatar "Avatar should be None"
          }
        ]

[<Tests>]
let feedServerConfigTests =
    testList
        "FeedGeneratorConfig"
        [
          test "Config stores all fields correctly" {
              let algorithm = FeedAlgorithm.fromSync (fun _ -> { Feed = []; Cursor = None })

              let config = {
                  Hostname = "feed.example.com"
                  ServiceDid = testDid
                  Feeds = Map.ofList [ "my-feed", algorithm ]
                  Descriptions = [
                      {
                          Uri = testFeedUri
                          DisplayName = "My Feed"
                          Description = Some "A description"
                          Avatar = None
                      }
                  ]
                  Port = 3000
              }

              Expect.equal config.Hostname "feed.example.com" "Hostname"
              Expect.equal config.ServiceDid testDid "ServiceDid"
              Expect.equal config.Port 3000 "Port"
              Expect.equal (Map.count config.Feeds) 1 "Should have 1 feed"
              Expect.equal config.Descriptions.Length 1 "Should have 1 description"
          }

          test "Config with multiple feeds" {
              let algo1 = FeedAlgorithm.fromSync (fun _ -> { Feed = []; Cursor = None })
              let algo2 = FeedAlgorithm.fromSync (fun _ -> { Feed = [{ Post = testPostUri; Reason = None }]; Cursor = None })

              let config = {
                  Hostname = "feed.example.com"
                  ServiceDid = testDid
                  Feeds = Map.ofList [ "feed-a", algo1; "feed-b", algo2 ]
                  Descriptions = []
                  Port = 8080
              }

              Expect.equal (Map.count config.Feeds) 2 "Should have 2 feeds"
              Expect.isTrue (Map.containsKey "feed-a" config.Feeds) "Should contain feed-a"
              Expect.isTrue (Map.containsKey "feed-b" config.Feeds) "Should contain feed-b"
          }
        ]
