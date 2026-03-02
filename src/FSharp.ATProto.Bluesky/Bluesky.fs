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
    {
        /// <summary>The AT-URI of the post record.</summary>
        Uri : AtUri
        /// <summary>The CID (content identifier) of the post record version.</summary>
        Cid : Cid
    }

/// <summary>
/// A reference to a like record, returned by <c>Bluesky.like</c>.
/// Pass to <c>Bluesky.unlike</c> to undo.
/// </summary>
type LikeRef =
    {
        /// <summary>The AT-URI of the like record.</summary>
        Uri : AtUri
    }

/// <summary>
/// A reference to a repost record, returned by <c>Bluesky.repost</c>.
/// Pass to <c>Bluesky.unrepost</c> to undo.
/// </summary>
type RepostRef =
    {
        /// <summary>The AT-URI of the repost record.</summary>
        Uri : AtUri
    }

/// <summary>
/// A reference to a follow record, returned by <c>Bluesky.follow</c>.
/// Pass to <c>Bluesky.unfollow</c> to undo.
/// </summary>
type FollowRef =
    {
        /// <summary>The AT-URI of the follow record.</summary>
        Uri : AtUri
    }

/// <summary>
/// A reference to a block record, returned by <c>Bluesky.block</c>.
/// Pass to <c>Bluesky.unblock</c> to undo.
/// </summary>
type BlockRef =
    {
        /// <summary>The AT-URI of the block record.</summary>
        Uri : AtUri
    }

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
    {
        /// <summary>The raw binary image data.</summary>
        Data : byte[]
        /// <summary>The MIME type for the image.</summary>
        MimeType : ImageMime
        /// <summary>Alt text describing the image for accessibility.</summary>
        AltText : string
    }

/// <summary>
/// A reference to an uploaded blob, as returned by <c>com.atproto.repo.uploadBlob</c>.
/// Contains both the raw JSON (for passing back to the API in embeds) and typed convenience fields.
/// </summary>
type BlobRef =
    {
        /// <summary>The raw JSON element for the blob object. Pass this directly in embed records.</summary>
        Json : JsonElement
        /// <summary>The content-addressed link (CID) of the blob.</summary>
        Ref : Cid
        /// <summary>The MIME type of the blob (e.g., <c>image/jpeg</c>).</summary>
        MimeType : string
        /// <summary>The size of the blob in bytes.</summary>
        Size : int64
    }

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

    static member UndoUri (UndoWitness, r : LikeRef) = r.Uri
    static member UndoUri (UndoWitness, r : RepostRef) = r.Uri
    static member UndoUri (UndoWitness, r : FollowRef) = r.Uri
    static member UndoUri (UndoWitness, r : BlockRef) = r.Uri

/// <summary>
/// Witness type enabling SRTP-based overloading for actor parameters.
/// Allows functions like <c>getProfile</c> to accept <see cref="Handle"/>, <see cref="Did"/>, or <c>string</c> directly.
/// This type is an implementation detail and should not be used directly.
/// </summary>
[<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
type ActorWitness =
    | ActorWitness

    static member ToActorString (ActorWitness, h : Handle) = Handle.value h
    static member ToActorString (ActorWitness, d : Did) = Did.value d
    static member ToActorString (ActorWitness, s : string) = s

/// <summary>
/// Type alias for the thread union returned by <c>getPostThread</c>.
/// Simplifies pattern matching when working with thread responses.
/// </summary>
type ThreadResult = AppBskyFeed.GetPostThread.OutputThreadUnion

// ── Read domain types ──────────────────────────────────────────────

/// <summary>A paginated result containing a list of items and an optional cursor for the next page.</summary>
type Page<'T> = { Items : 'T list; Cursor : string option }

/// <summary>
/// A lightweight profile summary used in feeds, notifications, and conversations.
/// Maps from <c>ProfileViewBasic</c> / <c>ProfileView</c>.
/// </summary>
type ProfileSummary =
    { Did : Did
      Handle : Handle
      DisplayName : string
      Avatar : string option }

module ProfileSummary =

    let internal toDateTimeOffset (dt : AtDateTime) =
        System.DateTimeOffset.Parse(
            AtDateTime.value dt,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind
        )

    let ofBasic (p : AppBskyActor.Defs.ProfileViewBasic) : ProfileSummary =
        { Did = p.Did
          Handle = p.Handle
          DisplayName = p.DisplayName |> Option.defaultValue ""
          Avatar = p.Avatar |> Option.map string }

    let ofView (p : AppBskyActor.Defs.ProfileView) : ProfileSummary =
        { Did = p.Did
          Handle = p.Handle
          DisplayName = p.DisplayName |> Option.defaultValue ""
          Avatar = p.Avatar |> Option.map string }

    let ofChatBasic (p : ChatBskyActor.Defs.ProfileViewBasic) : ProfileSummary =
        { Did = p.Did
          Handle = p.Handle
          DisplayName = p.DisplayName |> Option.defaultValue ""
          Avatar = p.Avatar |> Option.map string }

/// <summary>
/// A full user profile with engagement counts and relationship state.
/// Maps from <c>ProfileViewDetailed</c>.
/// </summary>
type Profile =
    { Did : Did
      Handle : Handle
      DisplayName : string
      Description : string
      Avatar : string option
      Banner : string option
      PostsCount : int64
      FollowersCount : int64
      FollowsCount : int64
      IsFollowing : bool
      IsFollowedBy : bool
      IsBlocking : bool
      IsBlockedBy : bool
      IsMuted : bool }

module Profile =

    let ofDetailed (p : AppBskyActor.Defs.ProfileViewDetailed) : Profile =
        let viewer = p.Viewer

        { Did = p.Did
          Handle = p.Handle
          DisplayName = p.DisplayName |> Option.defaultValue ""
          Description = p.Description |> Option.defaultValue ""
          Avatar = p.Avatar |> Option.map string
          Banner = p.Banner |> Option.map string
          PostsCount = p.PostsCount |> Option.defaultValue 0L
          FollowersCount = p.FollowersCount |> Option.defaultValue 0L
          FollowsCount = p.FollowsCount |> Option.defaultValue 0L
          IsFollowing = viewer |> Option.bind (fun v -> v.Following) |> Option.isSome
          IsFollowedBy = viewer |> Option.bind (fun v -> v.FollowedBy) |> Option.isSome
          IsBlocking = viewer |> Option.bind (fun v -> v.Blocking) |> Option.isSome
          IsBlockedBy =
              viewer
              |> Option.bind (fun v -> v.BlockedBy)
              |> Option.defaultValue false
          IsMuted =
              viewer
              |> Option.bind (fun v -> v.Muted)
              |> Option.defaultValue false }

/// <summary>
/// A post with engagement counts and viewer state, used in feeds and timelines.
/// Maps from <c>PostView</c>.
/// </summary>
type TimelinePost =
    { Uri : AtUri
      Cid : Cid
      Author : ProfileSummary
      Text : string
      Facets : AppBskyRichtext.Facet.Facet list
      LikeCount : int64
      RepostCount : int64
      ReplyCount : int64
      QuoteCount : int64
      IndexedAt : DateTimeOffset
      IsLiked : bool
      IsReposted : bool
      IsBookmarked : bool }

