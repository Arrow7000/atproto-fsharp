---
title: Chat / DMs
category: Guides
categoryindex: 2
index: 3
description: Send and receive Bluesky direct messages with FSharp.ATProto
keywords: chat, dm, direct messages, bluesky, conversations
---

# Chat / Direct Messages

Bluesky direct messages use a separate service (`api.bsky.chat`) from the main PDS. FSharp.ATProto handles the proxy routing transparently through a chat-configured agent.

## Creating a Chat Agent

All chat operations require an agent configured with the Bluesky chat proxy header. Create one from an existing authenticated agent:

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

let agent = AtpAgent.create "https://bsky.social"
let! _ = AtpAgent.login "alice.bsky.social" "app-password" agent

// Create a chat-proxied agent
let chatAgent = AtpAgent.withChatProxy agent
```

The chat agent shares the same HTTP client and session as the original. It adds the `atproto-proxy: did:web:api.bsky.chat#bsky_chat` header to all requests, which routes them through the chat service.

Use the regular `agent` for posts, likes, and follows. Use `chatAgent` for all DM operations.

## Starting a Conversation

Get or create a conversation with one or more members by their DIDs:

```fsharp
let! convoResult = Chat.getConvoForMembers chatAgent [ "did:plc:xyz123" ]

match convoResult with
| Ok result ->
    let convo = result.Convo
    printfn "Conversation: %s (members: %d)" convo.Id convo.Members.Length
| Error e ->
    printfn "Failed: %A" e.Message
```

If a conversation already exists between the specified members, the existing one is returned. Otherwise a new conversation is created.

## Sending Messages

### Plain Text

```fsharp
let! msgResult = Chat.sendMessage chatAgent convo.Id "Hello from F#!"

match msgResult with
| Ok msg -> printfn "Sent: %s (id: %s)" msg.Text msg.Id
| Error e -> printfn "Send failed: %A" e.Message
```

### Rich Text (with Links, Mentions, Hashtags)

For messages with rich text facets, use the generated XRPC wrapper directly with a `MessageInput`:

```fsharp
let text = "Check out https://atproto.com for the AT Protocol spec!"
let! facets = RichText.parse agent text

let! result =
    ChatBskyConvo.SendMessage.call chatAgent
        { ConvoId = convo.Id
          Message =
            { Text = text
              Facets = if facets.IsEmpty then None else Some facets
              Embed = None } }
```

## Reading Messages

Retrieve messages from a conversation with optional pagination:

```fsharp
let! msgsResult = Chat.getMessages chatAgent convo.Id (Some 20L) None

match msgsResult with
| Ok ms ->
    printfn "Messages (%d):" ms.Messages.Length
    for m in ms.Messages do
        let typ = m.GetProperty("$type").GetString()
        if typ = "chat.bsky.convo.defs#messageView" then
            let text = m.GetProperty("text").GetString()
            let sender = m.GetProperty("sender").GetProperty("did").GetString()
            printfn "  [%s] %s" sender text
| Error e ->
    printfn "Failed: %A" e.Message
```

Messages come back as `JsonElement` values because the schema uses a union type. Check the `$type` field to determine the message kind (`messageView` for regular messages, `deletedMessageView` for deleted ones).

To fetch older messages, pass the cursor from the previous response:

```fsharp
// Second page
let! page2 = Chat.getMessages chatAgent convo.Id (Some 20L) ms.Cursor
```

## Reactions

Add a reaction (emoji) to a message using the generated XRPC wrapper:

```fsharp
let! reactionResult =
    ChatBskyConvo.AddReaction.call chatAgent
        { ConvoId = convo.Id
          MessageId = msg.Id
          Value = "\u2764\uFE0F" }  // red heart

match reactionResult with
| Ok r ->
    let count =
        r.Message.Reactions
        |> Option.map List.length
        |> Option.defaultValue 0
    printfn "Reactions on message: %d" count
| Error e ->
    printfn "Reaction failed: %A" e.Message
```

## Managing Conversations

### List Conversations

```fsharp
let! convosResult = Chat.listConvos chatAgent (Some 20L) None

match convosResult with
| Ok cs ->
    for c in cs.Convos do
        let members =
            c.Members |> List.map (fun m -> m.Handle) |> String.concat ", "
        printfn "%s: %s (unread: %d)" c.Id members c.UnreadCount
| Error e ->
    printfn "Failed: %A" e.Message
```

### Mark as Read

```fsharp
let! _ = Chat.markRead chatAgent convo.Id
```

Or mark all conversations as read:

```fsharp
let! _ = Chat.markAllRead chatAgent
```

### Mute / Unmute

```fsharp
let! _ = Chat.muteConvo chatAgent convo.Id
// ...
let! _ = Chat.unmuteConvo chatAgent convo.Id
```

### Delete a Message

Deletes a message for yourself only (the other participant still sees it):

```fsharp
let! _ = Chat.deleteMessage chatAgent convo.Id msg.Id
```

## A Note on Attachments

The `MessageInput.Embed` field accepts an optional `JsonElement`, corresponding to a union type in the Lexicon schema. Currently the only defined embed type for DMs is record embeds (sharing a post into a DM). Image attachments in DMs are not yet part of the official Lexicon schema.
