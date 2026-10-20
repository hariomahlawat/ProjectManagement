using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using ProjectManagement.Services.Stages;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests.Stages;

// A refused Actual editor post is restored only onto the exact form it came from, so a
// restored entry can never silently revert a change made by someone else in the meantime.
public sealed class ActualsEditorRecoveryTests
{
    private const string User = "po-1";
    private static readonly DateOnly Jan10 = new(2026, 1, 10);
    private static readonly DateOnly Jan12 = new(2026, 1, 12);

    [Fact]
    public void FieldsAbsentFromThePost_AreNotRestored()
    {
        // A disabled input is not posted: IPA's completion was read-only, its start was blanked.
        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["Input.Rows[0].ActualStart"] = "2026-01-01",
            ["Input.Rows[0].CompletedOn"] = "2026-01-12",
            ["Input.Rows[1].ActualStart"] = ""
        });
        var payload = ActualsEditorRecovery.Serialize(
            Input(ActualsSubmissionMode.Direct, Row("FS", "t-fs", Jan10.AddDays(-9), Jan12), Row("IPA", "t-ipa", null, null)),
            form, User, new[] { "IPA: message" });

        var restored = ActualsEditorRecovery.Restore(Editor(), payload, 1, User, ActualsSubmissionMode.Direct);

        Assert.True(restored.RestoredSubmission);
        Assert.Equal(new ActualsEnteredValues(true, Jan10.AddDays(-9), true, Jan12), restored.EnteredValues["FS"]);
        Assert.Equal(new ActualsEnteredValues(true, null, false, null), restored.EnteredValues["IPA"]);
    }

    [Fact]
    public void AChangedStageToken_ShowsTheMessagesButNotTheEntries()
    {
        var payload = ActualsEditorRecovery.Serialize(
            Input(ActualsSubmissionMode.Direct, Row("FS", "stale", null, Jan12)), null, User, new[] { "IPA: message" });

        var restored = ActualsEditorRecovery.Restore(Editor(), payload, 1, User, ActualsSubmissionMode.Direct);

        Assert.False(restored.RestoredSubmission);
        Assert.Empty(restored.EnteredValues);
        Assert.Equal(new[] { "IPA: message" }, restored.SubmissionErrors);
    }

    [Fact]
    public void AProposalBasedOnAnotherPendingRevision_IsNotRestored()
    {
        var input = Input(ActualsSubmissionMode.Proposal, Row("FS", "t-fs", null, Jan12));
        input.BasedOnRevisionId = 7;
        input.BasedOnSubmittedOn = new DateTimeOffset(2026, 1, 15, 8, 0, 0, TimeSpan.Zero);
        var payload = ActualsEditorRecovery.Serialize(input, null, User, new[] { "IPA: message" });

        var none = ActualsEditorRecovery.Restore(Editor(), payload, 1, User, ActualsSubmissionMode.Proposal);
        Assert.False(none.RestoredSubmission);

        var samePending = Editor(new ActualProposalSummaryVm(7, 2, input.BasedOnSubmittedOn.Value, "note", true, new[] { "FS" }));
        Assert.True(ActualsEditorRecovery.Restore(samePending, payload, 1, User, ActualsSubmissionMode.Proposal).RestoredSubmission);
    }

    [Theory]
    [InlineData("other-user", 1, ActualsSubmissionMode.Direct)]
    [InlineData(User, 2, ActualsSubmissionMode.Direct)]
    [InlineData(User, 1, ActualsSubmissionMode.Proposal)]
    [InlineData(User, 1, null)]
    public void AnotherUserProjectOrAuthority_GetsTheEditorUnchanged(string userId, int projectId, string? mode)
    {
        var editor = Editor();
        var payload = ActualsEditorRecovery.Serialize(
            Input(ActualsSubmissionMode.Direct, Row("FS", "t-fs", null, Jan12)), null, User, new[] { "IPA: message" });

        Assert.Same(editor, ActualsEditorRecovery.Restore(editor, payload, projectId, userId, mode));
        Assert.Same(editor, ActualsEditorRecovery.Restore(editor, "{not json", 1, User, ActualsSubmissionMode.Direct));
        Assert.Same(editor, ActualsEditorRecovery.Restore(editor, null, 1, User, ActualsSubmissionMode.Direct));
    }

    private static ActualsEditInput Input(string mode, params ActualsEditRowInput[] rows) => new()
    {
        ProjectId = 1,
        BaselineVersion = ActualStageToken.BaselineVersion,
        SubmissionMode = mode,
        Rows = new List<ActualsEditRowInput>(rows)
    };

    private static ActualsEditRowInput Row(string code, string token, DateOnly? start, DateOnly? completed) =>
        new() { StageCode = code, StageToken = token, ActualStart = start, CompletedOn = completed };

    private static ActualsEditorVm Editor(ActualProposalSummaryVm? pending = null) => new()
    {
        ProjectId = 1,
        BaselineVersion = ActualStageToken.BaselineVersion,
        PendingProposal = pending,
        Rows = new[]
        {
            new ActualsEditorRowVm { StageCode = "FS", StageToken = "t-fs" },
            new ActualsEditorRowVm { StageCode = "IPA", StageToken = "t-ipa" }
        }
    };
}
