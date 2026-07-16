using System;
using System.Collections.Generic;

namespace ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

/// <summary>
/// Configuration for the reusable proliferation project search combobox.
/// The backing select retains immutable project IDs for form/state compatibility.
/// </summary>
public sealed record ProliferationProjectPickerModel(
    string InputId,
    string ValueInputId,
    string SuggestionsId,
    string Label,
    IReadOnlyList<ProliferationCompletedProjectOption> Projects,
    string Placeholder = "Search project name, acronym or code",
    string EligibilityText = "Search completed projects eligible for proliferation entry.",
    bool Required = false,
    bool Compact = false,
    bool ShowLabel = true,
    bool ShowEligibilityText = true,
    bool ShowRecent = true,
    string RecentStorageKey = "prism-proliferation-recent-projects",
    string? ErrorElementId = null,
    string? AriaLabel = null,
    string? WrapperClass = null)
{
    public string ClearButtonId => $"{InputId}-clear";
    public string StatusId => $"{InputId}-status";
    public string RootId => $"{InputId}-picker";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(InputId)) throw new ArgumentException("InputId is required.", nameof(InputId));
        if (string.IsNullOrWhiteSpace(ValueInputId)) throw new ArgumentException("ValueInputId is required.", nameof(ValueInputId));
        if (string.IsNullOrWhiteSpace(SuggestionsId)) throw new ArgumentException("SuggestionsId is required.", nameof(SuggestionsId));
    }
}
