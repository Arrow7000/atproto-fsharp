namespace FSharp.ATProto.FeedGenerator

open System
open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open FSharp.ATProto.Syntax

/// Configuration for the feed generator server.
type FeedGeneratorConfig = {
    Hostname: string
    ServiceDid: Did
    Feeds: Map<string, IFeedAlgorithm>
    Descriptions: FeedDescription list
    Port: int
}

module FeedServer =

    let private jsonOptions =
        let opts = JsonSerializerOptions (PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        opts.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
        opts

    let private serializeSkeletonItem (item : SkeletonItem) =
        let d = Dictionary<string, obj> ()
        d.["post"] <- box (AtUri.value item.Post)

        match item.Reason with
        | Some (RepostBy (did, indexedAt)) ->
            let reason = dict [
                "$type", box "app.bsky.feed.defs#skeletonReasonRepost"
                "repost", box (Did.value did)
                "indexedAt", box indexedAt
            ]

            d.["reason"] <- box reason
        | None -> ()

        d

    let private serializeSkeletonFeed (feed : SkeletonFeed) =
        let d = Dictionary<string, obj> ()
        d.["feed"] <- box (feed.Feed |> List.map serializeSkeletonItem)

        match feed.Cursor with
        | Some c -> d.["cursor"] <- box c
        | None -> ()

        d

    let private serializeFeedDescription (f : FeedDescription) =
        let d = Dictionary<string, obj> ()
        d.["uri"] <- box (AtUri.value f.Uri)

        match f.Description with
        | Some desc -> d.["description"] <- box desc
        | None -> ()

        match f.Avatar with
        | Some av -> d.["avatar"] <- box av
        | None -> ()

        d

    let private serializeGeneratorDescription (desc : GeneratorDescription) =
        dict [
            "did", box (Did.value desc.Did)
            "feeds", box (desc.Feeds |> List.map serializeFeedDescription)
        ]

    let private serializeDidDocument (config : FeedGeneratorConfig) =
        dict [
            "@context", box [| "https://www.w3.org/ns/did/v1" |]
            "id", box (Did.value config.ServiceDid)
            "service",
            box [|
                dict [
                    "id", box "#bsky_fg"
                    "type", box "BskyFeedGenerator"
                    "serviceEndpoint", box (sprintf "https://%s" config.Hostname)
                ]
            |]
        ]

    /// Configure and return a WebApplication with feed generator endpoints.
    /// Registers GET /.well-known/did.json, GET /xrpc/app.bsky.feed.getFeedSkeleton,
    /// and GET /xrpc/app.bsky.feed.describeFeedGenerator.
    let configure (config : FeedGeneratorConfig) : WebApplication =
        let builder = WebApplication.CreateBuilder ()
        builder.WebHost.UseUrls (sprintf "http://0.0.0.0:%d" config.Port) |> ignore
        let app = builder.Build ()

        app.MapGet (
            "/.well-known/did.json",
            Func<IResult> (fun () ->
                let doc = serializeDidDocument config
                Results.Json (doc, jsonOptions))
        )
        |> ignore

        app.MapGet (
            "/xrpc/app.bsky.feed.describeFeedGenerator",
            Func<IResult> (fun () ->
                let desc = {
                    Did = config.ServiceDid
                    Feeds = config.Descriptions
                }

                let serialized = serializeGeneratorDescription desc
                Results.Json (serialized, jsonOptions))
        )
        |> ignore

        app.MapGet (
            "/xrpc/app.bsky.feed.getFeedSkeleton",
            Func<HttpContext, Task<IResult>> (fun (ctx : HttpContext) ->
                task {
                    let feedParam = ctx.Request.Query.["feed"].ToString ()

                    let limitStr = ctx.Request.Query.["limit"].ToString ()

                    let limit =
                        match Int32.TryParse limitStr with
                        | true, v when v >= 1 && v <= 100 -> v
                        | _ -> 50

                    let cursor =
                        match ctx.Request.Query.["cursor"].ToString () with
                        | "" -> None
                        | c -> Some c

                    match AtUri.parse feedParam with
                    | Error _ ->
                        return Results.Json (dict [ "error", box "InvalidRequest"; "message", box "Invalid feed URI" ], jsonOptions, statusCode = 400)
                    | Ok feedUri ->
                        let rkey =
                            match AtUri.rkey feedUri with
                            | Some k -> k
                            | None -> ""

                        match Map.tryFind rkey config.Feeds with
                        | None ->
                            return Results.Json (dict [ "error", box "UnknownFeed"; "message", box (sprintf "Unknown feed: %s" feedParam) ], jsonOptions, statusCode = 404)
                        | Some algorithm ->
                            let query = {
                                Feed = feedUri
                                Limit = limit
                                Cursor = cursor
                            }

                            let! skeleton = algorithm.GetFeedSkeleton query
                            let serialized = serializeSkeletonFeed skeleton
                            return Results.Json (serialized, jsonOptions)
                })
        )
        |> ignore

        app
