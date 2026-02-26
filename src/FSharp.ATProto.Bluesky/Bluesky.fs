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
/// Accepted by <c>like</c>, <c>repost</c>, and <c>replyTo</c>.
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
/// Supported image MIME types for blob upload.
/// Use the named cases for common image types, or <c>Custom</c> for other MIME types.
/// </summary>
type ImageMime =
    | Png
    | Jpeg
    | Gif
    | Webp
    | Custom of string

/// <summary>
/// Functions for working with <see cref="ImageMime"/> values.
/// </summary>
module ImageMime =
    /// <summary>
    /// Convert an <see cref="ImageMime"/> to its MIME type string representation.
    /// </summary>
    let toMimeString =
        function
        | Png -> "image/png"
        | Jpeg -> "image/jpeg"
        | Gif -> "image/gif"
        | Webp -> "image/webp"
        | Custom s -> s

/// <summary>
/// Image data for upload with a post.
/// </summary>
type ImageUpload =
    { /// <summary>The raw binary image data.</summary>
      Data: byte[]
      /// <summary>The MIME type for the image.</summary>
      MimeType: ImageMime
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
/// Result of an undo operation. <c>Undone</c> means the record was deleted;
/// <c>WasNotPresent</c> means there was nothing to undo (e.g., the post was not liked).
/// </summary>
type UndoResult =
    | Undone
    | WasNotPresent

/// <summary>
/// Witness type enabling SRTP-based overloading for undo operations.
/// Allows the generic <c>Bluesky.undo</c> function to accept any ref type
/// (<see cref="LikeRef"/>, <see cref="RepostRef"/>, <see cref="FollowRef"/>, <see cref="BlockRef"/>).
/// This type is an implementation detail and should not be used directly.
/// </summary>
[<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
type UndoWitness =
    | UndoWitness
    static member UndoUri(UndoWitness, r: LikeRef) = r.Uri
    static member UndoUri(UndoWitness, r: RepostRef) = r.Uri
    static member UndoUri(UndoWitness, r: FollowRef) = r.Uri
    static member UndoUri(UndoWitness, r: BlockRef) = r.Uri

/// <summary>
/// Witness type enabling SRTP-based overloading for actor parameters.
/// Allows functions like <c>getProfile</c> to accept <see cref="Handle"/>, <see cref="Did"/>, or <c>string</c> directly.
/// This type is an implementation detail and should not be used directly.
/// </summary>
[<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
type ActorWitness =
    | ActorWitness
    static member ToActorString(ActorWitness, h: Handle) = Handle.value h
    static member ToActorString(ActorWitness, d: Did) = Did.value d
    static member ToActorString(ActorWitness, s: string) = s

/// <summary>
/// High-level convenience methods for common Bluesky operations:
/// posting, replying, liking, reposting, following, blocking, uploading blobs, and deleting records.
/// All methods require an authenticated <see cref="AtpAgent"/>.
/// </summary>
module Bluesky =

    let inline internal toActorString (x: ^a) : string =
        ((^a or ActorWitness) : (static member ToActorString : ActorWitness * ^a -> string) (ActorWitness, x))

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
    /// Create an agent and authenticate in one step.
    /// This is the simplest way to get started with the Bluesky API.
    /// </summary>
    /// <param name="baseUrl">The PDS base URL (e.g. <c>"https://bsky.social"</c>).</param>
    /// <param name="identifier">A handle (e.g. <c>"alice.bsky.social"</c>) or DID.</param>
    /// <param name="password">An app password (not the account password).</param>
    /// <returns>An authenticated <see cref="AtpAgent"/> on success, or an <see cref="XrpcError"/>.</returns>
    /// <example>
    /// <code>
    /// let! agent = Bluesky.login "https://bsky.social" "alice.bsky.social" "app-password"
    /// </code>
    /// </example>
    let login (baseUrl: string) (identifier: string) (password: string) : Task<Result<AtpAgent, XrpcError>> =
        task {
            let agent = AtpAgent.create baseUrl
            let! result = AtpAgent.login identifier password agent
            return result |> Result.map (fun _ -> agent)
        }

    /// <summary>
    /// Create an agent with a custom <see cref="System.Net.Http.HttpClient"/> and authenticate.
    /// Useful for testing with mock HTTP handlers or custom client configuration.
    /// </summary>
    /// <param name="client">The HTTP client to use for all requests.</param>
    /// <param name="baseUrl">The PDS base URL (e.g. <c>"https://bsky.social"</c>).</param>
    /// <param name="identifier">A handle (e.g. <c>"alice.bsky.social"</c>) or DID.</param>
    /// <param name="password">An app password (not the account password).</param>
    /// <returns>An authenticated <see cref="AtpAgent"/> on success, or an <see cref="XrpcError"/>.</returns>
    let loginWithClient
        (client: HttpClient)
        (baseUrl: string)
        (identifier: string)
        (password: string)
        : Task<Result<AtpAgent, XrpcError>> =
        task {
            let agent = AtpAgent.createWithClient client baseUrl
            let! result = AtpAgent.login identifier password agent
            return result |> Result.map (fun _ -> agent)
        }

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
    /// Create a reply with explicit parent and root references.
    /// Use this when you already know both the parent and root <see cref="PostRef"/>s.
    /// For most cases, prefer <see cref="replyTo"/> which resolves the root automatically.
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
    let replyWithKnownRoot (agent: AtpAgent) (text: string) (parent: PostRef) (root: PostRef)
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
    /// Reply to a post. Fetches the parent to auto-resolve the thread root.
    /// This is the recommended way to reply: you only need the parent post's <see cref="PostRef"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="text">The reply text. Mentions, links, and hashtags are auto-detected.</param>
    /// <param name="parentRef">A <see cref="PostRef"/> for the post being replied to.</param>
    /// <returns>A <see cref="PostRef"/> with the AT-URI and CID on success, or an <see cref="XrpcError"/>.</returns>
    /// <remarks>
    /// Fetches the parent post via <c>app.bsky.feed.getPosts</c> to determine the thread root.
    /// If the parent has a <c>reply</c> field, its root is used. Otherwise, the parent itself is the root.
    /// For full control over both parent and root, use <see cref="replyWithKnownRoot"/> instead.
    /// </remarks>
    let replyTo (agent: AtpAgent) (text: string) (parentRef: PostRef) : Task<Result<PostRef, XrpcError>> =
        task {
            let! postsResult = AppBskyFeed.GetPosts.query agent { Uris = [ parentRef.Uri ] }

            match postsResult with
            | Error e -> return Error e
            | Ok posts ->
                match posts.Posts with
                | [] -> return Error(toXrpcError "Parent post not found")
                | parentPost :: _ ->
                    // Check if parent has a reply field -> extract root
                    match parentPost.Record.TryGetProperty("reply") with
                    | true, replyProp ->
                        try
                            let rootProp = replyProp.GetProperty("root")
                            let rootUri = rootProp.GetProperty("uri").GetString()
                            let rootCid = rootProp.GetProperty("cid").GetString()

                            match AtUri.parse rootUri, Cid.parse rootCid with
                            | Ok uri, Ok cid ->
                                let root = { PostRef.Uri = uri; Cid = cid }
                                return! replyWithKnownRoot agent text parentRef root
                            | Error msg, _ ->
                                return Error(toXrpcError (sprintf "Invalid root AT-URI: %s" msg))
                            | _, Error msg ->
                                return Error(toXrpcError (sprintf "Invalid root CID: %s" msg))
                        with ex ->
                            return Error(toXrpcError (sprintf "Malformed reply field: %s" ex.Message))
                    | false, _ ->
                        // No reply field -> top-level post, use as both parent and root
                        return! replyWithKnownRoot agent text parentRef parentRef
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
    /// Resolve a string identifier (DID or handle) to a <see cref="Did"/>.
    /// If the string starts with <c>did:</c>, it is parsed directly.
    /// Otherwise, it is treated as a handle and resolved via <c>com.atproto.identity.resolveHandle</c>.
    /// </summary>
    let private resolveIdentifier (agent: AtpAgent) (identifier: string)
        : Task<Result<Did, XrpcError>> =
        task {
            if identifier.StartsWith("did:") then
                match Did.parse identifier with
                | Ok did -> return Ok did
                | Error msg -> return Error (toXrpcError (sprintf "Invalid DID: %s" msg))
            else
                match Handle.parse identifier with
                | Error msg -> return Error (toXrpcError (sprintf "Invalid handle: %s" msg))
                | Ok handle ->
                    let! result = ComAtprotoIdentity.ResolveHandle.query agent { Handle = handle }
                    return result |> Result.map (fun output -> output.Did)
        }

    /// <summary>
    /// Follow a user by DID string or handle. If the identifier starts with <c>did:</c>,
    /// it is parsed as a DID directly. Otherwise, the handle is resolved to a DID first.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="identifier">A DID string (e.g., <c>did:plc:abc123</c>) or handle (e.g., <c>alice.bsky.social</c>).</param>
    /// <returns>A <see cref="FollowRef"/> on success, or an <see cref="XrpcError"/>. Pass the <c>FollowRef</c> to <see cref="unfollow"/> to undo.</returns>
    /// <remarks>
    /// For type-safe usage when you already have a <see cref="Did"/>, use <see cref="follow"/> instead.
    /// </remarks>
    let followUser (agent: AtpAgent) (identifier: string)
        : Task<Result<FollowRef, XrpcError>> =
        task {
            match! resolveIdentifier agent identifier with
            | Error e -> return Error e
            | Ok did -> return! follow agent did
        }

    /// <summary>
    /// Block a user by DID string or handle. If the identifier starts with <c>did:</c>,
    /// it is parsed as a DID directly. Otherwise, the handle is resolved to a DID first.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="identifier">A DID string (e.g., <c>did:plc:abc123</c>) or handle (e.g., <c>alice.bsky.social</c>).</param>
    /// <returns>A <see cref="BlockRef"/> on success, or an <see cref="XrpcError"/>. Pass the <c>BlockRef</c> to <see cref="unblock"/> to undo.</returns>
    /// <remarks>
    /// For type-safe usage when you already have a <see cref="Did"/>, use <see cref="block"/> instead.
    /// </remarks>
    let blockUser (agent: AtpAgent) (identifier: string)
        : Task<Result<BlockRef, XrpcError>> =
        task {
            match! resolveIdentifier agent identifier with
            | Error e -> return Error e
            | Ok did -> return! block agent did
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

    // ── Typed undo functions (returning UndoResult) ────────────────────

    /// <summary>
    /// Undo a like by deleting the like record. Returns <see cref="UndoResult.Undone"/> on success.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="likeRef">The <see cref="LikeRef"/> returned by <see cref="like"/>.</param>
    /// <returns><c>Ok Undone</c> on success, or an <see cref="XrpcError"/>.</returns>
    let undoLike (agent: AtpAgent) (likeRef: LikeRef) : Task<Result<UndoResult, XrpcError>> =
        task {
            let! result = deleteRecord agent likeRef.Uri
            return result |> Result.map (fun () -> Undone)
        }

    /// <summary>
    /// Undo a repost by deleting the repost record. Returns <see cref="UndoResult.Undone"/> on success.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="repostRef">The <see cref="RepostRef"/> returned by <see cref="repost"/>.</param>
    /// <returns><c>Ok Undone</c> on success, or an <see cref="XrpcError"/>.</returns>
    let undoRepost (agent: AtpAgent) (repostRef: RepostRef) : Task<Result<UndoResult, XrpcError>> =
        task {
            let! result = deleteRecord agent repostRef.Uri
            return result |> Result.map (fun () -> Undone)
        }

    /// <summary>
    /// Undo a follow by deleting the follow record. Returns <see cref="UndoResult.Undone"/> on success.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="followRef">The <see cref="FollowRef"/> returned by <see cref="follow"/>.</param>
    /// <returns><c>Ok Undone</c> on success, or an <see cref="XrpcError"/>.</returns>
    let undoFollow (agent: AtpAgent) (followRef: FollowRef) : Task<Result<UndoResult, XrpcError>> =
        task {
            let! result = deleteRecord agent followRef.Uri
            return result |> Result.map (fun () -> Undone)
        }

    /// <summary>
    /// Undo a block by deleting the block record. Returns <see cref="UndoResult.Undone"/> on success.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="blockRef">The <see cref="BlockRef"/> returned by <see cref="block"/>.</param>
    /// <returns><c>Ok Undone</c> on success, or an <see cref="XrpcError"/>.</returns>
    let undoBlock (agent: AtpAgent) (blockRef: BlockRef) : Task<Result<UndoResult, XrpcError>> =
        task {
            let! result = deleteRecord agent blockRef.Uri
            return result |> Result.map (fun () -> Undone)
        }

    /// <summary>
    /// Generic undo: delete any ref type (<see cref="LikeRef"/>, <see cref="RepostRef"/>,
    /// <see cref="FollowRef"/>, or <see cref="BlockRef"/>). Dispatches to the correct typed
    /// undo function via SRTP.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="ref">Any ref type with an AT-URI (LikeRef, RepostRef, FollowRef, or BlockRef).</param>
    /// <returns><c>Ok Undone</c> on success, or an <see cref="XrpcError"/>.</returns>
    /// <example>
    /// <code>
    /// let! likeRef = Bluesky.like agent postRef
    /// let! result = Bluesky.undo agent likeRef  // works with any ref type
    /// </code>
    /// </example>
    let inline undo (agent: AtpAgent) (ref: ^a) : Task<Result<UndoResult, XrpcError>> =
        let uri = ((^a or UndoWitness) : (static member UndoUri : UndoWitness * ^a -> AtUri) (UndoWitness, ref))
        task {
            let! result = deleteRecord agent uri
            return result |> Result.map (fun () -> Undone)
        }

    // ── Target-based undo ──────────────────────────────────────────────

    /// <summary>
    /// Unlike a post by its <see cref="PostRef"/>, without needing the original <see cref="LikeRef"/>.
    /// Fetches the post to find the current user's like URI from the viewer state,
    /// then deletes it.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="postRef">The post to unlike.</param>
    /// <returns>
    /// <c>Ok Undone</c> if the like was found and deleted,
    /// <c>Ok WasNotPresent</c> if the post was not liked by the current user,
    /// or an <see cref="XrpcError"/> on failure.
    /// </returns>
    let unlikePost (agent: AtpAgent) (postRef: PostRef) : Task<Result<UndoResult, XrpcError>> =
        task {
            let! postsResult = AppBskyFeed.GetPosts.query agent { Uris = [ postRef.Uri ] }
            match postsResult with
            | Error e -> return Error e
            | Ok posts ->
                match posts.Posts with
                | [] -> return Ok WasNotPresent
                | post :: _ ->
                    match post.Viewer |> Option.bind (fun v -> v.Like) with
                    | Some likeUri ->
                        let! result = deleteRecord agent likeUri
                        return result |> Result.map (fun () -> Undone)
                    | None -> return Ok WasNotPresent
        }

    /// <summary>
    /// Un-repost a post by its <see cref="PostRef"/>, without needing the original <see cref="RepostRef"/>.
    /// Fetches the post to find the current user's repost URI from the viewer state,
    /// then deletes it.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="postRef">The post to un-repost.</param>
    /// <returns>
    /// <c>Ok Undone</c> if the repost was found and deleted,
    /// <c>Ok WasNotPresent</c> if the post was not reposted by the current user,
    /// or an <see cref="XrpcError"/> on failure.
    /// </returns>
    let unrepostPost (agent: AtpAgent) (postRef: PostRef) : Task<Result<UndoResult, XrpcError>> =
        task {
            let! postsResult = AppBskyFeed.GetPosts.query agent { Uris = [ postRef.Uri ] }
            match postsResult with
            | Error e -> return Error e
            | Ok posts ->
                match posts.Posts with
                | [] -> return Ok WasNotPresent
                | post :: _ ->
                    match post.Viewer |> Option.bind (fun v -> v.Repost) with
                    | Some repostUri ->
                        let! result = deleteRecord agent repostUri
                        return result |> Result.map (fun () -> Undone)
                    | None -> return Ok WasNotPresent
        }

    /// <summary>
    /// Upload a blob (image, video, or other binary data) to the PDS.
    /// Returns a typed <see cref="BlobRef"/> containing the blob reference needed to embed the blob in a record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="data">The raw binary content of the blob.</param>
    /// <param name="mimeType">The MIME type of the blob (e.g., <see cref="ImageMime.Jpeg"/>, <see cref="ImageMime.Png"/>).</param>
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
    let uploadBlob (agent: AtpAgent) (data: byte[]) (mimeType: ImageMime)
        : Task<Result<BlobRef, XrpcError>> =
        task {
            let url = System.Uri(agent.BaseUrl, sprintf "xrpc/%s" ComAtprotoRepo.UploadBlob.TypeId)
            let request = new HttpRequestMessage(HttpMethod.Post, url)
            request.Content <- new ByteArrayContent(data)
            request.Content.Headers.ContentType <- MediaTypeHeaderValue(ImageMime.toMimeString mimeType)
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

    let private uploadAllBlobs (agent: AtpAgent) (images: (byte[] * ImageMime * string) list)
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
    ///     [ { Data = imageBytes; MimeType = Jpeg; AltText = "A photo" } ]
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

    // ── Read convenience methods ────────────────────────────────────────

    /// <summary>
    /// Get a user's profile. Accepts a <see cref="Handle"/>, <see cref="Did"/>, or plain <c>string</c>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">A <see cref="Handle"/>, <see cref="Did"/>, or string identifier.</param>
    /// <returns>The profile view on success, or an <see cref="XrpcError"/>.</returns>
    let inline getProfile (agent: AtpAgent) (actor: ^a)
        : Task<Result<AppBskyActor.GetProfile.Output, XrpcError>> =
        let actorStr = toActorString actor
        AppBskyActor.GetProfile.query agent { Actor = actorStr }

    /// <summary>
    /// Get the authenticated user's home timeline.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="limit">Maximum number of posts to return (optional, pass <c>None</c> for server default).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional, pass <c>None</c> to start from the beginning).</param>
    /// <returns>A feed of posts with an optional cursor for pagination, or an <see cref="XrpcError"/>.</returns>
    let getTimeline (agent: AtpAgent) (limit: int64 option) (cursor: string option)
        : Task<Result<AppBskyFeed.GetTimeline.Output, XrpcError>> =
        AppBskyFeed.GetTimeline.query agent
            { Algorithm = None
              Cursor = cursor
              Limit = limit }

    /// <summary>
    /// Get a post thread by its AT-URI, including parent and reply context.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="uri">The AT-URI of the post (e.g., <c>at://did:plc:.../app.bsky.feed.post/...</c>).</param>
    /// <param name="depth">How many levels of replies to include (optional, pass <c>None</c> for server default).</param>
    /// <param name="parentHeight">How many levels of parent context to include (optional, pass <c>None</c> for server default).</param>
    /// <returns>The thread view on success, or an <see cref="XrpcError"/>.</returns>
    let getPostThread (agent: AtpAgent) (uri: AtUri) (depth: int64 option) (parentHeight: int64 option)
        : Task<Result<AppBskyFeed.GetPostThread.Output, XrpcError>> =
        AppBskyFeed.GetPostThread.query agent
            { Depth = depth
              ParentHeight = parentHeight
              Uri = uri }

    /// <summary>
    /// List notifications for the authenticated user.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="limit">Maximum number of notifications to return (optional, pass <c>None</c> for server default).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional, pass <c>None</c> to start from the beginning).</param>
    /// <returns>A list of notifications with an optional cursor for pagination, or an <see cref="XrpcError"/>.</returns>
    let getNotifications (agent: AtpAgent) (limit: int64 option) (cursor: string option)
        : Task<Result<AppBskyNotification.ListNotifications.Output, XrpcError>> =
        AppBskyNotification.ListNotifications.query agent
            { Cursor = cursor
              Limit = limit
              Priority = None
              Reasons = None
              SeenAt = None }
