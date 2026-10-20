using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace ProjectManagement.ViewModels;

// Carries a refused Actual editor post across the existing redirect so the editor
// reopens with the user's entries and the server's validation messages (C7.2).
// Chronology is decided only by the server's shared Actual validator; this type
// never validates. Authority, lifecycle state and tokens always come from the fresh
// server read model, and entries are restored only onto the exact form they came from.
internal static class ActualsEditorRecovery
{
    public const string TempDataKey = "ActualsEditRecovery";

    /// <summary>
    /// A disabled input is not posted, so a field is restored only when it was present in
    /// the post; a read-only row is never restored as blank.
    /// </summary>
    public static string Serialize(ActualsEditInput input, IFormCollection? form, string userId, IEnumerable<string> errors)
    {
        ArgumentNullException.ThrowIfNull(input);

        bool Posted(int index, string field, object? value) =>
            form is null ? value is not null : form.ContainsKey($"Input.Rows[{index}].{field}");

        var rows = (input.Rows ?? new List<ActualsEditRowInput>())
            .Select((row, index) => row is null
                ? null
                : new RecoveryRow(
                    row.StageCode,
                    row.StageToken,
                    Posted(index, nameof(ActualsEditRowInput.ActualStart), row.ActualStart),
                    row.ActualStart,
                    Posted(index, nameof(ActualsEditRowInput.CompletedOn), row.CompletedOn),
                    row.CompletedOn))
            .Where(row => row is not null)
            .Select(row => row!)
            .ToArray();

        return JsonSerializer.Serialize(new RecoveryState(
            userId,
            input.ProjectId,
            input.SubmissionMode,
            input.BaselineVersion,
            input.BasedOnRevisionId,
            input.BasedOnSubmittedOn,
            input.Note,
            rows,
            errors.Where(error => !string.IsNullOrWhiteSpace(error)).ToArray()));
    }

    /// <param name="submissionMode">The mode the editor is rendered in now (<see cref="ActualsSubmissionMode"/>).</param>
    public static ActualsEditorVm Restore(
        ActualsEditorVm editor, string? payload, int projectId, string? userId, string? submissionMode)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (string.IsNullOrEmpty(payload) || string.IsNullOrEmpty(userId) || submissionMode is null)
        {
            return editor;
        }

        RecoveryState? state;
        try
        {
            state = JsonSerializer.Deserialize<RecoveryState>(payload);
        }
        catch (JsonException)
        {
            return editor;
        }

        // Another user's post, another project, or a form rendered for a different authority.
        if (state is null || state.ProjectId != projectId ||
            !string.Equals(state.UserId, userId, StringComparison.Ordinal) ||
            !string.Equals(state.SubmissionMode, submissionMode, StringComparison.Ordinal))
        {
            return editor;
        }

        var isProposal = string.Equals(submissionMode, ActualsSubmissionMode.Proposal, StringComparison.Ordinal);
        var restore = IsSameForm(editor, state, isProposal);
        return new ActualsEditorVm
        {
            ProjectId = editor.ProjectId,
            Today = editor.Today,
            BaselineVersion = editor.BaselineVersion,
            PendingProposal = editor.PendingProposal,
            LastRejection = editor.LastRejection,
            Rows = editor.Rows,
            SubmissionErrors = state.Errors ?? Array.Empty<string>(),
            RestoredSubmission = restore,
            EnteredValues = restore
                ? state.Rows
                    .GroupBy(row => row.StageCode, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group =>
                        {
                            var row = group.Last();
                            return new ActualsEnteredValues(row.StartPosted, row.ActualStart, row.CompletedPosted, row.CompletedOn);
                        },
                        StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, ActualsEnteredValues>(StringComparer.OrdinalIgnoreCase),
            EnteredNote = restore && isProposal ? state.Note : null
        };
    }

    // Entries are restored only if nothing they were entered against has changed: the
    // baseline, every posted row's stored token, and (for a proposal) the pending revision.
    // Otherwise restoring them could silently revert a change made in the meantime.
    private static bool IsSameForm(ActualsEditorVm editor, RecoveryState state, bool isProposal)
    {
        if (state.BaselineVersion != editor.BaselineVersion || state.Rows is not { Length: > 0 })
        {
            return false;
        }

        if (isProposal &&
            (state.BasedOnRevisionId != editor.PendingProposal?.RevisionId ||
             state.BasedOnSubmittedOn != editor.PendingProposal?.SubmittedOn))
        {
            return false;
        }

        var tokens = editor.Rows.ToDictionary(row => row.StageCode, row => row.StageToken, StringComparer.OrdinalIgnoreCase);
        return state.Rows.All(row =>
            tokens.TryGetValue(row.StageCode, out var token) &&
            string.Equals(token, row.StageToken, StringComparison.Ordinal));
    }

    private sealed record RecoveryRow(
        string StageCode,
        string? StageToken,
        bool StartPosted,
        DateOnly? ActualStart,
        bool CompletedPosted,
        DateOnly? CompletedOn);

    private sealed record RecoveryState(
        string UserId,
        int ProjectId,
        string? SubmissionMode,
        int? BaselineVersion,
        int? BasedOnRevisionId,
        DateTimeOffset? BasedOnSubmittedOn,
        string? Note,
        RecoveryRow[] Rows,
        string[]? Errors);
}

/// <summary>What the user entered for one stage in a refused post; a field not posted (disabled) is not restored.</summary>
public sealed record ActualsEnteredValues(bool HasStart, DateOnly? ActualStart, bool HasCompletedOn, DateOnly? CompletedOn);
