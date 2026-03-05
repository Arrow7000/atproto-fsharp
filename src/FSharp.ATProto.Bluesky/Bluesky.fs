namespace FSharp.ATProto.Bluesky

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text.Json
open System.Threading.Tasks
open FSharp.ATProto.Core
open FSharp.ATProto.Syntax
open FSharp.ATProto.Streaming

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
/// A reference to a list block record, returned by <c>Bluesky.blockModList</c>.
/// Pass to <c>Bluesky.unblockModList</c> to undo.
/// </summary>
type ListBlockRef =
    {
        /// <summary>The AT-URI of the list block record.</summary>
        Uri : AtUri
    }

/// <summary>
/// A reference to a list record, returned by <c>Bluesky.createList</c>.
/// </summary>
type ListRef =
    {
        /// <summary>The AT-URI of the list record.</summary>
        Uri : AtUri
    }

/// <summary>
/// A reference to a list item record, returned by <c>Bluesky.addListItem</c>.
/// </summary>
type ListItemRef =
    {
        /// <summary>The AT-URI of the list item record.</summary>
        Uri : AtUri
    }

/// <summary>
/// A reference to a starter pack record, returned by <c>Bluesky.createStarterPack</c>.
/// </summary>
type StarterPackRef =
    {
        /// <summary>The AT-URI of the starter pack record.</summary>
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
/// Supported video MIME types for video upload.
/// </summary>
type VideoMime =
    | Mp4
    | Webm
    | Custom of string

module VideoMime =
    let toMimeString =
        function
        | Mp4 -> "video/mp4"
        | Webm -> "video/webm"
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

/// <summary>An image attached to a post.</summary>
type PostImage =
    { Thumb : string
      Fullsize : string
      Alt : string }

/// <summary>A video attached to a post.</summary>
type PostVideo =
    { Thumbnail : string option
      Playlist : string option
      Alt : string option }

/// <summary>An external link card attached to a post.</summary>
type PostExternalLink =
    { Uri : string
      Title : string
      Description : string
      Thumb : string option }

/// <summary>Media within a record-with-media embed.</summary>
[<RequireQualifiedAccess>]
type PostMediaEmbed =
    | Images of PostImage list
    | Video of PostVideo
    | ExternalLink of PostExternalLink

/// <summary>Embedded content in a post (images, video, link card, quoted post, or combination).</summary>
[<RequireQualifiedAccess>]
type PostEmbed =
    | Images of PostImage list
    | Video of PostVideo
    | ExternalLink of PostExternalLink
    | QuotedPost of AppBskyEmbed.Record.ViewRecordUnion
    | RecordWithMedia of AppBskyEmbed.Record.ViewRecordUnion * PostMediaEmbed
    | Unknown

module PostEmbed =

    let private uriToString (u : FSharp.ATProto.Syntax.Uri) = FSharp.ATProto.Syntax.Uri.value u

    let private mapImage (vi : AppBskyEmbed.Images.ViewImage) : PostImage =
        { Thumb = uriToString vi.Thumb
          Fullsize = uriToString vi.Fullsize
          Alt = vi.Alt }

    let private mapVideo (vv : AppBskyEmbed.Video.View) : PostVideo =
        { Thumbnail = vv.Thumbnail |> Option.map uriToString
          Playlist = Some (uriToString vv.Playlist)
          Alt = vv.Alt }

    let private mapExternal (ve : AppBskyEmbed.External.ViewExternal) : PostExternalLink =
        { Uri = uriToString ve.Uri
          Title = ve.Title
          Description = ve.Description
          Thumb = ve.Thumb |> Option.map uriToString }

    let private mapMediaUnion (mu : AppBskyEmbed.RecordWithMedia.ViewMediaUnion) : PostMediaEmbed =
        match mu with
        | AppBskyEmbed.RecordWithMedia.ViewMediaUnion.View iv ->
            PostMediaEmbed.Images (iv.Images |> List.map mapImage)
        | AppBskyEmbed.RecordWithMedia.ViewMediaUnion.View2 vv ->
            PostMediaEmbed.Video (mapVideo vv)
        | AppBskyEmbed.RecordWithMedia.ViewMediaUnion.View3 ev ->
            PostMediaEmbed.ExternalLink (mapExternal ev.External)
        | _ -> PostMediaEmbed.Images []

    let ofEmbedUnion (eu : AppBskyFeed.Defs.PostViewEmbedUnion) : PostEmbed =
        match eu with
        | AppBskyFeed.Defs.PostViewEmbedUnion.View iv ->
            PostEmbed.Images (iv.Images |> List.map mapImage)
        | AppBskyFeed.Defs.PostViewEmbedUnion.View2 vv ->
            PostEmbed.Video (mapVideo vv)
        | AppBskyFeed.Defs.PostViewEmbedUnion.View3 ev ->
            PostEmbed.ExternalLink (mapExternal ev.External)
        | AppBskyFeed.Defs.PostViewEmbedUnion.View4 rv ->
            PostEmbed.QuotedPost rv.Record
        | AppBskyFeed.Defs.PostViewEmbedUnion.View5 rwm ->
            PostEmbed.RecordWithMedia (rwm.Record.Record, mapMediaUnion rwm.Media)
        | _ -> PostEmbed.Unknown

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
      IsBookmarked : bool
      Embed : PostEmbed option }

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
              |> Option.defaultValue false
          Embed = pv.Embed |> Option.map PostEmbed.ofEmbedUnion }

/// <summary>Reason a post appeared in a feed.</summary>
type FeedReason =
    | Repost of by : ProfileSummary
    | Pin

/// <summary>A single item in a feed or timeline.</summary>
type FeedItem =
    { Post : TimelinePost
      Reason : FeedReason option
      ReplyParent : TimelinePost option }

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

        let replyParent =
            fvp.Reply
            |> Option.bind (fun r ->
                match r.Parent with
                | AppBskyFeed.Defs.ReplyRefParentUnion.PostView pv -> Some (TimelinePost.ofPostView pv)
                | _ -> None)

        { Post = TimelinePost.ofPostView fvp.Post
          Reason = reason
          ReplyParent = replyParent }

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

/// <summary>A user-created list (curate list, mod list, or reference list).</summary>
type ListView =
    { Uri : AtUri
      Cid : Cid
      Name : string
      Purpose : AppBskyGraph.Defs.ListPurpose
      Description : string option
      Avatar : string option
      Creator : ProfileSummary
      ListItemCount : int64
      IsMuted : bool
      IsBlocked : bool }

module ListView =

    let ofGenerated (lv : AppBskyGraph.Defs.ListView) : ListView =
        let viewer = lv.Viewer

        { Uri = lv.Uri
          Cid = lv.Cid
          Name = lv.Name
          Purpose = lv.Purpose
          Description = lv.Description
          Avatar = lv.Avatar |> Option.map string
          Creator = ProfileSummary.ofView lv.Creator
          ListItemCount = lv.ListItemCount |> Option.defaultValue 0L
          IsMuted =
              viewer
              |> Option.bind (fun v -> v.Muted)
              |> Option.defaultValue false
          IsBlocked = viewer |> Option.bind (fun v -> v.Blocked) |> Option.isSome }

/// <summary>A custom feed generator.</summary>
type FeedGenerator =
    { Uri : AtUri
      Cid : Cid
      Did : Did
      DisplayName : string
      Description : string option
      Avatar : string option
      Creator : ProfileSummary
      LikeCount : int64
      IsOnline : bool
      IsValid : bool
      IsLiked : bool }

module FeedGenerator =

    let ofGeneratorView (gv : AppBskyFeed.Defs.GeneratorView) (isOnline : bool) (isValid : bool) : FeedGenerator =
        let viewer = gv.Viewer

        { Uri = gv.Uri
          Cid = gv.Cid
          Did = gv.Did
          DisplayName = gv.DisplayName
          Description = gv.Description
          Avatar = gv.Avatar |> Option.map string
          Creator = ProfileSummary.ofView gv.Creator
          LikeCount = gv.LikeCount |> Option.defaultValue 0L
          IsOnline = isOnline
          IsValid = isValid
          IsLiked = viewer |> Option.bind (fun v -> v.Like) |> Option.isSome }

    let ofGeneratorViewOnly (gv : AppBskyFeed.Defs.GeneratorView) : FeedGenerator =
        ofGeneratorView gv true true

/// <summary>A bi-directional relationship between the authenticated user and another actor.</summary>
type Relationship =
    { Did : Did
      Following : AtUri option
      FollowedBy : AtUri option
      Blocking : AtUri option
      BlockedBy : AtUri option }

module Relationship =

    let ofGenerated (r : AppBskyGraph.Defs.Relationship) : Relationship =
        { Did = r.Did
          Following = r.Following
          FollowedBy = r.FollowedBy
          Blocking = r.Blocking
          BlockedBy = r.BlockedBy }

/// <summary>A list with its member profiles, returned by <c>getList</c>.</summary>
type ListDetail =
    { List : ListView
      Items : ProfileSummary list
      Cursor : string option }

// ── SRTP witness types (implementation detail) ───────────────────

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
    static member UndoUri (UndoWitness, r : ListBlockRef) = r.Uri