module TimelinePost =

    let ofPostView (pv : AppBskyFeed.Defs.PostView) : TimelinePost =
        let viewer = pv.Viewer

        { Uri = pv.Uri
          Cid = pv.Cid
          Author = ProfileSummary.ofBasic pv.Author
          Text = pv.Text
          Facets = pv.Facets
          LikeCount = pv.LikeCount |> Option.defaultValue 0L
          RepostCount = pv.RepostCount |> Option.defaultValue 0L
          ReplyCount = pv.ReplyCount |> Option.defaultValue 0L
          QuoteCount = pv.QuoteCount |> Option.defaultValue 0L
          IndexedAt = ProfileSummary.toDateTimeOffset pv.IndexedAt
          IsLiked = viewer |> Option.bind (fun v -> v.Like) |> Option.isSome
          IsReposted = viewer |> Option.bind (fun v -> v.Repost) |> Option.isSome
          IsBookmarked =
              viewer
              |> Option.bind (fun v -> v.Bookmarked)
              |> Option.defaultValue false }

/// <summary>Reason a post appeared in a feed.</summary>
type FeedReason =
    | Repost of by : ProfileSummary
    | Pin

/// <summary>A single item in a feed or timeline.</summary>
type FeedItem =
    { Post : TimelinePost
      Reason : FeedReason option }

module FeedItem =

    let ofFeedViewPost (fvp : AppBskyFeed.Defs.FeedViewPost) : FeedItem =
        let reason =
            fvp.Reason
            |> Option.bind (fun r ->
                match r with
                | AppBskyFeed.Defs.FeedViewPostReasonUnion.ReasonRepost rr ->
                    Some(FeedReason.Repost(ProfileSummary.ofBasic rr.By))
                | AppBskyFeed.Defs.FeedViewPostReasonUnion.ReasonPin _ -> Some FeedReason.Pin
                | _ -> None)

        { Post = TimelinePost.ofPostView fvp.Post
          Reason = reason }

/// <summary>The kind of notification received.</summary>
[<RequireQualifiedAccess>]
type NotificationKind =
    | Like
    | Repost
    | Follow
    | Mention
    | Reply
    | Quote
    | StarterpackJoined
    | Unknown of string

module NotificationKind =

    let ofReason (r : AppBskyNotification.ListNotifications.NotificationReason) : NotificationKind =
        match r with
        | AppBskyNotification.ListNotifications.NotificationReason.Like -> NotificationKind.Like
        | AppBskyNotification.ListNotifications.NotificationReason.Repost -> NotificationKind.Repost
        | AppBskyNotification.ListNotifications.NotificationReason.Follow -> NotificationKind.Follow
        | AppBskyNotification.ListNotifications.NotificationReason.Mention -> NotificationKind.Mention
        | AppBskyNotification.ListNotifications.NotificationReason.Reply -> NotificationKind.Reply
        | AppBskyNotification.ListNotifications.NotificationReason.Quote -> NotificationKind.Quote
        | AppBskyNotification.ListNotifications.NotificationReason.StarterpackJoined ->
            NotificationKind.StarterpackJoined
        | AppBskyNotification.ListNotifications.NotificationReason.Unknown s -> NotificationKind.Unknown s
        | other -> NotificationKind.Unknown(string other)

/// <summary>A notification from the user's notification feed.</summary>
type Notification =
    { Kind : NotificationKind
      Author : ProfileSummary
      SubjectUri : AtUri option
      IsRead : bool
      IndexedAt : DateTimeOffset }

module Notification =

    let ofRaw (n : AppBskyNotification.ListNotifications.Notification) : Notification =
        { Kind = NotificationKind.ofReason n.Reason
          Author = ProfileSummary.ofView n.Author
          SubjectUri = n.ReasonSubject
          IsRead = n.IsRead
          IndexedAt = ProfileSummary.toDateTimeOffset n.IndexedAt }

/// <summary>A message in a chat conversation.</summary>
[<RequireQualifiedAccess>]
type ChatMessage =
    | Message of
        {| Id : string
           Text : string
           Sender : Did
           SentAt : DateTimeOffset |}
    | Deleted of {| Id : string; Sender : Did |}

module ChatMessage =

    let ofMessagesItem (item : ChatBskyConvo.GetMessages.OutputMessagesItem) : ChatMessage option =
        match item with
        | ChatBskyConvo.GetMessages.OutputMessagesItem.MessageView mv ->
            Some(
                ChatMessage.Message
                    {| Id = mv.Id
                       Text = mv.Text
                       Sender = mv.Sender.Did
                       SentAt = ProfileSummary.toDateTimeOffset mv.SentAt |}
            )
        | ChatBskyConvo.GetMessages.OutputMessagesItem.DeletedMessageView dv ->
            Some(ChatMessage.Deleted {| Id = dv.Id; Sender = dv.Sender.Did |})
        | _ -> None

/// <summary>A summary of a chat conversation.</summary>
type ConvoSummary =
    { Id : string
      Members : ProfileSummary list
      LastMessageText : string option
      UnreadCount : int64
      IsMuted : bool }

module ConvoSummary =

    let ofConvoView (cv : ChatBskyConvo.Defs.ConvoView) : ConvoSummary =
        let lastText =
            cv.LastMessage
            |> Option.bind (fun lm ->
                match lm with
                | ChatBskyConvo.Defs.ConvoViewLastMessageUnion.MessageView mv -> Some mv.Text
                | _ -> None)

        { Id = cv.Id
          Members = cv.Members |> List.map ProfileSummary.ofChatBasic
          LastMessageText = lastText
          UnreadCount = cv.UnreadCount
          IsMuted = cv.Muted }

/// <summary>A node in a post thread tree.</summary>
[<RequireQualifiedAccess>]
type ThreadNode =
    | Post of ThreadPost
    | NotFound of AtUri
    | Blocked of AtUri

/// <summary>A post within a thread, with parent and reply context.</summary>
and ThreadPost =
    { Post : TimelinePost
      Parent : ThreadNode option
      Replies : ThreadNode list }

