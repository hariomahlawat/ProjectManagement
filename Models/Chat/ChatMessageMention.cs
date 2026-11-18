namespace ProjectManagement.Models.Chat;

/// <summary>
/// A person @mentioned in a group message (group members only). Their mention alerts them even
/// in a group they muted. The text keeps the plain "@Name", so pages that do not know mentions
/// still read naturally.
/// </summary>
public sealed class ChatMessageMention
{
    public long MessageId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public ChatMessage? Message { get; set; }
}
