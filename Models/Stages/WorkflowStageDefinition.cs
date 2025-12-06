using System;

namespace ProjectManagement.Models.Stages;

/// <summary>
/// Captures workflow-specific metadata for a stage.
/// </summary>
public sealed class WorkflowStageDefinition
{
    // SECTION: Constructor
    public WorkflowStageDefinition(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Stage code cannot be empty.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Stage name cannot be empty.", nameof(name));
        }

        Code = code;
        Name = name;
    }

    // SECTION: Properties
    public string Code { get; }

    public string Name { get; }
}