module ThreadNode =

    let rec ofParentUnion (u : AppBskyFeed.Defs.ThreadViewPostParentUnion) : ThreadNode =
        match u with
        | AppBskyFeed.Defs.ThreadViewPostParentUnion.ThreadViewPost tvp ->
            ThreadNode.Post(ofThreadViewPost tvp)
        | AppBskyFeed.Defs.ThreadViewPostParentUnion.NotFoundPost nfp -> ThreadNode.NotFound nfp.Uri
        | AppBskyFeed.Defs.ThreadViewPostParentUnion.BlockedPost bp -> ThreadNode.Blocked bp.Uri
        | _ -> ThreadNode.NotFound(AtUri.parse "at://unknown/unknown/unknown" |> Result.defaultWith failwith)

    and ofThreadViewPost (tvp : AppBskyFeed.Defs.ThreadViewPost) : ThreadPost =
        { Post = TimelinePost.ofPostView tvp.Post
          Parent = tvp.Parent |> Option.map ofParentUnion
          Replies =
              tvp.Replies
              |> Option.defaultValue []
              |> List.choose (fun r ->
                  match r with
                  | AppBskyFeed.Defs.ThreadViewPostParentUnion.ThreadViewPost tvp ->
                      Some(ThreadNode.Post(ofThreadViewPost tvp))
                  | AppBskyFeed.Defs.ThreadViewPostParentUnion.NotFoundPost nfp ->
                      Some(ThreadNode.NotFound nfp.Uri)
                  | AppBskyFeed.Defs.ThreadViewPostParentUnion.BlockedPost bp ->
                      Some(ThreadNode.Blocked bp.Uri)
                  | _ -> None) }

    let ofOutputThreadUnion (u : AppBskyFeed.GetPostThread.OutputThreadUnion) : ThreadNode =
        match u with
        | AppBskyFeed.GetPostThread.OutputThreadUnion.ThreadViewPost tvp ->
            ThreadNode.Post(ofThreadViewPost tvp)
        | AppBskyFeed.GetPostThread.OutputThreadUnion.NotFoundPost nfp -> ThreadNode.NotFound nfp.Uri
        | AppBskyFeed.GetPostThread.OutputThreadUnion.BlockedPost bp -> ThreadNode.Blocked bp.Uri
        | _ -> ThreadNode.NotFound(AtUri.parse "at://unknown/unknown/unknown" |> Result.defaultWith failwith)

/// <summary>The subject of a content report.</summary>
[<RequireQualifiedAccess>]
type ReportSubject =
    | Account of Did
    | Record of PostRef

