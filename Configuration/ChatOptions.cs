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

    /// <summary>
    /// Changes one user may make in <see cref="ChangeWindowSeconds"/>: status messages, unsends and
    /// group changes (create, add, remove, rename, leave). Most of these are pushed to many people.
    /// </summary>
    public int ChangePermitLimit { get; set; } = 20;

    public int ChangeWindowSeconds { get; set; } = 60;

    /// <summary>Message searches one user may run in <see cref="SearchWindowSeconds"/>.</summary>
    public int SearchPermitLimit { get; set; } = 60;

    public int SearchWindowSeconds { get; set; } = 60;

    /// <summary>Link-preview requests one user may make in <see cref="PreviewWindowSeconds"/> (each asks for up to 10 links).</summary>
    public int PreviewPermitLimit { get; set; } = 120;

    public int PreviewWindowSeconds { get; set; } = 60;

    /// <summary>How long after sending a message its sender may still unsend it.</summary>
    public int UnsendWindowMinutes { get; set; } = 5;
}
