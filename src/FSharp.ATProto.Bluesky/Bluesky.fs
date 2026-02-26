namespace FSharp.ATProto.Bluesky

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text.Json
open System.Threading.Tasks
open FSharp.ATProto.Core
open FSharp.ATProto.Syntax

/// <summary>
/// A reference to a specific version of a post record.
/// Contains both the AT-URI (identifying the record) and the CID (identifying the exact version).
/// Accepted by <c>like</c>, <c>repost</c>, and <c>reply</c>.
/// </summary>
type PostRef =
    { /// <summary>The AT-URI of the post record.</summary>
      Uri: AtUri
      /// <summary>The CID (content identifier) of the post record version.</summary>
      Cid: Cid }

/// <summary>
/// A reference to a like record, returned by <c>Bluesky.like</c>.
/// Pass to <c>Bluesky.unlike</c> to undo.
/// </summary>
type LikeRef =
    { /// <summary>The AT-URI of the like record.</summary>
      Uri: AtUri }

/// <summary>
/// A reference to a repost record, returned by <c>Bluesky.repost</c>.
/// Pass to <c>Bluesky.unrepost</c> to undo.
/// </summary>
type RepostRef =
    { /// <summary>The AT-URI of the repost record.</summary>
      Uri: AtUri }

/// <summary>
/// A reference to a follow record, returned by <c>Bluesky.follow</c>.
/// Pass to <c>Bluesky.unfollow</c> to undo.
/// </summary>
type FollowRef =
    { /// <summary>The AT-URI of the follow record.</summary>
      Uri: AtUri }

/// <summary>
/// A reference to a block record, returned by <c>Bluesky.block</c>.
/// Pass to <c>Bluesky.unblock</c> to undo.
/// </summary>
type BlockRef =
    { /// <summary>The AT-URI of the block record.</summary>
      Uri: AtUri }

/// <summary>
/// Image data for upload with a post.
/// </summary>
type ImageUpload =
    { /// <summary>The raw binary image data.</summary>
      Data: byte[]
      /// <summary>The MIME type (e.g., <c>image/jpeg</c>, <c>image/png</c>).</summary>
      MimeType: string
      /// <summary>Alt text describing the image for accessibility.</summary>
      AltText: string }

/// <summary>
/// A reference to an uploaded blob, as returned by <c>com.atproto.repo.uploadBlob</c>.
/// Contains both the raw JSON (for passing back to the API in embeds) and typed convenience fields.
/// </summary>
type BlobRef =
    { /// <summary>The raw JSON element for the blob object. Pass this directly in embed records.</summary>
      Json: JsonElement
      /// <summary>The content-addressed link (CID) of the blob.</summary>
      Ref: Cid
      /// <summary>The MIME type of the blob (e.g., <c>image/jpeg</c>).</summary>
      MimeType: string
      /// <summary>The size of the blob in bytes.</summary>
      Size: int64 }