/// <summary>
/// High-level convenience methods for common Bluesky operations:
/// posting, replying, liking, reposting, following, blocking, uploading blobs, and deleting records.
/// All methods require an authenticated <see cref="AtpAgent"/>.
/// </summary>
module Bluesky =

    let inline internal toActorString (x : ^a) : string =
        ((^a or ActorWitness) : (static member ToActorString : ActorWitness * ^a -> string) (ActorWitness, x))

    let private nowTimestamp () = DateTimeOffset.UtcNow.ToString ("o")

    let private notLoggedInError : XrpcError =
        { StatusCode = 401
          Error = Some "NotLoggedIn"
          Message = Some "No active session" }

    let private sessionDid (agent : AtpAgent) : Result<Did, XrpcError> =
        match agent.Session with
        | Some s -> Ok s.Did
        | None -> Error notLoggedInError

    let private toXrpcError (msg : string) : XrpcError =
        { StatusCode = 400
          Error = Some "InvalidRequest"
          Message = Some msg }

    let private parseBlobRef (blob : JsonElement) : Result<BlobRef, XrpcError> =
        try
            let link = blob.GetProperty("ref").GetProperty("$link").GetString ()
            let mimeType = blob.GetProperty("mimeType").GetString ()
            let size = blob.GetProperty("size").GetInt64 ()

            match Cid.parse link with
            | Ok cid ->
                Ok
                    { Json = blob
                      Ref = cid
                      MimeType = mimeType
                      Size = size }
            | Error msg -> Error (toXrpcError (sprintf "Invalid blob ref CID: %s" msg))
        with ex ->
            Error (toXrpcError (sprintf "Failed to parse blob reference: %s" ex.Message))

    let private createRecord
        (agent : AtpAgent)
        (collection : string)
        (record : obj)
        : Task<Result<ComAtprotoRepo.CreateRecord.Output, XrpcError>> =
        match sessionDid agent with
        | Error e -> Task.FromResult (Error e)
        | Ok did ->
            match Nsid.parse collection with
            | Error msg -> Task.FromResult (Error (toXrpcError (sprintf "Invalid NSID: %s" msg)))
            | Ok nsid ->
                let recordElement = JsonSerializer.SerializeToElement (record, Json.options)

                ComAtprotoRepo.CreateRecord.call
                    agent
                    { Repo = Did.value did
                      Collection = nsid
                      Record = recordElement
                      Rkey = None
                      SwapCommit = None
                      Validate = None }

    let private toPostRef (output : ComAtprotoRepo.CreateRecord.Output) : PostRef =
        { Uri = output.Uri; Cid = output.Cid }

    /// <summary>
    /// Create an agent and authenticate in one step.
    /// This is the simplest way to get started with the Bluesky API.
    /// </summary>
    /// <param name="baseUrl">The PDS base URL (e.g. <c>"https://bsky.social"</c>).</param>
    /// <param name="identifier">A handle (e.g. <c>"my-handle.bsky.social"</c>) or DID.</param>
    /// <param name="password">An app password (not the account password).</param>
    /// <returns>An authenticated <see cref="AtpAgent"/> on success, or an <see cref="XrpcError"/>.</returns>
    /// <example>
    /// <code>
    /// let! agent = Bluesky.login "https://bsky.social" "my-handle.bsky.social" "app-password"
    /// </code>
    /// </example>
    let login (baseUrl : string) (identifier : string) (password : string) : Task<Result<AtpAgent, XrpcError>> =
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
    /// <param name="identifier">A handle (e.g. <c>"my-handle.bsky.social"</c>) or DID.</param>
    /// <param name="password">An app password (not the account password).</param>
    /// <returns>An authenticated <see cref="AtpAgent"/> on success, or an <see cref="XrpcError"/>.</returns>
    let loginWithClient
        (client : HttpClient)
        (baseUrl : string)
        (identifier : string)
        (password : string)
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
    let postWithFacets
        (agent : AtpAgent)
        (text : string)
        (facets : AppBskyRichtext.Facet.Facet list)
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
    /// let! result = Bluesky.post agent "Hello @my-handle.bsky.social! Check out https://example.com #atproto"
    /// </code>
    /// </example>
    let post (agent : AtpAgent) (text : string) : Task<Result<PostRef, XrpcError>> =
        task {
            let! facets = RichText.parse agent text
            return! postWithFacets agent text facets
        }

    /// <summary>
    /// Create a quote post. The quoted post appears as an embedded record below your text.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="text">The post text. Mentions, links, and hashtags are auto-detected.</param>
    /// <param name="quotedPost">A <see cref="PostRef"/> identifying the post to quote.</param>
    /// <returns>A <see cref="PostRef"/> with the AT-URI and CID on success, or an <see cref="XrpcError"/>.</returns>
    let quotePost (agent : AtpAgent) (text : string) (quotedPost : PostRef) : Task<Result<PostRef, XrpcError>> =
        task {
            let! facets = RichText.parse agent text

            let embed =
                {| ``$type`` = "app.bsky.embed.record"
                   record =
                    {| uri = AtUri.value quotedPost.Uri
                       cid = Cid.value quotedPost.Cid |} |}

            let record =
                {| ``$type`` = AppBskyFeed.Post.TypeId
                   text = text
                   createdAt = nowTimestamp ()
                   facets = if facets.IsEmpty then null else facets |> box
                   embed = embed |}

            let! result = createRecord agent "app.bsky.feed.post" record
            return result |> Result.map toPostRef
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
    let replyWithKnownRoot
        (agent : AtpAgent)
        (text : string)
        (parent : PostRef)
        (root : PostRef)
        : Task<Result<PostRef, XrpcError>> =
        task {
            let! facets = RichText.parse agent text

            let record =
                {| ``$type`` = AppBskyFeed.Post.TypeId
                   text = text
                   createdAt = nowTimestamp ()
                   facets = if facets.IsEmpty then null else facets |> box
                   reply =
                    {| parent =
                        {| uri = AtUri.value parent.Uri
                           cid = Cid.value parent.Cid |}
                       root =
                        {| uri = AtUri.value root.Uri
                           cid = Cid.value root.Cid |} |} |}

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
    let replyTo (agent : AtpAgent) (text : string) (parentRef : PostRef) : Task<Result<PostRef, XrpcError>> =
        task {
            let! postsResult = AppBskyFeed.GetPosts.query agent { Uris = [ parentRef.Uri ] }

            match postsResult with
            | Error e -> return Error e
            | Ok posts ->
                match posts.Posts with
                | [] -> return Error (toXrpcError "Parent post not found")
                | parentPost :: _ ->
                    // Check if parent has a reply field -> extract root
                    match parentPost.Record.TryGetProperty ("reply") with
                    | true, replyProp ->
                        try
                            let rootProp = replyProp.GetProperty ("root")
                            let rootUri = rootProp.GetProperty("uri").GetString ()
                            let rootCid = rootProp.GetProperty("cid").GetString ()

                            match AtUri.parse rootUri, Cid.parse rootCid with
                            | Ok uri, Ok cid ->
                                let root = { PostRef.Uri = uri; Cid = cid }
                                return! replyWithKnownRoot agent text parentRef root
                            | Error msg, _ -> return Error (toXrpcError (sprintf "Invalid root AT-URI: %s" msg))
                            | _, Error msg -> return Error (toXrpcError (sprintf "Invalid root CID: %s" msg))
                        with ex ->
                            return Error (toXrpcError (sprintf "Malformed reply field: %s" ex.Message))
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
    let like (agent : AtpAgent) (postRef : PostRef) : Task<Result<LikeRef, XrpcError>> =
        task {
            let record =
                {| ``$type`` = AppBskyFeed.Like.TypeId
                   createdAt = nowTimestamp ()
                   subject =
                    {| uri = AtUri.value postRef.Uri
                       cid = Cid.value postRef.Cid |} |}

            let! result = createRecord agent "app.bsky.feed.like" record
            return result |> Result.map (fun o -> { LikeRef.Uri = o.Uri })
        }

    /// <summary>
    /// Repost (retweet) a post or other record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="postRef">A <see cref="PostRef"/> identifying the record to repost.</param>
    /// <returns>A <see cref="RepostRef"/> on success, or an <see cref="XrpcError"/>. Pass the <c>RepostRef</c> to <see cref="unrepost"/> to undo.</returns>
    let repost (agent : AtpAgent) (postRef : PostRef) : Task<Result<RepostRef, XrpcError>> =
        task {
            let record =
                {| ``$type`` = AppBskyFeed.Repost.TypeId
                   createdAt = nowTimestamp ()
                   subject =
                    {| uri = AtUri.value postRef.Uri
                       cid = Cid.value postRef.Cid |} |}

            let! result = createRecord agent "app.bsky.feed.repost" record
            return result |> Result.map (fun o -> { RepostRef.Uri = o.Uri })
        }

    /// <summary>
    /// Follow a user by their DID.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="did">The DID of the user to follow.</param>
    /// <returns>A <see cref="FollowRef"/> on success, or an <see cref="XrpcError"/>. Pass the <c>FollowRef</c> to <see cref="unfollow"/> to undo.</returns>
    let follow (agent : AtpAgent) (did : Did) : Task<Result<FollowRef, XrpcError>> =
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
    let block (agent : AtpAgent) (did : Did) : Task<Result<BlockRef, XrpcError>> =
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
    let private resolveIdentifier (agent : AtpAgent) (identifier : string) : Task<Result<Did, XrpcError>> =
        task {
            if identifier.StartsWith ("did:") then
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
    /// Follow a user by handle string. The handle is resolved to a DID, then the follow is created.
    /// Also accepts a DID string directly (if it starts with <c>did:</c>, it is parsed as a DID).
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="identifier">A handle (e.g., <c>my-handle.bsky.social</c>) or DID string (e.g., <c>did:plc:abc123</c>).</param>
    /// <returns>A <see cref="FollowRef"/> on success, or an <see cref="XrpcError"/>. Pass the <c>FollowRef</c> to <see cref="unfollow"/> to undo.</returns>
    /// <remarks>
    /// For type-safe usage when you already have a <see cref="Did"/>, use <see cref="follow"/> instead.
    /// </remarks>
    let followByHandle (agent : AtpAgent) (identifier : string) : Task<Result<FollowRef, XrpcError>> =
        task {
            match! resolveIdentifier agent identifier with
            | Error e -> return Error e
            | Ok did -> return! follow agent did
        }

    /// <summary>
    /// Block a user by handle string. The handle is resolved to a DID, then the block is created.
    /// Also accepts a DID string directly (if it starts with <c>did:</c>, it is parsed as a DID).
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="identifier">A handle (e.g., <c>my-handle.bsky.social</c>) or DID string (e.g., <c>did:plc:abc123</c>).</param>
    /// <returns>A <see cref="BlockRef"/> on success, or an <see cref="XrpcError"/>. Pass the <c>BlockRef</c> to <see cref="unblock"/> to undo.</returns>
    /// <remarks>
    /// For type-safe usage when you already have a <see cref="Did"/>, use <see cref="block"/> instead.
    /// </remarks>
    let blockByHandle (agent : AtpAgent) (identifier : string) : Task<Result<BlockRef, XrpcError>> =
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
    let deleteRecord (agent : AtpAgent) (atUri : AtUri) : Task<Result<unit, XrpcError>> =
        let repo = AtUri.authority atUri

        match AtUri.collection atUri, AtUri.rkey atUri with
        | None, _ -> Task.FromResult (Error (toXrpcError "AT-URI must include a collection"))
        | _, None -> Task.FromResult (Error (toXrpcError "AT-URI must include a record key"))
        | Some collStr, Some rkeyStr ->
            match Nsid.parse collStr, RecordKey.parse rkeyStr with
            | Error msg, _ -> Task.FromResult (Error (toXrpcError (sprintf "Invalid collection NSID: %s" msg)))
            | _, Error msg -> Task.FromResult (Error (toXrpcError (sprintf "Invalid record key: %s" msg)))
            | Ok collection, Ok rkey ->
                task {
                    let! result =
                        ComAtprotoRepo.DeleteRecord.call
                            agent
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
    let unlike (agent : AtpAgent) (likeRef : LikeRef) : Task<Result<unit, XrpcError>> = deleteRecord agent likeRef.Uri

    /// <summary>
    /// Undo a repost by deleting the repost record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="repostRef">The <see cref="RepostRef"/> returned by <see cref="repost"/>.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let unrepost (agent : AtpAgent) (repostRef : RepostRef) : Task<Result<unit, XrpcError>> =
        deleteRecord agent repostRef.Uri

    /// <summary>
    /// Unfollow a user by deleting the follow record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="followRef">The <see cref="FollowRef"/> returned by <see cref="follow"/>.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let unfollow (agent : AtpAgent) (followRef : FollowRef) : Task<Result<unit, XrpcError>> =
        deleteRecord agent followRef.Uri

    /// <summary>
    /// Unblock a user by deleting the block record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="blockRef">The <see cref="BlockRef"/> returned by <see cref="block"/>.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let unblock (agent : AtpAgent) (blockRef : BlockRef) : Task<Result<unit, XrpcError>> =
        deleteRecord agent blockRef.Uri

    // ── Typed undo functions (returning UndoResult) ────────────────────

    /// <summary>
    /// Undo a like by deleting the like record. Returns <see cref="UndoResult.Undone"/> on success.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="likeRef">The <see cref="LikeRef"/> returned by <see cref="like"/>.</param>
    /// <returns>
    /// <c>Ok Undone</c> on success, or an <see cref="XrpcError"/>.
    /// Note: the AT Protocol's deleteRecord is idempotent, so this always returns <c>Undone</c>
    /// even if the record was already deleted. Only target-based functions
    /// (<see cref="unlikePost"/>/<see cref="unrepostPost"/>) can return <c>WasNotPresent</c>.
    /// </returns>
    let undoLike (agent : AtpAgent) (likeRef : LikeRef) : Task<Result<UndoResult, XrpcError>> =
        task {
            let! result = deleteRecord agent likeRef.Uri
            return result |> Result.map (fun () -> Undone)
        }

    /// <summary>
    /// Undo a repost by deleting the repost record. Returns <see cref="UndoResult.Undone"/> on success.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="repostRef">The <see cref="RepostRef"/> returned by <see cref="repost"/>.</param>
    /// <returns>
    /// <c>Ok Undone</c> on success, or an <see cref="XrpcError"/>.
    /// Note: the AT Protocol's deleteRecord is idempotent, so this always returns <c>Undone</c>
    /// even if the record was already deleted. Only target-based functions
    /// (<see cref="unlikePost"/>/<see cref="unrepostPost"/>) can return <c>WasNotPresent</c>.
    /// </returns>
    let undoRepost (agent : AtpAgent) (repostRef : RepostRef) : Task<Result<UndoResult, XrpcError>> =
        task {
            let! result = deleteRecord agent repostRef.Uri
            return result |> Result.map (fun () -> Undone)
        }

    /// <summary>
    /// Undo a follow by deleting the follow record. Returns <see cref="UndoResult.Undone"/> on success.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="followRef">The <see cref="FollowRef"/> returned by <see cref="follow"/>.</param>
    /// <returns>
    /// <c>Ok Undone</c> on success, or an <see cref="XrpcError"/>.
    /// Note: the AT Protocol's deleteRecord is idempotent, so this always returns <c>Undone</c>
    /// even if the record was already deleted. Only target-based functions
    /// (<see cref="unlikePost"/>/<see cref="unrepostPost"/>) can return <c>WasNotPresent</c>.
    /// </returns>
    let undoFollow (agent : AtpAgent) (followRef : FollowRef) : Task<Result<UndoResult, XrpcError>> =
        task {
            let! result = deleteRecord agent followRef.Uri
            return result |> Result.map (fun () -> Undone)
        }

    /// <summary>
    /// Undo a block by deleting the block record. Returns <see cref="UndoResult.Undone"/> on success.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="blockRef">The <see cref="BlockRef"/> returned by <see cref="block"/>.</param>
    /// <returns>
    /// <c>Ok Undone</c> on success, or an <see cref="XrpcError"/>.
    /// Note: the AT Protocol's deleteRecord is idempotent, so this always returns <c>Undone</c>
    /// even if the record was already deleted. Only target-based functions
    /// (<see cref="unlikePost"/>/<see cref="unrepostPost"/>) can return <c>WasNotPresent</c>.
    /// </returns>
    let undoBlock (agent : AtpAgent) (blockRef : BlockRef) : Task<Result<UndoResult, XrpcError>> =
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
    /// <returns>
    /// <c>Ok Undone</c> on success, or an <see cref="XrpcError"/>.
    /// Note: the AT Protocol's deleteRecord is idempotent, so this always returns <c>Undone</c>
    /// even if the record was already deleted. Only target-based functions
    /// (<see cref="unlikePost"/>/<see cref="unrepostPost"/>) can return <c>WasNotPresent</c>.
    /// </returns>
    /// <example>
    /// <code>
    /// let! likeRef = Bluesky.like agent postRef
    /// let! result = Bluesky.undo agent likeRef  // works with any ref type
    /// </code>
    /// </example>
    let inline undo (agent : AtpAgent) (ref : ^a) : Task<Result<UndoResult, XrpcError>> =
        let uri =
            ((^a or UndoWitness) : (static member UndoUri : UndoWitness * ^a -> AtUri) (UndoWitness, ref))

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
    let unlikePost (agent : AtpAgent) (postRef : PostRef) : Task<Result<UndoResult, XrpcError>> =
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
    let unrepostPost (agent : AtpAgent) (postRef : PostRef) : Task<Result<UndoResult, XrpcError>> =
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
    let uploadBlob (agent : AtpAgent) (data : byte[]) (mimeType : ImageMime) : Task<Result<BlobRef, XrpcError>> =
        task {
            let url =
                System.Uri (agent.BaseUrl, sprintf "xrpc/%s" ComAtprotoRepo.UploadBlob.TypeId)

            let request = new HttpRequestMessage (HttpMethod.Post, url)
            request.Content <- new ByteArrayContent (data)
            request.Content.Headers.ContentType <- MediaTypeHeaderValue (ImageMime.toMimeString mimeType)

            match agent.Session with
            | Some session -> request.Headers.Authorization <- AuthenticationHeaderValue ("Bearer", session.AccessJwt)
            | None -> ()

            let! response = agent.HttpClient.SendAsync (request)

            if response.IsSuccessStatusCode then
                let! json = response.Content.ReadAsStringAsync ()
                let doc = JsonSerializer.Deserialize<JsonElement> (json)

                match doc.TryGetProperty ("blob") with
                | true, blob -> return parseBlobRef blob
                | false, _ -> return Error (toXrpcError "Response missing 'blob' property")
            else
                let! errorJson = response.Content.ReadAsStringAsync ()

                try
                    let err = JsonSerializer.Deserialize<XrpcError> (errorJson, Json.options)

                    return
                        Error
                            { err with
                                StatusCode = int response.StatusCode }
                with _ ->
                    return
                        Error
                            { StatusCode = int response.StatusCode
                              Error = None
                              Message = Some errorJson }
        }

    let private uploadAllBlobs
        (agent : AtpAgent)
        (images : (byte[] * ImageMime * string) list)
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
    let postWithImages
        (agent : AtpAgent)
        (text : string)
        (images : ImageUpload list)
        : Task<Result<PostRef, XrpcError>> =
        task {
            match! uploadAllBlobs agent (images |> List.map (fun i -> (i.Data, i.MimeType, i.AltText))) with
            | Error e -> return Error e
            | Ok blobRefs ->
                let! facets = RichText.parse agent text

                let embed =
                    {| ``$type`` = "app.bsky.embed.images"
                       images =
                        blobRefs
                        |> List.map (fun (blobRef, alt) -> {| alt = alt; image = blobRef.Json |}) |}

                let record =
                    {| ``$type`` = AppBskyFeed.Post.TypeId
                       text = text
                       createdAt = nowTimestamp ()
                       facets = if facets.IsEmpty then null else facets |> box
                       embed = embed |}

                let! result = createRecord agent "app.bsky.feed.post" record
                return result |> Result.map toPostRef
        }

    // ── Mute / report / bookmark / handle ───────────────────────────────

    /// <summary>
    /// Mute an account. Muted accounts are hidden from your feeds but not blocked.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">The actor identifier (handle or DID string) to mute.</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let muteUser (agent : AtpAgent) (actor : string) : Task<Result<unit, XrpcError>> =
        AppBskyGraph.MuteActor.call agent { Actor = actor }

    /// <summary>
    /// Unmute a previously muted account.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">The actor identifier (handle or DID string) to unmute.</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let unmuteUser (agent : AtpAgent) (actor : string) : Task<Result<unit, XrpcError>> =
        AppBskyGraph.UnmuteActor.call agent { Actor = actor }

    /// <summary>
    /// Mute a thread. Posts in the muted thread are hidden from your notifications.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="root">The AT-URI of the thread root post.</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let muteThread (agent : AtpAgent) (root : AtUri) : Task<Result<unit, XrpcError>> =
        AppBskyGraph.MuteThread.call agent { Root = root }

    /// <summary>
    /// Unmute a previously muted thread.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="root">The AT-URI of the thread root post.</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let unmuteThread (agent : AtpAgent) (root : AtUri) : Task<Result<unit, XrpcError>> =
        AppBskyGraph.UnmuteThread.call agent { Root = root }

    /// <summary>
    /// Report content to moderation. Use <see cref="ReportSubject.Account"/> to report an account
    /// or <see cref="ReportSubject.Record"/> to report a specific post.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="subject">The subject of the report (account or post).</param>
    /// <param name="reason">The reason type from <see cref="ComAtprotoModeration.Defs.ReasonType"/>.</param>
    /// <param name="description">An optional free-text description of the report.</param>
    /// <returns>The report ID on success, or an <see cref="XrpcError"/>.</returns>
    let reportContent
        (agent : AtpAgent)
        (subject : ReportSubject)
        (reason : ComAtprotoModeration.Defs.ReasonType)
        (description : string option)
        : Task<Result<int64, XrpcError>> =
        task {
            let subjectUnion =
                match subject with
                | ReportSubject.Account did ->
                    ComAtprotoModeration.CreateReport.InputSubjectUnion.RepoRef { Did = did }
                | ReportSubject.Record postRef ->
                    ComAtprotoModeration.CreateReport.InputSubjectUnion.StrongRef
                        { Uri = postRef.Uri; Cid = postRef.Cid }

            let! result =
                ComAtprotoModeration.CreateReport.call
                    agent
                    { Subject = subjectUnion
                      ReasonType = reason
                      Reason = description
                      ModTool = None }

            return result |> Result.map (fun output -> output.Id)
        }

    /// <summary>
    /// Add a post to your bookmarks.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="postRef">A <see cref="PostRef"/> identifying the post to bookmark.</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let addBookmark (agent : AtpAgent) (postRef : PostRef) : Task<Result<unit, XrpcError>> =
        AppBskyBookmark.CreateBookmark.call agent { Uri = postRef.Uri; Cid = postRef.Cid }

    /// <summary>
    /// Remove a post from your bookmarks.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="uri">The AT-URI of the bookmarked post to remove.</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let removeBookmark (agent : AtpAgent) (uri : AtUri) : Task<Result<unit, XrpcError>> =
        AppBskyBookmark.DeleteBookmark.call agent { Uri = uri }

    /// <summary>
    /// Update the authenticated user's handle.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="handle">The new <see cref="Handle"/> to set.</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let updateHandle (agent : AtpAgent) (handle : Handle) : Task<Result<unit, XrpcError>> =
        ComAtprotoIdentity.UpdateHandle.call agent { Handle = handle }

    // ── Read convenience methods ────────────────────────────────────────

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let getProfileImpl (agent : AtpAgent) (actorStr : string) : Task<Result<Profile, XrpcError>> =
        task {
            let! result = AppBskyActor.GetProfile.query agent { Actor = actorStr }
            return result |> Result.map Profile.ofDetailed
        }

    /// <summary>
    /// Get a user's profile. Accepts a <see cref="Handle"/>, <see cref="Did"/>, or plain <c>string</c>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">A <see cref="Handle"/>, <see cref="Did"/>, or string identifier.</param>
    /// <returns>A <see cref="Profile"/> on success, or an <see cref="XrpcError"/>.</returns>
    let inline getProfile (agent : AtpAgent) (actor : ^a) : Task<Result<Profile, XrpcError>> =
        getProfileImpl agent (toActorString actor)

    /// <summary>
    /// Get the authenticated user's home timeline.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="limit">Maximum number of posts to return (optional, pass <c>None</c> for server default).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional, pass <c>None</c> to start from the beginning).</param>
    /// <returns>A page of <see cref="FeedItem"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let getTimeline
        (agent : AtpAgent)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<FeedItem>, XrpcError>> =
        task {
            let! result =
                AppBskyFeed.GetTimeline.query
                    agent
                    { Algorithm = None
                      Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Feed |> List.map FeedItem.ofFeedViewPost
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get a post thread by its AT-URI, returning a <see cref="ThreadNode"/> tree.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="uri">The AT-URI of the post (e.g., <c>at://did:plc:.../app.bsky.feed.post/...</c>).</param>
    /// <param name="depth">How many levels of replies to include (optional, pass <c>None</c> for server default).</param>
    /// <param name="parentHeight">How many levels of parent context to include (optional, pass <c>None</c> for server default).</param>
    /// <returns>A <see cref="ThreadNode"/> tree on success, or an <see cref="XrpcError"/>.</returns>
    let getPostThread
        (agent : AtpAgent)
        (uri : AtUri)
        (depth : int64 option)
        (parentHeight : int64 option)
        : Task<Result<ThreadNode, XrpcError>> =
        task {
            let! result =
                AppBskyFeed.GetPostThread.query
                    agent
                    { Depth = depth
                      ParentHeight = parentHeight
                      Uri = uri }

            return result |> Result.map (fun output -> ThreadNode.ofOutputThreadUnion output.Thread)
        }

    /// <summary>
    /// Get a post thread, returning just the <see cref="ThreadPost"/> if available.
    /// Returns <c>Some threadPost</c> for normal threads, <c>None</c> for not-found or blocked posts.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="uri">The AT-URI of the post.</param>
    /// <param name="depth">How many levels of replies to include (optional).</param>
    /// <param name="parentHeight">How many levels of parent context to include (optional).</param>
    /// <returns><c>Some ThreadPost</c> if the post is accessible, <c>None</c> if not found or blocked, or an <see cref="XrpcError"/>.</returns>
    let getPostThreadView
        (agent : AtpAgent)
        (uri : AtUri)
        (depth : int64 option)
        (parentHeight : int64 option)
        : Task<Result<ThreadPost option, XrpcError>> =
        task {
            let! result = getPostThread agent uri depth parentHeight

            return
                result
                |> Result.map (fun node ->
                    match node with
                    | ThreadNode.Post tp -> Some tp
                    | _ -> None)
        }

    /// <summary>
    /// List notifications for the authenticated user.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="limit">Maximum number of notifications to return (optional, pass <c>None</c> for server default).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional, pass <c>None</c> to start from the beginning).</param>
    /// <returns>A page of <see cref="Notification"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let getNotifications
        (agent : AtpAgent)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<Notification>, XrpcError>> =
        task {
            let! result =
                AppBskyNotification.ListNotifications.query
                    agent
                    { Cursor = cursor
                      Limit = limit
                      Priority = None
                      Reasons = None
                      SeenAt = None }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Notifications |> List.map Notification.ofRaw
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get the followers of an actor.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">The actor identifier (handle or DID string).</param>
    /// <param name="limit">Maximum number of followers to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="ProfileSummary"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let getFollowers
        (agent : AtpAgent)
        (actor : string)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ProfileSummary>, XrpcError>> =
        task {
            let! result =
                AppBskyGraph.GetFollowers.query
                    agent
                    { Actor = actor
                      Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Followers |> List.map ProfileSummary.ofView
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get the accounts that an actor follows.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">The actor identifier (handle or DID string).</param>
    /// <param name="limit">Maximum number of follows to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="ProfileSummary"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let getFollows
        (agent : AtpAgent)
        (actor : string)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ProfileSummary>, XrpcError>> =
        task {
            let! result =
                AppBskyGraph.GetFollows.query
                    agent
                    { Actor = actor
                      Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Follows |> List.map ProfileSummary.ofView
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Search for posts matching a query string.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="query">The search query string.</param>
    /// <param name="limit">Maximum number of posts to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="TimelinePost"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let searchPosts
        (agent : AtpAgent)
        (query : string)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<TimelinePost>, XrpcError>> =
        task {
            let! result =
                AppBskyFeed.SearchPosts.query
                    agent
                    { Q = query
                      Author = None
                      Cursor = cursor
                      Domain = None
                      Lang = None
                      Limit = limit
                      Mentions = None
                      Since = None
                      Sort = None
                      Tag = None
                      Until = None
                      Url = None }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Posts |> List.map TimelinePost.ofPostView
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Search for actors (users) matching a query string.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="query">The search query string.</param>
    /// <param name="limit">Maximum number of actors to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="ProfileSummary"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let searchActors
        (agent : AtpAgent)
        (query : string)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ProfileSummary>, XrpcError>> =
        task {
            let! result =
                AppBskyActor.SearchActors.query
                    agent
                    { Q = Some query
                      Cursor = cursor
                      Limit = limit
                      Term = None }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Actors |> List.map ProfileSummary.ofView
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get a specific user's feed (posts by that actor).
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">The actor identifier (handle or DID string).</param>
    /// <param name="limit">Maximum number of posts to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="FeedItem"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let getAuthorFeed
        (agent : AtpAgent)
        (actor : string)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<FeedItem>, XrpcError>> =
        task {
            let! result =
                AppBskyFeed.GetAuthorFeed.query
                    agent
                    { Actor = actor
                      Cursor = cursor
                      Filter = None
                      IncludePins = None
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Feed |> List.map FeedItem.ofFeedViewPost
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get the posts that a specific actor has liked.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">The actor identifier (handle or DID string).</param>
    /// <param name="limit">Maximum number of posts to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="FeedItem"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let getActorLikes
        (agent : AtpAgent)
        (actor : string)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<FeedItem>, XrpcError>> =
        task {
            let! result =
                AppBskyFeed.GetActorLikes.query
                    agent
                    { Actor = actor
                      Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Feed |> List.map FeedItem.ofFeedViewPost
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get the accounts that have liked a specific post.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="uri">The AT-URI of the post.</param>
    /// <param name="limit">Maximum number of likes to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="ProfileSummary"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let getLikes
        (agent : AtpAgent)
        (uri : AtUri)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ProfileSummary>, XrpcError>> =
        task {
            let! result =
                AppBskyFeed.GetLikes.query
                    agent
                    { Uri = uri
                      Cid = None
                      Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Likes |> List.map (fun l -> ProfileSummary.ofView l.Actor)
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get the accounts that have reposted a specific post.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="uri">The AT-URI of the post.</param>
    /// <param name="limit">Maximum number of reposts to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="ProfileSummary"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let getRepostedBy
        (agent : AtpAgent)
        (uri : AtUri)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ProfileSummary>, XrpcError>> =
        task {
            let! result =
                AppBskyFeed.GetRepostedBy.query
                    agent
                    { Uri = uri
                      Cid = None
                      Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.RepostedBy |> List.map ProfileSummary.ofView
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get posts that quote a specific post.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="uri">The AT-URI of the quoted post.</param>
    /// <param name="limit">Maximum number of quotes to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="TimelinePost"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let getQuotes
        (agent : AtpAgent)
        (uri : AtUri)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<TimelinePost>, XrpcError>> =
        task {
            let! result =
                AppBskyFeed.GetQuotes.query
                    agent
                    { Uri = uri
                      Cid = None
                      Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Posts |> List.map TimelinePost.ofPostView
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get multiple posts by their AT-URIs in a single request.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="uris">A list of AT-URIs identifying the posts to retrieve.</param>
    /// <returns>A list of <see cref="TimelinePost"/> on success, or an <see cref="XrpcError"/>.</returns>
    let getPosts (agent : AtpAgent) (uris : AtUri list) : Task<Result<TimelinePost list, XrpcError>> =
        task {
            let! result = AppBskyFeed.GetPosts.query agent { Uris = uris }
            return result |> Result.map (fun output -> output.Posts |> List.map TimelinePost.ofPostView)
        }

    /// <summary>
    /// Get multiple profiles by their identifiers in a single request.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actors">A list of actor identifiers (handles or DID strings).</param>
    /// <returns>A list of <see cref="Profile"/> on success, or an <see cref="XrpcError"/>.</returns>
    let getProfiles (agent : AtpAgent) (actors : string list) : Task<Result<Profile list, XrpcError>> =
        task {
            let! result = AppBskyActor.GetProfiles.query agent { Actors = actors }
            return result |> Result.map (fun output -> output.Profiles |> List.map Profile.ofDetailed)
        }

    /// <summary>
    /// Get suggested accounts to follow based on a given actor.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">The actor identifier (handle or DID string) to base suggestions on.</param>
    /// <returns>A list of <see cref="ProfileSummary"/> on success, or an <see cref="XrpcError"/>.</returns>
    let getSuggestedFollows (agent : AtpAgent) (actor : string) : Task<Result<ProfileSummary list, XrpcError>> =
        task {
            let! result = AppBskyGraph.GetSuggestedFollowsByActor.query agent { Actor = actor }
            return result |> Result.map (fun output -> output.Suggestions |> List.map ProfileSummary.ofView)
        }

    /// <summary>
    /// Get the count of unread notifications for the authenticated user.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <returns>The unread notification count on success, or an <see cref="XrpcError"/>.</returns>
    let getUnreadNotificationCount (agent : AtpAgent) : Task<Result<int64, XrpcError>> =
        task {
            let! result =
                AppBskyNotification.GetUnreadCount.query
                    agent
                    { Priority = None
                      SeenAt = None }

            return result |> Result.map (fun output -> output.Count)
        }

    /// <summary>
    /// Mark all notifications as seen up to the current time.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let markNotificationsSeen (agent : AtpAgent) : Task<Result<unit, XrpcError>> =
        let now = AtDateTime.parse (DateTimeOffset.UtcNow.ToString ("o")) |> Result.defaultWith failwith
        AppBskyNotification.UpdateSeen.call agent { SeenAt = now }

    /// <summary>
    /// Get the authenticated user's preferences (saved feeds, content filters, etc.).
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <returns>The user's preferences on success, or an <see cref="XrpcError"/>.</returns>
    let getPreferences (agent : AtpAgent) : Task<Result<AppBskyActor.Defs.Preferences, XrpcError>> =
        task {
            let! result = AppBskyActor.GetPreferences.query agent
            return result |> Result.map (fun output -> output.Preferences)
        }

    /// <summary>
    /// Get the authenticated user's bookmarked posts.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="limit">Maximum number of bookmarks to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="TimelinePost"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let getBookmarks
        (agent : AtpAgent)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<TimelinePost>, XrpcError>> =
        task {
            let! result =
                AppBskyBookmark.GetBookmarks.query
                    agent
                    { Limit = limit
                      Cursor = cursor }

            return
                result
                |> Result.map (fun output ->
                    { Items =
                        output.Bookmarks
                        |> List.choose (fun bv ->
                            match bv.Item with
                            | AppBskyBookmark.Defs.BookmarkViewItemUnion.PostView pv ->
                                Some(TimelinePost.ofPostView pv)
                            | _ -> None)
                      Cursor = output.Cursor })
        }

    // ── Pre-built paginators ───────────────────────────────────────────

    let private mapAsyncEnum
        (f : 'a -> 'b)
        (source : System.Collections.Generic.IAsyncEnumerable<Result<'a, XrpcError>>)
        : System.Collections.Generic.IAsyncEnumerable<Result<'b, XrpcError>> =
        { new System.Collections.Generic.IAsyncEnumerable<Result<'b, XrpcError>> with
            member _.GetAsyncEnumerator(ct) =
                let inner = source.GetAsyncEnumerator(ct)

                { new System.Collections.Generic.IAsyncEnumerator<Result<'b, XrpcError>> with
                    member _.Current = inner.Current |> Result.map f
                    member _.MoveNextAsync() = inner.MoveNextAsync()
                    member _.DisposeAsync() = inner.DisposeAsync() } }

    /// <summary>
    /// Paginate the home timeline. Returns an async enumerable of pages.
    /// Each element is a <c>Result</c> containing one page of feed items.
    /// Pagination stops automatically when the server returns no cursor.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="pageSize">Maximum number of posts per page (optional, pass <c>None</c> for server default).</param>
    /// <returns>An <see cref="System.Collections.Generic.IAsyncEnumerable{T}"/> of paginated results.</returns>
    let paginateTimeline
        (agent : AtpAgent)
        (pageSize : int64 option)
        : System.Collections.Generic.IAsyncEnumerable<Result<Page<FeedItem>, XrpcError>> =
        Xrpc.paginate<AppBskyFeed.GetTimeline.Params, AppBskyFeed.GetTimeline.Output>
            AppBskyFeed.GetTimeline.TypeId
            { Algorithm = None
              Cursor = None
              Limit = pageSize }
            (fun o -> o.Cursor)
            (fun c p -> { p with Cursor = c })
            agent
        |> mapAsyncEnum (fun output ->
            { Items = output.Feed |> List.map FeedItem.ofFeedViewPost
              Cursor = output.Cursor })

    /// <summary>
    /// Paginate followers for an actor. Returns an async enumerable of pages.
    /// Each element is a <c>Result</c> containing one page of follower profiles.
    /// Pagination stops automatically when the server returns no cursor.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">The actor identifier (handle or DID string) whose followers to list.</param>
    /// <param name="pageSize">Maximum number of followers per page (optional, pass <c>None</c> for server default).</param>
    /// <returns>An <see cref="System.Collections.Generic.IAsyncEnumerable{T}"/> of paginated results.</returns>
    let paginateFollowers
        (agent : AtpAgent)
        (actor : string)
        (pageSize : int64 option)
        : System.Collections.Generic.IAsyncEnumerable<Result<Page<ProfileSummary>, XrpcError>> =
        Xrpc.paginate<AppBskyGraph.GetFollowers.Params, AppBskyGraph.GetFollowers.Output>
            AppBskyGraph.GetFollowers.TypeId
            { Actor = actor
              Cursor = None
              Limit = pageSize }
            (fun o -> o.Cursor)
            (fun c p -> { p with Cursor = c })
            agent
        |> mapAsyncEnum (fun output ->
            { Items = output.Followers |> List.map ProfileSummary.ofView
              Cursor = output.Cursor })

    /// <summary>
    /// Paginate notifications for the authenticated user. Returns an async enumerable of pages.
    /// Each element is a <c>Result</c> containing one page of notifications.
    /// Pagination stops automatically when the server returns no cursor.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="pageSize">Maximum number of notifications per page (optional, pass <c>None</c> for server default).</param>
    /// <returns>An <see cref="System.Collections.Generic.IAsyncEnumerable{T}"/> of paginated results.</returns>
    let paginateNotifications
        (agent : AtpAgent)
        (pageSize : int64 option)
        : System.Collections.Generic.IAsyncEnumerable<Result<Page<Notification>, XrpcError>> =
        Xrpc.paginate<AppBskyNotification.ListNotifications.Params, AppBskyNotification.ListNotifications.Output>
            AppBskyNotification.ListNotifications.TypeId
            { Cursor = None
              Limit = pageSize
              Priority = None
              Reasons = None
              SeenAt = None }
            (fun o -> o.Cursor)
            (fun c p -> { p with Cursor = c })
            agent
        |> mapAsyncEnum (fun output ->
            { Items = output.Notifications |> List.map Notification.ofRaw
              Cursor = output.Cursor })
