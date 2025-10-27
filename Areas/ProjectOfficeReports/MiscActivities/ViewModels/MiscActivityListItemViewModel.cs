using System;

namespace ProjectManagement.Areas.ProjectOfficeReports.MiscActivities.ViewModels;

public sealed class MiscActivityListItemViewModel
{
    public Guid Id { get; init; }

    public Guid? ActivityTypeId { get; init; }

    public string? ActivityTypeName { get; init; }

    public string Nomenclature { get; init; } = string.Empty;

    public DateOnly OccurrenceDate { get; init; }

    public string? Description { get; init; }

    public string? ExternalLink { get; init; }

    public int MediaCount { get; init; }

    public int ImageCount { get; init; }

    public int DocumentCount { get; init; }

    public bool IsDeleted { get; init; }

    public DateTimeOffset CapturedAtUtc { get; init; }

    public string CapturedByUserId { get; init; } = string.Empty;

    public string CapturedByDisplayName { get; init; } = string.Empty;

    public DateTimeOffset? LastModifiedAtUtc { get; init; }

    public string? LastModifiedByUserId { get; init; }

    public string? LastModifiedByDisplayName { get; init; }

    public string RowVersion { get; init; } = string.Empty;
}
