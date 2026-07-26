using System;
using System.Globalization;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.ViewModels;

/// <summary>
/// Compact repository presentation of the canonical project lifecycle position.
/// </summary>
public sealed record ProjectRepositoryStagePositionVm(
    string Primary,
    string? Secondary,
    bool IsMissing = false)
{
    public static ProjectRepositoryStagePositionVm Missing { get; } = new(
        "Lifecycle not recorded",
        "No stage activity is available for this project.",
        true);

    public static ProjectRepositoryStagePositionVm Create(
        Project project,
        IWorkflowStageMetadataProvider workflowStageMetadataProvider)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(workflowStageMetadataProvider);

        var stages = ProjectLifecyclePositionResolver.BuildProjectStages(
            project,
            workflowStageMetadataProvider);
        var position = ProjectLifecyclePositionResolver.Resolve(stages);

        if (stages.Count == 0 ||
            (project.LifecycleStatus != ProjectLifecycleStatus.Active &&
             !position.HasRecordedHistory))
        {
            return Missing;
        }

        if (position.CurrentStage is { } current)
        {
            if (current.Status is StageStatus.InProgress or StageStatus.Blocked)
            {
                var state = current.Status == StageStatus.Blocked ? "Blocked" : "In progress";
                var previous = position.PreviousCompletedStage;
                var secondary = previous is null
                    ? "Current project stage"
                    : $"Previous: {previous.Name}{FormatCompletedOn(previous.CompletedOn)}";

                return new ProjectRepositoryStagePositionVm(
                    $"{current.Name} · {state}",
                    secondary);
            }

            if (position.PreviousCompletedStage is { } completed)
            {
                var secondary = completed.CompletedOn.HasValue
                    ? $"Completed {FormatDate(completed.CompletedOn.Value)} · Next stage not started"
                    : "Next stage not started";

                return new ProjectRepositoryStagePositionVm(
                    $"{completed.Name} → {current.Name}",
                    secondary);
            }

            return new ProjectRepositoryStagePositionVm(
                $"Next: {current.Name}",
                "Project lifecycle has not started.");
        }

        if (position.LastCompletedStage is { } lastCompleted)
        {
            return new ProjectRepositoryStagePositionVm(
                $"{lastCompleted.Name} completed",
                lastCompleted.CompletedOn.HasValue
                    ? $"Completed {FormatDate(lastCompleted.CompletedOn.Value)}"
                    : "Latest recorded stage completed");
        }

        return Missing;
    }

    private static string FormatCompletedOn(DateOnly? completedOn) =>
        completedOn.HasValue ? $" · {FormatDate(completedOn.Value)}" : string.Empty;

    private static string FormatDate(DateOnly value) =>
        value.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
