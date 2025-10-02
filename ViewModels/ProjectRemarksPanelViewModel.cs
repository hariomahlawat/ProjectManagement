using System;
using System.Collections.Generic;

namespace ProjectManagement.ViewModels;

public sealed class ProjectRemarksPanelViewModel
{
    public static readonly ProjectRemarksPanelViewModel Empty = new();

    public int ProjectId { get; init; }

    public string? CurrentUserId { get; init; }

    public string? ActorDisplayName { get; init; }

    public string? ActorRole { get; init; }

    public string? ActorRoleLabel { get; init; }

    public IReadOnlyList<string> ActorRoles { get; init; } = Array.Empty<string>();

    public bool ShowComposer { get; init; }

    public bool AllowInternal { get; init; }

    public bool AllowExternal { get; init; }

    public bool ShowDeletedToggle { get; init; }

    public bool ActorHasOverride { get; init; }

    public string Today { get; init; } = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    public int PageSize { get; init; } = 20;

    public string TimeZone { get; init; } = "Asia/Kolkata";

    public IReadOnlyList<RemarkRoleOption> RoleOptions { get; init; } = Array.Empty<RemarkRoleOption>();

    public IReadOnlyList<RemarkStageOption> StageOptions { get; init; } = Array.Empty<RemarkStageOption>();

    public sealed record RemarkRoleOption(string Value, string Label, string Canonical);

    public sealed record RemarkStageOption(string Value, string Label);
}
