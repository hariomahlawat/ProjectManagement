using System;

namespace ProjectManagement.Areas.ProjectOfficeReports.MiscActivities.ViewModels;

public sealed class MiscActivityMediaViewModel
{
    public Guid Id { get; init; }

    public string OriginalFileName { get; init; } = string.Empty;

    public string MediaType { get; init; } = string.Empty;

    public long FileSize { get; init; }

    public string? Caption { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public DateTimeOffset UploadedAtUtc { get; init; }

    public string UploadedByUserId { get; init; } = string.Empty;

    public string RowVersion { get; init; } = string.Empty;

    public string StorageKey { get; init; } = string.Empty;
}
