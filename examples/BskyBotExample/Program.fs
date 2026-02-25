// BskyBotExample — demonstrates FSharp.ATProto library usage.
// Covers the same operations as a typical Bluesky bot: login, post, reply,
// like, repost, follow, delete, image upload, timeline, threads, notifications,
// author feed, profile lookup, and identity resolution.
//
// To run:
//   export BSKY_HANDLE="yourhandle.bsky.social"
//   export BSKY_PASSWORD="your-app-password"
//   dotnet run --project examples/BskyBotExample
//
// NOTE: This example performs real actions on the network. Use a test account.

open System
open System.IO
open System.Text.Json
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

let env key =
    match Environment.GetEnvironmentVariable(key) with
    | null -> failwithf "Set %s environment variable" key
    | v -> v

[<EntryPoint>]
let main _ =
    task {
        // ---------------------------------------------------------------
        // 1. Login
        // ---------------------------------------------------------------
        let agent = AtpAgent.create "https://bsky.social"

        let handle = env "BSKY_HANDLE"
        let password = env "BSKY_PASSWORD"

        let! loginResult = AtpAgent.login handle password agent

        let session =
            match loginResult with
            | Ok s ->
                printfn "Logged in as %s (%s)" s.Handle s.Did
                s
            | Error e ->
                failwithf "Login failed: %A" e

        // ---------------------------------------------------------------
        // 2. Post with rich text (auto-detects mentions, links, hashtags)
        // ---------------------------------------------------------------
        let! postResult = Bluesky.post agent "Hello from F#! Check out https://atproto.com #atproto"

        let post =
            match postResult with
            | Ok p ->
                printfn "Posted: %s (cid: %s)" p.Uri p.Cid
                p
            | Error e ->
                failwithf "Post failed: %A" e

        // ---------------------------------------------------------------
        // 3. Reply to a post
        // ---------------------------------------------------------------
        // For a reply, you need both the parent ref and the root ref.
        // When replying to a top-level post, parent and root are the same.
        let! replyResult =
            Bluesky.reply agent "Replying to my own post!" post.Uri post.Cid post.Uri post.Cid

        let _reply =
            match replyResult with
            | Ok r -> printfn "Replied: %s" r.Uri; r
            | Error e -> failwithf "Reply failed: %A" e

        // ---------------------------------------------------------------
        // 4. Like a post
        // ---------------------------------------------------------------
        let! likeResult = Bluesky.like agent post.Uri post.Cid

        match likeResult with
        | Ok l -> printfn "Liked: %s" l.Uri
        | Error e -> printfn "Like failed: %A" e

        // ---------------------------------------------------------------
        // 5. Repost
        // ---------------------------------------------------------------
        let! repostResult = Bluesky.repost agent post.Uri post.Cid

        match repostResult with
        | Ok r -> printfn "Reposted: %s" r.Uri
        | Error e -> printfn "Repost failed: %A" e

        // ---------------------------------------------------------------
        // 6. Follow a user (by DID)
        // ---------------------------------------------------------------
        // Use Identity.resolveIdentity (shown below) to resolve a handle to
        // a DID first if needed.
        let! followResult = Bluesky.follow agent session.Did // following ourselves as demo

        match followResult with
        | Ok f -> printfn "Followed: %s" f.Uri
        | Error e -> printfn "Follow failed: %A" e

        // ---------------------------------------------------------------
        // 7. Delete a record (e.g. delete the post we just created)
        // ---------------------------------------------------------------
        let! deleteResult = Bluesky.deleteRecord agent post.Uri

        match deleteResult with
        | Ok () -> printfn "Deleted: %s" post.Uri
        | Error e -> printfn "Delete failed: %A" e

        // ---------------------------------------------------------------
        // 8. Upload image + post
        // ---------------------------------------------------------------
        // postWithImages takes a list of (bytes, mimeType, altText) tuples.
        // Up to 4 images per post.
        let dummyImage = Array.create 100 0uy // placeholder; use real image bytes

        let! imgPostResult =
            Bluesky.postWithImages agent "Post with an image attached" [
                (dummyImage, "image/png", "A test image")
            ]

        match imgPostResult with
        | Ok p -> printfn "Image post: %s" p.Uri
        | Error e -> printfn "Image post failed (expected with dummy data): %A" e

        // ---------------------------------------------------------------
        // 9. Get timeline
        // ---------------------------------------------------------------
        let! timelineResult =
            AppBskyFeed.GetTimeline.query agent
                { Algorithm = None; Cursor = None; Limit = Some 5L }

        match timelineResult with
        | Ok tl ->
            printfn "Timeline (%d posts):" tl.Feed.Length
            for item in tl.Feed do
                printfn "  @%s: %s"
                    item.Post.Author.Handle
                    (item.Post.Record.GetProperty("text").GetString()
                     |> fun s -> if s.Length > 60 then s.[..59] + "..." else s)
        | Error e ->
            printfn "Timeline failed: %A" e

        // ---------------------------------------------------------------
        // 10. Get post thread
        // ---------------------------------------------------------------
        // Use any post AT-URI. Here we use the first timeline post if available.
        match timelineResult with
        | Ok tl when tl.Feed.Length > 0 ->
            let sampleUri = tl.Feed.[0].Post.Uri
            let! threadResult =
                AppBskyFeed.GetPostThread.query agent
                    { Uri = sampleUri; Depth = Some 3L; ParentHeight = Some 1L }

            match threadResult with
            | Ok t -> printfn "Thread loaded (type: %s)" (t.Thread.GetProperty("$type").GetString())
            | Error e -> printfn "Thread failed: %A" e
        | _ -> ()

        // ---------------------------------------------------------------
        // 11. List notifications
        // ---------------------------------------------------------------
        let! notifsResult =
            AppBskyNotification.ListNotifications.query agent
                { Cursor = None; Limit = Some 10L; Priority = None; Reasons = None; SeenAt = None }

        match notifsResult with
        | Ok n ->
            printfn "Notifications (%d):" n.Notifications.Length
            for notif in n.Notifications do
                printfn "  [%s] from @%s" notif.Reason notif.Author.Handle
        | Error e ->
            printfn "Notifications failed: %A" e

        // ---------------------------------------------------------------
        // 12. Get author feed
        // ---------------------------------------------------------------
        let! feedResult =
            AppBskyFeed.GetAuthorFeed.query agent
                { Actor = session.Handle
                  Cursor = None
                  Filter = None
                  IncludePins = None
                  Limit = Some 5L }

        match feedResult with
        | Ok f ->
            printfn "Author feed (%d posts):" f.Feed.Length
            for item in f.Feed do
                printfn "  %s (cid: %s)" item.Post.Uri item.Post.Cid
        | Error e ->
            printfn "Author feed failed: %A" e

        // ---------------------------------------------------------------
        // 13. Get profile
        // ---------------------------------------------------------------
        let! profileResult =
            AppBskyActor.GetProfile.query agent
                { Actor = session.Handle }

        match profileResult with
        | Ok p ->
            printfn "Profile: @%s (%s)" p.Handle p.Did
            printfn "  Display name: %s" (p.DisplayName |> Option.defaultValue "(none)")
            printfn "  Posts: %s, Followers: %s, Following: %s"
                (p.PostsCount |> Option.map string |> Option.defaultValue "?")
                (p.FollowersCount |> Option.map string |> Option.defaultValue "?")
                (p.FollowsCount |> Option.map string |> Option.defaultValue "?")
        | Error e ->
            printfn "Profile failed: %A" e

        // ---------------------------------------------------------------
        // 14. Resolve identity (handle <-> DID with bidirectional verification)
        // ---------------------------------------------------------------
        let! identityResult = Identity.resolveIdentity agent session.Handle

        match identityResult with
        | Ok id ->
            printfn "Identity: %s" id.Did
            printfn "  Handle: %s" (id.Handle |> Option.defaultValue "(unverified)")
            printfn "  PDS: %s" (id.PdsEndpoint |> Option.defaultValue "(unknown)")
        | Error e ->
            printfn "Identity resolution failed: %s" e

        // ---------------------------------------------------------------
        // Pagination example (bonus)
        // ---------------------------------------------------------------
        // Xrpc.paginate returns an IAsyncEnumerable of pages.
        printfn "Paginated timeline (first 2 pages):"
        let mutable pageCount = 0
        let pages =
            Xrpc.paginate<AppBskyFeed.GetTimeline.Params, AppBskyFeed.GetTimeline.Output>
                AppBskyFeed.GetTimeline.TypeId
                { Algorithm = None; Cursor = None; Limit = Some 3L }
                (fun o -> o.Cursor)
                (fun c p -> { p with Cursor = c })
                agent

        let enumerator = pages.GetAsyncEnumerator()
        let mutable hasMore = true
        while hasMore && pageCount < 2 do
            let! moved = enumerator.MoveNextAsync()
            hasMore <- moved
            if hasMore then
                match enumerator.Current with
                | Ok page ->
                    pageCount <- pageCount + 1
                    printfn "  Page %d: %d posts" pageCount page.Feed.Length
                | Error e ->
                    printfn "  Page error: %A" e
                    hasMore <- false

        // ===============================================================
        // DM / Chat operations
        // ===============================================================
        // Chat operations use the Bluesky chat proxy service. All chat
        // calls must go through a proxy agent that routes requests to
        // did:web:api.bsky.chat.

        // ---------------------------------------------------------------
        // 15. Create a chat-proxied agent
        // ---------------------------------------------------------------
        let chatAgent = AtpAgent.withChatProxy agent
        printfn "Chat agent created (proxy: did:web:api.bsky.chat)"

        // ---------------------------------------------------------------
        // 16. Get or create a conversation
        // ---------------------------------------------------------------
        // Pass a list of member DIDs. Using our own DID as a demo (self-chat).
        let! convoResult = Chat.getConvoForMembers chatAgent [ session.Did ]

        let convo =
            match convoResult with
            | Ok c ->
                printfn "Conversation: %s (members: %d, unread: %d)"
                    c.Convo.Id c.Convo.Members.Length c.Convo.UnreadCount
                c.Convo
            | Error e ->
                failwithf "Get convo failed: %A" e

        // ---------------------------------------------------------------
        // 17. Send a plain text message
        // ---------------------------------------------------------------
        let! msgResult = Chat.sendMessage chatAgent convo.Id "Hello from F# ATProto library!"

        let msg =
            match msgResult with
            | Ok m ->
                printfn "Sent message: %s (id: %s, sentAt: %s)" m.Text m.Id m.SentAt
                m
            | Error e ->
                failwithf "Send message failed: %A" e

        // ---------------------------------------------------------------
        // 18. Send a message with rich text (direct XRPC call)
        // ---------------------------------------------------------------
        // When you need more control (e.g. facets for links/mentions), use
        // the generated XRPC wrapper directly with a MessageInput.
        let richText = "Check out https://atproto.com for the AT Protocol spec!"
        let! facets = RichText.parse agent richText

        let! richMsgResult =
            ChatBskyConvo.SendMessage.call chatAgent
                { ConvoId = convo.Id
                  Message =
                    { Text = richText
                      Facets = if facets.IsEmpty then None else Some facets
                      Embed = None } }

        let _richMsg =
            match richMsgResult with
            | Ok m ->
                printfn "Sent rich message: %s (facets: %d)" m.Text
                    (m.Facets |> Option.map List.length |> Option.defaultValue 0)
                m
            | Error e ->
                failwithf "Send rich message failed: %A" e

        // ---------------------------------------------------------------
        // 19. Get messages in a conversation
        // ---------------------------------------------------------------
        let! msgsResult = Chat.getMessages chatAgent convo.Id (Some 10L) None

        match msgsResult with
        | Ok ms ->
            printfn "Messages in convo (%d):" ms.Messages.Length
            for m in ms.Messages do
                // Messages come back as JsonElement (union type in the schema).
                // MessageView has $type = "chat.bsky.convo.defs#messageView".
                let typ = m.GetProperty("$type").GetString()
                if typ = "chat.bsky.convo.defs#messageView" then
                    let text = m.GetProperty("text").GetString()
                    let sender = m.GetProperty("sender").GetProperty("did").GetString()
                    printfn "  [%s] %s" sender
                        (if text.Length > 50 then text.[..49] + "..." else text)
                else
                    printfn "  [%s]" typ
        | Error e ->
            printfn "Get messages failed: %A" e

        // ---------------------------------------------------------------
        // 20. Add a reaction to a message
        // ---------------------------------------------------------------
        // Reactions use the generated XRPC wrapper directly (no convenience method).
        // The "value" field is an emoji string.
        let! reactionResult =
            ChatBskyConvo.AddReaction.call chatAgent
                { ConvoId = convo.Id
                  MessageId = msg.Id
                  Value = "\u2764\uFE0F" }  // red heart emoji

        match reactionResult with
        | Ok r ->
            let reactionCount = r.Message.Reactions |> Option.map List.length |> Option.defaultValue 0
            printfn "Reaction added to message %s (total reactions: %d)" r.Message.Id reactionCount
        | Error e ->
            printfn "Add reaction failed: %A" e

        // ---------------------------------------------------------------
        // 21. List conversations
        // ---------------------------------------------------------------
        let! convosResult = Chat.listConvos chatAgent (Some 10L) None

        match convosResult with
        | Ok cs ->
            printfn "Conversations (%d):" cs.Convos.Length
            for c in cs.Convos do
                let memberHandles =
                    c.Members |> List.map (fun m -> m.Handle) |> String.concat ", "
                printfn "  %s (members: %s, muted: %b, unread: %d)"
                    c.Id memberHandles c.Muted c.UnreadCount
        | Error e ->
            printfn "List convos failed: %A" e

        // ---------------------------------------------------------------
        // 22. Mark conversation as read
        // ---------------------------------------------------------------
        let! readResult = Chat.markRead chatAgent convo.Id

        match readResult with
        | Ok r -> printfn "Marked as read: %s (unread now: %d)" r.Convo.Id r.Convo.UnreadCount
        | Error e -> printfn "Mark read failed: %A" e

        // ---------------------------------------------------------------
        // 23. Mute / unmute a conversation
        // ---------------------------------------------------------------
        let! muteResult = Chat.muteConvo chatAgent convo.Id

        match muteResult with
        | Ok r -> printfn "Muted: %s (muted: %b)" r.Convo.Id r.Convo.Muted
        | Error e -> printfn "Mute failed: %A" e

        let! unmuteResult = Chat.unmuteConvo chatAgent convo.Id

        match unmuteResult with
        | Ok r -> printfn "Unmuted: %s (muted: %b)" r.Convo.Id r.Convo.Muted
        | Error e -> printfn "Unmute failed: %A" e

        // ---------------------------------------------------------------
        // 24. Delete a message (for self only)
        // ---------------------------------------------------------------
        let! delMsgResult = Chat.deleteMessage chatAgent convo.Id msg.Id

        match delMsgResult with
        | Ok d -> printfn "Deleted message: %s (sentAt: %s)" d.Id d.SentAt
        | Error e -> printfn "Delete message failed: %A" e

        // ---------------------------------------------------------------
        // NOTE on DM attachments:
        // The MessageInput.Embed field accepts a JsonElement option, which
        // corresponds to a union type in the schema. Currently the only
        // defined embed type for DMs is record embeds (sharing a post
        // into a DM). Image attachments in DMs are not part of the
        // official lexicon schema yet -- they would require constructing
        // a custom embed JsonElement if/when the schema adds support.
        // ---------------------------------------------------------------

        printfn "Done!"
        return 0
    }
    |> fun t -> t.GetAwaiter().GetResult()
