namespace ProjectManagement.Configuration;

public sealed class FileDownloadOptions
{
    public int TokenLifetimeMinutes { get; set; } = 30;

    public bool BindTokensToUser { get; set; } = true;
}
