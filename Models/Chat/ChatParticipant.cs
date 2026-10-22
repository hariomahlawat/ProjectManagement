using System;

namespace ProjectManagement.Models.Chat;

public sealed class ChatParticipant
{
    public long ConversationId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateTimeOffset JoinedUtc { get; set; }

    /// <summary>
    /// Highest message id this participant has read. Unread count is the number of messages
    /// from other participants with a greater id. Only ever moves forward.
    /// </summary>
    public long? LastReadMessageId { get; set; }

    public ChatConversation? Conversation { get; set; }
}
