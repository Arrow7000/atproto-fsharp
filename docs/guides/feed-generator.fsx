(**
---
title: Feed Generator
category: Server-Side
categoryindex: 4
index: 22
description: Build custom feed algorithms with the feed generator framework
keywords: fsharp, atproto, bluesky, feed, generator, algorithm, server
---

# Feed Generator

A feed generator is an AT Protocol service that provides custom feed algorithms. When a user subscribes to a custom feed, Bluesky's app view calls your feed generator to get a "skeleton" -- a list of post AT-URIs -- and then hydrates those posts with full content before displaying them. Your generator decides *which* posts appear and in *what order*; the app view handles everything else.

`FSharp.ATProto.FeedGenerator` provides an ASP.NET minimal API framework for building feed generators. It handles the protocol-level endpoints so you can focus on your feed logic.
*)

(*** hide ***)
#nowarn "20"
#r "../../src/FSharp.ATProto.Syntax/bin/Release/net10.0/FSharp.ATProto.Syntax.dll"
#r "../../src/FSharp.ATProto.FeedGenerator/bin/Release/net10.0/FSharp.ATProto.FeedGenerator.dll"
open FSharp.ATProto.Syntax
open FSharp.ATProto.FeedGenerator
(***)

open FSharp.ATProto.FeedGenerator
open FSharp.ATProto.Syntax

(**
## Key Types

A feed skeleton response is built from these types:

```fsharp
type SkeletonItem = {
    Post: AtUri
    Reason: SkeletonReason option
}

type SkeletonFeed = {
    Feed: SkeletonItem list
    Cursor: string option
}
```

Each `SkeletonItem` contains a post AT-URI. The optional `Reason` field can indicate that a post is included because someone reposted it:

```fsharp
type SkeletonReason =
    | RepostBy of did: Did * indexedAt: string
```

Your feed algorithm receives a `FeedQuery` with the requested feed URI, page limit, and optional cursor for pagination:

```fsharp
type FeedQuery = {
    Feed: AtUri
    Limit: int
    Cursor: string option
}
```

## Implementing a Feed Algorithm

The `IFeedAlgorithm` interface has a single method:

```fsharp
type IFeedAlgorithm =
    abstract member GetFeedSkeleton : query: FeedQuery -> Task<SkeletonFeed>
```

You can implement this interface directly, or use the helper functions in the `FeedAlgorithm` module.

`FeedAlgorithm.fromFunction` wraps an async function:
*)

(*** hide ***)
let fetchRecentPosts (_limit: int) (_cursor: string option) : System.Threading.Tasks.Task<FSharp.ATProto.FeedGenerator.SkeletonItem list> = Unchecked.defaultof<_>
let nextCursor = Unchecked.defaultof<string option>
let allPosts = Unchecked.defaultof<AtUri list>
(***)

let myFeed =
    FeedAlgorithm.fromFunction (fun query ->
        task {
            // Your async feed logic here
            let! posts = fetchRecentPosts query.Limit query.Cursor
            return { Feed = posts; Cursor = nextCursor }
        })

(**
`FeedAlgorithm.fromSync` wraps a synchronous function -- useful for feeds backed by in-memory data:
*)

let myFeed2 =
    FeedAlgorithm.fromSync (fun query ->
        let posts =
            allPosts
            |> List.take (min query.Limit (List.length allPosts))
            |> List.map (fun uri -> { Post = uri; Reason = None })
        { Feed = posts; Cursor = None })

(**
## Server Configuration

`FeedGeneratorConfig` ties your algorithms to a hostname and DID:

```fsharp
type FeedGeneratorConfig = {
    Hostname: string
    ServiceDid: Did
    Feeds: Map<string, IFeedAlgorithm>
    Descriptions: FeedDescription list
    Port: int
}
```

The `Feeds` map is keyed by the record key (rkey) portion of the feed AT-URI. For example, if a user subscribes to `at://did:web:feed.example.com/app.bsky.feed.generator/chronological`, the framework looks up `"chronological"` in this map.

`Descriptions` are returned by the `describeFeedGenerator` endpoint so clients know what feeds you offer:

```fsharp
type FeedDescription = {
    Uri: AtUri
    DisplayName: string
    Description: string option
    Avatar: string option
}
```

## Running the Server

`FeedServer.configure` builds an ASP.NET `WebApplication` with three endpoints:

| Route | Purpose |
|---|---|
| `GET /.well-known/did.json` | DID document for `did:web` resolution |
| `GET /xrpc/app.bsky.feed.describeFeedGenerator` | Lists available feeds |
| `GET /xrpc/app.bsky.feed.getFeedSkeleton` | Returns the feed skeleton for a query |

## Complete Example
*)

open FSharp.ATProto.FeedGenerator
open FSharp.ATProto.Syntax

// Define a feed algorithm
let chronoFeed =
    FeedAlgorithm.fromSync (fun query ->
        // Your feed logic -- return post AT-URIs
        { Feed =
            [ { Post = AtUri.parse "at://did:plc:xyz/app.bsky.feed.post/abc" |> Result.defaultWith failwith
                Reason = None } ]
          Cursor = None })

// Build the feed URI for descriptions
let feedUri =
    AtUri.parse "at://did:web:feed.example.com/app.bsky.feed.generator/chronological"
    |> Result.defaultWith failwith

let serviceDid = Did.parse "did:web:feed.example.com" |> Result.defaultWith failwith

// Configure and run the server
let config : FeedGeneratorConfig =
    { Hostname = "feed.example.com"
      ServiceDid = serviceDid
      Feeds = Map.ofList [ "chronological", chronoFeed ]
      Descriptions =
        [ { Uri = feedUri
            DisplayName = "Chronological"
            Description = Some "Posts in reverse chronological order"
            Avatar = None } ]
      Port = 3000 }

(*** hide ***)
// FeedServer.configure and app.Run() would start the server;
// omitted here to avoid side effects during fsdocs build.
(***)

(**
```fsharp
let app = FeedServer.configure config
app.Run()
```

The server starts on port 3000. Bluesky's app view will call `GET /xrpc/app.bsky.feed.getFeedSkeleton?feed=at://...&limit=50` and your algorithm returns the skeleton. The framework handles parameter parsing, limit clamping (1--100, default 50), unknown feed errors, and JSON serialization.
*)