/// <summary>
/// Witness type enabling SRTP-based overloading for post reference parameters.
/// Allows functions like <c>like</c>, <c>repost</c>, and <c>replyTo</c> to accept
/// either a <see cref="PostRef"/> or a <see cref="TimelinePost"/> directly.
/// This type is an implementation detail and should not be used directly.
/// </summary>
[<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
type PostRefWitness =
    | PostRefWitness
    static member ToPostRef (PostRefWitness, pr : PostRef) = pr
    static member ToPostRef (PostRefWitness, tp : TimelinePost) = { PostRef.Uri = tp.Uri; Cid = tp.Cid }

/// <summary>
/// Witness type enabling SRTP-based overloading for actor parameters.
/// Allows functions like <c>getProfile</c> to accept <see cref="Handle"/>, <see cref="Did"/>,
/// <see cref="ProfileSummary"/>, or <see cref="Profile"/> directly.
/// This type is an implementation detail and should not be used directly.
/// </summary>
[<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
type ActorWitness =
    | ActorWitness

    static member ToActorString (ActorWitness, h : Handle) = Handle.value h
    static member ToActorString (ActorWitness, d : Did) = Did.value d
    static member ToActorString (ActorWitness, p : ProfileSummary) = Did.value p.Did
    static member ToActorString (ActorWitness, p : Profile) = Did.value p.Did

/// <summary>
/// Witness type enabling SRTP-based overloading for actor DID parameters.
/// Allows functions like <c>follow</c>, <c>block</c>, <c>muteUser</c>, and <c>unmuteUser</c> to accept
/// a <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/> directly.
/// This type is an implementation detail and should not be used directly.
/// </summary>
[<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
type ActorDidWitness =
    | ActorDidWitness
    static member ToDid (ActorDidWitness, d : Did) = d
    static member ToDid (ActorDidWitness, p : ProfileSummary) = p.Did
    static member ToDid (ActorDidWitness, p : Profile) = p.Did

/// <summary>
/// Witness type enabling SRTP-based overloading for post AT-URI parameters.
/// Allows functions like <c>deleteRecord</c>, <c>muteThread</c>, <c>getLikes</c>, and <c>getPostThread</c>
/// to accept an <see cref="AtUri"/>, <see cref="PostRef"/>, or <see cref="TimelinePost"/> directly.
/// This type is an implementation detail and should not be used directly.
/// </summary>
[<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
type PostUriWitness =
    | PostUriWitness
    static member ToAtUri (PostUriWitness, u : AtUri) = u
    static member ToAtUri (PostUriWitness, pr : PostRef) = pr.Uri
    static member ToAtUri (PostUriWitness, tp : TimelinePost) = tp.Uri

/// <summary>
/// High-level convenience methods for common Bluesky operations:
/// posting, replying, liking, reposting, following, blocking, uploading blobs, and deleting records.
/// All methods require an authenticated <see cref="AtpAgent"/>.
/// </summary>
module Bluesky =

    let inline internal toActorString (x : ^a) : string =
        ((^a or ActorWitness) : (static member ToActorString : ActorWitness * ^a -> string) (ActorWitness, x))

    let inline internal asPostRef (x : ^a) : PostRef =
        ((^a or PostRefWitness) : (static member ToPostRef : PostRefWitness * ^a -> PostRef) (PostRefWitness, x))

    let inline internal toActorDid (x : ^a) : Did =
        ((^a or ActorDidWitness) : (static member ToDid : ActorDidWitness * ^a -> Did) (ActorDidWitness, x))

    let inline internal toPostAtUri (x : ^a) : AtUri =
        ((^a or PostUriWitness) : (static member ToAtUri : PostUriWitness * ^a -> AtUri) (PostUriWitness, x))

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
    /// Resolve the user's PDS endpoint from their DID document and update the agent's BaseUrl.
    /// Silently keeps the original URL if resolution fails (best-effort).
    let private resolvePdsEndpoint (agent : AtpAgent) : Task<unit> =
        task {
            match agent.Session with
            | Some session ->
                let! identityResult = Identity.resolveDid agent session.Did

                match identityResult with
                | Ok identity ->
                    match identity.PdsEndpoint with
                    | Some pdsUrl -> agent.BaseUrl <- System.Uri (FSharp.ATProto.Syntax.Uri.value pdsUrl)
                    | None -> ()
                | Error _ -> ()
            | None -> ()
        }

    let login (baseUrl : string) (identifier : string) (password : string) : Task<Result<AtpAgent, XrpcError>> =
        task {
            let agent = AtpAgent.create baseUrl
            let! result = AtpAgent.login identifier password agent

            match result with
            | Ok _ ->
                do! resolvePdsEndpoint agent
                return Ok agent
            | Error e -> return Error e
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

            match result with
            | Ok _ ->
                do! resolvePdsEndpoint agent
                return Ok agent
            | Error e -> return Error e
        }

    /// <summary>
    /// Construct an agent from saved session data without making any network calls.
    /// Use this to restore a session from persisted tokens.
    /// </summary>
    /// <param name="baseUrl">The PDS base URL (e.g. <c>"https://bsky.social"</c>).</param>
    /// <param name="session">A previously obtained <see cref="AtpSession"/>.</param>
    /// <returns>An authenticated <see cref="AtpAgent"/> with the given session.</returns>
    let resumeSession (baseUrl : string) (session : AtpSession) : AtpAgent =
        let agent = AtpAgent.create baseUrl
        agent.Session <- Some session
        agent

    /// <summary>
    /// Construct an agent from saved session data with a custom <see cref="System.Net.Http.HttpClient"/>.
    /// Use this to restore a session from persisted tokens with custom HTTP configuration.
    /// </summary>
    /// <param name="client">The HTTP client to use for all requests.</param>
    /// <param name="baseUrl">The PDS base URL (e.g. <c>"https://bsky.social"</c>).</param>
    /// <param name="session">A previously obtained <see cref="AtpSession"/>.</param>
    /// <returns>An authenticated <see cref="AtpAgent"/> with the given session.</returns>
    let resumeSessionWithClient (client : HttpClient) (baseUrl : string) (session : AtpSession) : AtpAgent =
        let agent = AtpAgent.createWithClient client baseUrl
        agent.Session <- Some session
        agent

    /// <summary>
    /// Terminate the current session by deleting it on the server, then clear it locally.
    /// Uses the refresh JWT for authorization (per the AT Protocol spec).
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let logout (agent : AtpAgent) : Task<Result<unit, XrpcError>> =
        task {
            match agent.Session with
            | None ->
                return
                    Error
                        { StatusCode = 401
                          Error = Some "NoSession"
                          Message = Some "No session to delete" }
            | Some session ->
                let url = $"{agent.BaseUrl}xrpc/com.atproto.server.deleteSession"
                let request = new HttpRequestMessage (HttpMethod.Post, url)
                // deleteSession uses the refresh JWT, not the access JWT
                request.Headers.Authorization <- AuthenticationHeaderValue ("Bearer", session.RefreshJwt)

                let! response = agent.HttpClient.SendAsync (request)

                if response.IsSuccessStatusCode then
                    agent.Session <- None
                    return Ok ()
                else
                    let! body = response.Content.ReadAsStringAsync ()

                    try
                        let doc = JsonDocument.Parse (body)
                        let root = doc.RootElement

                        let error =
                            match root.TryGetProperty ("error") with
                            | true, v -> Some (v.GetString ())
                            | false, _ -> None

                        let message =
                            match root.TryGetProperty ("message") with
                            | true, v -> Some (v.GetString ())
                            | false, _ -> None

                        return
                            Error
                                { StatusCode = int response.StatusCode
                                  Error = error
                                  Message = message }
                    with _ ->
                        return
                            Error
                                { StatusCode = int response.StatusCode
                                  Error = None
                                  Message = Some body }
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

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let quotePostImpl (agent : AtpAgent) (text : string) (quotedPost : PostRef) : Task<Result<PostRef, XrpcError>> =
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
    /// Create a quote post. The quoted post appears as an embedded record below your text.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="text">The post text. Mentions, links, and hashtags are auto-detected.</param>
    /// <param name="quoted">A <see cref="PostRef"/> or <see cref="TimelinePost"/> identifying the post to quote.</param>
    /// <returns>A <see cref="PostRef"/> with the AT-URI and CID on success, or an <see cref="XrpcError"/>.</returns>
    let inline quotePost (agent : AtpAgent) (text : string) (quoted : ^a) : Task<Result<PostRef, XrpcError>> =
        quotePostImpl agent text (asPostRef quoted)

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

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let replyToImpl (agent : AtpAgent) (text : string) (parentRef : PostRef) : Task<Result<PostRef, XrpcError>> =
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
    /// Reply to a post. Fetches the parent to auto-resolve the thread root.
    /// This is the recommended way to reply: you only need the parent post's <see cref="PostRef"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="text">The reply text. Mentions, links, and hashtags are auto-detected.</param>
    /// <param name="parent">A <see cref="PostRef"/> or <see cref="TimelinePost"/> for the post being replied to.</param>
    /// <returns>A <see cref="PostRef"/> with the AT-URI and CID on success, or an <see cref="XrpcError"/>.</returns>
    /// <remarks>
    /// Fetches the parent post via <c>app.bsky.feed.getPosts</c> to determine the thread root.
    /// If the parent has a <c>reply</c> field, its root is used. Otherwise, the parent itself is the root.
    /// For full control over both parent and root, use <see cref="replyWithKnownRoot"/> instead.
    /// </remarks>
    let inline replyTo (agent : AtpAgent) (text : string) (parent : ^a) : Task<Result<PostRef, XrpcError>> =
        replyToImpl agent text (asPostRef parent)

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let likeImpl (agent : AtpAgent) (postRef : PostRef) : Task<Result<LikeRef, XrpcError>> =
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
    /// Like a post or other record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">A <see cref="PostRef"/> or <see cref="TimelinePost"/> identifying the record to like.</param>
    /// <returns>A <see cref="LikeRef"/> on success, or an <see cref="XrpcError"/>. Pass the <c>LikeRef</c> to <see cref="unlike"/> to undo.</returns>
    let inline like (agent : AtpAgent) (target : ^a) : Task<Result<LikeRef, XrpcError>> =
        likeImpl agent (asPostRef target)

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let repostImpl (agent : AtpAgent) (postRef : PostRef) : Task<Result<RepostRef, XrpcError>> =
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
    /// Repost (retweet) a post or other record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">A <see cref="PostRef"/> or <see cref="TimelinePost"/> identifying the record to repost.</param>
    /// <returns>A <see cref="RepostRef"/> on success, or an <see cref="XrpcError"/>. Pass the <c>RepostRef</c> to <see cref="unrepost"/> to undo.</returns>
    let inline repost (agent : AtpAgent) (target : ^a) : Task<Result<RepostRef, XrpcError>> =
        repostImpl agent (asPostRef target)

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let followImpl (agent : AtpAgent) (did : Did) : Task<Result<FollowRef, XrpcError>> =
        task {
            let record =
                {| ``$type`` = AppBskyGraph.Follow.TypeId
                   createdAt = nowTimestamp ()
                   subject = Did.value did |}

            let! result = createRecord agent "app.bsky.graph.follow" record
            return result |> Result.map (fun o -> { FollowRef.Uri = o.Uri })
        }

    /// <summary>
    /// Follow a user. Accepts a <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/> directly.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">The user to follow — a <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/>.</param>
    /// <returns>A <see cref="FollowRef"/> on success, or an <see cref="XrpcError"/>. Pass the <c>FollowRef</c> to <see cref="unfollow"/> to undo.</returns>
    let inline follow (agent : AtpAgent) (target : ^a) : Task<Result<FollowRef, XrpcError>> =
        followImpl agent (toActorDid target)

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let blockImpl (agent : AtpAgent) (did : Did) : Task<Result<BlockRef, XrpcError>> =
        task {
            let record =
                {| ``$type`` = AppBskyGraph.Block.TypeId
                   createdAt = nowTimestamp ()
                   subject = Did.value did |}

            let! result = createRecord agent "app.bsky.graph.block" record
            return result |> Result.map (fun o -> { BlockRef.Uri = o.Uri })
        }

    /// <summary>
    /// Block a user. Accepts a <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/> directly.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">The user to block — a <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/>.</param>
    /// <returns>A <see cref="BlockRef"/> on success, or an <see cref="XrpcError"/>. Pass the <c>BlockRef</c> to <see cref="unblock"/> to undo.</returns>
    let inline block (agent : AtpAgent) (target : ^a) : Task<Result<BlockRef, XrpcError>> =
        blockImpl agent (toActorDid target)

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
            | Ok did -> return! followImpl agent did
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
            | Ok did -> return! blockImpl agent did
        }

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let deleteRecordImpl (agent : AtpAgent) (atUri : AtUri) : Task<Result<unit, XrpcError>> =
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
    /// Delete a record by its AT-URI. Accepts an <see cref="AtUri"/>, <see cref="PostRef"/>,
    /// or <see cref="TimelinePost"/>.
    /// Can be used to unlike, un-repost, unfollow, unblock, or delete a post.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">The AT-URI of the record to delete (an <see cref="AtUri"/>, <see cref="PostRef"/>, or <see cref="TimelinePost"/>).</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    /// <remarks>
    /// The AT-URI is parsed to extract the repo DID, collection, and record key.
    /// This is a general-purpose delete; pass the AT-URI returned when the record was created.
    /// </remarks>
    let inline deleteRecord (agent : AtpAgent) (target : ^a) : Task<Result<unit, XrpcError>> =
        deleteRecordImpl agent (toPostAtUri target)

    /// <summary>
    /// Unlike a post by deleting the like record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="likeRef">The <see cref="LikeRef"/> returned by <see cref="like"/>.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let unlike (agent : AtpAgent) (likeRef : LikeRef) : Task<Result<unit, XrpcError>> = deleteRecordImpl agent likeRef.Uri

    /// <summary>
    /// Undo a repost by deleting the repost record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="repostRef">The <see cref="RepostRef"/> returned by <see cref="repost"/>.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let unrepost (agent : AtpAgent) (repostRef : RepostRef) : Task<Result<unit, XrpcError>> =
        deleteRecordImpl agent repostRef.Uri

    /// <summary>
    /// Unfollow a user by deleting the follow record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="followRef">The <see cref="FollowRef"/> returned by <see cref="follow"/>.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let unfollow (agent : AtpAgent) (followRef : FollowRef) : Task<Result<unit, XrpcError>> =
        deleteRecordImpl agent followRef.Uri

    /// <summary>
    /// Unblock a user by deleting the block record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="blockRef">The <see cref="BlockRef"/> returned by <see cref="block"/>.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let unblock (agent : AtpAgent) (blockRef : BlockRef) : Task<Result<unit, XrpcError>> =
        deleteRecordImpl agent blockRef.Uri

    /// <summary>
    /// Block an entire moderation list. Creates a <c>app.bsky.graph.listblock</c> record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="listUri">The AT-URI of the moderation list to block.</param>
    /// <returns>A <see cref="ListBlockRef"/> on success, or an <see cref="XrpcError"/>. Pass the <c>ListBlockRef</c> to <see cref="unblockModList"/> to undo.</returns>
    let blockModList (agent : AtpAgent) (listUri : AtUri) : Task<Result<ListBlockRef, XrpcError>> =
        task {
            let record =
                {| ``$type`` = AppBskyGraph.Listblock.TypeId
                   createdAt = nowTimestamp ()
                   subject = AtUri.value listUri |}

            let! result = createRecord agent "app.bsky.graph.listblock" record
            return result |> Result.map (fun o -> { ListBlockRef.Uri = o.Uri })
        }

    /// <summary>
    /// Unblock a previously blocked moderation list by deleting the list block record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="listBlockRef">The <see cref="ListBlockRef"/> returned by <see cref="blockModList"/>.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let unblockModList (agent : AtpAgent) (listBlockRef : ListBlockRef) : Task<Result<unit, XrpcError>> =
        deleteRecordImpl agent listBlockRef.Uri

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
            let! result = deleteRecordImpl agent likeRef.Uri
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
            let! result = deleteRecordImpl agent repostRef.Uri
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
            let! result = deleteRecordImpl agent followRef.Uri
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
            let! result = deleteRecordImpl agent blockRef.Uri
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
            let! result = deleteRecordImpl agent uri
            return result |> Result.map (fun () -> Undone)
        }

    // ── Target-based undo ──────────────────────────────────────────────

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let unlikePostImpl (agent : AtpAgent) (postRef : PostRef) : Task<Result<UndoResult, XrpcError>> =
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
                        let! result = deleteRecordImpl agent likeUri
                        return result |> Result.map (fun () -> Undone)
                    | None -> return Ok WasNotPresent
        }

    /// <summary>
    /// Unlike a post by its <see cref="PostRef"/>, without needing the original <see cref="LikeRef"/>.
    /// Fetches the post to find the current user's like URI from the viewer state,
    /// then deletes it.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">The post to unlike (a <see cref="PostRef"/> or <see cref="TimelinePost"/>).</param>
    /// <returns>
    /// <c>Ok Undone</c> if the like was found and deleted,
    /// <c>Ok WasNotPresent</c> if the post was not liked by the current user,
    /// or an <see cref="XrpcError"/> on failure.
    /// </returns>
    let inline unlikePost (agent : AtpAgent) (target : ^a) : Task<Result<UndoResult, XrpcError>> =
        unlikePostImpl agent (asPostRef target)

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let unrepostPostImpl (agent : AtpAgent) (postRef : PostRef) : Task<Result<UndoResult, XrpcError>> =
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
                        let! result = deleteRecordImpl agent repostUri
                        return result |> Result.map (fun () -> Undone)
                    | None -> return Ok WasNotPresent
        }

    /// <summary>
    /// Un-repost a post by its <see cref="PostRef"/>, without needing the original <see cref="RepostRef"/>.
    /// Fetches the post to find the current user's repost URI from the viewer state,
    /// then deletes it.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">The post to un-repost (a <see cref="PostRef"/> or <see cref="TimelinePost"/>).</param>
    /// <returns>
    /// <c>Ok Undone</c> if the repost was found and deleted,
    /// <c>Ok WasNotPresent</c> if the post was not reposted by the current user,
    /// or an <see cref="XrpcError"/> on failure.
    /// </returns>
    let inline unrepostPost (agent : AtpAgent) (target : ^a) : Task<Result<UndoResult, XrpcError>> =
        unrepostPostImpl agent (asPostRef target)

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

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let muteUserImpl (agent : AtpAgent) (did : Did) : Task<Result<unit, XrpcError>> =
        AppBskyGraph.MuteActor.call agent { Actor = Did.value did }

    /// <summary>
    /// Mute an account. Accepts a <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/> directly.
    /// Muted accounts are hidden from your feeds but not blocked.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">The user to mute — a <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/>.</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let inline muteUser (agent : AtpAgent) (target : ^a) : Task<Result<unit, XrpcError>> =
        muteUserImpl agent (toActorDid target)

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let unmuteUserImpl (agent : AtpAgent) (did : Did) : Task<Result<unit, XrpcError>> =
        AppBskyGraph.UnmuteActor.call agent { Actor = Did.value did }

    /// <summary>
    /// Unmute a previously muted account. Accepts a <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/> directly.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">The user to unmute — a <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/>.</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let inline unmuteUser (agent : AtpAgent) (target : ^a) : Task<Result<unit, XrpcError>> =
        unmuteUserImpl agent (toActorDid target)

    /// <summary>
    /// Mute an account by handle string. The handle is resolved to a DID, then the mute is created.
    /// Also accepts a DID string directly (if it starts with <c>did:</c>, it is parsed as a DID).
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="identifier">A handle (e.g., <c>my-handle.bsky.social</c>) or DID string (e.g., <c>did:plc:abc123</c>).</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    /// <remarks>
    /// For type-safe usage when you already have a <see cref="Did"/>, use <see cref="muteUser"/> instead.
    /// </remarks>
    let muteUserByHandle (agent : AtpAgent) (identifier : string) : Task<Result<unit, XrpcError>> =
        task {
            match! resolveIdentifier agent identifier with
            | Error e -> return Error e
            | Ok did -> return! muteUserImpl agent did
        }

    /// <summary>
    /// Unmute an account by handle string. The handle is resolved to a DID, then the unmute is performed.
    /// Also accepts a DID string directly (if it starts with <c>did:</c>, it is parsed as a DID).
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="identifier">A handle (e.g., <c>my-handle.bsky.social</c>) or DID string (e.g., <c>did:plc:abc123</c>).</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    /// <remarks>
    /// For type-safe usage when you already have a <see cref="Did"/>, use <see cref="unmuteUser"/> instead.
    /// </remarks>
    let unmuteUserByHandle (agent : AtpAgent) (identifier : string) : Task<Result<unit, XrpcError>> =
        task {
            match! resolveIdentifier agent identifier with
            | Error e -> return Error e
            | Ok did -> return! unmuteUserImpl agent did
        }

    /// <summary>
    /// Mute an entire moderation list. All accounts on the list are muted.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="listUri">The AT-URI of the moderation list to mute.</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let muteModList (agent : AtpAgent) (listUri : AtUri) : Task<Result<unit, XrpcError>> =
        AppBskyGraph.MuteActorList.call agent { List = listUri }

    /// <summary>
    /// Unmute a previously muted moderation list.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="listUri">The AT-URI of the moderation list to unmute.</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let unmuteModList (agent : AtpAgent) (listUri : AtUri) : Task<Result<unit, XrpcError>> =
        AppBskyGraph.UnmuteActorList.call agent { List = listUri }

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let muteThreadImpl (agent : AtpAgent) (root : AtUri) : Task<Result<unit, XrpcError>> =
        AppBskyGraph.MuteThread.call agent { Root = root }

    /// <summary>
    /// Mute a thread. Posts in the muted thread are hidden from your notifications.
    /// Accepts an <see cref="AtUri"/>, <see cref="PostRef"/>, or <see cref="TimelinePost"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="root">The thread root post (an <see cref="AtUri"/>, <see cref="PostRef"/>, or <see cref="TimelinePost"/>).</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let inline muteThread (agent : AtpAgent) (root : ^a) : Task<Result<unit, XrpcError>> =
        muteThreadImpl agent (toPostAtUri root)

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let unmuteThreadImpl (agent : AtpAgent) (root : AtUri) : Task<Result<unit, XrpcError>> =
        AppBskyGraph.UnmuteThread.call agent { Root = root }

    /// <summary>
    /// Unmute a previously muted thread.
    /// Accepts an <see cref="AtUri"/>, <see cref="PostRef"/>, or <see cref="TimelinePost"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="root">The thread root post (an <see cref="AtUri"/>, <see cref="PostRef"/>, or <see cref="TimelinePost"/>).</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let inline unmuteThread (agent : AtpAgent) (root : ^a) : Task<Result<unit, XrpcError>> =
        unmuteThreadImpl agent (toPostAtUri root)

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

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let addBookmarkImpl (agent : AtpAgent) (postRef : PostRef) : Task<Result<unit, XrpcError>> =
        AppBskyBookmark.CreateBookmark.call agent { Uri = postRef.Uri; Cid = postRef.Cid }

    /// <summary>
    /// Add a post to your bookmarks.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">A <see cref="PostRef"/> or <see cref="TimelinePost"/> identifying the post to bookmark.</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let inline addBookmark (agent : AtpAgent) (target : ^a) : Task<Result<unit, XrpcError>> =
        addBookmarkImpl agent (asPostRef target)

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let removeBookmarkImpl (agent : AtpAgent) (uri : AtUri) : Task<Result<unit, XrpcError>> =
        AppBskyBookmark.DeleteBookmark.call agent { Uri = uri }

    /// <summary>
    /// Remove a post from your bookmarks.
    /// Accepts an <see cref="AtUri"/>, <see cref="PostRef"/>, or <see cref="TimelinePost"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">The bookmarked post to remove (an <see cref="AtUri"/>, <see cref="PostRef"/>, or <see cref="TimelinePost"/>).</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let inline removeBookmark (agent : AtpAgent) (target : ^a) : Task<Result<unit, XrpcError>> =
        removeBookmarkImpl agent (toPostAtUri target)

    /// <summary>
    /// Update the authenticated user's handle.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="handle">The new <see cref="Handle"/> to set.</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let updateHandle (agent : AtpAgent) (handle : Handle) : Task<Result<unit, XrpcError>> =
        ComAtprotoIdentity.UpdateHandle.call agent { Handle = handle }

    // ── Video upload ─────────────────────────────────────────────────

    /// <summary>
    /// Upload a video to the video processing service.
    /// Returns a job status that can be polled with <see cref="getVideoJobStatus"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="data">The raw video bytes.</param>
    /// <param name="mimeType">The video MIME type.</param>
    /// <returns>A <see cref="AppBskyVideo.Defs.JobStatus"/> on success, or an <see cref="XrpcError"/>.</returns>
    let uploadVideo
        (agent : AtpAgent)
        (data : byte[])
        (mimeType : VideoMime)
        : Task<Result<AppBskyVideo.Defs.JobStatus, XrpcError>> =
        task {
            let url =
                System.Uri (agent.BaseUrl, sprintf "xrpc/%s" AppBskyVideo.UploadVideo.TypeId)

            let request = new HttpRequestMessage (HttpMethod.Post, url)
            request.Content <- new ByteArrayContent (data)
            request.Content.Headers.ContentType <- MediaTypeHeaderValue (VideoMime.toMimeString mimeType)

            match agent.Session with
            | Some session ->
                request.Headers.Authorization <- AuthenticationHeaderValue ("Bearer", session.AccessJwt)
            | None -> ()

            let! response = agent.HttpClient.SendAsync (request)

            if response.IsSuccessStatusCode then
                let! json = response.Content.ReadAsStringAsync ()

                try
                    let output = JsonSerializer.Deserialize<AppBskyVideo.UploadVideo.Output> (json, Json.options)
                    return Ok output.JobStatus
                with ex ->
                    return Error (toXrpcError (sprintf "Failed to parse upload response: %s" ex.Message))
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

    /// <summary>
    /// Get the status of a video processing job.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="jobId">The job ID returned by <see cref="uploadVideo"/>.</param>
    /// <returns>A <see cref="AppBskyVideo.Defs.JobStatus"/> on success, or an <see cref="XrpcError"/>.</returns>
    let getVideoJobStatus
        (agent : AtpAgent)
        (jobId : string)
        : Task<Result<AppBskyVideo.Defs.JobStatus, XrpcError>> =
        task {
            let! result = AppBskyVideo.GetJobStatus.query agent { JobId = jobId }
            return result |> Result.map (fun output -> output.JobStatus)
        }

    /// <summary>
    /// Poll a video processing job until it completes, then return the blob reference.
    /// Polls every 1.5 seconds, up to a maximum number of attempts.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="jobId">The job ID returned by <see cref="uploadVideo"/>.</param>
    /// <param name="maxAttempts">Maximum number of poll attempts (default 60, ~90 seconds).</param>
    /// <returns>A <see cref="BlobRef"/> on success, or an <see cref="XrpcError"/>.</returns>
    let awaitVideoProcessing
        (agent : AtpAgent)
        (jobId : string)
        (maxAttempts : int option)
        : Task<Result<BlobRef, XrpcError>> =
        let max = maxAttempts |> Option.defaultValue 60

        let rec poll (attempt : int) =
            task {
                if attempt >= max then
                    return Error (toXrpcError "Video processing timed out")
                else
                    let! result = getVideoJobStatus agent jobId

                    match result with
                    | Error e -> return Error e
                    | Ok status ->
                        match status.State with
                        | AppBskyVideo.Defs.JobStatusState.JOBSTATECOMPLETED ->
                            match status.Blob with
                            | Some blob -> return parseBlobRef blob
                            | None -> return Error (toXrpcError "Video completed but no blob reference")
                        | AppBskyVideo.Defs.JobStatusState.JOBSTATEFAILED ->
                            let msg =
                                status.Message
                                |> Option.defaultValue "Video processing failed"

                            return Error (toXrpcError msg)
                        | _ ->
                            do! Task.Delay 1500
                            return! poll (attempt + 1)
            }

        poll 0

    /// <summary>
    /// Upload a video, wait for processing, and create a post with the video embedded.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="text">The post text.</param>
    /// <param name="videoData">The raw video bytes.</param>
    /// <param name="mimeType">The video MIME type.</param>
    /// <param name="altText">Optional alt text for the video.</param>
    /// <returns>A <see cref="PostRef"/> on success, or an <see cref="XrpcError"/>.</returns>
    let postWithVideo
        (agent : AtpAgent)
        (text : string)
        (videoData : byte[])
        (mimeType : VideoMime)
        (altText : string option)
        : Task<Result<PostRef, XrpcError>> =
        task {
            let! uploadResult = uploadVideo agent videoData mimeType

            match uploadResult with
            | Error e -> return Error e
            | Ok jobStatus ->
                let! blobResult = awaitVideoProcessing agent jobStatus.JobId None

                match blobResult with
                | Error e -> return Error e
                | Ok blobRef ->
                    let embed =
                        {| ``$type`` = "app.bsky.embed.video"
                           video = blobRef.Json
                           alt = altText |}

                    let record =
                        {| ``$type`` = "app.bsky.feed.post"
                           text = text
                           embed = embed
                           createdAt = nowTimestamp () |}

                    let! result = createRecord agent "app.bsky.feed.post" record
                    return result |> Result.map toPostRef
        }

    // ── List management ─────────────────────────────────────────────────

    /// <summary>
    /// Create a new list.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="name">The name of the list.</param>
    /// <param name="purpose">The list purpose (curate list, mod list, or reference list).</param>
    /// <param name="description">Optional description for the list.</param>
    /// <returns>A <see cref="ListRef"/> on success, or an <see cref="XrpcError"/>.</returns>
    let createList
        (agent : AtpAgent)
        (name : string)
        (purpose : AppBskyGraph.Defs.ListPurpose)
        (description : string option)
        : Task<Result<ListRef, XrpcError>> =
        task {
            let purposeStr =
                JsonSerializer.Serialize (purpose, Json.options)
                |> fun s -> s.Trim ('"')

            let record =
                {| ``$type`` = AppBskyGraph.List.TypeId
                   createdAt = nowTimestamp ()
                   name = name
                   purpose = purposeStr
                   description = description |}

            let! result = createRecord agent "app.bsky.graph.list" record
            return result |> Result.map (fun o -> { ListRef.Uri = o.Uri })
        }

    /// <summary>
    /// Delete a list.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="listUri">The AT-URI of the list to delete.</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let deleteList (agent : AtpAgent) (listUri : AtUri) : Task<Result<unit, XrpcError>> =
        deleteRecordImpl agent listUri

    /// <summary>
    /// Add an account to a list.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="listUri">The AT-URI of the list.</param>
    /// <param name="subject">The DID of the account to add.</param>
    /// <returns>A <see cref="ListItemRef"/> on success, or an <see cref="XrpcError"/>.</returns>
    let addListItem
        (agent : AtpAgent)
        (listUri : AtUri)
        (subject : Did)
        : Task<Result<ListItemRef, XrpcError>> =
        task {
            let record =
                {| ``$type`` = AppBskyGraph.Listitem.TypeId
                   createdAt = nowTimestamp ()
                   list = AtUri.value listUri
                   subject = Did.value subject |}

            let! result = createRecord agent "app.bsky.graph.listitem" record
            return result |> Result.map (fun o -> { ListItemRef.Uri = o.Uri })
        }

    /// <summary>
    /// Remove an account from a list by deleting the list item record.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="listItemUri">The AT-URI of the list item record to remove.</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let removeListItem (agent : AtpAgent) (listItemUri : AtUri) : Task<Result<unit, XrpcError>> =
        deleteRecordImpl agent listItemUri

    /// <summary>
    /// Create a starter pack.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="name">The name of the starter pack.</param>
    /// <param name="listUri">The AT-URI of the list containing the starter pack members.</param>
    /// <param name="description">Optional description for the starter pack.</param>
    /// <param name="feedUris">Optional list of feed generator URIs to include.</param>
    /// <returns>A <see cref="StarterPackRef"/> on success, or an <see cref="XrpcError"/>.</returns>
    let createStarterPack
        (agent : AtpAgent)
        (name : string)
        (listUri : AtUri)
        (description : string option)
        (feedUris : AtUri list option)
        : Task<Result<StarterPackRef, XrpcError>> =
        task {
            let feeds =
                feedUris
                |> Option.map (List.map (fun u -> {| uri = AtUri.value u |}))

            let record =
                {| ``$type`` = AppBskyGraph.Starterpack.TypeId
                   createdAt = nowTimestamp ()
                   name = name
                   list = AtUri.value listUri
                   description = description
                   feeds = feeds |}

            let! result = createRecord agent "app.bsky.graph.starterpack" record
            return result |> Result.map (fun o -> { StarterPackRef.Uri = o.Uri })
        }

    /// <summary>
    /// Delete a starter pack.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="starterPackUri">The AT-URI of the starter pack to delete.</param>
    /// <returns><c>unit</c> on success, or an <see cref="XrpcError"/>.</returns>
    let deleteStarterPack (agent : AtpAgent) (starterPackUri : AtUri) : Task<Result<unit, XrpcError>> =
        deleteRecordImpl agent starterPackUri

    // ── Read convenience methods ────────────────────────────────────────

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let getProfileImpl (agent : AtpAgent) (actorStr : string) : Task<Result<Profile, XrpcError>> =
        task {
            let! result = AppBskyActor.GetProfile.query agent { Actor = actorStr }
            return result |> Result.map Profile.ofDetailed
        }

    /// <summary>
    /// Get a user's profile. Accepts a <see cref="Handle"/>, <see cref="Did"/>,
    /// <see cref="ProfileSummary"/>, or <see cref="Profile"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">A <see cref="Handle"/>, <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/>.</param>
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

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let getPostThreadImpl
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
    /// Get a post thread, returning a <see cref="ThreadNode"/> tree.
    /// Accepts an <see cref="AtUri"/>, <see cref="PostRef"/>, or <see cref="TimelinePost"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">The post (an <see cref="AtUri"/>, <see cref="PostRef"/>, or <see cref="TimelinePost"/>).</param>
    /// <param name="depth">How many levels of replies to include (optional, pass <c>None</c> for server default).</param>
    /// <param name="parentHeight">How many levels of parent context to include (optional, pass <c>None</c> for server default).</param>
    /// <returns>A <see cref="ThreadNode"/> tree on success, or an <see cref="XrpcError"/>.</returns>
    let inline getPostThread (agent : AtpAgent) (target : ^a) (depth : int64 option) (parentHeight : int64 option) : Task<Result<ThreadNode, XrpcError>> =
        getPostThreadImpl agent (toPostAtUri target) depth parentHeight

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let getPostThreadViewImpl
        (agent : AtpAgent)
        (uri : AtUri)
        (depth : int64 option)
        (parentHeight : int64 option)
        : Task<Result<ThreadPost option, XrpcError>> =
        task {
            let! result = getPostThreadImpl agent uri depth parentHeight

            return
                result
                |> Result.map (fun node ->
                    match node with
                    | ThreadNode.Post tp -> Some tp
                    | _ -> None)
        }

    /// <summary>
    /// Get a post thread, returning just the <see cref="ThreadPost"/> if available.
    /// Returns <c>Some threadPost</c> for normal threads, <c>None</c> for not-found or blocked posts.
    /// Accepts an <see cref="AtUri"/>, <see cref="PostRef"/>, or <see cref="TimelinePost"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">The post (an <see cref="AtUri"/>, <see cref="PostRef"/>, or <see cref="TimelinePost"/>).</param>
    /// <param name="depth">How many levels of replies to include (optional).</param>
    /// <param name="parentHeight">How many levels of parent context to include (optional).</param>
    /// <returns><c>Some ThreadPost</c> if the post is accessible, <c>None</c> if not found or blocked, or an <see cref="XrpcError"/>.</returns>
    let inline getPostThreadView (agent : AtpAgent) (target : ^a) (depth : int64 option) (parentHeight : int64 option) : Task<Result<ThreadPost option, XrpcError>> =
        getPostThreadViewImpl agent (toPostAtUri target) depth parentHeight

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

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let getFollowersImpl
        (agent : AtpAgent)
        (actorStr : string)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ProfileSummary>, XrpcError>> =
        task {
            let! result =
                AppBskyGraph.GetFollowers.query
                    agent
                    { Actor = actorStr
                      Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Followers |> List.map ProfileSummary.ofView
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get the followers of an actor. Accepts a <see cref="Handle"/>, <see cref="Did"/>,
    /// <see cref="ProfileSummary"/>, or <see cref="Profile"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">A <see cref="Handle"/>, <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/>.</param>
    /// <param name="limit">Maximum number of followers to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="ProfileSummary"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let inline getFollowers (agent : AtpAgent) (actor : ^a) (limit : int64 option) (cursor : string option) : Task<Result<Page<ProfileSummary>, XrpcError>> =
        getFollowersImpl agent (toActorString actor) limit cursor

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let getFollowsImpl
        (agent : AtpAgent)
        (actorStr : string)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ProfileSummary>, XrpcError>> =
        task {
            let! result =
                AppBskyGraph.GetFollows.query
                    agent
                    { Actor = actorStr
                      Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Follows |> List.map ProfileSummary.ofView
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get the accounts that an actor follows. Accepts a <see cref="Handle"/>, <see cref="Did"/>,
    /// <see cref="ProfileSummary"/>, or <see cref="Profile"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">A <see cref="Handle"/>, <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/>.</param>
    /// <param name="limit">Maximum number of follows to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="ProfileSummary"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let inline getFollows (agent : AtpAgent) (actor : ^a) (limit : int64 option) (cursor : string option) : Task<Result<Page<ProfileSummary>, XrpcError>> =
        getFollowsImpl agent (toActorString actor) limit cursor

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
    /// Lightweight actor search for autocomplete/typeahead. Returns a flat list (no pagination).
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="query">The search query string (prefix).</param>
    /// <param name="limit">Maximum number of actors to return (optional).</param>
    /// <returns>A list of <see cref="ProfileSummary"/> on success, or an <see cref="XrpcError"/>.</returns>
    let searchActorsTypeahead
        (agent : AtpAgent)
        (query : string)
        (limit : int64 option)
        : Task<Result<ProfileSummary list, XrpcError>> =
        task {
            let! result =
                AppBskyActor.SearchActorsTypeahead.query
                    agent
                    { Limit = limit
                      Q = Some query
                      Term = None }

            return result |> Result.map (fun output -> output.Actors |> List.map ProfileSummary.ofBasic)
        }

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let getAuthorFeedImpl
        (agent : AtpAgent)
        (actorStr : string)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<FeedItem>, XrpcError>> =
        task {
            let! result =
                AppBskyFeed.GetAuthorFeed.query
                    agent
                    { Actor = actorStr
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
    /// Get a specific user's feed (posts by that actor). Accepts a <see cref="Handle"/>, <see cref="Did"/>,
    /// <see cref="ProfileSummary"/>, or <see cref="Profile"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">A <see cref="Handle"/>, <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/>.</param>
    /// <param name="limit">Maximum number of posts to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="FeedItem"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let inline getAuthorFeed (agent : AtpAgent) (actor : ^a) (limit : int64 option) (cursor : string option) : Task<Result<Page<FeedItem>, XrpcError>> =
        getAuthorFeedImpl agent (toActorString actor) limit cursor

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let getActorLikesImpl
        (agent : AtpAgent)
        (actorStr : string)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<FeedItem>, XrpcError>> =
        task {
            let! result =
                AppBskyFeed.GetActorLikes.query
                    agent
                    { Actor = actorStr
                      Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Feed |> List.map FeedItem.ofFeedViewPost
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get the posts that a specific actor has liked. Accepts a <see cref="Handle"/>, <see cref="Did"/>,
    /// <see cref="ProfileSummary"/>, or <see cref="Profile"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">A <see cref="Handle"/>, <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/>.</param>
    /// <param name="limit">Maximum number of posts to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="FeedItem"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let inline getActorLikes (agent : AtpAgent) (actor : ^a) (limit : int64 option) (cursor : string option) : Task<Result<Page<FeedItem>, XrpcError>> =
        getActorLikesImpl agent (toActorString actor) limit cursor

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let getLikesImpl
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
    /// Get the accounts that have liked a specific post.
    /// Accepts an <see cref="AtUri"/>, <see cref="PostRef"/>, or <see cref="TimelinePost"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">The post (an <see cref="AtUri"/>, <see cref="PostRef"/>, or <see cref="TimelinePost"/>).</param>
    /// <param name="limit">Maximum number of likes to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="ProfileSummary"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let inline getLikes (agent : AtpAgent) (target : ^a) (limit : int64 option) (cursor : string option) : Task<Result<Page<ProfileSummary>, XrpcError>> =
        getLikesImpl agent (toPostAtUri target) limit cursor

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let getRepostedByImpl
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
    /// Get the accounts that have reposted a specific post.
    /// Accepts an <see cref="AtUri"/>, <see cref="PostRef"/>, or <see cref="TimelinePost"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">The post (an <see cref="AtUri"/>, <see cref="PostRef"/>, or <see cref="TimelinePost"/>).</param>
    /// <param name="limit">Maximum number of reposts to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="ProfileSummary"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let inline getRepostedBy (agent : AtpAgent) (target : ^a) (limit : int64 option) (cursor : string option) : Task<Result<Page<ProfileSummary>, XrpcError>> =
        getRepostedByImpl agent (toPostAtUri target) limit cursor

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let getQuotesImpl
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
    /// Get posts that quote a specific post.
    /// Accepts an <see cref="AtUri"/>, <see cref="PostRef"/>, or <see cref="TimelinePost"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">The post (an <see cref="AtUri"/>, <see cref="PostRef"/>, or <see cref="TimelinePost"/>).</param>
    /// <param name="limit">Maximum number of quotes to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="TimelinePost"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let inline getQuotes (agent : AtpAgent) (target : ^a) (limit : int64 option) (cursor : string option) : Task<Result<Page<TimelinePost>, XrpcError>> =
        getQuotesImpl agent (toPostAtUri target) limit cursor

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
    /// Get multiple profiles by their DIDs in a single request.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actors">A list of <see cref="Did"/> values identifying the actors.</param>
    /// <returns>A list of <see cref="Profile"/> on success, or an <see cref="XrpcError"/>.</returns>
    let getProfiles (agent : AtpAgent) (actors : Did list) : Task<Result<Profile list, XrpcError>> =
        task {
            let! result = AppBskyActor.GetProfiles.query agent { Actors = actors |> List.map Did.value }
            return result |> Result.map (fun output -> output.Profiles |> List.map Profile.ofDetailed)
        }

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let getSuggestedFollowsImpl (agent : AtpAgent) (actorStr : string) : Task<Result<ProfileSummary list, XrpcError>> =
        task {
            let! result = AppBskyGraph.GetSuggestedFollowsByActor.query agent { Actor = actorStr }
            return result |> Result.map (fun output -> output.Suggestions |> List.map ProfileSummary.ofView)
        }

    /// <summary>
    /// Get suggested accounts to follow based on a given actor. Accepts a <see cref="Handle"/>, <see cref="Did"/>,
    /// <see cref="ProfileSummary"/>, or <see cref="Profile"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">A <see cref="Handle"/>, <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/>.</param>
    /// <returns>A list of <see cref="ProfileSummary"/> on success, or an <see cref="XrpcError"/>.</returns>
    let inline getSuggestedFollows (agent : AtpAgent) (actor : ^a) : Task<Result<ProfileSummary list, XrpcError>> =
        getSuggestedFollowsImpl agent (toActorString actor)

    /// <summary>
    /// Get general account suggestions for the authenticated user.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="limit">Maximum number of suggestions to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="ProfileSummary"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let getSuggestions
        (agent : AtpAgent)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ProfileSummary>, XrpcError>> =
        task {
            let! result =
                AppBskyActor.GetSuggestions.query
                    agent
                    { Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Actors |> List.map ProfileSummary.ofView
                      Cursor = output.Cursor })
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

    /// <summary>
    /// Get the authenticated user's block list.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="limit">Maximum number of blocked accounts to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="ProfileSummary"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let getBlocks
        (agent : AtpAgent)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ProfileSummary>, XrpcError>> =
        task {
            let! result =
                AppBskyGraph.GetBlocks.query
                    agent
                    { Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Blocks |> List.map ProfileSummary.ofView
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get the authenticated user's mute list.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="limit">Maximum number of muted accounts to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="ProfileSummary"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let getMutes
        (agent : AtpAgent)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ProfileSummary>, XrpcError>> =
        task {
            let! result =
                AppBskyGraph.GetMutes.query
                    agent
                    { Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Mutes |> List.map ProfileSummary.ofView
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get a list's details and members.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="listUri">The AT-URI of the list.</param>
    /// <param name="limit">Maximum number of list items to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A <see cref="ListDetail"/> containing list metadata and member profiles, or an <see cref="XrpcError"/>.</returns>
    let getList
        (agent : AtpAgent)
        (listUri : AtUri)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<ListDetail, XrpcError>> =
        task {
            let! result =
                AppBskyGraph.GetList.query
                    agent
                    { List = listUri
                      Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { List = ListView.ofGenerated output.List
                      Items = output.Items |> List.map (fun item -> ProfileSummary.ofView item.Subject)
                      Cursor = output.Cursor })
        }

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let getListsImpl
        (agent : AtpAgent)
        (actorStr : string)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ListView>, XrpcError>> =
        task {
            let! result =
                AppBskyGraph.GetLists.query
                    agent
                    { Actor = actorStr
                      Cursor = cursor
                      Limit = limit
                      Purposes = None }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Lists |> List.map ListView.ofGenerated
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get lists created by an actor. Accepts a <see cref="Handle"/>, <see cref="Did"/>,
    /// <see cref="ProfileSummary"/>, or <see cref="Profile"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">A <see cref="Handle"/>, <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/>.</param>
    /// <param name="limit">Maximum number of lists to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="ListView"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let inline getLists (agent : AtpAgent) (actor : ^a) (limit : int64 option) (cursor : string option) : Task<Result<Page<ListView>, XrpcError>> =
        getListsImpl agent (toActorString actor) limit cursor

    /// <summary>
    /// Get the relationships between the authenticated user and one or more other actors.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">The primary actor DID to check relationships for.</param>
    /// <param name="others">Optional list of other DIDs to check relationships with.</param>
    /// <returns>A list of <see cref="Relationship"/> on success, or an <see cref="XrpcError"/>.</returns>
    let getRelationships
        (agent : AtpAgent)
        (actor : Did)
        (others : Did list option)
        : Task<Result<Relationship list, XrpcError>> =
        task {
            let! result =
                AppBskyGraph.GetRelationships.query
                    agent
                    { Actor = Did.value actor
                      Others = others |> Option.map (List.map Did.value) }

            return
                result
                |> Result.map (fun output ->
                    output.Relationships
                    |> List.choose (fun item ->
                        match item with
                        | AppBskyGraph.GetRelationships.OutputRelationshipsItem.Relationship r ->
                            Some(Relationship.ofGenerated r)
                        | _ -> None))
        }

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let getKnownFollowersImpl
        (agent : AtpAgent)
        (actorStr : string)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ProfileSummary>, XrpcError>> =
        task {
            let! result =
                AppBskyGraph.GetKnownFollowers.query
                    agent
                    { Actor = actorStr
                      Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Followers |> List.map ProfileSummary.ofView
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get followers of an actor that the authenticated user also follows.
    /// Accepts a <see cref="Handle"/>, <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">A <see cref="Handle"/>, <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/>.</param>
    /// <param name="limit">Maximum number of known followers to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="ProfileSummary"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let inline getKnownFollowers (agent : AtpAgent) (actor : ^a) (limit : int64 option) (cursor : string option) : Task<Result<Page<ProfileSummary>, XrpcError>> =
        getKnownFollowersImpl agent (toActorString actor) limit cursor

    /// <summary>
    /// Get posts from a custom feed generator.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="feedUri">The AT-URI of the feed generator.</param>
    /// <param name="limit">Maximum number of posts to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="FeedItem"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let getFeed
        (agent : AtpAgent)
        (feedUri : AtUri)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<FeedItem>, XrpcError>> =
        task {
            let! result =
                AppBskyFeed.GetFeed.query
                    agent
                    { Feed = feedUri
                      Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Feed |> List.map FeedItem.ofFeedViewPost
                      Cursor = output.Cursor })
        }

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let getActorFeedsImpl
        (agent : AtpAgent)
        (actorStr : string)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<FeedGenerator>, XrpcError>> =
        task {
            let! result =
                AppBskyFeed.GetActorFeeds.query
                    agent
                    { Actor = actorStr
                      Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Feeds |> List.map FeedGenerator.ofGeneratorViewOnly
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get feed generators created by an actor. Accepts a <see cref="Handle"/>, <see cref="Did"/>,
    /// <see cref="ProfileSummary"/>, or <see cref="Profile"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">A <see cref="Handle"/>, <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/>.</param>
    /// <param name="limit">Maximum number of feed generators to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="FeedGenerator"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let inline getActorFeeds (agent : AtpAgent) (actor : ^a) (limit : int64 option) (cursor : string option) : Task<Result<Page<FeedGenerator>, XrpcError>> =
        getActorFeedsImpl agent (toActorString actor) limit cursor

    /// <summary>
    /// Get posts from a list feed.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="listUri">The AT-URI of the list.</param>
    /// <param name="limit">Maximum number of posts to return (optional).</param>
    /// <param name="cursor">Pagination cursor from a previous response (optional).</param>
    /// <returns>A page of <see cref="FeedItem"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let getListFeed
        (agent : AtpAgent)
        (listUri : AtUri)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<FeedItem>, XrpcError>> =
        task {
            let! result =
                AppBskyFeed.GetListFeed.query
                    agent
                    { List = listUri
                      Cursor = cursor
                      Limit = limit }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Feed |> List.map FeedItem.ofFeedViewPost
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get details about a specific feed generator.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="feedUri">The AT-URI of the feed generator.</param>
    /// <returns>A <see cref="FeedGenerator"/> on success, or an <see cref="XrpcError"/>.</returns>
    let getFeedGenerator
        (agent : AtpAgent)
        (feedUri : AtUri)
        : Task<Result<FeedGenerator, XrpcError>> =
        task {
            let! result = AppBskyFeed.GetFeedGenerator.query agent { Feed = feedUri }

            return
                result
                |> Result.map (fun output ->
                    FeedGenerator.ofGeneratorView output.View output.IsOnline output.IsValid)
        }

    // ── Profile upsert ─────────────────────────────────────────────────

    /// <summary>
    /// Read-modify-write the authenticated user's profile with CAS (compare-and-swap) retry.
    /// Reads the current profile, applies your update function, and writes the result back.
    /// Automatically retries (up to 3 times) if another write conflicts.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="updateFn">
    /// A function that receives the current profile (or <c>None</c> if the user has no profile record)
    /// and returns the updated profile to write.
    /// </param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    /// <example>
    /// <code>
    /// let! result = Bluesky.upsertProfile agent (fun currentProfile ->
    ///     let current = currentProfile |> Option.defaultValue { DisplayName = None; Description = None; Avatar = None; Banner = None; CreatedAt = None; JoinedViaStarterPack = None; Labels = None; Pinnedpost = None }
    ///     { current with DisplayName = Some "New Name" })
    /// </code>
    /// </example>
    let upsertProfile
        (agent : AtpAgent)
        (updateFn : AppBskyActor.Profile.Profile option -> AppBskyActor.Profile.Profile)
        : Task<Result<unit, XrpcError>> =
        let maxRetries = 3

        let rec attempt (retryCount : int) =
            task {
                match sessionDid agent with
                | Error e -> return Error e
                | Ok did ->
                    let collection = Nsid.parse "app.bsky.actor.profile" |> Result.defaultWith failwith
                    let rkey = RecordKey.parse "self" |> Result.defaultWith failwith

                    // Step 1: Read the current profile record
                    let! getResult =
                        ComAtprotoRepo.GetRecord.query
                            agent
                            { Repo = Did.value did
                              Collection = collection
                              Rkey = rkey
                              Cid = None }

                    let currentProfile, currentCid =
                        match getResult with
                        | Ok output ->
                            let profile =
                                try
                                    Some (JsonSerializer.Deserialize<AppBskyActor.Profile.Profile> (output.Value, Json.options))
                                with _ ->
                                    None

                            (profile, output.Cid)
                        | Error e when e.Error = Some "RecordNotFound" -> (None, None)
                        | Error e -> (None, None) // Treat other errors as "no record" for first-time profile creation

                    // Step 2: Apply the update function
                    let updatedProfile = updateFn currentProfile

                    // Step 3: Serialize and inject $type
                    let jsonStr = JsonSerializer.Serialize (updatedProfile, Json.options)
                    let mutable doc = JsonDocument.Parse (jsonStr)
                    let dict = System.Collections.Generic.Dictionary<string, JsonElement> ()
                    dict.["$type"] <- JsonSerializer.SerializeToElement ("app.bsky.actor.profile", Json.options)

                    for prop in doc.RootElement.EnumerateObject () do
                        dict.[prop.Name] <- prop.Value.Clone ()

                    let recordElement = JsonSerializer.SerializeToElement (dict, Json.options)

                    // Step 4: PutRecord with CAS
                    let! putResult =
                        ComAtprotoRepo.PutRecord.call
                            agent
                            { Repo = Did.value did
                              Collection = collection
                              Rkey = rkey
                              Record = recordElement
                              SwapRecord = currentCid
                              SwapCommit = None
                              Validate = None }

                    match putResult with
                    | Ok _ -> return Ok ()
                    | Error e when e.Error = Some "InvalidSwap" && retryCount < maxRetries ->
                        return! attempt (retryCount + 1)
                    | Error e -> return Error e
            }

        attempt 0

    // ── Preference mutation ──────────────────────────────────────────

    /// Get the $type tag from a preference JsonElement.
    let private prefType (el : JsonElement) : string option =
        match el.TryGetProperty ("$type") with
        | true, prop when prop.ValueKind = JsonValueKind.String -> Some (prop.GetString ())
        | _ -> None

    /// Deserialize a JsonElement to a typed preference record using Json.options.
    let private deserializePref<'T> (el : JsonElement) : 'T =
        JsonSerializer.Deserialize<'T> (el.GetRawText (), Json.options)

    /// Serialize a preference record back to JsonElement, injecting the $type tag.
    let private serializePref (typeTag : string) (value : obj) : JsonElement =
        let json = JsonSerializer.Serialize (value, Json.options)
        let mutable doc = JsonDocument.Parse (json)
        let dict = System.Collections.Generic.Dictionary<string, JsonElement> ()
        dict.["$type"] <- JsonSerializer.SerializeToElement (typeTag, Json.options)

        for prop in doc.RootElement.EnumerateObject () do
            dict.[prop.Name] <- prop.Value.Clone ()

        JsonSerializer.SerializeToElement (dict, Json.options)

    /// Find, update, or insert a preference item by $type tag.
    let private upsertPrefItem
        (typeTag : string)
        (update : JsonElement option -> JsonElement)
        (prefs : JsonElement list)
        : JsonElement list =
        let mutable found = false

        let updated =
            prefs
            |> List.map (fun el ->
                if prefType el = Some typeTag then
                    found <- true
                    update (Some el)
                else
                    el)

        if found then updated else updated @ [ update None ]

    /// <summary>
    /// Read-modify-write the authenticated user's preferences.
    /// Reads current preferences, applies your transform function, and writes the result back.
    /// Preferences use last-write-wins semantics (no CAS).
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="updateFn">A function that receives the current preferences list and returns the updated list.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let upsertPreferences
        (agent : AtpAgent)
        (updateFn : JsonElement list -> JsonElement list)
        : Task<Result<unit, XrpcError>> =
        task {
            let! getResult = getPreferences agent

            match getResult with
            | Error e -> return Error e
            | Ok currentPrefs ->
                let updatedPrefs = updateFn currentPrefs
                return! AppBskyActor.PutPreferences.call agent { Preferences = updatedPrefs }
        }

    /// <summary>
    /// Add a saved feed to the user's preferences.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="feed">The <see cref="AppBskyActor.Defs.SavedFeed"/> to add.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let addSavedFeed
        (agent : AtpAgent)
        (feed : AppBskyActor.Defs.SavedFeed)
        : Task<Result<unit, XrpcError>> =
        let typeTag = "app.bsky.actor.defs#savedFeedsPrefV2"

        upsertPreferences agent (fun prefs ->
            upsertPrefItem typeTag (fun existing ->
                let current =
                    existing
                    |> Option.map deserializePref<AppBskyActor.Defs.SavedFeedsPrefV2>
                    |> Option.defaultValue { Items = [] }

                serializePref typeTag { current with Items = current.Items @ [ feed ] }) prefs)

    /// <summary>
    /// Remove a saved feed from the user's preferences by its ID.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="feedId">The ID of the saved feed to remove.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let removeSavedFeed (agent : AtpAgent) (feedId : string) : Task<Result<unit, XrpcError>> =
        let typeTag = "app.bsky.actor.defs#savedFeedsPrefV2"

        upsertPreferences agent (fun prefs ->
            upsertPrefItem typeTag (fun existing ->
                let current =
                    existing
                    |> Option.map deserializePref<AppBskyActor.Defs.SavedFeedsPrefV2>
                    |> Option.defaultValue { Items = [] }

                serializePref
                    typeTag
                    { current with
                        Items = current.Items |> List.filter (fun f -> f.Id <> feedId) }) prefs)

    /// <summary>
    /// Add a muted word to the user's preferences.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="word">The <see cref="AppBskyActor.Defs.MutedWord"/> to add.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let addMutedWord
        (agent : AtpAgent)
        (word : AppBskyActor.Defs.MutedWord)
        : Task<Result<unit, XrpcError>> =
        let typeTag = "app.bsky.actor.defs#mutedWordsPref"

        upsertPreferences agent (fun prefs ->
            upsertPrefItem typeTag (fun existing ->
                let current =
                    existing
                    |> Option.map deserializePref<AppBskyActor.Defs.MutedWordsPref>
                    |> Option.defaultValue { Items = [] }

                serializePref typeTag { current with Items = current.Items @ [ word ] }) prefs)

    /// <summary>
    /// Remove a muted word from the user's preferences by value.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="wordValue">The muted word string to remove.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let removeMutedWord (agent : AtpAgent) (wordValue : string) : Task<Result<unit, XrpcError>> =
        let typeTag = "app.bsky.actor.defs#mutedWordsPref"

        upsertPreferences agent (fun prefs ->
            upsertPrefItem typeTag (fun existing ->
                let current =
                    existing
                    |> Option.map deserializePref<AppBskyActor.Defs.MutedWordsPref>
                    |> Option.defaultValue { Items = [] }

                serializePref
                    typeTag
                    { current with
                        Items = current.Items |> List.filter (fun w -> w.Value <> wordValue) }) prefs)

    /// <summary>
    /// Set the visibility for a content label.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="label">The label identifier (e.g. "nsfw", "gore").</param>
    /// <param name="visibility">The desired visibility setting.</param>
    /// <param name="labelerDid">Optional labeler DID (for custom labelers).</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let setContentLabelPref
        (agent : AtpAgent)
        (label : string)
        (visibility : AppBskyActor.Defs.ContentLabelPrefVisibility)
        (labelerDid : Did option)
        : Task<Result<unit, XrpcError>> =
        let typeTag = "app.bsky.actor.defs#contentLabelPref"

        upsertPreferences agent (fun prefs ->
            // Content label prefs are individual items (one per label), not a single container
            let matchesLabel (el : JsonElement) =
                prefType el = Some typeTag
                && (match el.TryGetProperty ("label") with
                    | true, v -> v.GetString () = label
                    | _ -> false)
                && (match el.TryGetProperty ("labelerDid") with
                    | true, v when v.ValueKind = JsonValueKind.String ->
                        labelerDid |> Option.map Did.value = Some(v.GetString ())
                    | _ -> labelerDid.IsNone)

            let newPref : AppBskyActor.Defs.ContentLabelPref =
                { Label = label
                  Visibility = visibility
                  LabelerDid = labelerDid }

            let newElement = serializePref typeTag newPref
            let mutable found = false

            let updated =
                prefs
                |> List.map (fun el ->
                    if matchesLabel el then
                        found <- true
                        newElement
                    else
                        el)

            if found then updated else updated @ [ newElement ])

    /// <summary>
    /// Enable or disable adult content in the user's preferences.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="enabled">Whether adult content should be enabled.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let setAdultContentEnabled (agent : AtpAgent) (enabled : bool) : Task<Result<unit, XrpcError>> =
        let typeTag = "app.bsky.actor.defs#adultContentPref"

        upsertPreferences agent (fun prefs ->
            upsertPrefItem typeTag (fun _ ->
                serializePref typeTag ({ Enabled = enabled } : AppBskyActor.Defs.AdultContentPref)) prefs)

    /// <summary>
    /// Set thread view preferences (sort order).
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="sort">The desired thread sort order.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let setThreadViewPref
        (agent : AtpAgent)
        (sort : AppBskyActor.Defs.ThreadViewPrefSort)
        : Task<Result<unit, XrpcError>> =
        let typeTag = "app.bsky.actor.defs#threadViewPref"

        upsertPreferences agent (fun prefs ->
            upsertPrefItem typeTag (fun _ ->
                serializePref typeTag ({ Sort = Some sort } : AppBskyActor.Defs.ThreadViewPref)) prefs)

    /// <summary>
    /// Add a post URI to the hidden posts list.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="postUri">The AT-URI of the post to hide.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let addHiddenPost (agent : AtpAgent) (postUri : AtUri) : Task<Result<unit, XrpcError>> =
        let typeTag = "app.bsky.actor.defs#hiddenPostsPref"

        upsertPreferences agent (fun prefs ->
            upsertPrefItem typeTag (fun existing ->
                let current =
                    existing
                    |> Option.map deserializePref<AppBskyActor.Defs.HiddenPostsPref>
                    |> Option.defaultValue { Items = [] }

                serializePref typeTag { current with Items = current.Items @ [ postUri ] }) prefs)

    /// <summary>
    /// Remove a post URI from the hidden posts list.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="postUri">The AT-URI of the post to unhide.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let removeHiddenPost (agent : AtpAgent) (postUri : AtUri) : Task<Result<unit, XrpcError>> =
        let typeTag = "app.bsky.actor.defs#hiddenPostsPref"

        upsertPreferences agent (fun prefs ->
            upsertPrefItem typeTag (fun existing ->
                let current =
                    existing
                    |> Option.map deserializePref<AppBskyActor.Defs.HiddenPostsPref>
                    |> Option.defaultValue { Items = [] }

                serializePref typeTag { current with Items = current.Items |> List.filter (fun u -> u <> postUri) })
                prefs)

    // ── Sync / binary endpoints ────────────────────────────────────────

    /// <summary>
    /// Fetch a blob by DID and CID from the repository sync endpoint.
    /// Returns the raw blob bytes.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="did">The DID of the repository that owns the blob.</param>
    /// <param name="cid">The CID of the blob to fetch.</param>
    /// <returns>The raw blob bytes, or an <see cref="XrpcError"/>.</returns>
    let getBlob (agent : AtpAgent) (did : Did) (cid : Cid) : Task<Result<byte[], XrpcError>> =
        Xrpc.queryBinary<ComAtprotoSync.GetBlob.Params>
            ComAtprotoSync.GetBlob.TypeId
            { Did = did; Cid = cid }
            agent

    /// <summary>
    /// Fetch a full repository as a CAR (Content Addressable aRchive) file.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="did">The DID of the repository to fetch.</param>
    /// <returns>A parsed <see cref="CarFile"/>, or an <see cref="XrpcError"/>.</returns>
    let getRepo (agent : AtpAgent) (did : Did) : Task<Result<CarFile, XrpcError>> =
        task {
            let! result =
                Xrpc.queryBinary<ComAtprotoSync.GetRepo.Params>
                    ComAtprotoSync.GetRepo.TypeId
                    { Did = did; Since = None }
                    agent

            return
                result
                |> Result.bind (fun bytes ->
                    CarParser.parse bytes
                    |> Result.mapError (fun msg ->
                        { StatusCode = 0
                          Error = Some "CarParseError"
                          Message = Some msg }))
        }

    /// <summary>
    /// Fetch a single record as a CAR (Content Addressable aRchive) file from the repository sync endpoint.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="did">The DID of the repository that owns the record.</param>
    /// <param name="collection">The NSID of the record collection (e.g. <c>"app.bsky.feed.post"</c>).</param>
    /// <param name="rkey">The record key within the collection.</param>
    /// <returns>A parsed <see cref="CarFile"/>, or an <see cref="XrpcError"/>.</returns>
    let getRecord (agent : AtpAgent) (did : Did) (collection : Nsid) (rkey : RecordKey) : Task<Result<CarFile, XrpcError>> =
        task {
            let! result =
                Xrpc.queryBinary<ComAtprotoSync.GetRecord.Params>
                    ComAtprotoSync.GetRecord.TypeId
                    { Did = did; Collection = collection; Rkey = rkey }
                    agent

            return
                result
                |> Result.bind (fun bytes ->
                    CarParser.parse bytes
                    |> Result.mapError (fun msg ->
                        { StatusCode = 0
                          Error = Some "CarParseError"
                          Message = Some msg }))
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

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let paginateFollowersImpl
        (agent : AtpAgent)
        (actorStr : string)
        (pageSize : int64 option)
        : System.Collections.Generic.IAsyncEnumerable<Result<Page<ProfileSummary>, XrpcError>> =
        Xrpc.paginate<AppBskyGraph.GetFollowers.Params, AppBskyGraph.GetFollowers.Output>
            AppBskyGraph.GetFollowers.TypeId
            { Actor = actorStr
              Cursor = None
              Limit = pageSize }
            (fun o -> o.Cursor)
            (fun c p -> { p with Cursor = c })
            agent
        |> mapAsyncEnum (fun output ->
            { Items = output.Followers |> List.map ProfileSummary.ofView
              Cursor = output.Cursor })

    /// <summary>
    /// Paginate followers for an actor. Returns an async enumerable of pages.
    /// Each element is a <c>Result</c> containing one page of follower profiles.
    /// Pagination stops automatically when the server returns no cursor.
    /// Accepts a <see cref="Handle"/>, <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="actor">A <see cref="Handle"/>, <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/> whose followers to list.</param>
    /// <param name="pageSize">Maximum number of followers per page (optional, pass <c>None</c> for server default).</param>
    /// <returns>An <see cref="System.Collections.Generic.IAsyncEnumerable{T}"/> of paginated results.</returns>
    let inline paginateFollowers (agent : AtpAgent) (actor : ^a) (pageSize : int64 option) : System.Collections.Generic.IAsyncEnumerable<Result<Page<ProfileSummary>, XrpcError>> =
        paginateFollowersImpl agent (toActorString actor) pageSize

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

    /// <summary>
    /// Paginate the authenticated user's block list. Returns an async enumerable of pages.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="pageSize">Maximum number of blocked accounts per page (optional).</param>
    /// <returns>An <see cref="System.Collections.Generic.IAsyncEnumerable{T}"/> of paginated results.</returns>
    let paginateBlocks
        (agent : AtpAgent)
        (pageSize : int64 option)
        : System.Collections.Generic.IAsyncEnumerable<Result<Page<ProfileSummary>, XrpcError>> =
        Xrpc.paginate<AppBskyGraph.GetBlocks.Params, AppBskyGraph.GetBlocks.Output>
            AppBskyGraph.GetBlocks.TypeId
            { Cursor = None
              Limit = pageSize }
            (fun o -> o.Cursor)
            (fun c p -> { p with Cursor = c })
            agent
        |> mapAsyncEnum (fun output ->
            { Items = output.Blocks |> List.map ProfileSummary.ofView
              Cursor = output.Cursor })

    /// <summary>
    /// Paginate the authenticated user's mute list. Returns an async enumerable of pages.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="pageSize">Maximum number of muted accounts per page (optional).</param>
    /// <returns>An <see cref="System.Collections.Generic.IAsyncEnumerable{T}"/> of paginated results.</returns>
    let paginateMutes
        (agent : AtpAgent)
        (pageSize : int64 option)
        : System.Collections.Generic.IAsyncEnumerable<Result<Page<ProfileSummary>, XrpcError>> =
        Xrpc.paginate<AppBskyGraph.GetMutes.Params, AppBskyGraph.GetMutes.Output>
            AppBskyGraph.GetMutes.TypeId
            { Cursor = None
              Limit = pageSize }
            (fun o -> o.Cursor)
            (fun c p -> { p with Cursor = c })
            agent
        |> mapAsyncEnum (fun output ->
            { Items = output.Mutes |> List.map ProfileSummary.ofView
              Cursor = output.Cursor })

    /// <summary>
    /// Paginate a custom feed. Returns an async enumerable of pages.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="feedUri">The AT-URI of the feed generator.</param>
    /// <param name="pageSize">Maximum number of posts per page (optional).</param>
    /// <returns>An <see cref="System.Collections.Generic.IAsyncEnumerable{T}"/> of paginated results.</returns>
    let paginateFeed
        (agent : AtpAgent)
        (feedUri : AtUri)
        (pageSize : int64 option)
        : System.Collections.Generic.IAsyncEnumerable<Result<Page<FeedItem>, XrpcError>> =
        Xrpc.paginate<AppBskyFeed.GetFeed.Params, AppBskyFeed.GetFeed.Output>
            AppBskyFeed.GetFeed.TypeId
            { Feed = feedUri
              Cursor = None
              Limit = pageSize }
            (fun o -> o.Cursor)
            (fun c p -> { p with Cursor = c })
            agent
        |> mapAsyncEnum (fun output ->
            { Items = output.Feed |> List.map FeedItem.ofFeedViewPost
              Cursor = output.Cursor })

    /// <summary>
    /// Paginate a list feed. Returns an async enumerable of pages.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="listUri">The AT-URI of the list.</param>
    /// <param name="pageSize">Maximum number of posts per page (optional).</param>
    /// <returns>An <see cref="System.Collections.Generic.IAsyncEnumerable{T}"/> of paginated results.</returns>
    let paginateListFeed
        (agent : AtpAgent)
        (listUri : AtUri)
        (pageSize : int64 option)
        : System.Collections.Generic.IAsyncEnumerable<Result<Page<FeedItem>, XrpcError>> =
        Xrpc.paginate<AppBskyFeed.GetListFeed.Params, AppBskyFeed.GetListFeed.Output>
            AppBskyFeed.GetListFeed.TypeId
            { List = listUri
              Cursor = None
              Limit = pageSize }
            (fun o -> o.Cursor)
            (fun c p -> { p with Cursor = c })
            agent
        |> mapAsyncEnum (fun output ->
            { Items = output.Feed |> List.map FeedItem.ofFeedViewPost
              Cursor = output.Cursor })

    // ── Account management ────────────────────────────────────────────

    /// <summary>
    /// Create a new account on the given PDS and return an authenticated agent.
    /// Only the handle is required; email, password, and invite code are optional.
    /// </summary>
    /// <param name="baseUrl">The PDS base URL (e.g. <c>"https://bsky.social"</c>).</param>
    /// <param name="handle">The requested handle for the new account.</param>
    /// <param name="email">Optional email address for the account.</param>
    /// <param name="password">Optional password. May need to meet instance-specific strength requirements.</param>
    /// <param name="inviteCode">Optional invite code, if the PDS requires one.</param>
    /// <returns>An authenticated <see cref="AtpAgent"/> on success, or an <see cref="XrpcError"/>.</returns>
    let createAccount
        (baseUrl : string)
        (handle : Handle)
        (email : string option)
        (password : string option)
        (inviteCode : string option)
        : Task<Result<AtpAgent, XrpcError>> =
        task {
            let agent = AtpAgent.create baseUrl

            let! result =
                ComAtprotoServer.CreateAccount.call
                    agent
                    { Handle = handle
                      Email = email
                      Password = password
                      InviteCode = inviteCode
                      Did = None
                      PlcOp = None
                      RecoveryKey = None
                      VerificationCode = None
                      VerificationPhone = None }

            return
                result
                |> Result.map (fun output ->
                    let session : AtpSession =
                        { AccessJwt = output.AccessJwt
                          RefreshJwt = output.RefreshJwt
                          Did = output.Did
                          Handle = output.Handle }

                    agent.Session <- Some session
                    agent)
        }

    /// <summary>
    /// Request account deletion. Sends a confirmation email to the account's email address.
    /// After receiving the email, call <see cref="deleteAccount"/> with the token from the email.
    /// Requires an authenticated session.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let requestAccountDelete (agent : AtpAgent) : Task<Result<unit, XrpcError>> =
        task {
            match agent.Session with
            | None -> return Error notLoggedInError
            | Some session ->
                let url = $"{agent.BaseUrl}xrpc/{ComAtprotoServer.RequestAccountDelete.TypeId}"
                let request = new HttpRequestMessage (HttpMethod.Post, url)
                request.Headers.Authorization <- AuthenticationHeaderValue ("Bearer", session.AccessJwt)

                for (key, value) in agent.ExtraHeaders do
                    request.Headers.TryAddWithoutValidation (key, value) |> ignore

                let! response = agent.HttpClient.SendAsync (request)

                if response.IsSuccessStatusCode then
                    return Ok ()
                else
                    let! body = response.Content.ReadAsStringAsync ()

                    try
                        let doc = JsonDocument.Parse (body)
                        let root = doc.RootElement

                        let error =
                            match root.TryGetProperty ("error") with
                            | true, v -> Some (v.GetString ())
                            | false, _ -> None

                        let message =
                            match root.TryGetProperty ("message") with
                            | true, v -> Some (v.GetString ())
                            | false, _ -> None

                        return
                            Error
                                { StatusCode = int response.StatusCode
                                  Error = error
                                  Message = message }
                    with _ ->
                        return
                            Error
                                { StatusCode = int response.StatusCode
                                  Error = None
                                  Message = Some body }
        }

    /// <summary>
    /// Delete the authenticated user's account. Requires a token from <see cref="requestAccountDelete"/>
    /// (sent via email) and the account password.
    /// After successful deletion, the agent's session is cleared.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="password">The account password.</param>
    /// <param name="token">The deletion token received via email after calling <see cref="requestAccountDelete"/>.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let deleteAccount (agent : AtpAgent) (password : string) (token : string) : Task<Result<unit, XrpcError>> =
        match sessionDid agent with
        | Error e -> Task.FromResult(Error e)
        | Ok did ->
            task {
                let! result =
                    ComAtprotoServer.DeleteAccount.call
                        agent
                        { Did = did
                          Password = password
                          Token = token }

                match result with
                | Ok () ->
                    agent.Session <- None
                    return Ok ()
                | Error e -> return Error e
            }

    // ── Via attribution ───────────────────────────────────────────────

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let likeViaImpl
        (agent : AtpAgent)
        (postRef : PostRef)
        (viaRef : PostRef)
        : Task<Result<LikeRef, XrpcError>> =
        task {
            let record =
                {| ``$type`` = AppBskyFeed.Like.TypeId
                   createdAt = nowTimestamp ()
                   subject =
                    {| uri = AtUri.value postRef.Uri
                       cid = Cid.value postRef.Cid |}
                   via =
                    {| uri = AtUri.value viaRef.Uri
                       cid = Cid.value viaRef.Cid |} |}

            let! result = createRecord agent "app.bsky.feed.like" record
            return result |> Result.map (fun o -> { LikeRef.Uri = o.Uri })
        }

    /// <summary>
    /// Like a post with via attribution. The <paramref name="via"/> parameter records where
    /// the user discovered the content (e.g. a feed generator post or a quote post).
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">A <see cref="PostRef"/> or <see cref="TimelinePost"/> identifying the record to like.</param>
    /// <param name="via">A <see cref="PostRef"/> or <see cref="TimelinePost"/> identifying how the content was discovered.</param>
    /// <returns>A <see cref="LikeRef"/> on success, or an <see cref="XrpcError"/>.</returns>
    let inline likeVia (agent : AtpAgent) (target : ^a) (via : ^b) : Task<Result<LikeRef, XrpcError>> =
        likeViaImpl agent (asPostRef target) (asPostRef via)

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let repostViaImpl
        (agent : AtpAgent)
        (postRef : PostRef)
        (viaRef : PostRef)
        : Task<Result<RepostRef, XrpcError>> =
        task {
            let record =
                {| ``$type`` = AppBskyFeed.Repost.TypeId
                   createdAt = nowTimestamp ()
                   subject =
                    {| uri = AtUri.value postRef.Uri
                       cid = Cid.value postRef.Cid |}
                   via =
                    {| uri = AtUri.value viaRef.Uri
                       cid = Cid.value viaRef.Cid |} |}

            let! result = createRecord agent "app.bsky.feed.repost" record
            return result |> Result.map (fun o -> { RepostRef.Uri = o.Uri })
        }

    /// <summary>
    /// Repost with via attribution. The <paramref name="via"/> parameter records where
    /// the user discovered the content.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">A <see cref="PostRef"/> or <see cref="TimelinePost"/> identifying the record to repost.</param>
    /// <param name="via">A <see cref="PostRef"/> or <see cref="TimelinePost"/> identifying how the content was discovered.</param>
    /// <returns>A <see cref="RepostRef"/> on success, or an <see cref="XrpcError"/>.</returns>
    let inline repostVia (agent : AtpAgent) (target : ^a) (via : ^b) : Task<Result<RepostRef, XrpcError>> =
        repostViaImpl agent (asPostRef target) (asPostRef via)

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let followViaImpl
        (agent : AtpAgent)
        (did : Did)
        (viaRef : PostRef)
        : Task<Result<FollowRef, XrpcError>> =
        task {
            let record =
                {| ``$type`` = AppBskyGraph.Follow.TypeId
                   createdAt = nowTimestamp ()
                   subject = Did.value did
                   via =
                    {| uri = AtUri.value viaRef.Uri
                       cid = Cid.value viaRef.Cid |} |}

            let! result = createRecord agent "app.bsky.graph.follow" record
            return result |> Result.map (fun o -> { FollowRef.Uri = o.Uri })
        }

    /// <summary>
    /// Follow a user with via attribution. The <paramref name="via"/> parameter records where
    /// the user discovered the account (e.g. a post that introduced the account).
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="target">The user to follow -- a <see cref="Did"/>, <see cref="ProfileSummary"/>, or <see cref="Profile"/>.</param>
    /// <param name="via">A <see cref="PostRef"/> or <see cref="TimelinePost"/> identifying how the account was discovered.</param>
    /// <returns>A <see cref="FollowRef"/> on success, or an <see cref="XrpcError"/>.</returns>
    let inline followVia (agent : AtpAgent) (target : ^a) (via : ^b) : Task<Result<FollowRef, XrpcError>> =
        followViaImpl agent (toActorDid target) (asPostRef via)

    // ── Labeler convenience methods ────────────────────────────────────

    /// <summary>
    /// Get labeler service views for the given DIDs.
    /// Returns the detailed view for each labeler, which includes label value definitions.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="dids">List of labeler DIDs to look up.</param>
    /// <returns>A list of <see cref="AppBskyLabeler.Defs.LabelerViewDetailed"/> on success, or an <see cref="XrpcError"/>.</returns>
    let getLabelers
        (agent : AtpAgent)
        (dids : Did list)
        : Task<Result<AppBskyLabeler.Defs.LabelerViewDetailed list, XrpcError>> =
        task {
            let! result =
                AppBskyLabeler.GetServices.query
                    agent
                    { Dids = dids
                      Detailed = Some true }

            return
                result
                |> Result.map (fun output ->
                    output.Views
                    |> List.choose (fun v ->
                        match v with
                        | AppBskyLabeler.GetServices.OutputViewsItem.LabelerViewDetailed detailed -> Some detailed
                        | _ -> None))
        }

    /// <summary>
    /// Get custom label definitions published by a labeler.
    /// Returns a list of <see cref="ComAtprotoLabel.Defs.LabelValueDefinition"/> from the labeler's policies.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="labelerDid">The DID of the labeler to query.</param>
    /// <returns>A list of <see cref="ComAtprotoLabel.Defs.LabelValueDefinition"/> on success, or an <see cref="XrpcError"/>.</returns>
    let getLabelDefinitions
        (agent : AtpAgent)
        (labelerDid : Did)
        : Task<Result<ComAtprotoLabel.Defs.LabelValueDefinition list, XrpcError>> =
        task {
            let! result = getLabelers agent [ labelerDid ]

            return
                result
                |> Result.map (fun labelers ->
                    labelers
                    |> List.tryHead
                    |> Option.bind (fun l -> l.Policies.LabelValueDefinitions)
                    |> Option.defaultValue [])
        }

// ── Test factory ──────────────────────────────────────────────────

/// <summary>
/// Factory methods for creating domain objects with sensible defaults.
/// Useful for testing consumers of the library without needing a live API.
/// All parameters are optional; unspecified fields use deterministic default values.
/// </summary>
type TestFactory private () =

    static let defaultDid = Did.parse "did:plc:testfactory" |> Result.defaultWith failwith
    static let defaultHandle = Handle.parse "test.bsky.social" |> Result.defaultWith failwith
    static let defaultAtUri = AtUri.parse "at://did:plc:testfactory/app.bsky.feed.post/default" |> Result.defaultWith failwith
    static let defaultCid = Cid.parse "bafyreiaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" |> Result.defaultWith failwith

    /// <summary>
    /// Create a <see cref="PostRef"/> with sensible defaults. Override any field by passing named parameters.
    /// </summary>
    static member PostRef (?uri : AtUri, ?cid : Cid) : PostRef =
        { Uri = uri |> Option.defaultValue defaultAtUri
          Cid = cid |> Option.defaultValue defaultCid }

    /// <summary>
    /// Create a <see cref="ProfileSummary"/> with sensible defaults. Override any field by passing named parameters.
    /// </summary>
    static member ProfileSummary (?did : Did, ?handle : Handle, ?displayName : string, ?avatar : string) : ProfileSummary =
        { Did = did |> Option.defaultValue defaultDid
          Handle = handle |> Option.defaultValue defaultHandle
          DisplayName = displayName |> Option.defaultValue "Test User"
          Avatar = avatar }

    /// <summary>
    /// Create a <see cref="Profile"/> with sensible defaults. Override any field by passing named parameters.
    /// </summary>
    static member Profile
        (?did : Did,
         ?handle : Handle,
         ?displayName : string,
         ?description : string,
         ?avatar : string,
         ?banner : string,
         ?postsCount : int64,
         ?followersCount : int64,
         ?followsCount : int64,
         ?isFollowing : bool,
         ?isFollowedBy : bool,
         ?isBlocking : bool,
         ?isBlockedBy : bool,
         ?isMuted : bool)
        : Profile =
        { Did = did |> Option.defaultValue defaultDid
          Handle = handle |> Option.defaultValue defaultHandle
          DisplayName = displayName |> Option.defaultValue "Test User"
          Description = description |> Option.defaultValue ""
          Avatar = avatar
          Banner = banner
          PostsCount = postsCount |> Option.defaultValue 0L
          FollowersCount = followersCount |> Option.defaultValue 0L
          FollowsCount = followsCount |> Option.defaultValue 0L
          IsFollowing = isFollowing |> Option.defaultValue false
          IsFollowedBy = isFollowedBy |> Option.defaultValue false
          IsBlocking = isBlocking |> Option.defaultValue false
          IsBlockedBy = isBlockedBy |> Option.defaultValue false
          IsMuted = isMuted |> Option.defaultValue false }

    /// <summary>
    /// Create a <see cref="TimelinePost"/> with sensible defaults. Override any field by passing named parameters.
    /// </summary>
    static member TimelinePost
        (?uri : AtUri,
         ?cid : Cid,
         ?author : ProfileSummary,
         ?text : string,
         ?facets : AppBskyRichtext.Facet.Facet list,
         ?likeCount : int64,
         ?repostCount : int64,
         ?replyCount : int64,
         ?quoteCount : int64,
         ?indexedAt : DateTimeOffset,
         ?isLiked : bool,
         ?isReposted : bool,
         ?isBookmarked : bool)
        : TimelinePost =
        { Uri = uri |> Option.defaultValue defaultAtUri
          Cid = cid |> Option.defaultValue defaultCid
          Author = author |> Option.defaultValue (TestFactory.ProfileSummary ())
          Text = text |> Option.defaultValue "Test post"
          Facets = facets |> Option.defaultValue []
          LikeCount = likeCount |> Option.defaultValue 0L
          RepostCount = repostCount |> Option.defaultValue 0L
          ReplyCount = replyCount |> Option.defaultValue 0L
          QuoteCount = quoteCount |> Option.defaultValue 0L
          IndexedAt = indexedAt |> Option.defaultValue DateTimeOffset.UtcNow
          IsLiked = isLiked |> Option.defaultValue false
          IsReposted = isReposted |> Option.defaultValue false
          IsBookmarked = isBookmarked |> Option.defaultValue false
          Embed = None }

    /// <summary>
    /// Create a <see cref="LikeRef"/> with a default AT-URI. Override by passing a uri.
    /// </summary>
    static member LikeRef (?uri : AtUri) : LikeRef =
        let defaultLikeUri =
            AtUri.parse "at://did:plc:testfactory/app.bsky.feed.like/default"
            |> Result.defaultWith failwith

        { Uri = uri |> Option.defaultValue defaultLikeUri }

    /// <summary>
    /// Create a <see cref="RepostRef"/> with a default AT-URI. Override by passing a uri.
    /// </summary>
    static member RepostRef (?uri : AtUri) : RepostRef =
        let defaultRepostUri =
            AtUri.parse "at://did:plc:testfactory/app.bsky.feed.repost/default"
            |> Result.defaultWith failwith

        { Uri = uri |> Option.defaultValue defaultRepostUri }

    /// <summary>
    /// Create a <see cref="FollowRef"/> with a default AT-URI. Override by passing a uri.
    /// </summary>
    static member FollowRef (?uri : AtUri) : FollowRef =
        let defaultFollowUri =
            AtUri.parse "at://did:plc:testfactory/app.bsky.graph.follow/default"
            |> Result.defaultWith failwith

        { Uri = uri |> Option.defaultValue defaultFollowUri }

    /// <summary>
    /// Create a <see cref="BlockRef"/> with a default AT-URI. Override by passing a uri.
    /// </summary>
    static member BlockRef (?uri : AtUri) : BlockRef =
        let defaultBlockUri =
            AtUri.parse "at://did:plc:testfactory/app.bsky.graph.block/default"
            |> Result.defaultWith failwith

        { Uri = uri |> Option.defaultValue defaultBlockUri }

    /// <summary>
    /// Create a <see cref="FeedItem"/> wrapping a <see cref="TimelinePost"/> with no feed reason.
    /// </summary>
    static member FeedItem (?post : TimelinePost, ?reason : FeedReason, ?replyParent : TimelinePost) : FeedItem =
        { Post = post |> Option.defaultValue (TestFactory.TimelinePost ())
          Reason = reason
          ReplyParent = replyParent }

    /// <summary>
    /// Create a <see cref="Notification"/> with sensible defaults.
    /// </summary>
    static member Notification
        (?kind : NotificationKind,
         ?author : ProfileSummary,
         ?subjectUri : AtUri,
         ?isRead : bool,
         ?indexedAt : DateTimeOffset)
        : Notification =
        { Kind = kind |> Option.defaultValue NotificationKind.Like
          Author = author |> Option.defaultValue (TestFactory.ProfileSummary ())
          SubjectUri = subjectUri
          IsRead = isRead |> Option.defaultValue false
          IndexedAt = indexedAt |> Option.defaultValue DateTimeOffset.UtcNow }
