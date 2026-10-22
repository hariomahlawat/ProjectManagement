namespace ProjectManagement.Models.Chat;

/// <summary>Kinds of group notice stored as <see cref="ChatMessage.EventKind"/>.</summary>
public static class ChatGroupEvent
{
    public const string Created = "Created";
    public const string Added = "Added";
    public const string Removed = "Removed";
    public const string Left = "Left";
    public const string Renamed = "Renamed";

    public const int MaxDataLength = 450;
}
