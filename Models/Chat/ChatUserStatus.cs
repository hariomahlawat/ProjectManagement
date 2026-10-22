using System;

namespace ProjectManagement.Models.Chat;

/// <summary>
/// A person's chat status message ("In review meeting till 4"), Yahoo-style. One row per user
/// while a message is set; clearing the message deletes the row. No foreign key to AspNetUsers,
/// like the rest of chat.
/// </summary>
public sealed class ChatUserStatus
{
    public const int MaxMessageLength = 140;

    public string UserId { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset UpdatedUtc { get; set; }
}
