using System.Security.Claims;
using System.Text.Json;
using ProjectManagement.Configuration;

namespace ProjectManagement.ViewModels;

// Carries a refused planner post across the existing redirect so the editor reopens
// with the user's entries and the server's messages (discovery C3.1, Increment 2a/2b).
// This type never validates. Authority and lifecycle metadata always come from the
// fresh server read model; entries are only presented, never saved, by restoring them.
internal static class PlanEditorRecovery
{
    public const string TempDataKey = "PlanEditRecovery";

    /// <param name="conflict">
    /// A HoD/Admin direct publication that lost a concurrency race. Its entries are shown
    /// over the changed plan on purpose so they can be reviewed and saved again.
    /// </param>
    public static string Serialize(PlanEditInput input, string userId, IEnumerable<string>? errors = null, bool conflict = false) =>
        JsonSerializer.Serialize(new RecoveryState(
            userId,
            input,
            (errors ?? Array.Empty<string>()).Where(error => !string.IsNullOrWhiteSpace(error)).ToArray(),
            conflict));

    public static PlanEditorVm Restore(
        PlanEditorVm editor, string? payload, int projectId, string? userId, ClaimsPrincipal principal)
    {
        if (string.IsNullOrEmpty(payload) || string.IsNullOrEmpty(userId))
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

        // A payload written before Conflict existed came only from the HoD/Admin race.
        var conflict = recovery.Conflict ?? true;
        var isDirectRole = principal.IsInRole(RoleNames.Admin) || principal.IsInRole(RoleNames.HoD);
        var errors = recovery.Errors ?? Array.Empty<string>();

        // Restore entries only onto the draft they were typed against. If the draft changed
        // in the meantime (another tab, a decision), restoring would silently revert that.
        var restore = conflict
            ? isDirectRole
            : string.Equals(input.EditToken, editor.State.EditToken, StringComparison.Ordinal);
        if (!restore)
        {
            return errors.Length == 0
                ? editor
                : new PlanEditorVm
                {
                    Exact = editor.Exact,
                    Durations = editor.Durations,
                    ActiveMode = editor.ActiveMode,
                    State = editor.State,
                    SubmissionErrors = errors
                };
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
            State = editor.State,
            SubmissionErrors = errors,
            RestoredSubmission = true
        };
    }

    private sealed record RecoveryState(string UserId, PlanEditInput Input, string[]? Errors = null, bool? Conflict = null);
}
