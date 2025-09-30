using System;
using System.Collections.Generic;

namespace ProjectManagement.ViewModels;

public sealed record ProjectMetaChangeFieldVm(string CurrentValue, string ProposedValue, bool HasChanged);

public sealed record ProjectMetaChangeDriftVm(string Field, string OriginalValue, string CurrentValue, bool IsProjectRecord = false);

public sealed class ProjectMetaChangeRequestVm
{
    public int RequestId { get; init; }

    public string RequestedBy { get; init; } = string.Empty;

    public string? RequestedByUserId { get; init; }

    public DateTimeOffset RequestedOnUtc { get; init; }

    public string? RequestNote { get; init; }

    public string OriginalName { get; init; } = string.Empty;

    public string OriginalDescription { get; init; } = "—";

    public string OriginalCaseFileNumber { get; init; } = "—";

    public string OriginalCategory { get; init; } = "—";

    public ProjectMetaChangeFieldVm Name { get; init; } = new("", "", false);

    public ProjectMetaChangeFieldVm Description { get; init; } = new("", "", false);

    public ProjectMetaChangeFieldVm CaseFileNumber { get; init; } = new("", "", false);

    public ProjectMetaChangeFieldVm Category { get; init; } = new("", "", false);

    public bool HasDrift { get; init; }

    public IReadOnlyList<ProjectMetaChangeDriftVm> Drift { get; init; } = Array.Empty<ProjectMetaChangeDriftVm>();
}
