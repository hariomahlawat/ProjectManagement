using System;
using System.Collections.Generic;
using ProjectManagement.Models.Plans;

namespace ProjectManagement.Models.Stages;

public interface IWorkflowChecklistProvider
{
    IReadOnlyList<string> GetChecklist(string? workflowVersion, string? stageCode);
    string? GetPurpose(string? workflowVersion, string? stageCode);
}

/// <summary>
/// Supplies initial stage guidance from local workflow configuration.
/// Once a stage guidance record exists, the database value is authoritative.
/// </summary>
public sealed class WorkflowChecklistProvider : IWorkflowChecklistProvider
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> _versionedChecklists;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _versionedPurposes;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _defaultChecklistLookup;
    private readonly IReadOnlyDictionary<string, string> _defaultPurposeLookup;

    public WorkflowChecklistProvider()
    {
        _versionedChecklists = WorkflowChecklistConfiguration.All;
        _versionedPurposes = WorkflowChecklistConfiguration.AllPurposes;
        _defaultChecklistLookup = WorkflowChecklistConfiguration.GetForVersion(PlanConstants.DefaultStageTemplateVersion);
        _defaultPurposeLookup = WorkflowChecklistConfiguration.GetPurposesForVersion(PlanConstants.DefaultStageTemplateVersion);
    }

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

        return _defaultChecklistLookup.TryGetValue(stageCode, out var fallbackItems)
            ? fallbackItems
            : Array.Empty<string>();
    }

    public string? GetPurpose(string? workflowVersion, string? stageCode)
    {
        if (string.IsNullOrWhiteSpace(stageCode))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(workflowVersion)
            && _versionedPurposes.TryGetValue(workflowVersion, out var lookup)
            && lookup.TryGetValue(stageCode, out var purpose))
        {
            return string.IsNullOrWhiteSpace(purpose) ? null : purpose;
        }

        return _defaultPurposeLookup.TryGetValue(stageCode, out var fallbackPurpose)
            && !string.IsNullOrWhiteSpace(fallbackPurpose)
                ? fallbackPurpose
                : null;
    }
}
