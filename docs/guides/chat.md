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
| Ok result ->
    let convo = result.Convo
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
| Ok msg -> printfn "Sent: %s (id: %s)" msg.Text msg.Id
| Error e -> printfn "Send failed: %A" e
```

### Rich Text (with Links, Mentions, Hashtags)

For messages with rich text facets, use `ChatBskyConvo.SendMessage.call` with a `MessageInput` that includes resolved facets:

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

See the [Rich Text](rich-text.html) guide for more on facet detection and resolution.

## Reading Messages

Retrieve messages from a conversation with optional pagination:

```fsharp
let! msgsResult = Chat.getMessages agent convo.Id (Some 20L) None

match msgsResult with
| Ok ms ->
    printfn "Messages (%d):" ms.Messages.Length
    for m in ms.Messages do
        match m with
        | ChatBskyConvo.GetMessages.OutputMessagesItem.MessageView msg ->
            printfn "  [%s] %s" (Did.value msg.Sender.Did) msg.Text
        | ChatBskyConvo.GetMessages.OutputMessagesItem.DeletedMessageView _ ->
            printfn "  (deleted)"
        | ChatBskyConvo.GetMessages.OutputMessagesItem.Unknown _ ->
            ()
| Error e ->
    printfn "Failed: %A" e
```

Messages are returned as an `OutputMessagesItem` discriminated union with cases for `MessageView`, `DeletedMessageView`, and `Unknown` (for forward compatibility). Pattern match to handle each kind. The `MessageView` record gives you typed access to `Text`, `Sender.Did`, `SentAt`, optional `Facets`, and more.

To fetch older messages, pass the cursor from the previous response:

```fsharp
let! page2 = Chat.getMessages agent convo.Id (Some 20L) ms.Cursor
```

## Reactions

Add a reaction (emoji) to a message using the generated XRPC wrapper:

```fsharp
let! reactionResult =
    ChatBskyConvo.AddReaction.call (AtpAgent.withChatProxy agent)
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
    printfn "Reaction failed: %A" e
```

## Managing Conversations

### List Conversations

```fsharp
let! convosResult = Chat.listConvos agent (Some 20L) None

match convosResult with
| Ok cs ->
    for c in cs.Convos do
        let members =
            c.Members |> List.map (fun m -> Handle.value m.Handle) |> String.concat ", "
        printfn "%s: %s (unread: %d)" c.Id members c.UnreadCount
| Error e ->
    printfn "Failed: %A" e
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
let! _ = Chat.deleteMessage agent convo.Id msg.Id
```

## A Note on Attachments

The `MessageInput.Embed` field accepts a `MessageInputEmbedUnion option`. This is a discriminated union with a `Record` case (for sharing a post into a DM via `AppBskyEmbed.Record.Record`) and an `Unknown` fallback for forward compatibility. Image attachments in DMs are not yet part of the official Lexicon schema.
