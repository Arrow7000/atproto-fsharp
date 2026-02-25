namespace FSharp.ATProto.Bluesky

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text.Json
open System.Threading.Tasks
open FSharp.ATProto.Core

module Bluesky =

    let private nowTimestamp () =
        DateTimeOffset.UtcNow.ToString("o")

    let private sessionDid (agent: AtpAgent) =
        match agent.Session with
        | Some s -> s.Did
        | None -> failwith "Not logged in"

    let private createRecord (agent: AtpAgent) (collection: string) (record: obj) =
        let recordElement = JsonSerializer.SerializeToElement(record, Json.options)
        ComAtprotoRepo.CreateRecord.call agent
            { Repo = sessionDid agent
              Collection = collection
              Record = recordElement
              Rkey = None
              SwapCommit = None
              Validate = None }

    let postWith (agent: AtpAgent) (text: string) (facets: AppBskyRichtext.Facet.Facet list)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        let record =
            {| ``$type`` = AppBskyFeed.Post.TypeId
               text = text
               createdAt = nowTimestamp ()
               facets = if facets.IsEmpty then null else facets |> box |}
        createRecord agent "app.bsky.feed.post" record

    let post (agent: AtpAgent) (text: string)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        task {
            let! facets = RichText.parse agent text
            return! postWith agent text facets
        }

    let reply (agent: AtpAgent) (text: string) (parentUri: string) (parentCid: string) (rootUri: string) (rootCid: string)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        task {
            let! facets = RichText.parse agent text
            let record =
                {| ``$type`` = AppBskyFeed.Post.TypeId
                   text = text
                   createdAt = nowTimestamp ()
                   facets = if facets.IsEmpty then null else facets |> box
                   reply = {| parent = {| uri = parentUri; cid = parentCid |}
                              root = {| uri = rootUri; cid = rootCid |} |} |}
            return! createRecord agent "app.bsky.feed.post" record
        }

    let like (agent: AtpAgent) (uri: string) (cid: string)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        let record =
            {| ``$type`` = AppBskyFeed.Like.TypeId
               createdAt = nowTimestamp ()
               subject = {| uri = uri; cid = cid |} |}
        createRecord agent "app.bsky.feed.like" record

    let repost (agent: AtpAgent) (uri: string) (cid: string)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        let record =
            {| ``$type`` = AppBskyFeed.Repost.TypeId
               createdAt = nowTimestamp ()
               subject = {| uri = uri; cid = cid |} |}
        createRecord agent "app.bsky.feed.repost" record

    let follow (agent: AtpAgent) (did: string)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        let record =
            {| ``$type`` = AppBskyGraph.Follow.TypeId
               createdAt = nowTimestamp ()
               subject = did |}
        createRecord agent "app.bsky.graph.follow" record

    let block (agent: AtpAgent) (did: string)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        let record =
            {| ``$type`` = AppBskyGraph.Block.TypeId
               createdAt = nowTimestamp ()
               subject = did |}
        createRecord agent "app.bsky.graph.block" record

    let deleteRecord (agent: AtpAgent) (atUri: string)
        : Task<Result<unit, XrpcError>> =
        task {
            // Parse AT-URI: at://did/collection/rkey
            let parts = atUri.Replace("at://", "").Split('/')
            let repo = parts.[0]
            let collection = parts.[1]
            let rkey = parts.[2]
            let! result = ComAtprotoRepo.DeleteRecord.call agent
                            { Repo = repo
                              Collection = collection
                              Rkey = rkey
                              SwapCommit = None
                              SwapRecord = None }
            return result |> Result.map ignore
        }

    let uploadBlob (agent: AtpAgent) (data: byte[]) (mimeType: string)
        : Task<Result<JsonElement, XrpcError>> =
        task {
            let url = Uri(agent.BaseUrl, sprintf "xrpc/%s" ComAtprotoRepo.UploadBlob.TypeId)
            let request = new HttpRequestMessage(HttpMethod.Post, url)
            request.Content <- new ByteArrayContent(data)
            request.Content.Headers.ContentType <- MediaTypeHeaderValue(mimeType)
            match agent.Session with
            | Some session ->
                request.Headers.Authorization <-
                    AuthenticationHeaderValue("Bearer", session.AccessJwt)
            | None -> ()
            let! response = agent.HttpClient.SendAsync(request)
            if response.IsSuccessStatusCode then
                let! json = response.Content.ReadAsStringAsync()
                let doc = JsonSerializer.Deserialize<JsonElement>(json)
                return Ok(doc.GetProperty("blob"))
            else
                let! errorJson = response.Content.ReadAsStringAsync()
                try
                    let err = JsonSerializer.Deserialize<XrpcError>(errorJson, Json.options)
                    return Error { err with StatusCode = int response.StatusCode }
                with _ ->
                    return Error { StatusCode = int response.StatusCode; Error = None; Message = Some errorJson }
        }

    let private uploadAllBlobs (agent: AtpAgent) (images: (byte[] * string * string) list)
        : Task<Result<(JsonElement * string) list, XrpcError>> =
        task {
            let mutable blobRefs : (JsonElement * string) list = []
            let mutable error : XrpcError option = None
            for (data, mimeType, altText) in images do
                if error.IsNone then
                    match! uploadBlob agent data mimeType with
                    | Ok blob -> blobRefs <- blobRefs @ [ (blob, altText) ]
                    | Error e -> error <- Some e
            match error with
            | Some e -> return Error e
            | None -> return Ok blobRefs
        }

    let postWithImages (agent: AtpAgent) (text: string) (images: (byte[] * string * string) list)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        task {
            match! uploadAllBlobs agent images with
            | Error e -> return Error e
            | Ok blobRefs ->
                let! facets = RichText.parse agent text
                let embed =
                    {| ``$type`` = "app.bsky.embed.images"
                       images = blobRefs |> List.map (fun (blob, alt) ->
                        {| alt = alt; image = blob |}) |}
                let record =
                    {| ``$type`` = AppBskyFeed.Post.TypeId
                       text = text
                       createdAt = nowTimestamp ()
                       facets = if facets.IsEmpty then null else facets |> box
                       embed = embed |}
                return! createRecord agent "app.bsky.feed.post" record
        }
