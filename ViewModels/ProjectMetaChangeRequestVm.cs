using System;

namespace ProjectManagement.ViewModels;

public sealed record ProjectMetaChangeFieldVm(string CurrentValue, string ProposedValue, bool HasChanged);

public sealed class ProjectMetaChangeRequestVm
{
    public int RequestId { get; init; }

    public string RequestedBy { get; init; } = string.Empty;

    public string? RequestedByUserId { get; init; }

    public DateTimeOffset RequestedOnUtc { get; init; }

    public string? RequestNote { get; init; }

    public ProjectMetaChangeFieldVm Name { get; init; } = new("", "", false);

    public ProjectMetaChangeFieldVm Description { get; init; } = new("", "", false);

    public ProjectMetaChangeFieldVm CaseFileNumber { get; init; } = new("", "", false);

    public ProjectMetaChangeFieldVm Category { get; init; } = new("", "", false);
}
