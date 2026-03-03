---
title: Chat / DMs
category: Type Reference
categoryindex: 2
index: 9
description: Send and receive Bluesky direct messages with FSharp.ATProto
keywords: chat, dm, direct messages, bluesky, conversations, fsharp, atproto
---

# Chat / Direct Messages

All examples use `taskResult {}`. See the [Error Handling guide](error-handling.html) for details.

Bluesky direct messages use a separate service (`api.bsky.chat`) from the main PDS. The `Chat` module adds the required proxy header automatically -- you use the same agent as for everything else.

## Starting a Conversation

Get or create a conversation with one or more members by their [DIDs](../concepts.html). `getConvoForMembers` takes a `Did list`, so you first need to resolve a handle to a DID (or use a DID you already have from a profile lookup):

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax

taskResult {
    let! agent = Bluesky.login "https://bsky.social" "my-handle.bsky.social" "app-password"

    // Look up the user's DID via their profile
    let! profile = Bluesky.getProfile agent "alice.bsky.social"

    // Start (or resume) a conversation with that user
    let! convo = Chat.getConvoForMembers agent [ profile.Did ]
    printfn "Conversation: %s (members: %d)" convo.Id convo.Members.Length

    return convo
}
```

If a conversation already exists between the specified members, the existing one is returned. Otherwise a new conversation is created.

## Sending Messages

### Plain Text

`Chat.sendMessage` takes the agent, conversation ID, and message text:

```fsharp
taskResult {
    let! msg = Chat.sendMessage agent convo.Id "Hello from F#!"

    match msg with
    | ChatMessage.Message m -> printfn "Sent: %s (id: %s)" m.Text m.Id
    | ChatMessage.Deleted _ -> ()
}
```

### Rich Text (Links, Mentions, Hashtags)

`Chat.sendMessage` **automatically detects** links, mentions, and hashtags -- just like `Bluesky.post`. No extra steps needed:

```fsharp
taskResult {
    let! _ = Chat.sendMessage agent convo.Id "Check out https://atproto.com for the AT Protocol spec!"
    return ()
}
```

See the [Rich Text](rich-text.html) guide for more on how facet detection works.

## Reading Messages

Retrieve messages from a conversation:

```fsharp
taskResult {
    let! page = Chat.getMessages agent convo.Id (Some 20L) None

    for m in page.Items do
        match m with
        | ChatMessage.Message msg ->
            printfn "  [%s] %s" (Did.value msg.Sender) msg.Text
        | ChatMessage.Deleted del ->
            printfn "  (deleted: %s)" del.Id

    return page
}
```

Each `ChatMessage` is a discriminated union with `Message` and `Deleted` cases. The `Message` record gives you typed access to `Id`, `Text`, `Sender` (a [DID](../concepts.html)), and `SentAt` (a `DateTimeOffset`). To fetch older messages, pass `page.Cursor` as the last argument.

## Reactions

Add or remove emoji reactions on a message:

```fsharp
taskResult {
    // Add a reaction
    let! _ = Chat.addReaction agent convo.Id msgId "\u2764\uFE0F"

    // Remove it
    let! _ = Chat.removeReaction agent convo.Id msgId "\u2764\uFE0F"
    return ()
}
```

## Managing Conversations

### List Conversations

```fsharp
taskResult {
    let! page = Chat.listConvos agent (Some 20L) None

    for c in page.Items do
        let members = c.Members |> List.map (fun m -> m.DisplayName) |> String.concat ", "
        printfn "%s: %s (unread: %d)" c.Id members c.UnreadCount

    return page
}
```

Each `ConvoSummary` gives you `Id`, `Members` (a `ProfileSummary list`), `LastMessageText`, `UnreadCount`, and `IsMuted`.

### Other Operations

```fsharp
taskResult {
    let! convo = Chat.getConvo agent convoId         // get by ID
    let! _ = Chat.acceptConvo agent convoId           // accept a request
    let! _ = Chat.leaveConvo agent convoId            // leave
    let! _ = Chat.markRead agent convo.Id             // mark one as read
    let! _ = Chat.markAllRead agent                   // mark all as read
    let! _ = Chat.muteConvo agent convo.Id            // mute
    let! _ = Chat.unmuteConvo agent convo.Id          // unmute
    let! _ = Chat.deleteMessage agent convo.Id msgId  // delete (for you only)
    return ()
}
```

## Attachments

Image attachments in DMs are not yet supported by the Bluesky API.

## Power Users: Raw XRPC

If you need full control over facets or want to include an embed (e.g., sharing a post into a DM), drop to the raw XRPC wrapper:

```fsharp
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
```
