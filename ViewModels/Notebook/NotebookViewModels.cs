using ProjectManagement.Models;

// SECTION: My Notebook module read models
namespace ProjectManagement.ViewModels.Notebook;

public sealed class NotebookIndexVm
{
    public string View { get; set; } = "home";

    public string? Query { get; set; }

    public string? Filter { get; set; }

    public string? Tag { get; set; }

    public IReadOnlyList<NotebookRailItemVm> RailItems { get; set; } = Array.Empty<NotebookRailItemVm>();

    public IReadOnlyList<NotebookItemListVm> Items { get; set; } = Array.Empty<NotebookItemListVm>();

    public IReadOnlyList<NotebookItemListVm> PinnedItems { get; set; } = Array.Empty<NotebookItemListVm>();

    public IReadOnlyList<NotebookItemListVm> StickyItems { get; set; } = Array.Empty<NotebookItemListVm>();

    public IReadOnlyList<NotebookItemListVm> DueItems { get; set; } = Array.Empty<NotebookItemListVm>();

    public IReadOnlyList<NotebookItemListVm> RecentItems { get; set; } = Array.Empty<NotebookItemListVm>();

    public IReadOnlyList<NotebookTagVm> Tags { get; set; } = Array.Empty<NotebookTagVm>();

    public NotebookItemDetailVm? SelectedItem { get; set; }

    public NotebookSummaryVm Summary { get; set; } = new();
}

public sealed class NotebookRailItemVm
{
    public string Label { get; set; } = string.Empty;

    public string Icon { get; set; } = "bi-dot";

    public string Url { get; set; } = string.Empty;

    public int Count { get; set; }

    public bool IsActive { get; set; }
}

public class NotebookItemListVm
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Preview { get; set; }

    public NotebookItemType Type { get; set; }

    public NotebookItemStatus Status { get; set; }

    public NotebookPriority Priority { get; set; }

    public DateTimeOffset? ReminderAtUtc { get; set; }

    public string? ReminderDisplay { get; set; }

    public bool IsPinned { get; set; }

    public bool IsFavorite { get; set; }

    public string ColorKey { get; set; } = "white";

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();

    public int ChecklistTotal { get; set; }

    public int ChecklistDone { get; set; }

    public IReadOnlyList<NotebookChecklistItemVm> ChecklistPreviewItems { get; set; } = Array.Empty<NotebookChecklistItemVm>();

    public bool IsOverdue { get; set; }

    public bool IsDueToday { get; set; }
}

public sealed class NotebookItemDetailVm : NotebookItemListVm
{
    public string? BodyMarkdown { get; set; }

    public IReadOnlyList<NotebookChecklistItemVm> ChecklistItems { get; set; } = Array.Empty<NotebookChecklistItemVm>();

    public IReadOnlyList<NotebookAttachmentVm> Attachments { get; set; } = Array.Empty<NotebookAttachmentVm>();
}

public sealed class NotebookChecklistItemVm
{
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;

    public bool IsDone { get; set; }

    public int SortOrder { get; set; }
}

public sealed class NotebookAttachmentVm
{
    public int Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string DownloadUrl { get; set; } = string.Empty;
}

public sealed class NotebookTagVm
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}

public sealed class NotebookSummaryVm
{
    public int TotalActive { get; set; }

    public int DueToday { get; set; }

    public int Overdue { get; set; }

    public int StickyCount { get; set; }

    public int PinnedCount { get; set; }

    public int ChecklistCount { get; set; }
}

public sealed class NotebookWidgetVm
{
    public int DueTodayCount { get; set; }

    public int OverdueCount { get; set; }

    public IReadOnlyList<NotebookWidgetItemVm> DueItems { get; set; } = Array.Empty<NotebookWidgetItemVm>();

    public IReadOnlyList<NotebookWidgetItemVm> PinnedItems { get; set; } = Array.Empty<NotebookWidgetItemVm>();

    public IReadOnlyList<NotebookWidgetItemVm> StickyItems { get; set; } = Array.Empty<NotebookWidgetItemVm>();
}

public sealed class NotebookWidgetItemVm
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public NotebookItemType Type { get; set; }

    public DateTimeOffset? ReminderAtUtc { get; set; }

    public bool IsOverdue { get; set; }

    public string OpenUrl { get; set; } = string.Empty;
}

public sealed class NotebookCardRenderVm
{
    public NotebookItemListVm Item { get; set; } = new();

    public string View { get; set; } = "home";

    public string? Query { get; set; }

    public string? Filter { get; set; }

    public string? Tag { get; set; }

    public Guid? SelectedId { get; set; }

    public bool UseStickySurface { get; set; }
}

public sealed class NotebookItemActionVm
{
    public NotebookItemListVm Item { get; set; } = new();

    public string View { get; set; } = "home";

    public string? Query { get; set; }

    public string? Filter { get; set; }

    public string? Tag { get; set; }

    public Guid? SelectedId { get; set; }
}
