namespace FSharp.ATProto.Bluesky

open System.Threading.Tasks
open FSharp.ATProto.Core
open FSharp.ATProto.Syntax

/// <summary>
/// Witness type enabling SRTP-based overloading for conversation parameters.
/// Allows Chat functions to accept either a <see cref="ConvoSummary"/> or a <c>string</c> convo ID directly.
/// This type is an implementation detail and should not be used directly.
/// </summary>
[<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
type ConvoWitness =
    | ConvoWitness
    static member ToConvoId(ConvoWitness, c: ConvoSummary) = c.Id
    static member ToConvoId(ConvoWitness, s: string) = s

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

    let inline internal toConvoId (x : ^a) : string =
        ((^a or ConvoWitness) : (static member ToConvoId : ConvoWitness * ^a -> string) (ConvoWitness, x))

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

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let sendMessageImpl
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
                           SentAt = ProfileSummary.toDateTimeOffset mv.SentAt
                           Embed = mv.Embed |> Option.map ChatMessage.mapEmbed |})
        }

    /// <summary>
    /// Send a message to a conversation. Rich text (links, mentions, hashtags) is
    /// automatically detected and resolved, matching the behaviour of <c>Bluesky.post</c>.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">A <see cref="ConvoSummary"/> or <c>string</c> conversation ID.</param>
    /// <param name="text">The message text content. Links, mentions, and hashtags are auto-detected.</param>
    /// <returns>The sent message as a <see cref="ChatMessage"/>, or an <see cref="XrpcError"/>.</returns>
    let inline sendMessage (agent : AtpAgent) (convoId : ^a) (text : string) : Task<Result<ChatMessage, XrpcError>> =
        sendMessageImpl agent (toConvoId convoId) text

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let getMessagesImpl
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
    /// Get messages in a conversation, ordered by most recent first.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">A <see cref="ConvoSummary"/> or <c>string</c> conversation ID.</param>
    /// <param name="limit">Maximum number of messages to return. Pass <c>None</c> for the server default.</param>
    /// <param name="cursor">Pagination cursor from a previous response. Pass <c>None</c> for the most recent messages.</param>
    /// <returns>A page of <see cref="ChatMessage"/> with an optional cursor, or an <see cref="XrpcError"/>.</returns>
    let inline getMessages (agent : AtpAgent) (convoId : ^a) (limit : int64 option) (cursor : string option) : Task<Result<Page<ChatMessage>, XrpcError>> =
        getMessagesImpl agent (toConvoId convoId) limit cursor

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let deleteMessageImpl
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
    /// Delete a message from a conversation for the authenticated user only.
    /// The message remains visible to other participants.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">A <see cref="ConvoSummary"/> or <c>string</c> conversation ID.</param>
    /// <param name="messageId">The ID of the message to delete.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let inline deleteMessage (agent : AtpAgent) (convoId : ^a) (messageId : string) : Task<Result<unit, XrpcError>> =
        deleteMessageImpl agent (toConvoId convoId) messageId

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let markReadImpl (agent : AtpAgent) (convoId : string) : Task<Result<unit, XrpcError>> =
        task {
            let! result =
                ChatBskyConvo.UpdateRead.call (ensureChatProxy agent) { ConvoId = convoId; MessageId = None }

            return result |> Result.map ignore
        }

    /// <summary>
    /// Mark a conversation as read up to the latest message.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">A <see cref="ConvoSummary"/> or <c>string</c> conversation ID.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let inline markRead (agent : AtpAgent) (convoId : ^a) : Task<Result<unit, XrpcError>> =
        markReadImpl agent (toConvoId convoId)

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

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let muteConvoImpl (agent : AtpAgent) (convoId : string) : Task<Result<unit, XrpcError>> =
        task {
            let! result = ChatBskyConvo.MuteConvo.call (ensureChatProxy agent) { ConvoId = convoId }
            return result |> Result.map ignore
        }

    /// <summary>
    /// Mute a conversation. Muted conversations do not generate notifications.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">A <see cref="ConvoSummary"/> or <c>string</c> conversation ID.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let inline muteConvo (agent : AtpAgent) (convoId : ^a) : Task<Result<unit, XrpcError>> =
        muteConvoImpl agent (toConvoId convoId)

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let unmuteConvoImpl (agent : AtpAgent) (convoId : string) : Task<Result<unit, XrpcError>> =
        task {
            let! result = ChatBskyConvo.UnmuteConvo.call (ensureChatProxy agent) { ConvoId = convoId }
            return result |> Result.map ignore
        }

    /// <summary>
    /// Unmute a previously muted conversation, restoring notifications.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">A <see cref="ConvoSummary"/> or <c>string</c> conversation ID.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let inline unmuteConvo (agent : AtpAgent) (convoId : ^a) : Task<Result<unit, XrpcError>> =
        unmuteConvoImpl agent (toConvoId convoId)

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let acceptConvoImpl (agent : AtpAgent) (convoId : string) : Task<Result<unit, XrpcError>> =
        task {
            let! result = ChatBskyConvo.AcceptConvo.call (ensureChatProxy agent) { ConvoId = convoId }
            return result |> Result.map ignore
        }

    /// <summary>
    /// Accept a conversation request, allowing messages to be exchanged.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">A <see cref="ConvoSummary"/> or <c>string</c> conversation ID.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let inline acceptConvo (agent : AtpAgent) (convoId : ^a) : Task<Result<unit, XrpcError>> =
        acceptConvoImpl agent (toConvoId convoId)

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let leaveConvoImpl (agent : AtpAgent) (convoId : string) : Task<Result<unit, XrpcError>> =
        task {
            let! result = ChatBskyConvo.LeaveConvo.call (ensureChatProxy agent) { ConvoId = convoId }
            return result |> Result.map ignore
        }

    /// <summary>
    /// Leave a conversation. The conversation will no longer appear in your list.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">A <see cref="ConvoSummary"/> or <c>string</c> conversation ID.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let inline leaveConvo (agent : AtpAgent) (convoId : ^a) : Task<Result<unit, XrpcError>> =
        leaveConvoImpl agent (toConvoId convoId)

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let addReactionImpl
        (agent : AtpAgent)
        (convoId : string)
        (messageId : string)
        (emoji : string)
        : Task<Result<unit, XrpcError>> =
        task {
            let! result =
                ChatBskyConvo.AddReaction.call
                    (ensureChatProxy agent)
                    { ConvoId = convoId
                      MessageId = messageId
                      Value = emoji }

            return result |> Result.map ignore
        }

    /// <summary>
    /// Add an emoji reaction to a message in a conversation.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">A <see cref="ConvoSummary"/> or <c>string</c> conversation ID.</param>
    /// <param name="messageId">The ID of the message to react to.</param>
    /// <param name="emoji">The emoji reaction value (e.g., a Unicode emoji string).</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let inline addReaction (agent : AtpAgent) (convoId : ^a) (messageId : string) (emoji : string) : Task<Result<unit, XrpcError>> =
        addReactionImpl agent (toConvoId convoId) messageId emoji

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let removeReactionImpl
        (agent : AtpAgent)
        (convoId : string)
        (messageId : string)
        (emoji : string)
        : Task<Result<unit, XrpcError>> =
        task {
            let! result =
                ChatBskyConvo.RemoveReaction.call
                    (ensureChatProxy agent)
                    { ConvoId = convoId
                      MessageId = messageId
                      Value = emoji }

            return result |> Result.map ignore
        }

    /// <summary>
    /// Remove an emoji reaction from a message in a conversation.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">A <see cref="ConvoSummary"/> or <c>string</c> conversation ID.</param>
    /// <param name="messageId">The ID of the message to remove the reaction from.</param>
    /// <param name="emoji">The emoji reaction value to remove.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let inline removeReaction (agent : AtpAgent) (convoId : ^a) (messageId : string) (emoji : string) : Task<Result<unit, XrpcError>> =
        removeReactionImpl agent (toConvoId convoId) messageId emoji

    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let getConvoImpl (agent : AtpAgent) (convoId : string) : Task<Result<ConvoSummary, XrpcError>> =
        task {
            let! result = ChatBskyConvo.GetConvo.query (ensureChatProxy agent) { ConvoId = convoId }
            return result |> Result.map (fun output -> ConvoSummary.ofConvoView output.Convo)
        }

    /// <summary>
    /// Get a single conversation by its ID.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">A <see cref="ConvoSummary"/> or <c>string</c> conversation ID.</param>
    /// <returns>A <see cref="ConvoSummary"/> on success, or an <see cref="XrpcError"/>.</returns>
    let inline getConvo (agent : AtpAgent) (convoId : ^a) : Task<Result<ConvoSummary, XrpcError>> =
        getConvoImpl agent (toConvoId convoId)
