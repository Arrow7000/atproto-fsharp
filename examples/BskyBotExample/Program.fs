// BskyBotExample — comprehensive demonstration of the FSharp.ATProto library.
//
// Covers every workflow a Bluesky bot/app would need: authentication, profiles,
// timeline, posting (plain/rich/facets/images), replies, likes, reposts, follows,
// blocks, deletes, identity resolution, rich text processing, notifications,
// threads, chat/DMs, pagination, blob upload, and error handling.
//
// To run:
//   export BSKY_HANDLE="yourhandle.bsky.social"
//   export BSKY_PASSWORD="your-app-password"
//   dotnet run --project examples/BskyBotExample
//
// NOTE: This performs real actions on the network. Use a test account!

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

[<EntryPoint>]
let main _ =
    task {
        // ─────────────────────────────────────────────────────────────
        // 1. AUTHENTICATION
        // Bluesky.login creates an agent, authenticates, and returns
        // the authenticated agent in one step. The session is stored
        // on the agent and used for all subsequent requests.
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

        // ─────────────────────────────────────────────────────────────
        // 2. READ PROFILES
        // getProfile returns a ProfileViewDetailed with stats, bio,
        // avatar, banner, and viewer relationship state. It accepts
        // a Handle, Did, or string directly.
        // ─────────────────────────────────────────────────────────────
        section "2. Read Profiles"

        // Own profile — pass the typed Handle directly
        let! ownProfile = Bluesky.getProfile agent session.Handle

        match ownProfile with
        | Ok p ->
            printfn "Own profile: @%s (%s)" (Handle.value p.Handle) (Did.value p.Did)
            printfn "  Display name: %s" (p.DisplayName |> Option.defaultValue "(none)")

            printfn
                "  Posts: %s, Followers: %s, Following: %s"
                (p.PostsCount |> Option.map string |> Option.defaultValue "?")
                (p.FollowersCount |> Option.map string |> Option.defaultValue "?")
                (p.FollowsCount |> Option.map string |> Option.defaultValue "?")
        | Error e -> printfn "Own profile failed: %A" e

        // Another user's profile (raw XRPC query — equivalent to getProfile)
        let! otherProfile = AppBskyActor.GetProfile.query agent { Actor = "bsky.app" }

        match otherProfile with
        | Ok p ->
            printfn "Other profile: @%s" (Handle.value p.Handle)

            printfn
                "  Bio: %s"
                (p.Description
                 |> Option.defaultValue "(no bio)"
                 |> fun s -> if s.Length > 80 then s.[..79] + "..." else s)
            // Check viewer relationship state
            match p.Viewer with
            | Some v -> printfn "  Following: %b, Followed by: %b" v.Following.IsSome v.FollowedBy.IsSome
            | None -> ()
        | Error e -> printfn "Other profile failed: %A" e

        // ─────────────────────────────────────────────────────────────
        // 3. TIMELINE
        // Fetch the home timeline and iterate posts. Each FeedViewPost
        // wraps a PostView with optional reply context and reason
        // (e.g. ReasonRepost when it appears because someone reposted).
        // Use the .Text extension property to read post content.
        // ─────────────────────────────────────────────────────────────
        section "3. Timeline"

        let! timelineResult = Bluesky.getTimeline agent (Some 10L) None

        let timelineFeed =
            match timelineResult with
            | Ok tl ->
                printfn "Timeline: %d posts (cursor: %s)" tl.Feed.Length (tl.Cursor |> Option.defaultValue "(end)")

                for item in tl.Feed do
                    // Check if this is a repost or pinned post
                    let prefix =
                        match item.Reason with
                        | Some(AppBskyFeed.Defs.FeedViewPostReasonUnion.ReasonRepost _) -> "[repost] "
                        | Some(AppBskyFeed.Defs.FeedViewPostReasonUnion.ReasonPin _) -> "[pinned] "
                        | _ -> ""
                    // Post text via the .Text extension property
                    let text = item.Post.Text
                    let truncated = if text.Length > 60 then text.[..59] + "..." else text
                    printfn "  %s@%s: %s" prefix (Handle.value item.Post.Author.Handle) truncated
                    // Access engagement counts
                    printfn
                        "    likes: %s, reposts: %s, replies: %s"
                        (item.Post.LikeCount |> Option.map string |> Option.defaultValue "?")
                        (item.Post.RepostCount |> Option.map string |> Option.defaultValue "?")
                        (item.Post.ReplyCount |> Option.map string |> Option.defaultValue "?")

                tl.Feed
            | Error e ->
                printfn "Timeline failed: %A" e
                []

        // ─────────────────────────────────────────────────────────────
        // 4. POSTING
        // Four ways to create a post:
        //   a) Auto-detected rich text (mentions/links/tags resolved)
        //   b) Explicitly plain text (no detection via postWithFacets)
        //   c) Pre-built facets (manual byte offsets for full control)
        //   d) With images attached (using the ImageMime DU)
        // ─────────────────────────────────────────────────────────────
        section "4. Posting"

        // 4a. Auto-detected rich text — mentions, links, and hashtags
        //     are detected and resolved to facets automatically
        let! autoPost = Bluesky.post agent "Hello from F#! Visit https://atproto.com #atproto"

        let postRef =
            match autoPost with
            | Ok p ->
                printfn "Auto-detected post: %s" (AtUri.value p.Uri)
                p
            | Error e -> failwithf "Auto post failed: %A" e

        // 4b. Explicitly plain text — pass empty facets to skip detection.
        //     Useful when you know the text has no rich content.
        let! plainPost = Bluesky.postWithFacets agent "Just a plain text post, no detection." []

        match plainPost with
        | Ok p -> printfn "Plain text post: %s" (AtUri.value p.Uri)
        | Error e -> printfn "Plain post failed: %A" e

        // 4c. Pre-built facets — full control over byte offsets and features.
        //     Useful when you've already processed the text yourself.
        let facetText = "Check example.com for details"
        let linkStart = int64 (RichText.byteLength "Check ")
        let linkEnd = int64 (RichText.byteLength "Check example.com")

        match Uri.parse "https://example.com" with
        | Ok linkUri ->
            let manualFacets: AppBskyRichtext.Facet.Facet list =
                [ { Index =
                      { ByteStart = linkStart
                        ByteEnd = linkEnd }
                    Features = [ AppBskyRichtext.Facet.FacetFeaturesItem.Link { Uri = linkUri } ] } ]

            let! facetPost = Bluesky.postWithFacets agent facetText manualFacets

            match facetPost with
            | Ok p -> printfn "Manual facet post: %s" (AtUri.value p.Uri)
            | Error e -> printfn "Manual facet post failed: %A" e
        | Error _ -> printfn "Skipped manual facet post (URI parse error)"

        // 4d. Post with images — upload + embed in one call.
        //     Supports up to 4 images. Each needs data, MIME type (as ImageMime DU), and alt text.
        let dummyImage = Array.create 100 0uy // replace with real image bytes

        let! imgPost =
            Bluesky.postWithImages
                agent
                "Post with an image attached!"
                [ { Data = dummyImage
                    MimeType = Png
                    AltText = "A test image" } ]

        match imgPost with
        | Ok p -> printfn "Image post: %s" (AtUri.value p.Uri)
        | Error e -> printfn "Image post failed (expected with dummy data): %A" e

        // ─────────────────────────────────────────────────────────────
        // 5. REPLYING
        // Two ways to reply:
        //   a) replyTo — auto-resolves the thread root from the parent
        //      (recommended for most cases)
        //   b) replyWithKnownRoot — when you already have both the
        //      parent and root refs on hand
        // ─────────────────────────────────────────────────────────────
        section "5. Replying"

        // 5a. replyTo — pass the parent ref. The library fetches the
        //     parent post and resolves the thread root automatically.
        if timelineFeed.Length > 0 then
            let parentPost = timelineFeed.[0].Post

            let parentRef: PostRef =
                { Uri = parentPost.Uri
                  Cid = parentPost.Cid }

            let! replyToResult = Bluesky.replyTo agent "Great post!" parentRef

            match replyToResult with
            | Ok r -> printfn "replyTo (auto-root): %s" (AtUri.value r.Uri)
            | Error e -> printfn "replyTo failed: %A" e

        // 5b. replyTo a top-level post — root is resolved automatically
        let! topLevelReply = Bluesky.replyTo agent "Replying to myself!" postRef

        match topLevelReply with
        | Ok r -> printfn "Top-level reply: %s" (AtUri.value r.Uri)
        | Error e -> printfn "Top-level reply failed: %A" e

        // 5c. replyWithKnownRoot — when you already know the root and parent refs
        //     (e.g., you're building a thread yourself and have both refs on hand)
        match topLevelReply with
        | Ok replyRef ->
            let! nestedReply = Bluesky.replyWithKnownRoot agent "Nested reply!" replyRef postRef

            match nestedReply with
            | Ok r -> printfn "Nested reply: %s" (AtUri.value r.Uri)
            | Error e -> printfn "Nested reply failed: %A" e
        | _ -> ()

        // ─────────────────────────────────────────────────────────────
        // 6. LIKE / UNLIKE
        // like returns a typed LikeRef. The generic undo function
        // accepts any ref type (LikeRef, RepostRef, FollowRef, BlockRef)
        // and returns an UndoResult (Undone | WasNotPresent).
        // You can also use unlikePost to unlike by target post.
        // ─────────────────────────────────────────────────────────────
        section "6. Like / Unlike"

        let! likeResult = Bluesky.like agent postRef

        match likeResult with
        | Ok likeRef ->
            printfn "Liked: %s" (AtUri.value likeRef.Uri)
            // Undo using the generic undo function
            let! undoResult = Bluesky.undo agent likeRef

            match undoResult with
            | Ok Undone -> printfn "Unliked successfully"
            | Ok WasNotPresent -> printfn "Was not liked"
            | Error e -> printfn "Unlike failed: %A" e
        | Error e -> printfn "Like failed: %A" e

        // ─────────────────────────────────────────────────────────────
        // 7. REPOST / UNREPOST
        // Same typed-ref pattern as like/unlike.
        // repost returns a RepostRef; undo removes it.
        // ─────────────────────────────────────────────────────────────
        section "7. Repost / Unrepost"

        let! repostResult = Bluesky.repost agent postRef

        match repostResult with
        | Ok repostRef ->
            printfn "Reposted: %s" (AtUri.value repostRef.Uri)
            let! undoResult = Bluesky.undo agent repostRef

            match undoResult with
            | Ok Undone -> printfn "Unreposted successfully"
            | Ok WasNotPresent -> printfn "Was not reposted"
            | Error e -> printfn "Unrepost failed: %A" e
        | Error e -> printfn "Repost failed: %A" e

        // ─────────────────────────────────────────────────────────────
        // 8. FOLLOW / UNFOLLOW
        // Two ways to follow:
        //   a) follow — takes a typed Did (type-safe)
        //   b) followUser — takes a string (handle or DID), resolves
        //      automatically (convenient for user input)
        // Both return a FollowRef. Use undo to unfollow.
        // ─────────────────────────────────────────────────────────────
        section "8. Follow / Unfollow"

        // 8a. Follow by typed DID
        let! followResult = Bluesky.follow agent session.Did

        match followResult with
        | Ok followRef ->
            printfn "Followed (by DID): %s" (AtUri.value followRef.Uri)
            let! undoResult = Bluesky.undo agent followRef

            match undoResult with
            | Ok Undone -> printfn "Unfollowed successfully"
            | Ok WasNotPresent -> printfn "Was not following"
            | Error e -> printfn "Unfollow failed: %A" e
        | Error e -> printfn "Follow failed: %A" e

        // 8b. Follow by handle string (auto-resolves to DID)
        let! followUserResult = Bluesky.followUser agent (Handle.value session.Handle)

        match followUserResult with
        | Ok followRef ->
            printfn "Followed (by handle): %s" (AtUri.value followRef.Uri)
            let! _ = Bluesky.undo agent followRef
            printfn "Unfollowed"
        | Error e -> printfn "followUser failed: %A" e

        // ─────────────────────────────────────────────────────────────
        // 9. BLOCK / UNBLOCK
        // Same pattern as follow: block takes a typed Did, blockUser
        // takes a string. Both return a BlockRef. Use undo to unblock.
        // ─────────────────────────────────────────────────────────────
        section "9. Block / Unblock"

        // 9a. Block by typed DID (self-block as harmless demo)
        let! blockResult = Bluesky.block agent session.Did

        match blockResult with
        | Ok blockRef ->
            printfn "Blocked: %s" (AtUri.value blockRef.Uri)
            let! undoResult = Bluesky.undo agent blockRef

            match undoResult with
            | Ok Undone -> printfn "Unblocked successfully"
            | Ok WasNotPresent -> printfn "Was not blocked"
            | Error e -> printfn "Unblock failed: %A" e
        | Error e -> printfn "Block failed: %A" e

        // 9b. Block by handle string (auto-resolves to DID)
        let! blockUserResult = Bluesky.blockUser agent (Handle.value session.Handle)

        match blockUserResult with
        | Ok blockRef ->
            printfn "Blocked (by handle): %s" (AtUri.value blockRef.Uri)
            let! _ = Bluesky.undo agent blockRef
            printfn "Unblocked"
        | Error e -> printfn "blockUser failed: %A" e

        // ─────────────────────────────────────────────────────────────
        // 10. DELETE A RECORD
        // deleteRecord works for any record type: posts, likes, reposts,
        // follows, blocks. Pass the AT-URI from when the record was created.
        // ─────────────────────────────────────────────────────────────
        section "10. Delete"

        let! deleteResult = Bluesky.deleteRecord agent postRef.Uri

        match deleteResult with
        | Ok() -> printfn "Deleted post: %s" (AtUri.value postRef.Uri)
        | Error e -> printfn "Delete failed: %A" e

        // Clean up the plain text post too
        match plainPost with
        | Ok p ->
            let! _ = Bluesky.deleteRecord agent p.Uri
            printfn "Cleaned up plain text post"
        | _ -> ()

        // ─────────────────────────────────────────────────────────────
        // 11. IDENTITY RESOLUTION
        // Resolve handles <-> DIDs with bidirectional verification.
        // Supports did:plc (PLC directory) and did:web (.well-known).
        // ─────────────────────────────────────────────────────────────
        section "11. Identity Resolution"

        // Full identity resolution (handle -> DID -> DID doc -> verify)
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

        // Resolve DID -> identity (fetches DID document from PLC directory)
        let! didResult = Identity.resolveDid agent session.Did

        match didResult with
        | Ok id -> printfn "Resolved DID -> PDS: %s" (id.PdsEndpoint |> Option.map Uri.value |> Option.defaultValue "?")
        | Error e -> printfn "resolveDid failed: %A" e

        // Resolve handle -> DID only (lighter than full identity)
        let! handleResult = Identity.resolveHandle agent session.Handle

        match handleResult with
        | Ok did -> printfn "Handle -> DID: %s" (Did.value did)
        | Error e -> printfn "resolveHandle failed: %A" e

        // Parse a DID document directly from JSON
        let sampleDoc =
            """{"id":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","alsoKnownAs":["at://atproto.com"],"service":[{"id":"did:plc:ewvi7nxzyoun6zhxrhs64oiz#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://bsky.network"}]}"""

        let docElement = JsonSerializer.Deserialize<JsonElement>(sampleDoc)

        match Identity.parseDidDocument docElement with
        | Ok parsed ->
            printfn
                "Parsed DID doc: %s (handle: %s)"
                (Did.value parsed.Did)
                (parsed.Handle |> Option.map Handle.value |> Option.defaultValue "(none)")
        | Error msg -> printfn "Parse failed: %s" msg

        // ─────────────────────────────────────────────────────────────
        // 12. RICH TEXT PROCESSING
        // Detect facets (local, no network), resolve them (resolves
        // mentions to DIDs), or do both in one step with parse.
        // Also: grapheme length (for 300-char limit) and byte length
        // (for facet offsets).
        // ─────────────────────────────────────────────────────────────
        section "12. Rich Text"

        let sampleText = "Hello @bsky.app! Visit https://atproto.com #decentralization"

        // Step 1: Detect facets (local only, no network calls)
        let detected = RichText.detect sampleText
        printfn "Detected %d facets in: \"%s\"" detected.Length sampleText

        for d in detected do
            match d with
            | RichText.DetectedMention(s, e, h) -> printfn "  Mention: @%s [byte %d..%d]" h s e
            | RichText.DetectedLink(s, e, u) -> printfn "  Link: %s [byte %d..%d]" u s e
            | RichText.DetectedTag(s, e, t) -> printfn "  Tag: #%s [byte %d..%d]" t s e

        // Step 2: Resolve facets (resolves mentions to DIDs via network)
        let! resolved = RichText.resolve agent detected
        printfn "Resolved %d facets (unresolvable mentions dropped)" resolved.Length

        // Combined: detect + resolve in one step
        let! combined = RichText.parse agent sampleText
        printfn "Parse returned %d facets" combined.Length

        // Length checks — important for post validation
        let graphemes = RichText.graphemeLength sampleText
        let bytes = RichText.byteLength sampleText
        printfn "Text length: %d graphemes, %d UTF-8 bytes" graphemes bytes

        // Emoji: grapheme length differs from string length and byte length
        let emojiText = "Hello 👨‍👩‍👧‍👦!"
        printfn "Emoji text: \"%s\"" emojiText

        printfn
            "  %d graphemes, %d chars, %d bytes"
            (RichText.graphemeLength emojiText)
            emojiText.Length
            (RichText.byteLength emojiText)

        // ─────────────────────────────────────────────────────────────
        // 13. NOTIFICATIONS
        // Each notification has a reason (like, follow, reply, mention,
        // repost, quote) and the associated author + record.
        // ─────────────────────────────────────────────────────────────
        section "13. Notifications"

        let! notifsResult = Bluesky.getNotifications agent (Some 10L) None

        match notifsResult with
        | Ok n ->
            printfn "Notifications: %d" n.Notifications.Length

            for notif in n.Notifications do
                printfn "  [%s] from @%s (read: %b)" notif.Reason (Handle.value notif.Author.Handle) notif.IsRead
        | Error e -> printfn "Notifications failed: %A" e

        // ─────────────────────────────────────────────────────────────
        // 14. POST THREAD
        // Fetch a thread by AT-URI. The response is a recursive
        // structure: ThreadViewPost has a Parent (up the chain) and
        // Replies (down), each as a ThreadViewPostParentUnion.
        // Use the .Text extension property to read post content.
        // ─────────────────────────────────────────────────────────────
        section "14. Post Thread"

        if timelineFeed.Length > 0 then
            let threadUri = timelineFeed.[0].Post.Uri
            let! threadResult = Bluesky.getPostThread agent threadUri (Some 6L) (Some 3L)

            match threadResult with
            | Ok t ->
                match t.Thread with
                | AppBskyFeed.GetPostThread.OutputThreadUnion.ThreadViewPost tvp ->
                    // Access the post content via the .Text extension property
                    let postText = tvp.Post.Text

                    printfn
                        "Thread root: @%s — %s"
                        (Handle.value tvp.Post.Author.Handle)
                        (if postText.Length > 50 then
                             postText.[..49] + "..."
                         else
                             postText)

                    // Navigate parent chain (recursive)
                    match tvp.Parent with
                    | Some(AppBskyFeed.Defs.ThreadViewPostParentUnion.ThreadViewPost parent) ->
                        printfn "  Parent by @%s" (Handle.value parent.Post.Author.Handle)
                    | Some(AppBskyFeed.Defs.ThreadViewPostParentUnion.NotFoundPost _) ->
                        printfn "  Parent not found (deleted?)"
                    | Some(AppBskyFeed.Defs.ThreadViewPostParentUnion.BlockedPost _) -> printfn "  Parent blocked"
                    | Some(AppBskyFeed.Defs.ThreadViewPostParentUnion.Unknown(tag, _)) ->
                        printfn "  Parent unknown type: %s" tag
                    | None -> printfn "  (top-level post, no parent)"

                    // Navigate replies (also recursive — same union type)
                    let replyCount = tvp.Replies |> Option.map List.length |> Option.defaultValue 0
                    printfn "  Replies: %d" replyCount

                    match tvp.Replies with
                    | Some replies ->
                        for r in replies |> List.truncate 3 do
                            match r with
                            | AppBskyFeed.Defs.ThreadViewPostParentUnion.ThreadViewPost rtvp ->
                                printfn "    @%s replied" (Handle.value rtvp.Post.Author.Handle)
                            | _ -> printfn "    (non-post reply node)"
                    | None -> ()

                | AppBskyFeed.GetPostThread.OutputThreadUnion.NotFoundPost _ -> printfn "Thread not found"
                | AppBskyFeed.GetPostThread.OutputThreadUnion.BlockedPost _ -> printfn "Thread blocked"
                | AppBskyFeed.GetPostThread.OutputThreadUnion.Unknown(tag, _) -> printfn "Unknown thread type: %s" tag
            | Error e -> printfn "Thread failed: %A" e
        else
            printfn "(no timeline posts to fetch thread for)"

        // ─────────────────────────────────────────────────────────────
        // 15. CHAT / DIRECT MESSAGES
        // All Chat.* functions auto-apply the chat proxy header
        // (atproto-proxy: did:web:api.bsky.chat#bsky_chat) — no need
        // to call withChatProxy manually for convenience methods.
        // For raw XRPC calls, use AtpAgent.withChatProxy explicitly.
        // ─────────────────────────────────────────────────────────────
        section "15. Chat / DMs"

        // List conversations
        let! convosResult = Chat.listConvos agent (Some 10L) None

        match convosResult with
        | Ok cs ->
            printfn "Conversations: %d" cs.Convos.Length

            for c in cs.Convos do
                let members =
                    c.Members |> List.map (fun m -> Handle.value m.Handle) |> String.concat ", "

                printfn "  %s (members: %s, unread: %d, muted: %b)" c.Id members c.UnreadCount c.Muted
        | Error e -> printfn "List convos failed: %A" e

        // Get or create a conversation with specific members
        let! convoResult = Chat.getConvoForMembers agent [ session.Did ]

        match convoResult with
        | Ok c ->
            let convo = c.Convo
            printfn "Convo: %s (members: %d)" convo.Id convo.Members.Length

            // Send a plain text message
            let! msgResult = Chat.sendMessage agent convo.Id "Hello from the F# ATProto bot!"

            match msgResult with
            | Ok m ->
                printfn "Sent: \"%s\" (id: %s)" m.Text m.Id

                // Delete the message (for self only — others still see it)
                let! delResult = Chat.deleteMessage agent convo.Id m.Id

                match delResult with
                | Ok d -> printfn "Deleted message: %s" d.Id
                | Error e -> printfn "Delete message failed: %A" e
            | Error e -> printfn "Send message failed: %A" e

            // Get messages in the conversation
            let! msgsResult = Chat.getMessages agent convo.Id (Some 5L) None

            match msgsResult with
            | Ok ms ->
                printfn "Messages: %d" ms.Messages.Length

                for m in ms.Messages do
                    match m with
                    | ChatBskyConvo.GetMessages.OutputMessagesItem.MessageView mv ->
                        printfn
                            "  [%s] %s"
                            (Did.value mv.Sender.Did)
                            (if mv.Text.Length > 40 then
                                 mv.Text.[..39] + "..."
                             else
                                 mv.Text)
                    | ChatBskyConvo.GetMessages.OutputMessagesItem.DeletedMessageView dv ->
                        printfn "  [deleted by %s]" (Did.value dv.Sender.Did)
                    | ChatBskyConvo.GetMessages.OutputMessagesItem.Unknown(tag, _) -> printfn "  [unknown: %s]" tag
            | Error e -> printfn "Get messages failed: %A" e

            // Mark conversation as read
            let! readResult = Chat.markRead agent convo.Id

            match readResult with
            | Ok r -> printfn "Marked read: unread=%d" r.Convo.UnreadCount
            | Error e -> printfn "Mark read failed: %A" e

            // Mark ALL conversations as read
            let! markAllResult = Chat.markAllRead agent

            match markAllResult with
            | Ok _ -> printfn "Marked all read"
            | Error e -> printfn "Mark all read failed: %A" e

            // Mute / unmute a conversation
            let! muteResult = Chat.muteConvo agent convo.Id

            match muteResult with
            | Ok r -> printfn "Muted: %b" r.Convo.Muted
            | Error e -> printfn "Mute failed: %A" e

            let! unmuteResult = Chat.unmuteConvo agent convo.Id

            match unmuteResult with
            | Ok r -> printfn "Unmuted: %b" r.Convo.Muted
            | Error e -> printfn "Unmute failed: %A" e

            // Send a rich text DM with facets using the raw XRPC wrapper.
            // Use AtpAgent.withChatProxy explicitly for raw XRPC calls.
            let dmText = "Check out https://atproto.com!"
            let! dmFacets = RichText.parse agent dmText
            let chatAgent = AtpAgent.withChatProxy agent

            let! richDm =
                ChatBskyConvo.SendMessage.call
                    chatAgent
                    { ConvoId = convo.Id
                      Message =
                        { Text = dmText
                          Facets = if dmFacets.IsEmpty then None else Some dmFacets
                          Embed = None } }

            match richDm with
            | Ok m ->
                printfn
                    "Rich DM: \"%s\" (facets: %d)"
                    m.Text
                    (m.Facets |> Option.map List.length |> Option.defaultValue 0)
            | Error e -> printfn "Rich DM failed: %A" e

        | Error e -> printfn "Get convo failed: %A" e

        // ─────────────────────────────────────────────────────────────
        // 16. PAGINATION
        // Pre-built paginators wrap cursor-based XRPC queries into
        // IAsyncEnumerable that lazily fetches one page at a time.
        // For custom queries, use Xrpc.paginate directly.
        // ─────────────────────────────────────────────────────────────
        section "16. Pagination"

        // Paginate the timeline (first 3 pages of 5 posts each)
        printfn "Paginated timeline:"

        let timelinePages = Bluesky.paginateTimeline agent (Some 5L)

        let mutable pageNum = 0
        let enumerator = timelinePages.GetAsyncEnumerator()
        let mutable hasMore = true

        while hasMore && pageNum < 3 do
            let! moved = enumerator.MoveNextAsync()
            hasMore <- moved

            if hasMore then
                match enumerator.Current with
                | Ok page ->
                    pageNum <- pageNum + 1

                    printfn
                        "  Page %d: %d posts (cursor: %s)"
                        pageNum
                        page.Feed.Length
                        (page.Cursor |> Option.defaultValue "(end)")
                | Error e ->
                    printfn "  Page error: %A" e
                    hasMore <- false

        // Paginate followers
        printfn "Paginated followers:"

        let followerPages = Bluesky.paginateFollowers agent (Handle.value session.Handle) (Some 10L)

        let followerEnum = followerPages.GetAsyncEnumerator()
        let! hasFirst = followerEnum.MoveNextAsync()

        if hasFirst then
            match followerEnum.Current with
            | Ok page ->
                printfn "  First page: %d followers" page.Followers.Length

                for f in page.Followers |> List.truncate 5 do
                    printfn "    @%s (%s)" (Handle.value f.Handle) (Did.value f.Did)
            | Error e -> printfn "  Followers error: %A" e

        // ─────────────────────────────────────────────────────────────
        // 17. IMAGES / BLOB UPLOAD
        // uploadBlob is the low-level API for uploading any binary data.
        // Use postWithImages for the common case. For custom embeds
        // (e.g. external link cards with thumbnails), use uploadBlob
        // directly and construct the embed record yourself.
        // ─────────────────────────────────────────────────────────────
        section "17. Images / Blob Upload"

        // Low-level: upload a blob and inspect the BlobRef
        let imageBytes = Array.create 100 0uy // replace with real data
        let! blobResult = Bluesky.uploadBlob agent imageBytes Png

        match blobResult with
        | Ok blob ->
            printfn "Uploaded blob:"
            printfn "  CID: %s" (Cid.value blob.Ref)
            printfn "  Size: %d bytes, MIME: %s" blob.Size blob.MimeType
            // blob.Json is the raw JSON to embed in a post record
            printfn "  JSON: %s" (blob.Json.ToString() |> fun s -> if s.Length > 80 then s.[..79] + "..." else s)
        | Error e -> printfn "Upload failed (expected with dummy data): %A" e

        // High-level: postWithImages handles upload + embed automatically
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
        // 18. ERROR HANDLING
        // All API calls return Result<T, XrpcError>. Pattern match to
        // handle success and failure. XrpcError has StatusCode, Error
        // (string option), and Message (string option).
        // Identity errors use a separate IdentityError type.
        // ─────────────────────────────────────────────────────────────
        section "18. Error Handling"

        // XRPC errors: status code + optional error name + message
        let! badProfile = Bluesky.getProfile agent "this-handle-does-not-exist.invalid"

        match badProfile with
        | Ok _ -> printfn "Unexpectedly succeeded"
        | Error e ->
            printfn "XrpcError example:"
            printfn "  Status: %d" e.StatusCode
            printfn "  Error: %s" (e.Error |> Option.defaultValue "(none)")
            printfn "  Message: %s" (e.Message |> Option.defaultValue "(none)")

        // Identity errors: either an XrpcError or a DocumentParseError
        let! badIdentity = Identity.resolveIdentity agent "nonexistent.invalid"

        match badIdentity with
        | Ok _ -> printfn "Unexpectedly succeeded"
        | Error(IdentityError.XrpcError xe) ->
            printfn "Identity XRPC error: %d — %s" xe.StatusCode (xe.Message |> Option.defaultValue "(none)")
        | Error(IdentityError.DocumentParseError msg) -> printfn "Identity parse error: %s" msg

        // Composing with Result.map / Result.bind
        let! postCount =
            task {
                let! profile = Bluesky.getProfile agent session.Handle
                return profile |> Result.map (fun p -> p.PostsCount |> Option.defaultValue 0L)
            }

        match postCount with
        | Ok n -> printfn "You have %d posts" n
        | Error e -> printfn "Couldn't get post count: %A" e

        // ─────────────────────────────────────────────────────────────
        section "Done!"
        return 0
    }
    |> fun t -> t.GetAwaiter().GetResult()
