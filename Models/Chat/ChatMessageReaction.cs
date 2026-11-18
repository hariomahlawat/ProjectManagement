using System;

namespace ProjectManagement.Models.Chat;

/// <summary>
/// One person's reaction to a message: one row per message, person and kind (a person may give
/// several kinds). Removed with the message's conversation, and cleared when the message is unsent.
/// </summary>
public sealed class ChatMessageReaction
{
    public long MessageId { get; set; }

    public string UserId { get; set; } = string.Empty;

    /// <summary>One of <see cref="ChatReactionKind"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public ChatMessage? Message { get; set; }
}

/// <summary>
/// The fixed set of reactions (owner default, 2026-11-18): a short list that reads well in a
/// chain of command, rather than a full emoji picker.
/// </summary>
public static class ChatReactionKind
{
    /// <summary>👍</summary>
    public const string Like = "like";

    /// <summary>✓ Noted: the acknowledgement of an instruction or information.</summary>
    public const string Noted = "noted";

    /// <summary>👀 Looking into it.</summary>
    public const string Looking = "looking";

    public const int MaxLength = 16;

    /// <summary>In the order they are shown.</summary>
    public static readonly string[] All = { Noted, Like, Looking };

    public static bool IsKnown(string? kind) => kind is Like or Noted or Looking;
}
