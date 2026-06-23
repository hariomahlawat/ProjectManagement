namespace ProjectManagement.Configuration;

public sealed class NotebookTrashOptions
{
    public const string SectionName = "Notebook:Trash";
    public int RetentionDays { get; set; } = 30;
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(6);
}
