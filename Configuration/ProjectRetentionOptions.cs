namespace ProjectManagement.Configuration;

public sealed class ProjectRetentionOptions
{
    public int TrashRetentionDays { get; set; } = 30;

    public bool RemoveAssetsOnPurge { get; set; } = true;
}
