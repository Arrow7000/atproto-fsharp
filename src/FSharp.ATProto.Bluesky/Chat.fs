namespace FSharp.ATProto.Bluesky

open System.Threading.Tasks
open FSharp.ATProto.Core
open FSharp.ATProto.Syntax

/// <summary>
/// Convenience methods for Bluesky direct message (DM) and chat operations.
/// Wraps the <c>chat.bsky.convo.*</c> XRPC endpoints with a simplified API.
/// All methods require an authenticated <see cref="AtpAgent"/>.
/// The chat proxy header (<c>atproto-proxy: did:web:api.bsky.chat#bsky_chat</c>) is applied
/// automatically -- callers do not need to use <see cref="AtpAgent.withChatProxy"/> manually.
/// </summary>
module Chat =

    /// Ensures the agent has the chat proxy header. Idempotent: does not add a
    /// duplicate header if one is already present.
    let private ensureChatProxy (agent : AtpAgent) : AtpAgent =
        let hasProxy = agent.ExtraHeaders |> List.exists (fun (k, _) -> k = "atproto-proxy")
        if hasProxy then agent else AtpAgent.withChatProxy agent

    /// <summary>
    /// List the authenticated user's conversations, ordered by most recent activity.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="limit">Maximum number of conversations to return. Pass <c>None</c> for the server default.</param>
    /// <param name="cursor">Pagination cursor from a previous response. Pass <c>None</c> for the first page.</param>
    /// <returns>A page of <see cref="ConvoSummary"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let listConvos
        (agent : AtpAgent)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ConvoSummary>, XrpcError>> =
        task {
            let! result =
                ChatBskyConvo.ListConvos.query
                    (ensureChatProxy agent)
                    { Limit = limit
                      Cursor = cursor
                      ReadState = None
                      Status = None }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Convos |> List.map ConvoSummary.ofConvoView
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Get an existing conversation with the specified members, or create a new one if none exists.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="members">A list of DIDs of the conversation members (excluding the authenticated user, who is added automatically).</param>
    /// <returns>A <see cref="ConvoSummary"/>, or an <see cref="XrpcError"/>.</returns>
    let getConvoForMembers
        (agent : AtpAgent)
        (members : Did list)
        : Task<Result<ConvoSummary, XrpcError>> =
        task {
            let! result = ChatBskyConvo.GetConvoForMembers.query (ensureChatProxy agent) { Members = members }
            return result |> Result.map (fun output -> ConvoSummary.ofConvoView output.Convo)
        }

    /// <summary>
    /// Send a message to a conversation. Rich text (links, mentions, hashtags) is
    /// automatically detected and resolved, matching the behaviour of <c>Bluesky.post</c>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">The ID of the conversation to send the message to.</param>
    /// <param name="text">The message text content. Links, mentions, and hashtags are auto-detected.</param>
    /// <returns>The sent message as a <see cref="ChatMessage"/>, or an <see cref="XrpcError"/>.</returns>
    let sendMessage
        (agent : AtpAgent)
        (convoId : string)
        (text : string)
        : Task<Result<ChatMessage, XrpcError>> =
        task {
            let! facets = RichText.parse agent text
            let facetOption = if facets.IsEmpty then None else Some facets

            let! result =
                ChatBskyConvo.SendMessage.call
                    (ensureChatProxy agent)
                    { ConvoId = convoId
                      Message =
                        { Text = text
                          Facets = facetOption
                          Embed = None } }

            return
                result
                |> Result.map (fun mv ->
                    ChatMessage.Message
                        {| Id = mv.Id
                           Text = mv.Text
                           Sender = mv.Sender.Did
                           SentAt = ProfileSummary.toDateTimeOffset mv.SentAt |})
        }

    /// <summary>
    /// Get messages in a conversation, ordered by most recent first.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">The ID of the conversation to retrieve messages from.</param>
    /// <param name="limit">Maximum number of messages to return. Pass <c>None</c> for the server default.</param>
    /// <param name="cursor">Pagination cursor from a previous response. Pass <c>None</c> for the most recent messages.</param>
    /// <returns>A page of <see cref="ChatMessage"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let getMessages
        (agent : AtpAgent)
        (convoId : string)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ChatMessage>, XrpcError>> =
        task {
            let! result =
                ChatBskyConvo.GetMessages.query
                    (ensureChatProxy agent)
                    { ConvoId = convoId
                      Limit = limit
                      Cursor = cursor }

            return
                result
                |> Result.map (fun output ->
                    { Items = output.Messages |> List.choose ChatMessage.ofMessagesItem
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Delete a message from a conversation for the authenticated user only.
    /// The message remains visible to other participants.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">The ID of the conversation containing the message.</param>
    /// <param name="messageId">The ID of the message to delete.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let deleteMessage
        (agent : AtpAgent)
        (convoId : string)
        (messageId : string)
        : Task<Result<unit, XrpcError>> =
        task {
            let! result =
                ChatBskyConvo.DeleteMessageForSelf.call
                    (ensureChatProxy agent)
                    { ConvoId = convoId
                      MessageId = messageId }

            return result |> Result.map ignore
        }

    /// <summary>
    /// Mark a conversation as read up to the latest message.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">The ID of the conversation to mark as read.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let markRead (agent : AtpAgent) (convoId : string) : Task<Result<unit, XrpcError>> =
        task {
            let! result =
                ChatBskyConvo.UpdateRead.call (ensureChatProxy agent) { ConvoId = convoId; MessageId = None }

            return result |> Result.map ignore
        }

    /// <summary>
    /// Mark all conversations as read.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <returns>The number of conversations updated, or an <see cref="XrpcError"/>.</returns>
    let markAllRead (agent : AtpAgent) : Task<Result<int64, XrpcError>> =
        task {
            let! result = ChatBskyConvo.UpdateAllRead.call (ensureChatProxy agent) { Status = None }
            return result |> Result.map (fun output -> output.UpdatedCount)
        }

    /// <summary>
    /// Mute a conversation. Muted conversations do not generate notifications.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">The ID of the conversation to mute.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let muteConvo (agent : AtpAgent) (convoId : string) : Task<Result<unit, XrpcError>> =
        task {
            let! result = ChatBskyConvo.MuteConvo.call (ensureChatProxy agent) { ConvoId = convoId }
            return result |> Result.map ignore
        }

    /// <summary>
    /// Unmute a previously muted conversation, restoring notifications.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">The ID of the conversation to unmute.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let unmuteConvo (agent : AtpAgent) (convoId : string) : Task<Result<unit, XrpcError>> =
        task {
            let! result = ChatBskyConvo.UnmuteConvo.call (ensureChatProxy agent) { ConvoId = convoId }
            return result |> Result.map ignore
        }
