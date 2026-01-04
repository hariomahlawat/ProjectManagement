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

    public DateTimeOffset RequestedOn => RequestedOnUtc;

    public string? RequestNote { get; init; }

    public string OriginalName { get; init; } = string.Empty;

    public string OriginalDescription { get; init; } = "—";

    public string OriginalCaseFileNumber { get; init; } = "—";

    public string OriginalCategory { get; init; } = "—";
    public string OriginalTechnicalCategory { get; init; } = "—";

    // SECTION: Project type and build flag snapshot
    public string OriginalProjectType { get; init; } = "—";
    public string OriginalIsBuild { get; init; } = "—";

    public ProjectMetaChangeFieldVm Name { get; init; } = new("", "", false);

    public ProjectMetaChangeFieldVm Description { get; init; } = new("", "", false);

    public ProjectMetaChangeFieldVm CaseFileNumber { get; init; } = new("", "", false);

    public ProjectMetaChangeFieldVm Category { get; init; } = new("", "", false);
    public ProjectMetaChangeFieldVm TechnicalCategory { get; init; } = new("", "", false);

    // SECTION: Project type and build flag fields
    public ProjectMetaChangeFieldVm ProjectType { get; init; } = new("", "", false);
    public ProjectMetaChangeFieldVm IsBuild { get; init; } = new("", "", false);

    public ProjectMetaChangeFieldVm SponsoringUnit { get; init; } = new("", "", false);

    public ProjectMetaChangeFieldVm SponsoringLineDirectorate { get; init; } = new("", "", false);

    public bool HasDrift { get; init; }

    public IReadOnlyList<ProjectMetaChangeDriftVm> Drift { get; init; } = Array.Empty<ProjectMetaChangeDriftVm>();

    public string Summary { get; init; } = string.Empty;
}
