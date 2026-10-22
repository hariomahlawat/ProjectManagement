namespace ProjectManagement.Configuration;

/// <summary>
/// Person-to-person chat. Turning it off hides the chat UI, refuses the chat API and hub, and
/// leaves stored conversations untouched, so it can be switched back on without data loss.
/// </summary>
public sealed class ChatOptions
{
    public const string SectionName = "Chat";

    public bool Enabled { get; set; } = true;

    /// <summary>Messages one user may send in <see cref="SendWindowSeconds"/>.</summary>
    public int SendPermitLimit { get; set; } = 20;

    public int SendWindowSeconds { get; set; } = 10;

    /// <summary>How long after sending a message its sender may still unsend it.</summary>
    public int UnsendWindowMinutes { get; set; } = 5;
}
