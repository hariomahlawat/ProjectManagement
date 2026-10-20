using System.Security.Claims;
using System.Text.Json;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests;

// PO Timeline Increment 2a/2b: which refused posts reopen the planner with the user's entries.
public sealed class PlanEditorRecoveryTests
{
    private const string User = "po-user";

    [Fact]
    public void RefusedPost_OnTheSameDraft_RestoresEntriesAndMessages()
    {
        var payload = PlanEditorRecovery.Serialize(Input("token-1"), User, new[] { "For Feasibility, Start date cannot be after Due date." });

        var restored = PlanEditorRecovery.Restore(Editor("token-1"), payload, 1, User, Principal("Project Officer"));

        Assert.True(restored.RestoredSubmission);
        Assert.Equal(new DateOnly(2026, 3, 10), restored.Exact.Rows.Single().PlannedStart);
        Assert.Equal("For Feasibility, Start date cannot be after Due date.", Assert.Single(restored.SubmissionErrors));
        Assert.Single(restored.ErrorsFor("Feasibility"));
    }

    [Fact]
    public void RefusedPost_AfterTheDraftChanged_ShowsMessagesButKeepsTheCurrentPlan()
    {
        var payload = PlanEditorRecovery.Serialize(Input("token-old"), User, new[] { "This timeline draft changed in another session. Reload before saving." });

        var restored = PlanEditorRecovery.Restore(Editor("token-new"), payload, 1, User, Principal("Project Officer"));

        Assert.False(restored.RestoredSubmission);
        Assert.Null(restored.Exact.Rows.Single().PlannedStart);
        Assert.Single(restored.SubmissionErrors);
    }

    [Theory]
    [InlineData("Project Officer", false)]
    [InlineData("HoD", true)]
    [InlineData("Admin", true)]
    public void ConflictRecovery_RestoresOverTheChangedPlanOnlyForDirectPublishers(string role, bool restores)
    {
        var payload = PlanEditorRecovery.Serialize(Input("token-old"), User, new[] { "The project plan changed while you were saving." }, conflict: true);

        var restored = PlanEditorRecovery.Restore(Editor("token-new"), payload, 1, User, Principal(role));

        Assert.Equal(restores, restored.RestoredSubmission);
        Assert.Equal(restores ? new DateOnly(2026, 3, 10) : null, restored.Exact.Rows.Single().PlannedStart);
    }

    [Fact]
    public void PayloadWithoutTheNewFields_IsTreatedAsTheEarlierHoDConflictRecovery()
    {
        // Written by the previous release during a rolling deployment.
        var legacy = JsonSerializer.Serialize(new { UserId = User, Input = Input("token-old") });
        var editor = Editor("token-new");

        Assert.Same(editor, PlanEditorRecovery.Restore(editor, legacy, 1, User, Principal("Project Officer")));
        Assert.True(PlanEditorRecovery.Restore(Editor("token-new"), legacy, 1, User, Principal("HoD")).RestoredSubmission);
    }

    [Theory]
    [InlineData("another-user", 1)]
    [InlineData(User, 2)]
    public void PayloadForAnotherUserOrProject_IsIgnored(string userId, int projectId)
    {
        var payload = PlanEditorRecovery.Serialize(Input("token-1"), User, new[] { "message" });
        var editor = Editor("token-1");

        Assert.Same(editor, PlanEditorRecovery.Restore(editor, payload, projectId, userId, Principal("Project Officer")));
    }

    [Fact]
    public void ErrorsFor_MatchesOnlyMessagesThatNameTheStage()
    {
        var editor = new PlanEditorVm
        {
            SubmissionErrors = new[]
            {
                "For PNC, Start date cannot be after Due date.",
                "Duration for Supply Order must be a positive number of days.",
                "Supply Order Planned start must be on or after EAS Approval's next working day (12 Oct 2026).",
                "Please provide the anchor start date."
            }
        };

        Assert.Single(editor.ErrorsFor("PNC"));
        Assert.Equal(2, editor.ErrorsFor("Supply Order").Count);
        Assert.Empty(editor.ErrorsFor("EAS Approval"));
        Assert.Empty(editor.ErrorsFor(string.Empty));
    }

    private static PlanEditInput Input(string token) => new()
    {
        ProjectId = 1,
        Mode = PlanEditorModes.Exact,
        Action = "SaveDraft",
        EditToken = token,
        Rows = new() { new() { Code = "FS", Name = "Feasibility", PlannedStart = new DateOnly(2026, 3, 10), PlannedDue = new DateOnly(2026, 3, 1) } }
    };

    private static PlanEditorVm Editor(string token) => new()
    {
        Exact = new PlanEditVm { ProjectId = 1, Rows = new() { new PlanEditVm.PlanEditRow { Code = "FS", Name = "Feasibility" } } },
        Durations = new PlanDurationVm(),
        ActiveMode = PlanEditorModes.Durations,
        State = new PlanEditorStateVm { EditToken = token }
    };

    private static ClaimsPrincipal Principal(string role) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, User),
        new Claim(ClaimTypes.Role, role)
    }, "Test"));
}
