namespace FSharp.ATProto.Bluesky

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text.Json
open System.Threading.Tasks
open FSharp.ATProto.Core

/// <summary>
/// High-level convenience methods for common Bluesky operations:
/// posting, replying, liking, reposting, following, blocking, uploading blobs, and deleting records.
/// All methods require an authenticated <see cref="AtpAgent"/>.
/// </summary>
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

    /// <summary>
    /// Create a post with pre-resolved facets. Use this when you have already detected
    /// and resolved rich text facets, or when you want full control over facet content.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="text">The post text content.</param>
    /// <param name="facets">Pre-resolved facets (mentions, links, hashtags). Pass an empty list for plain text.</param>
    /// <returns>The created record's AT-URI and CID on success, or an <see cref="XrpcError"/>.</returns>
    let postWith (agent: AtpAgent) (text: string) (facets: AppBskyRichtext.Facet.Facet list)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        let record =
            {| ``$type`` = AppBskyFeed.Post.TypeId
               text = text
               createdAt = nowTimestamp ()
               facets = if facets.IsEmpty then null else facets |> box |}
        createRecord agent "app.bsky.feed.post" record

    /// <summary>
    /// Create a post with automatic rich text detection.
    /// Mentions, links, and hashtags are automatically detected and resolved to facets.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="text">The post text. Mentions (<c>@handle</c>), links (<c>https://...</c>), and hashtags (<c>#tag</c>) are auto-detected.</param>
    /// <returns>The created record's AT-URI and CID on success, or an <see cref="XrpcError"/>.</returns>
    /// <remarks>
    /// Internally calls <see cref="RichText.parse"/> to detect and resolve facets before creating the post.
    /// Unresolvable mentions are silently omitted from facets.
    /// For pre-resolved facets, use <see cref="postWith"/> instead.
    /// </remarks>
    /// <example>
    /// <code>
    /// let! result = Bluesky.post agent "Hello @alice.bsky.social! Check out https://example.com #atproto"
    /// </code>
    /// </example>
    let post (agent: AtpAgent) (text: string)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        task {
            let! facets = RichText.parse agent text
            return! postWith agent text facets
        }

    /// <summary>
    /// Create a reply to an existing post with automatic rich text detection.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="text">The reply text. Mentions, links, and hashtags are auto-detected.</param>
    /// <param name="parentUri">The AT-URI of the post being directly replied to.</param>
    /// <param name="parentCid">The CID of the post being directly replied to.</param>
    /// <param name="rootUri">The AT-URI of the thread root post. Same as <paramref name="parentUri"/> for top-level replies.</param>
    /// <param name="rootCid">The CID of the thread root post. Same as <paramref name="parentCid"/> for top-level replies.</param>
    /// <returns>The created record's AT-URI and CID on success, or an <see cref="XrpcError"/>.</returns>
    /// <remarks>
    /// The AT Protocol threading model requires both parent and root references.
    /// For a reply to a top-level post, the parent and root are the same.
    /// For a reply deeper in a thread, the root points to the original post
    /// while the parent points to the immediate post being replied to.
    /// </remarks>
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

    /// <summary>
    /// Like a post or other record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="uri">The AT-URI of the record to like (e.g., <c>at://did:plc:.../app.bsky.feed.post/...</c>).</param>
    /// <param name="cid">The CID of the record to like.</param>
    /// <returns>The created like record's AT-URI and CID on success, or an <see cref="XrpcError"/>.</returns>
    let like (agent: AtpAgent) (uri: string) (cid: string)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        let record =
            {| ``$type`` = AppBskyFeed.Like.TypeId
               createdAt = nowTimestamp ()
               subject = {| uri = uri; cid = cid |} |}
        createRecord agent "app.bsky.feed.like" record

    /// <summary>
    /// Repost (retweet) a post or other record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="uri">The AT-URI of the record to repost.</param>
    /// <param name="cid">The CID of the record to repost.</param>
    /// <returns>The created repost record's AT-URI and CID on success, or an <see cref="XrpcError"/>.</returns>
    let repost (agent: AtpAgent) (uri: string) (cid: string)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        let record =
            {| ``$type`` = AppBskyFeed.Repost.TypeId
               createdAt = nowTimestamp ()
               subject = {| uri = uri; cid = cid |} |}
        createRecord agent "app.bsky.feed.repost" record

    /// <summary>
    /// Follow a user by their DID.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="did">The DID of the user to follow.</param>
    /// <returns>The created follow record's AT-URI and CID on success, or an <see cref="XrpcError"/>.</returns>
    let follow (agent: AtpAgent) (did: string)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        let record =
            {| ``$type`` = AppBskyGraph.Follow.TypeId
               createdAt = nowTimestamp ()
               subject = did |}
        createRecord agent "app.bsky.graph.follow" record

    /// <summary>
    /// Block a user by their DID.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="did">The DID of the user to block.</param>
    /// <returns>The created block record's AT-URI and CID on success, or an <see cref="XrpcError"/>.</returns>
    let block (agent: AtpAgent) (did: string)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        let record =
            {| ``$type`` = AppBskyGraph.Block.TypeId
               createdAt = nowTimestamp ()
               subject = did |}
        createRecord agent "app.bsky.graph.block" record

    /// <summary>
    /// Delete a record by its AT-URI.
    /// Can be used to unlike, un-repost, unfollow, unblock, or delete a post.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="atUri">The AT-URI of the record to delete (e.g., <c>at://did:plc:.../app.bsky.feed.like/...</c>).</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    /// <remarks>
    /// The AT-URI is parsed to extract the repo DID, collection, and record key.
    /// This is a general-purpose delete; pass the AT-URI returned when the record was created.
    /// </remarks>
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

    /// <summary>
    /// Upload a blob (image, video, or other binary data) to the PDS.
    /// Returns the blob reference needed to embed the blob in a record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="data">The raw binary content of the blob.</param>
    /// <param name="mimeType">The MIME type of the blob (e.g., <c>image/jpeg</c>, <c>image/png</c>).</param>
    /// <returns>
    /// <c>Ok</c> with a <see cref="JsonElement"/> containing the blob reference on success,
    /// or an <see cref="XrpcError"/>. The blob reference can be used in embed records.
    /// </returns>
    /// <remarks>
    /// The returned blob reference is a JSON object with <c>$type: "blob"</c>, <c>ref</c>, <c>mimeType</c>,
    /// and <c>size</c> fields. Pass it as the <c>image</c> field in an <c>app.bsky.embed.images</c> embed,
    /// or use <see cref="postWithImages"/> for a higher-level API.
    /// </remarks>
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

    /// <summary>
    /// Create a post with attached images and automatic rich text detection.
    /// Uploads each image as a blob, then creates the post with an <c>app.bsky.embed.images</c> embed.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="text">The post text. Mentions, links, and hashtags are auto-detected.</param>
    /// <param name="images">
    /// A list of images to attach, each as a tuple of <c>(imageBytes, mimeType, altText)</c>.
    /// The MIME type should be e.g. <c>image/jpeg</c> or <c>image/png</c>.
    /// Alt text is required for accessibility.
    /// </param>
    /// <returns>The created record's AT-URI and CID on success, or an <see cref="XrpcError"/>.</returns>
    /// <remarks>
    /// Images are uploaded sequentially. If any image upload fails, the entire operation
    /// returns the error without creating the post. Bluesky supports up to 4 images per post.
    /// </remarks>
    /// <example>
    /// <code>
    /// let imageBytes = System.IO.File.ReadAllBytes("photo.jpg")
    /// let! result = Bluesky.postWithImages agent "Check this out!" [ (imageBytes, "image/jpeg", "A photo") ]
    /// </code>
    /// </example>
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
