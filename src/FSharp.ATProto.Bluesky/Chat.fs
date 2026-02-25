namespace FSharp.ATProto.Bluesky

open System.Threading.Tasks
open FSharp.ATProto.Core
open FSharp.ATProto.Syntax

/// <summary>
/// Convenience methods for Bluesky direct message (DM) and chat operations.
/// Wraps the <c>chat.bsky.convo.*</c> XRPC endpoints with a simplified API.
/// All methods require an authenticated <see cref="AtpAgent"/>.
/// </summary>
module Chat =

    /// <summary>
    /// List the authenticated user's conversations, ordered by most recent activity.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="limit">Maximum number of conversations to return. Pass <c>None</c> for the server default.</param>
    /// <param name="cursor">Pagination cursor from a previous response. Pass <c>None</c> for the first page.</param>
    /// <returns>The list of conversations with pagination info, or an <see cref="XrpcError"/>.</returns>
    let listConvos (agent: AtpAgent) (limit: int64 option) (cursor: string option)
        : Task<Result<ChatBskyConvo.ListConvos.Output, XrpcError>> =
        ChatBskyConvo.ListConvos.query agent
            { Limit = limit; Cursor = cursor; ReadState = None; Status = None }

    /// <summary>
    /// Get an existing conversation with the specified members, or create a new one if none exists.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="members">A list of DIDs of the conversation members (excluding the authenticated user, who is added automatically).</param>
    /// <returns>The conversation details, or an <see cref="XrpcError"/>.</returns>
    let getConvoForMembers (agent: AtpAgent) (members: Did list)
        : Task<Result<ChatBskyConvo.GetConvoForMembers.Output, XrpcError>> =
        ChatBskyConvo.GetConvoForMembers.query agent
            { Members = members }

    /// <summary>
    /// Send a plain text message to a conversation.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">The ID of the conversation to send the message to.</param>
    /// <param name="text">The message text content.</param>
    /// <returns>The sent message details, or an <see cref="XrpcError"/>.</returns>
    let sendMessage (agent: AtpAgent) (convoId: string) (text: string)
        : Task<Result<ChatBskyConvo.SendMessage.Output, XrpcError>> =
        ChatBskyConvo.SendMessage.call agent
            { ConvoId = convoId
              Message = { Text = text; Facets = None; Embed = None } }

    /// <summary>
    /// Get messages in a conversation, ordered by most recent first.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">The ID of the conversation to retrieve messages from.</param>
    /// <param name="limit">Maximum number of messages to return. Pass <c>None</c> for the server default.</param>
    /// <param name="cursor">Pagination cursor from a previous response. Pass <c>None</c> for the most recent messages.</param>
    /// <returns>The list of messages with pagination info, or an <see cref="XrpcError"/>.</returns>
    let getMessages (agent: AtpAgent) (convoId: string) (limit: int64 option) (cursor: string option)
        : Task<Result<ChatBskyConvo.GetMessages.Output, XrpcError>> =
        ChatBskyConvo.GetMessages.query agent
            { ConvoId = convoId; Limit = limit; Cursor = cursor }

    /// <summary>
    /// Delete a message from a conversation for the authenticated user only.
    /// The message remains visible to other participants.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">The ID of the conversation containing the message.</param>
    /// <param name="messageId">The ID of the message to delete.</param>
    /// <returns>The deleted message details, or an <see cref="XrpcError"/>.</returns>
    let deleteMessage (agent: AtpAgent) (convoId: string) (messageId: string)
        : Task<Result<ChatBskyConvo.DeleteMessageForSelf.Output, XrpcError>> =
        ChatBskyConvo.DeleteMessageForSelf.call agent
            { ConvoId = convoId; MessageId = messageId }

    /// <summary>
    /// Mark a conversation as read up to the latest message.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">The ID of the conversation to mark as read.</param>
    /// <returns>The updated conversation details, or an <see cref="XrpcError"/>.</returns>
    let markRead (agent: AtpAgent) (convoId: string)
        : Task<Result<ChatBskyConvo.UpdateRead.Output, XrpcError>> =
        ChatBskyConvo.UpdateRead.call agent
            { ConvoId = convoId; MessageId = None }

    /// <summary>
    /// Mark all conversations as read.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <returns>The result of the operation, or an <see cref="XrpcError"/>.</returns>
    let markAllRead (agent: AtpAgent)
        : Task<Result<ChatBskyConvo.UpdateAllRead.Output, XrpcError>> =
        ChatBskyConvo.UpdateAllRead.call agent
            { Status = None }

    /// <summary>
    /// Mute a conversation. Muted conversations do not generate notifications.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">The ID of the conversation to mute.</param>
    /// <returns>The updated conversation details, or an <see cref="XrpcError"/>.</returns>
    let muteConvo (agent: AtpAgent) (convoId: string)
        : Task<Result<ChatBskyConvo.MuteConvo.Output, XrpcError>> =
        ChatBskyConvo.MuteConvo.call agent
            { ConvoId = convoId }

    /// <summary>
    /// Unmute a previously muted conversation, restoring notifications.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="convoId">The ID of the conversation to unmute.</param>
    /// <returns>The updated conversation details, or an <see cref="XrpcError"/>.</returns>
    let unmuteConvo (agent: AtpAgent) (convoId: string)
        : Task<Result<ChatBskyConvo.UnmuteConvo.Output, XrpcError>> =
        ChatBskyConvo.UnmuteConvo.call agent
            { ConvoId = convoId }