/// <summary>
/// High-level convenience methods for common Bluesky operations:
/// posting, replying, liking, reposting, following, blocking, uploading blobs, and deleting records.
/// All methods require an authenticated <see cref="AtpAgent"/>.
/// </summary>
module Bluesky =

    let private nowTimestamp () =
        DateTimeOffset.UtcNow.ToString("o")

    let private notLoggedInError : XrpcError =
        { StatusCode = 401; Error = Some "NotLoggedIn"; Message = Some "No active session" }

    let private sessionDid (agent: AtpAgent) : Result<Did, XrpcError> =
        match agent.Session with
        | Some s -> Ok s.Did
        | None -> Error notLoggedInError

    let private toXrpcError (msg: string) : XrpcError =
        { StatusCode = 400; Error = Some "InvalidRequest"; Message = Some msg }

    let private parseBlobRef (blob: JsonElement) : Result<BlobRef, XrpcError> =
        try
            let link =
                blob.GetProperty("ref").GetProperty("$link").GetString()
            let mimeType = blob.GetProperty("mimeType").GetString()
            let size = blob.GetProperty("size").GetInt64()
            match Cid.parse link with
            | Ok cid ->
                Ok { Json = blob; Ref = cid; MimeType = mimeType; Size = size }
            | Error msg ->
                Error (toXrpcError (sprintf "Invalid blob ref CID: %s" msg))
        with ex ->
            Error (toXrpcError (sprintf "Failed to parse blob reference: %s" ex.Message))

    let private createRecord (agent: AtpAgent) (collection: string) (record: obj)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        match sessionDid agent with
        | Error e -> Task.FromResult(Error e)
        | Ok did ->
            match Nsid.parse collection with
            | Error msg -> Task.FromResult(Error (toXrpcError (sprintf "Invalid NSID: %s" msg)))
            | Ok nsid ->
                let recordElement = JsonSerializer.SerializeToElement(record, Json.options)
                ComAtprotoRepo.CreateRecord.call agent
                    { Repo = Did.value did
                      Collection = nsid
                      Record = recordElement
                      Rkey = None
                      SwapCommit = None
                      Validate = None }

    let private toPostRef (output: ComAtprotoRepo.CreateRecord.Output) : PostRef =
        { Uri = output.Uri; Cid = output.Cid }

    /// <summary>
    /// Create a post with pre-resolved facets. Use this when you have already detected
    /// and resolved rich text facets, or when you want full control over facet content.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="text">The post text content.</param>
    /// <param name="facets">Pre-resolved facets (mentions, links, hashtags). Pass an empty list for plain text.</param>
    /// <returns>A <see cref="PostRef"/> with the AT-URI and CID on success, or an <see cref="XrpcError"/>.</returns>
    let postWithFacets (agent: AtpAgent) (text: string) (facets: AppBskyRichtext.Facet.Facet list)
        : Task<Result<PostRef, XrpcError>> =
        task {
            let record =
                {| ``$type`` = AppBskyFeed.Post.TypeId
                   text = text
                   createdAt = nowTimestamp ()
                   facets = if facets.IsEmpty then null else facets |> box |}
            let! result = createRecord agent "app.bsky.feed.post" record
            return result |> Result.map toPostRef
        }

    /// <summary>
    /// Create a post with automatic rich text detection.
    /// Mentions, links, and hashtags are automatically detected and resolved to facets.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="text">The post text. Mentions (<c>@handle</c>), links (<c>https://...</c>), and hashtags (<c>#tag</c>) are auto-detected.</param>
    /// <returns>A <see cref="PostRef"/> with the AT-URI and CID on success, or an <see cref="XrpcError"/>.</returns>
    /// <remarks>
    /// Internally calls <see cref="RichText.parse"/> to detect and resolve facets before creating the post.
    /// Unresolvable mentions are silently omitted from facets.
    /// For pre-resolved facets, use <see cref="postWithFacets"/> instead.
    /// </remarks>
    /// <example>
    /// <code>
    /// let! result = Bluesky.post agent "Hello @alice.bsky.social! Check out https://example.com #atproto"
    /// </code>
    /// </example>
    let post (agent: AtpAgent) (text: string)
        : Task<Result<PostRef, XrpcError>> =
        task {
            let! facets = RichText.parse agent text
            return! postWithFacets agent text facets
        }

    /// <summary>
    /// Create a reply to an existing post with automatic rich text detection.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="text">The reply text. Mentions, links, and hashtags are auto-detected.</param>
    /// <param name="parent">A <see cref="PostRef"/> for the post being directly replied to.</param>
    /// <param name="root">A <see cref="PostRef"/> for the thread root post. Same as <paramref name="parent"/> for top-level replies.</param>
    /// <returns>A <see cref="PostRef"/> with the AT-URI and CID on success, or an <see cref="XrpcError"/>.</returns>
    /// <remarks>
    /// The AT Protocol threading model requires both parent and root references.
    /// For a reply to a top-level post, the parent and root are the same.
    /// For a reply deeper in a thread, the root points to the original post
    /// while the parent points to the immediate post being replied to.
    /// </remarks>
    let reply (agent: AtpAgent) (text: string) (parent: PostRef) (root: PostRef)
        : Task<Result<PostRef, XrpcError>> =
        task {
            let! facets = RichText.parse agent text
            let record =
                {| ``$type`` = AppBskyFeed.Post.TypeId
                   text = text
                   createdAt = nowTimestamp ()
                   facets = if facets.IsEmpty then null else facets |> box
                   reply = {| parent = {| uri = AtUri.value parent.Uri; cid = Cid.value parent.Cid |}
                              root = {| uri = AtUri.value root.Uri; cid = Cid.value root.Cid |} |} |}
            let! result = createRecord agent "app.bsky.feed.post" record
            return result |> Result.map toPostRef
        }

    /// <summary>
    /// Like a post or other record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="postRef">A <see cref="PostRef"/> identifying the record to like.</param>
    /// <returns>A <see cref="LikeRef"/> on success, or an <see cref="XrpcError"/>. Pass the <c>LikeRef</c> to <see cref="unlike"/> to undo.</returns>
    let like (agent: AtpAgent) (postRef: PostRef)
        : Task<Result<LikeRef, XrpcError>> =
        task {
            let record =
                {| ``$type`` = AppBskyFeed.Like.TypeId
                   createdAt = nowTimestamp ()
                   subject = {| uri = AtUri.value postRef.Uri; cid = Cid.value postRef.Cid |} |}
            let! result = createRecord agent "app.bsky.feed.like" record
            return result |> Result.map (fun o -> { LikeRef.Uri = o.Uri })
        }

    /// <summary>
    /// Repost (retweet) a post or other record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="postRef">A <see cref="PostRef"/> identifying the record to repost.</param>
    /// <returns>A <see cref="RepostRef"/> on success, or an <see cref="XrpcError"/>. Pass the <c>RepostRef</c> to <see cref="unrepost"/> to undo.</returns>
    let repost (agent: AtpAgent) (postRef: PostRef)
        : Task<Result<RepostRef, XrpcError>> =
        task {
            let record =
                {| ``$type`` = AppBskyFeed.Repost.TypeId
                   createdAt = nowTimestamp ()
                   subject = {| uri = AtUri.value postRef.Uri; cid = Cid.value postRef.Cid |} |}
            let! result = createRecord agent "app.bsky.feed.repost" record
            return result |> Result.map (fun o -> { RepostRef.Uri = o.Uri })
        }

    /// <summary>
    /// Follow a user by their DID.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="did">The DID of the user to follow.</param>
    /// <returns>A <see cref="FollowRef"/> on success, or an <see cref="XrpcError"/>. Pass the <c>FollowRef</c> to <see cref="unfollow"/> to undo.</returns>
    let follow (agent: AtpAgent) (did: Did)
        : Task<Result<FollowRef, XrpcError>> =
        task {
            let record =
                {| ``$type`` = AppBskyGraph.Follow.TypeId
                   createdAt = nowTimestamp ()
                   subject = Did.value did |}
            let! result = createRecord agent "app.bsky.graph.follow" record
            return result |> Result.map (fun o -> { FollowRef.Uri = o.Uri })
        }

    /// <summary>
    /// Block a user by their DID.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="did">The DID of the user to block.</param>
    /// <returns>A <see cref="BlockRef"/> on success, or an <see cref="XrpcError"/>. Pass the <c>BlockRef</c> to <see cref="unblock"/> to undo.</returns>
    let block (agent: AtpAgent) (did: Did)
        : Task<Result<BlockRef, XrpcError>> =
        task {
            let record =
                {| ``$type`` = AppBskyGraph.Block.TypeId
                   createdAt = nowTimestamp ()
                   subject = Did.value did |}
            let! result = createRecord agent "app.bsky.graph.block" record
            return result |> Result.map (fun o -> { BlockRef.Uri = o.Uri })
        }

    /// <summary>
    /// Delete a record by its AT-URI.
    /// Can be used to unlike, un-repost, unfollow, unblock, or delete a post.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="atUri">The AT-URI of the record to delete.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    /// <remarks>
    /// The AT-URI is parsed to extract the repo DID, collection, and record key.
    /// This is a general-purpose delete; pass the AT-URI returned when the record was created.
    /// </remarks>
    let deleteRecord (agent: AtpAgent) (atUri: AtUri)
        : Task<Result<unit, XrpcError>> =
        let repo = AtUri.authority atUri
        match AtUri.collection atUri, AtUri.rkey atUri with
        | None, _ ->
            Task.FromResult(Error (toXrpcError "AT-URI must include a collection"))
        | _, None ->
            Task.FromResult(Error (toXrpcError "AT-URI must include a record key"))
        | Some collStr, Some rkeyStr ->
            match Nsid.parse collStr, RecordKey.parse rkeyStr with
            | Error msg, _ ->
                Task.FromResult(Error (toXrpcError (sprintf "Invalid collection NSID: %s" msg)))
            | _, Error msg ->
                Task.FromResult(Error (toXrpcError (sprintf "Invalid record key: %s" msg)))
            | Ok collection, Ok rkey ->
                task {
                    let! result = ComAtprotoRepo.DeleteRecord.call agent
                                    { Repo = repo
                                      Collection = collection
                                      Rkey = rkey
                                      SwapCommit = None
                                      SwapRecord = None }
                    return result |> Result.map ignore
                }

    /// <summary>
    /// Unlike a post by deleting the like record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="likeRef">The <see cref="LikeRef"/> returned by <see cref="like"/>.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let unlike (agent: AtpAgent) (likeRef: LikeRef)
        : Task<Result<unit, XrpcError>> =
        deleteRecord agent likeRef.Uri

    /// <summary>
    /// Undo a repost by deleting the repost record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="repostRef">The <see cref="RepostRef"/> returned by <see cref="repost"/>.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let unrepost (agent: AtpAgent) (repostRef: RepostRef)
        : Task<Result<unit, XrpcError>> =
        deleteRecord agent repostRef.Uri

    /// <summary>
    /// Unfollow a user by deleting the follow record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="followRef">The <see cref="FollowRef"/> returned by <see cref="follow"/>.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let unfollow (agent: AtpAgent) (followRef: FollowRef)
        : Task<Result<unit, XrpcError>> =
        deleteRecord agent followRef.Uri

    /// <summary>
    /// Unblock a user by deleting the block record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="blockRef">The <see cref="BlockRef"/> returned by <see cref="block"/>.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let unblock (agent: AtpAgent) (blockRef: BlockRef)
        : Task<Result<unit, XrpcError>> =
        deleteRecord agent blockRef.Uri

    /// <summary>
    /// Upload a blob (image, video, or other binary data) to the PDS.
    /// Returns a typed <see cref="BlobRef"/> containing the blob reference needed to embed the blob in a record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="data">The raw binary content of the blob.</param>
    /// <param name="mimeType">The MIME type of the blob (e.g., <c>image/jpeg</c>, <c>image/png</c>).</param>
    /// <returns>
    /// <c>Ok</c> with a <see cref="BlobRef"/> on success, or an <see cref="XrpcError"/>.
    /// The <see cref="BlobRef.Json"/> field contains the raw JSON for use in embed records,
    /// while <see cref="BlobRef.Ref"/>, <see cref="BlobRef.MimeType"/>, and <see cref="BlobRef.Size"/>
    /// provide typed access to individual fields.
    /// </returns>
    /// <remarks>
    /// Use <see cref="BlobRef.Json"/> when constructing custom embed records, or use
    /// <see cref="postWithImages"/> for a higher-level API that handles blob references automatically.
    /// </remarks>
    let uploadBlob (agent: AtpAgent) (data: byte[]) (mimeType: string)
        : Task<Result<BlobRef, XrpcError>> =
        task {
            let url = System.Uri(agent.BaseUrl, sprintf "xrpc/%s" ComAtprotoRepo.UploadBlob.TypeId)
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
                match doc.TryGetProperty("blob") with
                | true, blob -> return parseBlobRef blob
                | false, _ -> return Error (toXrpcError "Response missing 'blob' property")
            else
                let! errorJson = response.Content.ReadAsStringAsync()
                try
                    let err = JsonSerializer.Deserialize<XrpcError>(errorJson, Json.options)
                    return Error { err with StatusCode = int response.StatusCode }
                with _ ->
                    return Error { StatusCode = int response.StatusCode; Error = None; Message = Some errorJson }
        }

    let private uploadAllBlobs (agent: AtpAgent) (images: (byte[] * string * string) list)
        : Task<Result<(BlobRef * string) list, XrpcError>> =
        task {
            let mutable blobRefs : (BlobRef * string) list = []
            let mutable error : XrpcError option = None
            for (data, mimeType, altText) in images do
                if error.IsNone then
                    match! uploadBlob agent data mimeType with
                    | Ok blobRef -> blobRefs <- blobRefs @ [ (blobRef, altText) ]
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
    /// A list of <see cref="ImageUpload"/> records describing the images to attach.
    /// Alt text is required for accessibility. Bluesky supports up to 4 images per post.
    /// </param>
    /// <returns>A <see cref="PostRef"/> with the AT-URI and CID on success, or an <see cref="XrpcError"/>.</returns>
    /// <remarks>
    /// Images are uploaded sequentially. If any image upload fails, the entire operation
    /// returns the error without creating the post.
    /// </remarks>
    /// <example>
    /// <code>
    /// let imageBytes = System.IO.File.ReadAllBytes("photo.jpg")
    /// let! result = Bluesky.postWithImages agent "Check this out!"
    ///     [ { Data = imageBytes; MimeType = "image/jpeg"; AltText = "A photo" } ]
    /// </code>
    /// </example>
    let postWithImages (agent: AtpAgent) (text: string) (images: ImageUpload list)
        : Task<Result<PostRef, XrpcError>> =
        task {
            match! uploadAllBlobs agent (images |> List.map (fun i -> (i.Data, i.MimeType, i.AltText))) with
            | Error e -> return Error e
            | Ok blobRefs ->
                let! facets = RichText.parse agent text
                let embed =
                    {| ``$type`` = "app.bsky.embed.images"
                       images = blobRefs |> List.map (fun (blobRef, alt) ->
                        {| alt = alt; image = blobRef.Json |}) |}
                let record =
                    {| ``$type`` = AppBskyFeed.Post.TypeId
                       text = text
                       createdAt = nowTimestamp ()
                       facets = if facets.IsEmpty then null else facets |> box
                       embed = embed |}
                let! result = createRecord agent "app.bsky.feed.post" record
                return result |> Result.map toPostRef
        }
