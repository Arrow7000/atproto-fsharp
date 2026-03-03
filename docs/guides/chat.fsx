(**
---
title: Chat / DMs
category: Type Reference
categoryindex: 2
index: 9
description: Send and receive Bluesky direct messages with FSharp.ATProto
keywords: chat, dm, direct messages, bluesky, conversations, fsharp, atproto
---

# Chat / Direct Messages

Send and receive Bluesky direct messages through the `Chat` module.

All examples use `taskResult {}` -- see the [Error Handling guide](error-handling.html) for details. The chat proxy header (`atproto-proxy: did:web:api.bsky.chat#bsky_chat`) is applied automatically -- you use the same agent as for everything else.

## Domain Types

### ConvoSummary

A summary of a chat conversation.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `string` | The conversation identifier |
| `Members` | `ProfileSummary list` | Participants in the conversation |
| `LastMessageText` | `string option` | Text of the most recent message, if any |
| `UnreadCount` | `int64` | Number of unread messages |
| `IsMuted` | `bool` | Whether notifications are muted for this conversation |

### ChatMessage

Discriminated union representing a message in a conversation. Uses `[<RequireQualifiedAccess>]`.

| Case | Fields | Description |
|------|--------|-------------|
| `ChatMessage.Message` | `Id : string`, `Text : string`, `Sender : Did`, `SentAt : DateTimeOffset` | A visible message |
| `ChatMessage.Deleted` | `Id : string`, `Sender : Did` | A deleted message placeholder |

### Page&lt;'T&gt;

A paginated result containing a list of items and an optional cursor for the next page.

| Field | Type | Description |
|-------|------|-------------|
| `Items` | `'T list` | The items in this page |
| `Cursor` | `string option` | Cursor for the next page, or `None` if this is the last page |

## Functions

**SRTP:** All functions that take a `convoId` parameter accept either a `ConvoSummary` or a `string` conversation ID.

### Conversations

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Chat.listConvos` | `agent`, `limit: int64 option`, `cursor: string option` | `Result<Page<ConvoSummary>, XrpcError>` | List conversations, most recent first |
| `Chat.getConvoForMembers` | `agent`, `members: Did list` | `Result<ConvoSummary, XrpcError>` | Get or create a conversation with the given members |
| `Chat.getConvo` | `agent`, `convoId` | `Result<ConvoSummary, XrpcError>` | Get a conversation by ID |
| `Chat.acceptConvo` | `agent`, `convoId` | `Result<unit, XrpcError>` | Accept a conversation request |
| `Chat.leaveConvo` | `agent`, `convoId` | `Result<unit, XrpcError>` | Leave a conversation |
*)

(*** hide ***)
#nowarn "20"
#r "../../src/FSharp.ATProto.Syntax/bin/Release/net10.0/FSharp.ATProto.Syntax.dll"
#r "../../src/FSharp.ATProto.Core/bin/Release/net10.0/FSharp.ATProto.Core.dll"
#r "../../src/FSharp.ATProto.Bluesky/bin/Release/net10.0/FSharp.ATProto.Bluesky.dll"
open FSharp.ATProto.Syntax
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky

let agent = Unchecked.defaultof<AtpAgent>
let convo = Unchecked.defaultof<ConvoSummary>
let msgId = ""
let aliceHandle = Unchecked.defaultof<Handle>

(***)

taskResult {
    let! profile = Bluesky.getProfile agent aliceHandle
    let! convo = Chat.getConvoForMembers agent [ profile.Did ]
    printfn "Conversation: %s (members: %d)" convo.Id convo.Members.Length
}

(**
### Messages

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Chat.sendMessage` | `agent`, `convoId`, `text: string` | `Result<ChatMessage, XrpcError>` | Send a message with auto-detected rich text |
| `Chat.getMessages` | `agent`, `convoId`, `limit: int64 option`, `cursor: string option` | `Result<Page<ChatMessage>, XrpcError>` | Get messages, most recent first |
| `Chat.deleteMessage` | `agent`, `convoId`, `messageId: string` | `Result<unit, XrpcError>` | Delete a message (for you only) |

`sendMessage` automatically detects links, mentions, and hashtags -- just like `Bluesky.post`. No extra steps needed.
*)

taskResult {
    let! msg = Chat.sendMessage agent convo "Check out https://atproto.com!"

    match msg with
    | ChatMessage.Message m -> printfn "Sent: %s (id: %s)" m.Text m.Id
    | ChatMessage.Deleted _ -> ()
}

(** *)

taskResult {
    let! page = Chat.getMessages agent convo (Some 20L) None

    for m in page.Items do
        match m with
        | ChatMessage.Message msg ->
            printfn "[%s] %s" (Did.value msg.Sender) msg.Text
        | ChatMessage.Deleted del ->
            printfn "(deleted: %s)" del.Id
}

(**
### Read State

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Chat.markRead` | `agent`, `convoId` | `Result<unit, XrpcError>` | Mark a conversation as read |
| `Chat.markAllRead` | `agent` | `Result<int64, XrpcError>` | Mark all conversations as read; returns count updated |
*)

taskResult {
    let! _ = Chat.markRead agent convo
    let! updatedCount = Chat.markAllRead agent
    printfn "Marked %d conversations as read" updatedCount
}

(**
### Muting

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Chat.muteConvo` | `agent`, `convoId` | `Result<unit, XrpcError>` | Mute a conversation (no notifications) |
| `Chat.unmuteConvo` | `agent`, `convoId` | `Result<unit, XrpcError>` | Unmute a conversation |
*)

taskResult {
    let! _ = Chat.muteConvo agent convo
    let! _ = Chat.unmuteConvo agent convo
    return ()
}

(**
### Reactions

| Function | Accepts | Returns | Description |
|----------|---------|---------|-------------|
| `Chat.addReaction` | `agent`, `convoId`, `messageId: string`, `emoji: string` | `Result<unit, XrpcError>` | Add an emoji reaction to a message |
| `Chat.removeReaction` | `agent`, `convoId`, `messageId: string`, `emoji: string` | `Result<unit, XrpcError>` | Remove an emoji reaction from a message |
*)

taskResult {
    let! _ = Chat.addReaction agent convo msgId "❤️"
    let! _ = Chat.removeReaction agent convo msgId "❤️"
    return ()
}

(**
## Power Users: Raw XRPC

For full control over facets or to include an embed (e.g., sharing a post into a DM), drop to the raw XRPC wrapper. You must apply the chat proxy header manually when using raw wrappers:
*)

task {
    let text = "Check out https://atproto.com!"
    let! facets = RichText.parse agent text

    let! result =
        ChatBskyConvo.SendMessage.call (AtpAgent.withChatProxy agent)
            { ConvoId = convo.Id
              Message =
                { Text = text
                  Facets = if facets.IsEmpty then None else Some facets
                  Embed = None } }

    match result with
    | Ok msg -> printfn "Sent with custom facets"
    | Error err -> printfn "Failed: %A" err
}
