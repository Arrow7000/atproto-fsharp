// BskyBotExample — comprehensive demonstration of the FSharp.ATProto library.
//
// The core workflow (sections 2-14) uses the taskResult computation
// expression, which chains Task<Result<'T, 'E>> operations with automatic
// error short-circuiting — no nested match expressions needed. Compare
// with the standalone sections (15-19) that use explicit match expressions.
//
// To run (make this the last file in the .fsproj to use as entry point):
//   export BSKY_HANDLE="yourhandle.bsky.social"
//   export BSKY_PASSWORD="your-app-password"
//   dotnet run --project examples/BskyBotExample
//
// NOTE: This performs real actions on the network. Use a test account!

module Program

open System
open System.Text.Json
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax

let env key =
    match Environment.GetEnvironmentVariable key with
    | null -> failwithf "Set %s environment variable" key
    | v -> v

let section title = printfn "\n══════ %s ══════" title

let main _ =
    task {
        // ─────────────────────────────────────────────────────────────
        // 1. AUTHENTICATION
        // Bluesky.login creates an agent, authenticates, and returns
        // the authenticated agent in one step.
        // ─────────────────────────────────────────────────────────────
        section "1. Authentication"

        let! loginResult = Bluesky.login "https://bsky.social" (env "BSKY_HANDLE") (env "BSKY_PASSWORD")

        let agent =
            match loginResult with
            | Ok a ->
                let session = a.Session.Value
                printfn "Logged in as @%s (%s)" (Handle.value session.Handle) (Did.value session.Did)
                a
            | Error e -> failwithf "Login failed: %A" e

        let session = agent.Session.Value

        // ═══════════════════════════════════════════════════════════════
        // Core workflow — taskResult CE
        //
        // The taskResult CE chains Task<Result<'T, XrpcError>> calls
        // with automatic error short-circuiting. Each let! unwraps
        // the Ok value; any Error aborts the chain immediately.
        // Compare this flat style with the standalone sections below
        // (15-19) which use explicit match expressions.
        // ═══════════════════════════════════════════════════════════════

        let! workflowResult =
            taskResult {

                // ─────────────────────────────────────────────────────
                // 2. READ PROFILES
                // getProfile accepts a Handle, Did, or string directly.
                // ─────────────────────────────────────────────────────
                section "2. Read Profiles"

                let! ownProfile = Bluesky.getProfile agent session.Handle
                printfn "Own profile: @%s (%s)" (Handle.value ownProfile.Handle) (Did.value ownProfile.Did)
                printfn "  Display name: %s" ownProfile.DisplayName

                printfn
                    "  Posts: %d, Followers: %d, Following: %d"
                    ownProfile.PostsCount
                    ownProfile.FollowersCount
                    ownProfile.FollowsCount

                let bskyHandle = Handle.parse "bsky.app" |> Result.defaultWith failwith
                let! otherProfile = Bluesky.getProfile agent bskyHandle
                printfn "Other profile: @%s" (Handle.value otherProfile.Handle)

                printfn
                    "  Bio: %s"
                    (otherProfile.Description
                     |> fun s -> if s.Length > 80 then s.[..79] + "..." else s)

                printfn "  Following: %b, Followed by: %b" otherProfile.IsFollowing otherProfile.IsFollowedBy

                // ─────────────────────────────────────────────────────
                // 3. TIMELINE
                // ─────────────────────────────────────────────────────
                section "3. Timeline"

                let! tl = Bluesky.getTimeline agent (Some 10L) None

                printfn "Timeline: %d posts (cursor: %s)" tl.Items.Length (tl.Cursor |> Option.defaultValue "(end)")

                for item in tl.Items do
                    let prefix =
                        match item.Reason with
                        | Some (FeedReason.Repost _) -> "[repost] "
                        | Some FeedReason.Pin -> "[pinned] "
                        | None -> ""

                    let text = item.Post.Text
                    let truncated = if text.Length > 60 then text.[..59] + "..." else text
                    printfn "  %s@%s: %s" prefix (Handle.value item.Post.Author.Handle) truncated

                    printfn
                        "    likes: %d, reposts: %d, replies: %d"
                        item.Post.LikeCount
                        item.Post.RepostCount
                        item.Post.ReplyCount

                // ─────────────────────────────────────────────────────
                // 4. POSTING
                //   a) Auto-detected rich text
                //   b) Explicitly plain text
                //   c) Pre-built facets (manual byte offsets)
                //   d) Quote post (embeds another post)
                // ─────────────────────────────────────────────────────
                section "4. Posting"

                // 4a. Auto-detected rich text
                let! postRef = Bluesky.post agent "Hello from F#! Visit https://atproto.com #atproto"
                printfn "Auto-detected post: %s" (AtUri.value postRef.Uri)

                // 4b. Explicitly plain text — pass empty facets to skip detection
                let! plainRef = Bluesky.postWithFacets agent "Just a plain text post, no detection." []
                printfn "Plain text post: %s" (AtUri.value plainRef.Uri)

                // 4c. Pre-built facets — full control over byte offsets
                let facetText = "Check example.com for details"
                let linkStart = int64 (RichText.byteLength "Check ")
                let linkEnd = int64 (RichText.byteLength "Check example.com")

                match Uri.parse "https://example.com" with
                | Ok linkUri ->
                    let manualFacets : AppBskyRichtext.Facet.Facet list =
                        [ { Index =
                              { ByteStart = linkStart
                                ByteEnd = linkEnd }
                            Features = [ AppBskyRichtext.Facet.FacetFeaturesItem.Link { Uri = linkUri } ] } ]

                    let! facetRef = Bluesky.postWithFacets agent facetText manualFacets
                    printfn "Manual facet post: %s" (AtUri.value facetRef.Uri)
                | Error _ -> printfn "Skipped manual facet post (URI parse error)"

                // 4d. Quote post — embeds another post below your text
                let! quoteRef = Bluesky.quotePost agent "Interesting take! #atproto" postRef
                printfn "Quote post: %s" (AtUri.value quoteRef.Uri)

                // ─────────────────────────────────────────────────────
                // 5. REPLYING
                // replyTo auto-resolves the thread root from the parent.
                // ─────────────────────────────────────────────────────
                section "5. Replying"

                match tl.Items with
                | first :: _ ->
                    let! replyRef = Bluesky.replyTo agent "Great post!" first.Post
                    printfn "replyTo (auto-root): %s" (AtUri.value replyRef.Uri)
                | [] -> ()

                // replyWithKnownRoot — when you have both parent and root refs
                let! topReply = Bluesky.replyTo agent "Replying to myself!" postRef
                let! nestedReply = Bluesky.replyWithKnownRoot agent "Nested reply!" topReply postRef
                printfn "Nested reply: %s" (AtUri.value nestedReply.Uri)

                // ─────────────────────────────────────────────────────
                // 6. LIKE / UNLIKE
                // ─────────────────────────────────────────────────────
                section "6. Like / Unlike"

                let! likeRef = Bluesky.like agent postRef
                printfn "Liked: %s" (AtUri.value likeRef.Uri)

                let! undoLike = Bluesky.undoLike agent likeRef

                match undoLike with
                | Undone -> printfn "Unliked successfully"
                | WasNotPresent -> printfn "Was not liked"

                // ─────────────────────────────────────────────────────
                // 7. REPOST / UNREPOST
                // ─────────────────────────────────────────────────────
                section "7. Repost / Unrepost"

                let! repostRef = Bluesky.repost agent postRef
                printfn "Reposted: %s" (AtUri.value repostRef.Uri)

                let! undoRepost = Bluesky.undoRepost agent repostRef

                match undoRepost with
                | Undone -> printfn "Unreposted successfully"
                | WasNotPresent -> printfn "Was not reposted"

                // ─────────────────────────────────────────────────────
                // 8. FOLLOW / UNFOLLOW
                // ─────────────────────────────────────────────────────
                section "8. Follow / Unfollow"

                let! followRef = Bluesky.follow agent session.Did
                printfn "Followed (by DID): %s" (AtUri.value followRef.Uri)

                let! undoFollow = Bluesky.undoFollow agent followRef

                match undoFollow with
                | Undone -> printfn "Unfollowed successfully"
                | WasNotPresent -> printfn "Was not following"

                let! followByHandleRef = Bluesky.followByHandle agent (Handle.value session.Handle)
                printfn "Followed (by handle): %s" (AtUri.value followByHandleRef.Uri)
                let! _ = Bluesky.undoFollow agent followByHandleRef
                printfn "Unfollowed"

                // ─────────────────────────────────────────────────────
                // 9. BLOCK / UNBLOCK
                // ─────────────────────────────────────────────────────
                section "9. Block / Unblock"

                let! blockRef = Bluesky.block agent session.Did
                printfn "Blocked: %s" (AtUri.value blockRef.Uri)

                let! undoBlock = Bluesky.undoBlock agent blockRef

                match undoBlock with
                | Undone -> printfn "Unblocked successfully"
                | WasNotPresent -> printfn "Was not blocked"

                let! blockByHandleRef = Bluesky.blockByHandle agent (Handle.value session.Handle)
                printfn "Blocked (by handle): %s" (AtUri.value blockByHandleRef.Uri)
                let! _ = Bluesky.undoBlock agent blockByHandleRef
                printfn "Unblocked"

                // ─────────────────────────────────────────────────────
                // 10. DELETE
                // ─────────────────────────────────────────────────────
                section "10. Delete"

                let! _ = Bluesky.deleteRecord agent postRef.Uri
                printfn "Deleted post: %s" (AtUri.value postRef.Uri)
                let! _ = Bluesky.deleteRecord agent plainRef.Uri
                printfn "Cleaned up plain text post"

                // ─────────────────────────────────────────────────────
                // 11. FOLLOWERS / FOLLOWS
                // ─────────────────────────────────────────────────────
                section "11. Followers / Follows"

                let! followers = Bluesky.getFollowers agent session.Handle (Some 5L) None
                printfn "Followers: %d (showing first page)" followers.Items.Length

                for f in followers.Items |> List.truncate 5 do
                    printfn "  @%s (%s)" (Handle.value f.Handle) (Did.value f.Did)

                let! follows = Bluesky.getFollows agent session.Handle (Some 5L) None
                printfn "Following: %d (showing first page)" follows.Items.Length

                for f in follows.Items |> List.truncate 5 do
                    printfn "  @%s (%s)" (Handle.value f.Handle) (Did.value f.Did)

                // ─────────────────────────────────────────────────────
                // 12. NOTIFICATIONS
                // Each notification has a typed Content DU with associated data.
                // ─────────────────────────────────────────────────────
                section "12. Notifications"

                let! notifs = Bluesky.getNotifications agent (Some 10L) None
                printfn "Notifications: %d" notifs.Items.Length

                for notif in notifs.Items do
                    let reasonStr =
                        match notif.Content with
                        | NotificationContent.Like _ -> "like"
                        | NotificationContent.Repost _ -> "repost"
                        | NotificationContent.Follow -> "follow"
                        | NotificationContent.Mention _ -> "mention"
                        | NotificationContent.Reply _ -> "reply"
                        | NotificationContent.Quote _ -> "quote"
                        | NotificationContent.StarterpackJoined _ -> "starterpack-joined"
                        | NotificationContent.Unknown s -> sprintf "unknown(%s)" s

                    printfn "  [%s] from @%s (read: %b)" reasonStr (Handle.value notif.Author.Handle) notif.IsRead

                // ─────────────────────────────────────────────────────
                // 13. POST THREAD
                // getPostThreadView returns Some ThreadViewPost or None.
                // ─────────────────────────────────────────────────────
                section "13. Post Thread"

                if tl.Items.Length > 0 then
                    let threadUri = tl.Items.[0].Post.Uri

                    let! threadView = Bluesky.getPostThreadView agent threadUri (Some 6L) (Some 3L)

                    match threadView with
                    | Some tvp ->
                        let postText = tvp.Post.Text

                        printfn
                            "Thread root: @%s — %s"
                            (Handle.value tvp.Post.Author.Handle)
                            (if postText.Length > 50 then
                                 postText.[..49] + "..."
                             else
                                 postText)

                        match tvp.Parent with
                        | Some (ThreadNode.Post parent) ->
                            printfn "  Parent by @%s" (Handle.value parent.Post.Author.Handle)
                        | Some (ThreadNode.NotFound _) ->
                            printfn "  Parent not found (deleted?)"
                        | Some (ThreadNode.Blocked _) -> printfn "  Parent blocked"
                        | None -> printfn "  (top-level post, no parent)"

                        let replyCount = tvp.Replies.Length
                        printfn "  Replies: %d" replyCount

                        for r in tvp.Replies |> List.truncate 3 do
                            match r with
                            | ThreadNode.Post rtvp ->
                                printfn "    @%s replied" (Handle.value rtvp.Post.Author.Handle)
                            | _ -> printfn "    (non-post reply node)"
                    | None -> printfn "Thread not found or blocked"

                    // Full getPostThread — returns a ThreadNode DU
                    let! rawThread = Bluesky.getPostThread agent threadUri (Some 1L) None

                    match rawThread with
                    | ThreadNode.Post _ -> printfn "Raw thread: Post"
                    | ThreadNode.NotFound _ -> printfn "Raw thread: NotFound"
                    | ThreadNode.Blocked _ -> printfn "Raw thread: Blocked"
                else
                    printfn "(no timeline posts to fetch thread for)"

                // ─────────────────────────────────────────────────────
                // 14. CHAT / DIRECT MESSAGES
                // All Chat.* functions auto-apply the chat proxy header.
                // ─────────────────────────────────────────────────────
                section "14. Chat / DMs"

                let! convos = Chat.listConvos agent (Some 10L) None
                printfn "Conversations: %d" convos.Items.Length

                for c in convos.Items do
                    let members =
                        c.Members |> List.map (fun m -> Handle.value m.Handle) |> String.concat ", "

                    printfn "  %s (members: %s, unread: %d, muted: %b)" c.Id members c.UnreadCount c.IsMuted

                let! convo = Chat.getConvoForMembers agent [ session.Did ]
                printfn "Convo: %s (members: %d)" convo.Id convo.Members.Length

                // Chat.sendMessage auto-detects rich text facets (links, mentions, etc.)
                let! msg = Chat.sendMessage agent convo.Id "Hello from the F# ATProto bot!"

                match msg with
                | ChatMessage.Message m -> printfn "Sent: \"%s\" (id: %s)" m.Text m.Id
                | ChatMessage.Deleted d -> printfn "Sent but deleted: %s" d.Id

                let msgId =
                    match msg with
                    | ChatMessage.Message m -> m.Id
                    | ChatMessage.Deleted d -> d.Id

                let! _ = Chat.deleteMessage agent convo.Id msgId
                printfn "Deleted message: %s" msgId

                let! msgs = Chat.getMessages agent convo.Id (Some 5L) None
                printfn "Messages: %d" msgs.Items.Length

                for m in msgs.Items do
                    match m with
                    | ChatMessage.Message mv ->
                        printfn
                            "  [%s] %s"
                            (Did.value mv.Sender)
                            (if mv.Text.Length > 40 then
                                 mv.Text.[..39] + "..."
                             else
                                 mv.Text)
                    | ChatMessage.Deleted dv ->
                        printfn "  [deleted by %s]" (Did.value dv.Sender)

                let! _ = Chat.markRead agent convo.Id
                printfn "Marked read"

                let! readCount = Chat.markAllRead agent
                printfn "Marked all read (updated: %d)" readCount

                let! _ = Chat.muteConvo agent convo.Id
                printfn "Muted convo"

                let! _ = Chat.unmuteConvo agent convo.Id
                printfn "Unmuted convo"

                // Rich text DM — sendMessage already auto-detects facets,
                // so no need for manual facet construction or raw XRPC calls
                let! richDm = Chat.sendMessage agent convo.Id "Check out https://atproto.com!"

                match richDm with
                | ChatMessage.Message m -> printfn "Rich DM: \"%s\" (id: %s)" m.Text m.Id
                | ChatMessage.Deleted _ -> printfn "Rich DM was deleted"

                return ()
            }

        match workflowResult with
        | Ok () -> ()
        | Error e ->
            printfn "\nWorkflow error:"
            printfn "  Status: %d" e.StatusCode
            printfn "  Error: %s" (e.Error |> Option.defaultValue "(none)")
            printfn "  Message: %s" (e.Message |> Option.defaultValue "(none)")

        // ═══════════════════════════════════════════════════════════════
        // Standalone sections
        //
        // These sections use different error types, non-Result returns,
        // or intentionally trigger errors, so they stay outside the
        // taskResult chain with explicit match expressions.
        // ═══════════════════════════════════════════════════════════════

        // ─────────────────────────────────────────────────────────────
        // 15. IDENTITY RESOLUTION
        // Uses IdentityError (not XrpcError), so can't chain with
        // the core workflow above.
        // ─────────────────────────────────────────────────────────────
        section "15. Identity Resolution"

        let! identityResult = Identity.resolveIdentity agent (Handle.value session.Handle)

        match identityResult with
        | Ok id ->
            printfn "Resolved identity:"
            printfn "  DID: %s" (Did.value id.Did)

            printfn
                "  Handle: %s (verified: %b)"
                (id.Handle |> Option.map Handle.value |> Option.defaultValue "(none)")
                id.Handle.IsSome

            printfn "  PDS: %s" (id.PdsEndpoint |> Option.map Uri.value |> Option.defaultValue "(unknown)")
            printfn "  Signing key: %s" (id.SigningKey |> Option.defaultValue "(none)")
        | Error e -> printfn "Identity resolution failed: %A" e

        let! didResult = Identity.resolveDid agent session.Did

        match didResult with
        | Ok id -> printfn "Resolved DID -> PDS: %s" (id.PdsEndpoint |> Option.map Uri.value |> Option.defaultValue "?")
        | Error e -> printfn "resolveDid failed: %A" e

        let! handleResult = Identity.resolveHandle agent session.Handle

        match handleResult with
        | Ok did -> printfn "Handle -> DID: %s" (Did.value did)
        | Error e -> printfn "resolveHandle failed: %A" e

        let sampleDoc =
            """{"id":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","alsoKnownAs":["at://atproto.com"],"service":[{"id":"did:plc:ewvi7nxzyoun6zhxrhs64oiz#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://bsky.network"}]}"""

        let docElement = JsonSerializer.Deserialize<JsonElement> (sampleDoc)

        match Identity.parseDidDocument docElement with
        | Ok parsed ->
            printfn
                "Parsed DID doc: %s (handle: %s)"
                (Did.value parsed.Did)
                (parsed.Handle |> Option.map Handle.value |> Option.defaultValue "(none)")
        | Error msg -> printfn "Parse failed: %s" msg

        // ─────────────────────────────────────────────────────────────
        // 16. RICH TEXT PROCESSING
        // RichText functions return sync values or Task<list> (not
        // Task<Result>), so they use the outer task CE directly.
        // ─────────────────────────────────────────────────────────────
        section "16. Rich Text"

        let sampleText = "Hello @bsky.app! Visit https://atproto.com #decentralization"

        let detected = RichText.detect sampleText
        printfn "Detected %d facets in: \"%s\"" detected.Length sampleText

        for d in detected do
            match d with
            | RichText.DetectedMention (s, e, h) -> printfn "  Mention: @%s [byte %d..%d]" h s e
            | RichText.DetectedLink (s, e, u) -> printfn "  Link: %s [byte %d..%d]" u s e
            | RichText.DetectedTag (s, e, t) -> printfn "  Tag: #%s [byte %d..%d]" t s e

        let! resolved = RichText.resolve agent detected
        printfn "Resolved %d facets (unresolvable mentions dropped)" resolved.Length

        let! combined = RichText.parse agent sampleText
        printfn "Parse returned %d facets" combined.Length

        let graphemes = RichText.graphemeLength sampleText
        let bytes = RichText.byteLength sampleText
        printfn "Text length: %d graphemes, %d UTF-8 bytes" graphemes bytes

        let emojiText = "Hello 👨‍👩‍👧‍👦!"
        printfn "Emoji text: \"%s\"" emojiText

        printfn
            "  %d graphemes, %d chars, %d bytes"
            (RichText.graphemeLength emojiText)
            emojiText.Length
            (RichText.byteLength emojiText)

        // ─────────────────────────────────────────────────────────────
        // 17. PAGINATION
        // Pre-built paginators wrap cursor-based XRPC queries into
        // IAsyncEnumerable that lazily fetches one page at a time.
        // ─────────────────────────────────────────────────────────────
        section "17. Pagination"

        printfn "Paginated timeline:"

        let timelinePages = Bluesky.paginateTimeline agent (Some 5L)

        let mutable pageNum = 0
        let enumerator = timelinePages.GetAsyncEnumerator ()
        let mutable hasMore = true

        while hasMore && pageNum < 3 do
            let! moved = enumerator.MoveNextAsync ()
            hasMore <- moved

            if hasMore then
                match enumerator.Current with
                | Ok page ->
                    pageNum <- pageNum + 1

                    printfn
                        "  Page %d: %d posts (cursor: %s)"
                        pageNum
                        page.Items.Length
                        (page.Cursor |> Option.defaultValue "(end)")
                | Error e ->
                    printfn "  Page error: %A" e
                    hasMore <- false

        printfn "Paginated followers:"

        let followerPages =
            Bluesky.paginateFollowers agent session.Handle (Some 10L)

        let followerEnum = followerPages.GetAsyncEnumerator ()
        let! hasFirst = followerEnum.MoveNextAsync ()

        if hasFirst then
            match followerEnum.Current with
            | Ok page ->
                printfn "  First page: %d followers" page.Items.Length

                for f in page.Items |> List.truncate 5 do
                    printfn "    @%s (%s)" (Handle.value f.Handle) (Did.value f.Did)
            | Error e -> printfn "  Followers error: %A" e

        // ─────────────────────────────────────────────────────────────
        // 18. IMAGES / BLOB UPLOAD
        // These use dummy data that the server will reject, so they
        // stay outside the taskResult chain (expected failures).
        // ─────────────────────────────────────────────────────────────
        section "18. Images / Blob Upload"

        let imageBytes = Array.create 100 0uy // replace with real data
        let! blobResult = Bluesky.uploadBlob agent imageBytes Png

        match blobResult with
        | Ok blob ->
            printfn "Uploaded blob:"
            printfn "  CID: %s" (Cid.value blob.Ref)
            printfn "  Size: %d bytes, MIME: %s" blob.Size blob.MimeType

            printfn "  JSON: %s" (blob.Json.ToString () |> fun s -> if s.Length > 80 then s.[..79] + "..." else s)
        | Error e -> printfn "Upload failed (expected with dummy data): %A" e

        let! multiImgPost =
            Bluesky.postWithImages
                agent
                "Photo dump! #photography"
                [ { Data = imageBytes
                    MimeType = Jpeg
                    AltText = "First photo" }
                  { Data = imageBytes
                    MimeType = Jpeg
                    AltText = "Second photo" } ]

        match multiImgPost with
        | Ok p -> printfn "Multi-image post: %s" (AtUri.value p.Uri)
        | Error e -> printfn "Multi-image post failed (expected): %A" e

        // ─────────────────────────────────────────────────────────────
        // 19. ERROR HANDLING
        // All API calls return Result<T, XrpcError>. These examples
        // intentionally trigger errors to show the error structure.
        // ─────────────────────────────────────────────────────────────
        section "19. Error Handling"

        let badHandle = Handle.parse "this-handle-does-not-exist.invalid" |> Result.defaultWith failwith
        let! badProfile = Bluesky.getProfile agent badHandle

        match badProfile with
        | Ok _ -> printfn "Unexpectedly succeeded"
        | Error e ->
            printfn "XrpcError example:"
            printfn "  Status: %d" e.StatusCode
            printfn "  Error: %s" (e.Error |> Option.defaultValue "(none)")
            printfn "  Message: %s" (e.Message |> Option.defaultValue "(none)")

        let! badIdentity = Identity.resolveIdentity agent "nonexistent.invalid"

        match badIdentity with
        | Ok _ -> printfn "Unexpectedly succeeded"
        | Error (IdentityError.XrpcError xe) ->
            printfn "Identity XRPC error: %d — %s" xe.StatusCode (xe.Message |> Option.defaultValue "(none)")
        | Error (IdentityError.DocumentParseError msg) -> printfn "Identity parse error: %s" msg

        // ─────────────────────────────────────────────────────────────
        section "Done!"
        return 0
    }
    |> fun t -> t.GetAwaiter().GetResult ()
