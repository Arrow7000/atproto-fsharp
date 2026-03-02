---
title: Chat / DMs
category: Guides
categoryindex: 1
index: 6
description: Send and receive Bluesky direct messages with FSharp.ATProto
keywords: chat, dm, direct messages, bluesky, conversations
---

# Chat / Direct Messages

Bluesky direct messages use a separate service (`api.bsky.chat`) from the main PDS. The `Chat` module adds the required proxy header automatically -- you use the same agent as for everything else.

## Getting Started

Log in with `Bluesky.login` and start chatting. No extra configuration needed:

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax

let! agent = Bluesky.login "https://bsky.social" "my-handle.bsky.social" "app-password"

match agent with
| Ok agent ->
    // Use this same agent for posts, likes, follows, AND chat
    printfn "Logged in and ready to chat!"
| Error e ->
    printfn "Login failed: %A" e
```

## Starting a Conversation

Get or create a conversation with one or more members by their DIDs. Note that `getConvoForMembers` takes a `Did list`, not raw strings:

```fsharp
let did = Did.parse "did:plc:xyz123" |> Result.defaultWith failwith
let! convoResult = Chat.getConvoForMembers agent [ did ]

match convoResult with
| Ok convo ->
    printfn "Conversation: %s (members: %d)" convo.Id convo.Members.Length
| Error e ->
    printfn "Failed: %A" e
```

If a conversation already exists between the specified members, the existing one is returned. Otherwise a new conversation is created.

## Sending Messages

### Plain Text

For simple text messages, `Chat.sendMessage` takes the agent, conversation ID, and message text:

```fsharp
let! msgResult = Chat.sendMessage agent convo.Id "Hello from F#!"

match msgResult with
| Ok (ChatMessage.Message msg) ->
    printfn "Sent: %s (id: %s)" msg.Text msg.Id
| Ok (ChatMessage.Deleted _) ->
    () // shouldn't happen for a fresh send
| Error e ->
    printfn "Send failed: %A" e
```

### Rich Text (with Links, Mentions, Hashtags)

`Chat.sendMessage` automatically detects links, mentions, and hashtags in your message text -- just like `Bluesky.post`. No extra steps needed:

```fsharp
let! result = Chat.sendMessage agent convo.Id "Check out https://atproto.com for the AT Protocol spec!"
```

See the [Rich Text](rich-text.html) guide for more on how facet detection works.

**Power Users:** If you need full control over facets or want to include an embed (e.g., sharing a post into a DM), use the raw XRPC wrapper:

```fsharp
let text = "Check out https://atproto.com for the AT Protocol spec!"
let! facets = RichText.parse agent text

let! result =
    ChatBskyConvo.SendMessage.call (AtpAgent.withChatProxy agent)
        { ConvoId = convo.Id
          Message =
            { Text = text
              Facets = if facets.IsEmpty then None else Some facets
              Embed = None } }
```

## Reading Messages

Retrieve messages from a conversation with optional pagination:

```fsharp
let! msgsResult = Chat.getMessages agent convo.Id (Some 20L) None

match msgsResult with
| Ok page ->
    printfn "Messages (%d):" page.Items.Length
    for m in page.Items do
        match m with
        | ChatMessage.Message msg ->
            printfn "  [%s] %s" (Did.value msg.Sender) msg.Text
        | ChatMessage.Deleted del ->
            printfn "  (deleted: %s)" del.Id
| Error e ->
    printfn "Failed: %A" e
```

Messages are returned as a `Page<ChatMessage>`. Each `ChatMessage` is a discriminated union with `Message` and `Deleted` cases. The `Message` record gives you typed access to `Id`, `Text`, `Sender` (a `Did`), and `SentAt` (a `DateTimeOffset`).

To fetch older messages, pass the cursor from the previous response:

```fsharp
let! page2 = Chat.getMessages agent convo.Id (Some 20L) page.Cursor
```

## Reactions

Add or remove a reaction (emoji) on a message:

```fsharp
// Add a reaction -- takes convoId, messageId, and emoji string
let! _ = Chat.addReaction agent convo.Id msgId "\u2764\uFE0F"  // red heart

// Remove a reaction
let! _ = Chat.removeReaction agent convo.Id msgId "\u2764\uFE0F"
```

## Managing Conversations

### List Conversations

```fsharp
let! convosResult = Chat.listConvos agent (Some 20L) None

match convosResult with
| Ok page ->
    for c in page.Items do
        let members =
            c.Members |> List.map (fun m -> m.DisplayName) |> String.concat ", "
        printfn "%s: %s (unread: %d)" c.Id members c.UnreadCount
| Error e ->
    printfn "Failed: %A" e
```

Each `ConvoSummary` gives you `Id`, `Members` (a `ProfileSummary list`), `LastMessageText`, `UnreadCount`, and `IsMuted`.

### Get a Conversation by ID

If you already have a conversation ID, fetch its details directly:

```fsharp
let! convoResult = Chat.getConvo agent convoId

match convoResult with
| Ok convo -> printfn "Conversation with %d members" convo.Members.Length
| Error e -> printfn "Failed: %A" e
```

### Accept / Leave a Conversation

Accept or leave a conversation (e.g., for moderation or cleanup):

```fsharp
// Accept a conversation request
let! _ = Chat.acceptConvo agent convoId

// Leave a conversation
let! _ = Chat.leaveConvo agent convoId
```

### Mark as Read

```fsharp
let! _ = Chat.markRead agent convo.Id
```

Or mark all conversations as read:

```fsharp
let! _ = Chat.markAllRead agent
```

### Mute / Unmute

```fsharp
let! _ = Chat.muteConvo agent convo.Id
// ...
let! _ = Chat.unmuteConvo agent convo.Id
```

### Delete a Message

Deletes a message for yourself only (the other participant still sees it):

```fsharp
let! _ = Chat.deleteMessage agent convo.Id msgId
```

## A Note on Attachments

The `MessageInput.Embed` field accepts a `MessageInputEmbedUnion option`. This is a discriminated union with a `Record` case (for sharing a post into a DM via `AppBskyEmbed.Record.Record`) and an `Unknown` fallback for forward compatibility. Image attachments in DMs are not yet part of the official Lexicon schema.
