using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Plans;

namespace ProjectManagement.Models.Stages;

public interface IWorkflowStageMetadataProvider
{
    IReadOnlyList<WorkflowStageDefinition> GetStages(string? workflowVersion);

    IReadOnlyDictionary<string, string> GetDisplayNameLookup(string? workflowVersion);

    string GetDisplayName(string? workflowVersion, string? stageCode);
}

/// <summary>
/// Provides workflow-specific stage metadata for display and lookup.
/// </summary>
public sealed class WorkflowStageMetadataProvider : IWorkflowStageMetadataProvider
{
    // SECTION: Backing stores
    private readonly Dictionary<string, WorkflowStageDefinition[]> _definitions;
    private readonly WorkflowStageDefinition[] _defaultDefinitions;

    // SECTION: Constructor
    public WorkflowStageMetadataProvider()
    {
        _definitions = new Dictionary<string, WorkflowStageDefinition[]>(StringComparer.OrdinalIgnoreCase)
        {
            [ProcurementWorkflow.VersionV1] = ProcurementWorkflow.StageDefinitionsFor(ProcurementWorkflow.VersionV1),
            [ProcurementWorkflow.VersionV2] = ProcurementWorkflow.StageDefinitionsFor(ProcurementWorkflow.VersionV2)
        };

        _defaultDefinitions = ProcurementWorkflow.StageDefinitionsFor(PlanConstants.DefaultStageTemplateVersion);
    }

    // SECTION: API
    public IReadOnlyList<WorkflowStageDefinition> GetStages(string? workflowVersion)
    {
        if (!string.IsNullOrWhiteSpace(workflowVersion)
            && _definitions.TryGetValue(workflowVersion, out var definitions))
        {
            return definitions;
        }

        return _defaultDefinitions;
    }

    public IReadOnlyDictionary<string, string> GetDisplayNameLookup(string? workflowVersion)
    {
        return GetStages(workflowVersion)
            .ToDictionary(stage => stage.Code, stage => stage.Name, StringComparer.OrdinalIgnoreCase);
    }

    public string GetDisplayName(string? workflowVersion, string? stageCode)
    {
        if (string.IsNullOrWhiteSpace(stageCode))
        {
            return "—";
        }

        var lookup = GetDisplayNameLookup(workflowVersion);
        return lookup.TryGetValue(stageCode, out var name) ? name : stageCode;
    }
}
