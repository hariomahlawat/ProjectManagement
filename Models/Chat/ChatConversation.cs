using System;
using System.Collections.Generic;

namespace ProjectManagement.Models.Chat;

/// <summary>
/// A chat thread. Phase 1 only creates direct (one-to-one) conversations; the participant
/// table already allows more than two members so group chats need no schema rework.
/// </summary>
public sealed class ChatConversation
{
    public const string DirectKind = "Direct";

    public long Id { get; set; }

    public string Kind { get; set; } = DirectKind;

    /// <summary>
    /// For direct conversations: the two user ids in ordinal order joined by '|'. Unique, so a
    /// pair of users can never end up with two parallel threads. Null for future group chats.
    /// </summary>
    public string? DirectKey { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Sorts the buddy list by recent conversation; written in the same save as the message.</summary>
    public DateTimeOffset? LastMessageUtc { get; set; }

    public ICollection<ChatParticipant> Participants { get; set; } = new List<ChatParticipant>();

    public static string BuildDirectKey(string userA, string userB)
        => string.CompareOrdinal(userA, userB) <= 0 ? $"{userA}|{userB}" : $"{userB}|{userA}";
}
