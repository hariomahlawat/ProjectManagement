using System.Security.Claims;
using System.Text.Json;
using ProjectManagement.Configuration;

namespace ProjectManagement.ViewModels;

// Carries failed direct-publication form values across the existing redirect.
// Authority and lifecycle metadata always come from the fresh server read model.
internal static class PlanEditorRecovery
{
    public const string TempDataKey = "PlanEditRecovery";

    public static string Serialize(PlanEditInput input, string userId) =>
        JsonSerializer.Serialize(new RecoveryState(userId, input));

    public static PlanEditorVm Restore(
        PlanEditorVm editor, string? payload, int projectId, string? userId, ClaimsPrincipal principal)
    {
        if (string.IsNullOrEmpty(payload) || string.IsNullOrEmpty(userId) ||
            (!principal.IsInRole(RoleNames.Admin) && !principal.IsInRole(RoleNames.HoD)))
        {
            return editor;
        }

        RecoveryState? recovery;
        try
        {
            recovery = JsonSerializer.Deserialize<RecoveryState>(payload);
        }
        catch (JsonException)
        {
            return editor;
        }

        if (recovery?.Input is not { } input || input.ProjectId != projectId || input.Rows is null ||
            !string.Equals(recovery.UserId, userId, StringComparison.Ordinal))
        {
            return editor;
        }

        var mode = string.Equals(input.Mode, PlanEditorModes.Exact, StringComparison.OrdinalIgnoreCase)
            ? PlanEditorModes.Exact
            : string.Equals(input.Mode, PlanEditorModes.Durations, StringComparison.OrdinalIgnoreCase)
                ? PlanEditorModes.Durations : null;
        if (mode is null)
        {
            return editor;
        }

        var submitted = input.Rows
            .Where(row => row is not null && !string.IsNullOrWhiteSpace(row.Code))
            .GroupBy(row => row.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        if (mode == PlanEditorModes.Exact)
        {
            foreach (var row in editor.Exact.Rows)
            {
                if (submitted.TryGetValue(row.Code, out var values))
                {
                    row.PlannedStart = values.PlannedStart;
                    row.PlannedDue = values.PlannedDue;
                }
            }
        }
        else
        {
            editor.Durations.AnchorStart = input.AnchorStart;
            editor.Durations.IncludeWeekends = input.IncludeWeekends;
            editor.Durations.SkipHolidays = input.SkipHolidays;
            editor.Durations.NextStageStartPolicy = input.NextStageStartPolicy;
            foreach (var row in editor.Durations.Rows)
            {
                if (submitted.TryGetValue(row.Code, out var values))
                {
                    row.DurationDays = values.DurationDays;
                }
            }
        }

        return new PlanEditorVm
        {
            Exact = editor.Exact,
            Durations = editor.Durations,
            ActiveMode = mode,
            State = editor.State
        };
    }

    private sealed record RecoveryState(string UserId, PlanEditInput Input);
}
