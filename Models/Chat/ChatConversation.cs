using System;
using System.Collections.Generic;

namespace ProjectManagement.Models.Chat;

/// <summary>
/// A chat thread: a direct (one-to-one) conversation, or a named group of up to
/// <see cref="MaxGroupMembers"/> people. Membership is the ChatParticipants rows; leaving or
/// being removed deletes the row, and with it access to the group's history.
/// </summary>
public sealed class ChatConversation
{
    public const string DirectKind = "Direct";

    public const string GroupKind = "Group";

    public const int MaxGroupMembers = 50;

    public const int MaxTitleLength = 100;

    public long Id { get; set; }

    public string Kind { get; set; } = DirectKind;

    /// <summary>
    /// For direct conversations: the two user ids in ordinal order joined by '|'. Unique, so a
    /// pair of users can never end up with two parallel threads. Null for future group chats.
    /// </summary>
    public string? DirectKey { get; set; }

    /// <summary>Group name. Null for direct conversations.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// The group's manager: may remove members and rename. Starts as the creator; passes to the
    /// longest-standing member if they leave. Null for direct conversations.
    /// </summary>
    public string? ManagerUserId { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Sorts the buddy list by recent conversation; written in the same save as the message.</summary>
    public DateTimeOffset? LastMessageUtc { get; set; }

    public ICollection<ChatParticipant> Participants { get; set; } = new List<ChatParticipant>();

    public static string BuildDirectKey(string userA, string userB)
        => string.CompareOrdinal(userA, userB) <= 0 ? $"{userA}|{userB}" : $"{userB}|{userA}";
}
