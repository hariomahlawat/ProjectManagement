using System;
using System.Collections.Generic;
using ProjectManagement.Models.Plans;

namespace ProjectManagement.Models.Stages;

public interface IWorkflowChecklistProvider
{
    IReadOnlyList<string> GetChecklist(string? workflowVersion, string? stageCode);
}

/// <summary>
/// Supplies checklist entries based on workflow version configuration.
/// </summary>
public sealed class WorkflowChecklistProvider : IWorkflowChecklistProvider
{
    // SECTION: Backing store
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> _versionedChecklists;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _defaultLookup;

    // SECTION: Constructor
    public WorkflowChecklistProvider()
    {
        _versionedChecklists = WorkflowChecklistConfiguration.All;
        _defaultLookup = WorkflowChecklistConfiguration.GetForVersion(PlanConstants.DefaultStageTemplateVersion);
    }

    // SECTION: API
    public IReadOnlyList<string> GetChecklist(string? workflowVersion, string? stageCode)
    {
        if (string.IsNullOrWhiteSpace(stageCode))
        {
            return Array.Empty<string>();
        }

        if (!string.IsNullOrWhiteSpace(workflowVersion)
            && _versionedChecklists.TryGetValue(workflowVersion, out var lookup)
            && lookup.TryGetValue(stageCode, out var items))
        {
            return items;
        }

        return _defaultLookup.TryGetValue(stageCode, out var fallbackItems)
            ? fallbackItems
            : Array.Empty<string>();
    }
}
