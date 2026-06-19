namespace ProjectManagement.Services.Notebook;

// SECTION: Shared My Notebook validation limits
public static class NotebookLimits
{
    public const int TitleMaxLength = 220;
    public const int BodyMaxLength = 20_000;
    public const int ChecklistTextMaxLength = 500;
    public const int MaxChecklistRows = 200;
    public const int LabelNameMaxLength = 60;
    public const int MaxLabelsPerItem = 12;
}
