using System;
using System.Collections.Generic;

namespace ProjectManagement.Areas.ProjectOfficeReports.MiscActivities.ViewModels;

public sealed class MiscActivityDetailViewModel
{
    public Guid Id { get; init; }

    public Guid? ActivityTypeId { get; init; }

    public string? ActivityTypeName { get; init; }

    public string Nomenclature { get; init; } = string.Empty;

    public DateOnly OccurrenceDate { get; init; }

    public string? Description { get; init; }

    public string? ExternalLink { get; init; }

    public bool IsDeleted { get; init; }

    public DateTimeOffset CapturedAtUtc { get; init; }

    public string CapturedByUserId { get; init; } = string.Empty;

    public string CapturedByDisplayName { get; init; } = string.Empty;

    public DateTimeOffset? LastModifiedAtUtc { get; init; }

    public string? LastModifiedByUserId { get; init; }

    public string? LastModifiedByDisplayName { get; init; }

    public DateTimeOffset? DeletedUtc { get; init; }

    public string? DeletedByUserId { get; init; }

    public string? DeletedByDisplayName { get; init; }

    public string RowVersion { get; init; } = string.Empty;

    public IReadOnlyList<MiscActivityMediaViewModel> Media { get; init; } = Array.Empty<MiscActivityMediaViewModel>();

    public MiscActivityMediaUploadViewModel Upload { get; init; } = new();
}
