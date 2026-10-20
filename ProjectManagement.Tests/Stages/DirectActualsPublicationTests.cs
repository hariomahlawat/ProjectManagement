using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Pages.Projects.Stages;
using ProjectManagement.Pages.Projects.Timeline;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests.Stages;

/// <summary>
/// Phase D-1b: the authoritative direct Actual-correction and completion-backfill
/// entry points publish only states accepted by the shared final-state validator,
/// atomically with their stage history. Relational concurrency is covered on
/// PostgreSQL by <see cref="StageActualsConcurrencyPostgresFixture"/>.
/// </summary>
public sealed class DirectActualsPublicationTests
{
    private const int ProjectId = 1;
    private static readonly DateOnly Today = new(2026, 3, 31);

    // SECTION: Authorization through the real page handlers
    [Theory]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task DirectCorrection_ByHodOrAdmin_PublishesExactlyTheValidatedDatesWithHistory(string role)
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db,
            Completed(StageCodes.FS, Jan(1), Jan(10)),
            InProgress(StageCodes.IPA, Jan(12))));
        await using var db = fixture.NewContext();

        var page = EditActualsPage(db, fixture.Actuals(db), role, "actor");
        page.Input = await EditorInputAsync(db, Row(StageCodes.IPA, actualStart: Jan(11)));

        var result = await page.OnPostAsync(ProjectId, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Actual dates updated successfully.", page.TempData["Flash"]);
        await using var verify = fixture.NewContext();
        var ipa = await verify.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA);
        Assert.Equal(Jan(11), ipa.ActualStart);
        Assert.Equal(StageStatus.InProgress, ipa.Status);
        var log = await verify.StageChangeLogs.SingleAsync();
        Assert.Equal("ActualsUpdated", log.Action);
        Assert.Equal(Jan(12), log.FromActualStart);
        Assert.Equal(Jan(11), log.ToActualStart);
        Assert.Equal("actor", log.UserId);
    }

    [Fact]
    public async Task DirectCorrection_ByAProjectOfficerOfAnotherProject_IsForbiddenWithoutWrites()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db, InProgress(StageCodes.FS, Jan(5))));
        await using var db = fixture.NewContext();

        var page = EditActualsPage(db, fixture.Actuals(db), "Project Officer", "someone-else");
        page.Input = await EditorInputAsync(db, Row(StageCodes.FS, actualStart: Jan(6)));

        Assert.IsType<ForbidResult>(await page.OnPostAsync(ProjectId, CancellationToken.None));
        await AssertUnchangedAsync(fixture, StageCodes.FS, Jan(5), null);
    }

    [Fact]
    public async Task DirectCorrection_OnASoftDeletedProject_IsNotFoundWithoutWrites()
    {
        await using var fixture = await Fixture.CreateAsync(
            db => Stages(db, InProgress(StageCodes.FS, Jan(5))),
            project => project.IsDeleted = true);
        await using var db = fixture.NewContext();

        var page = EditActualsPage(db, fixture.Actuals(db), "HoD", "actor");
        page.Input = await EditorInputAsync(db, Row(StageCodes.FS, actualStart: Jan(6)));

        Assert.IsType<NotFoundResult>(await page.OnPostAsync(ProjectId, CancellationToken.None));
        await AssertUnchangedAsync(fixture, StageCodes.FS, Jan(5), null);
    }

    // SECTION: Service-level authority (re-established inside the transaction)
    [Fact]
    public async Task AssignedProjectOfficerOnly_IsToldApprovalIsRequired_ByBothServices_WithoutWrites()
    {
        // Phase D-2: the assigned PO-only user no longer publishes directly, even when a
        // caller bypasses the page check.
        var audit = new RecordingAudit();
        await using var fixture = await Fixture.CreateAsync(db => Stages(db,
            InProgress(StageCodes.FS, Jan(5)),
            AwaitingBackfill(StageCodes.BID, Feb(1))));
        await using var db = fixture.NewContext();
        var input = await EditorInputAsync(db, Row(StageCodes.FS, actualStart: Jan(6)));

        await Assert.ThrowsAsync<ActualsApprovalRequiredException>(() =>
            fixture.Actuals(db, audit).UpdateAsync(input, TestActors.ProjectOfficer("assigned-po"), "PO"));
        await Assert.ThrowsAsync<ActualsApprovalRequiredException>(() =>
            fixture.Backfill(db, audit).ApplyAsync(
                ProjectId, new[] { new StageBackfillUpdate(StageCodes.BID, null, Feb(5)) }, TestActors.ProjectOfficer("assigned-po")));

        await AssertUnchangedAsync(fixture, StageCodes.FS, Jan(5), null);
        await using (var verify = fixture.NewContext())
        {
            var bid = await verify.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.BID);
            Assert.Null(bid.CompletedOn);
            Assert.True(bid.RequiresBackfill);
        }

        await AssertNoHistoryAsync(fixture);
        Assert.Empty(audit.Entries);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HodOrAdminWhoIsAlsoTheAssignedProjectOfficer_PublishesDirectly(bool hod)
    {
        // Role precedence: HoD/Admin authority supersedes the PO role.
        await using var fixture = await Fixture.CreateAsync(db => Stages(db,
            InProgress(StageCodes.FS, Jan(5)),
            AwaitingBackfill(StageCodes.BID, Feb(1))));
        await using var db = fixture.NewContext();
        var actor = hod ? TestActors.HodAndProjectOfficer("assigned-po") : TestActors.AdminAndProjectOfficer("assigned-po");

        var corrected = await fixture.Actuals(db).UpdateAsync(
            await EditorInputAsync(db, Row(StageCodes.FS, actualStart: Jan(6))), actor, "HoD/Admin + PO");
        var backfilled = await fixture.Backfill(db).ApplyAsync(
            ProjectId, new[] { new StageBackfillUpdate(StageCodes.BID, null, Feb(5)) }, actor);

        Assert.Equal(1, corrected.UpdatedCount);
        Assert.Equal(1, backfilled.UpdatedCount);
        await using var verify = fixture.NewContext();
        Assert.Equal(Jan(6), (await verify.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.FS)).ActualStart);
        Assert.Equal(Feb(5), (await verify.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.BID)).CompletedOn);
        Assert.Equal(new[] { "ActualsUpdated", "Backfill" },
            await verify.StageChangeLogs.OrderBy(log => log.Id).Select(log => log.Action).ToArrayAsync());
    }

    [Fact]
    public async Task UnassignedProjectOfficer_IsRefusedByTheService_WithoutHistoryOrAudit()
    {
        var audit = new RecordingAudit();
        await using var fixture = await Fixture.CreateAsync(db => Stages(db, InProgress(StageCodes.FS, Jan(5))));
        await using var db = fixture.NewContext();
        var input = await EditorInputAsync(db, Row(StageCodes.FS, actualStart: Jan(6)));

        await Assert.ThrowsAsync<ActualsWriteForbiddenException>(() =>
            fixture.Actuals(db, audit).UpdateAsync(input, TestActors.ProjectOfficer("someone-else"), "PO"));

        await AssertUnchangedAsync(fixture, StageCodes.FS, Jan(5), null);
        await AssertNoHistoryAsync(fixture);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task ProjectOfficerReassignedAwayBeforePublication_IsForbidden_AndTheNewLeadIsToldApprovalIsRequired()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db, InProgress(StageCodes.FS, Jan(5))));
        await using var db = fixture.NewContext();
        var input = await EditorInputAsync(db, Row(StageCodes.FS, actualStart: Jan(6)));
        await ReassignLeadAsync(fixture, "someone-else"); // after the page-level check, before publication

        await Assert.ThrowsAsync<ActualsWriteForbiddenException>(() =>
            fixture.Actuals(db).UpdateAsync(input, TestActors.ProjectOfficer("assigned-po"), "PO"));
        await AssertUnchangedAsync(fixture, StageCodes.FS, Jan(5), null);

        await using var newLead = fixture.NewContext();
        await Assert.ThrowsAsync<ActualsApprovalRequiredException>(async () => await fixture.Actuals(newLead).UpdateAsync(
            await EditorInputAsync(newLead, Row(StageCodes.FS, actualStart: Jan(6))),
            TestActors.ProjectOfficer("someone-else"),
            "New PO"));
        await AssertUnchangedAsync(fixture, StageCodes.FS, Jan(5), null);
        await AssertNoHistoryAsync(fixture);
    }

    // SECTION: Backfill page role matrix (Phase D-2)
    [Theory]
    [InlineData("Project Officer", "assigned-po", 409, "reload-required")]
    [InlineData("Project Officer", "someone-else", 0, "forbid")]
    [InlineData("HoD", "actor", 200, "applied")]
    [InlineData("Admin", "actor", 200, "applied")]
    [InlineData("HoD,Project Officer", "assigned-po", 200, "applied")]
    [InlineData("Admin,Project Officer", "assigned-po", 200, "applied")]
    public async Task BackfillPage_FollowsRolePrecedence(string roles, string userId, int status, string outcome)
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db, AwaitingBackfill(StageCodes.BID, Feb(1))));
        await using var db = fixture.NewContext();
        var page = new BackfillApplyModel(fixture.Backfill(db), db, NullLogger<BackfillApplyModel>.Instance)
        {
            PageContext = PageContextFor(roles, userId)
        };

        var result = await page.OnPostAsync(new BackfillApplyModel.BackfillApplyInput
        {
            ProjectId = ProjectId,
            Stages = new List<BackfillApplyModel.BackfillStageInput> { new() { StageCode = StageCodes.BID, CompletedOn = Feb(5) } }
        }, CancellationToken.None);

        await using var verify = fixture.NewContext();
        var bid = await verify.ProjectStages.SingleAsync();
        if (outcome == "applied")
        {
            Assert.IsType<JsonResult>(result);
            Assert.Equal(Feb(5), bid.CompletedOn);
            Assert.False(bid.RequiresBackfill);
            Assert.Equal("Backfill", (await verify.StageChangeLogs.SingleAsync()).Action);
            return;
        }

        if (outcome == "forbid")
        {
            Assert.IsType<ForbidResult>(result);
        }
        else
        {
            var refusal = Assert.IsType<ObjectResult>(result);
            Assert.Equal(status, refusal.StatusCode);
            var body = JsonSerializer.SerializeToElement(refusal.Value);
            Assert.False(body.GetProperty("ok").GetBoolean());
            // A direct-mode form posted by the assigned PO-only user is refused, never published (D-3c).
            Assert.Equal("reload-required", body.GetProperty("error").GetString());
            Assert.Equal(EditActualsModel.AccessChangedMessage, body.GetProperty("message").GetString());
        }

        Assert.Null(bid.CompletedOn);
        Assert.True(bid.RequiresBackfill);
        await AssertNoHistoryAsync(fixture);
    }

    [Fact]
    public async Task CrossProjectSubmission_IsRefused()
    {
        await using var fixture = await Fixture.CreateAsync(db =>
        {
            Stages(db, InProgress(StageCodes.FS, Jan(5)));
            db.Projects.Add(new Project
            {
                Id = 2,
                Name = "Someone else's project",
                CreatedByUserId = "seed",
                LeadPoUserId = "someone-else",
                WorkflowVersion = ProcurementWorkflow.VersionV1
            });
            db.ProjectStages.Add(new ProjectStage { ProjectId = 2, StageCode = StageCodes.FS, Status = StageStatus.InProgress, ActualStart = Jan(5) });
        });
        await using var db = fixture.NewContext();

        var actuals = new ActualsEditInput
        {
            ProjectId = 2,
            Rows = new List<ActualsEditRowInput> { new() { StageCode = StageCodes.FS, ActualStart = Jan(6) } }
        };
        await Assert.ThrowsAsync<ActualsWriteForbiddenException>(() =>
            fixture.Actuals(db).UpdateAsync(actuals, TestActors.ProjectOfficer("assigned-po"), "PO"));
        await Assert.ThrowsAsync<ActualsWriteForbiddenException>(() =>
            fixture.Backfill(db).ApplyAsync(2, new[] { new StageBackfillUpdate(StageCodes.FS, null, Jan(6)) }, TestActors.ProjectOfficer("assigned-po")));

        await using var verify = fixture.NewContext();
        Assert.Equal(Jan(5), (await verify.ProjectStages.SingleAsync(s => s.ProjectId == 2)).ActualStart);
        await AssertNoHistoryAsync(fixture);
    }

    // SECTION: Deleted and missing projects
    [Theory]
    [InlineData("HoD")]
    [InlineData("Admin")]
    [InlineData("Project Officer")]
    public async Task Backfill_OnADeletedProject_IsNotFoundForEveryActor_WithoutWrites(string role)
    {
        await using var fixture = await Fixture.CreateAsync(
            db => Stages(db, AwaitingBackfill(StageCodes.BID, Feb(1))),
            project => project.IsDeleted = true);
        await using var db = fixture.NewContext();
        var userId = role == "Project Officer" ? "assigned-po" : "actor";

        var page = new BackfillApplyModel(fixture.Backfill(db), db, NullLogger<BackfillApplyModel>.Instance)
        {
            PageContext = PageContextFor(role, userId)
        };
        var result = await page.OnPostAsync(new BackfillApplyModel.BackfillApplyInput
        {
            ProjectId = ProjectId,
            Stages = new List<BackfillApplyModel.BackfillStageInput> { new() { StageCode = StageCodes.BID, CompletedOn = Feb(5) } }
        }, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        await using var verify = fixture.NewContext();
        var stage = await verify.ProjectStages.SingleAsync();
        Assert.Null(stage.CompletedOn);
        Assert.True(stage.RequiresBackfill);
        await AssertNoHistoryAsync(fixture);
    }

    [Fact]
    public async Task DirectCorrection_OnADeletedProject_IsRefusedByTheService()
    {
        await using var fixture = await Fixture.CreateAsync(
            db => Stages(db, InProgress(StageCodes.FS, Jan(5))),
            project => project.IsDeleted = true);
        await using var db = fixture.NewContext();
        var input = await EditorInputAsync(db, Row(StageCodes.FS, actualStart: Jan(6)));

        await Assert.ThrowsAsync<ActualsProjectUnavailableException>(() =>
            fixture.Actuals(db).UpdateAsync(input, TestActors.Admin("actor"), "Admin"));

        await AssertUnchangedAsync(fixture, StageCodes.FS, Jan(5), null);
        await AssertNoHistoryAsync(fixture);
    }

    [Fact]
    public async Task NonexistentProject_IsRefusedOnBothPaths()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db, InProgress(StageCodes.FS, Jan(5))));
        await using var db = fixture.NewContext();

        await Assert.ThrowsAsync<ActualsProjectUnavailableException>(() => fixture.Actuals(db).UpdateAsync(
            new ActualsEditInput
            {
                ProjectId = 999,
                Rows = new List<ActualsEditRowInput> { new() { StageCode = StageCodes.FS, ActualStart = Jan(6) } }
            },
            TestActors.Hod("actor"),
            "HoD"));
        await Assert.ThrowsAsync<ActualsProjectUnavailableException>(() =>
            fixture.Backfill(db).ApplyAsync(999, new[] { new StageBackfillUpdate(StageCodes.FS, null, Jan(6)) }, TestActors.Hod("actor")));

        await AssertUnchangedAsync(fixture, StageCodes.FS, Jan(5), null);
        await AssertNoHistoryAsync(fixture);
    }

    // SECTION: Final-state validation at the authoritative boundary
    [Fact]
    public async Task PredecessorCorrection_InvalidatingAnUntouchedSuccessor_IsRejectedWithoutWrites()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db,
            Completed(StageCodes.FS, Jan(1), Jan(10)),
            InProgress(StageCodes.IPA, Jan(11))));
        await using var db = fixture.NewContext();

        var exception = await Assert.ThrowsAsync<StageActualsValidationException>(() =>
            UpdateAsync(fixture, db, Row(StageCodes.FS, completedOn: Jan(20))));

        Assert.Contains(exception.Errors, error => error.StartsWith("IPA:", StringComparison.Ordinal)
            && error.Contains("20 Jan 2026", StringComparison.Ordinal));
        await AssertUnchangedAsync(fixture, StageCodes.FS, Jan(1), Jan(10));
    }

    // C7.2: the editor leaves chronology to this boundary; a refusal reopens it with the
    // user's entries and the affected successor named, and the corrected post publishes.
    [Fact]
    public async Task RefusedCorrection_ReopensTheEditorWithTheEntries_AndTheCorrectedResubmissionPublishes()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db,
            Completed(StageCodes.FS, Jan(1), Jan(10)),
            InProgress(StageCodes.IPA, Jan(11))));
        await using var db = fixture.NewContext();

        var page = EditActualsPage(db, fixture.Actuals(db), "HoD", "actor");
        page.Input = await EditorInputAsync(db, Row(StageCodes.FS, completedOn: Jan(12)), Row(StageCodes.IPA, actualStart: Jan(11)));
        page.Input.SubmissionMode = ActualsSubmissionMode.Direct;
        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(ProjectId, CancellationToken.None));
        Assert.StartsWith("IPA:", Assert.IsType<string>(page.TempData["Error"]), StringComparison.Ordinal);
        await AssertUnchangedAsync(fixture, StageCodes.FS, Jan(1), Jan(10));

        var payload = Assert.IsType<string>(page.TempData[ActualsEditorRecovery.TempDataKey]);
        var editor = await ReadService(db).GetActualsEditorAsync(ProjectId);
        var reopened = ActualsEditorRecovery.Restore(editor, payload, ProjectId, "actor", ActualsSubmissionMode.Direct);
        Assert.True(reopened.RestoredSubmission);
        var error = Assert.Single(reopened.SubmissionErrors);
        Assert.Contains("Correct IPA in the same update", error, StringComparison.Ordinal);
        Assert.Equal(Jan(12), reopened.EnteredValues[StageCodes.FS].CompletedOn);
        Assert.Equal(Jan(11), reopened.EnteredValues[StageCodes.IPA].ActualStart);
        Assert.Same(editor, ActualsEditorRecovery.Restore(editor, payload, ProjectId, "someone-else", ActualsSubmissionMode.Direct));

        var resubmit = EditActualsPage(db, fixture.Actuals(db), "HoD", "actor");
        resubmit.Input = await EditorInputAsync(db, Row(StageCodes.FS, completedOn: Jan(12)), Row(StageCodes.IPA, actualStart: Jan(12)));
        resubmit.Input.SubmissionMode = ActualsSubmissionMode.Direct;
        Assert.IsType<RedirectToPageResult>(await resubmit.OnPostAsync(ProjectId, CancellationToken.None));
        Assert.Equal("Actual dates updated successfully.", resubmit.TempData["Flash"]);
        Assert.Null(resubmit.TempData[ActualsEditorRecovery.TempDataKey]);
        await AssertUnchangedAsync(fixture, StageCodes.IPA, Jan(12), null);
    }

    [Fact]
    public async Task CoordinatedCorrection_OfPredecessorAndSuccessor_IsPublishedTogether()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db,
            Completed(StageCodes.FS, Jan(1), Jan(10)),
            InProgress(StageCodes.IPA, Jan(11))));
        await using var db = fixture.NewContext();

        var result = await UpdateAsync(fixture, db,
            Row(StageCodes.FS, completedOn: Jan(20)),
            Row(StageCodes.IPA, actualStart: Jan(20)));

        Assert.Equal(2, result.UpdatedCount);
        await using var verify = fixture.NewContext();
        Assert.Equal(Jan(20), (await verify.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.FS)).CompletedOn);
        Assert.Equal(Jan(20), (await verify.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA)).ActualStart);
        Assert.Equal(2, await verify.StageChangeLogs.CountAsync());
    }

    [Fact]
    public async Task UnrelatedHistoricalViolation_DoesNotBlock_AndIsShownAsANote()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db,
            Completed(StageCodes.FS, Jan(1), Jan(10)),
            InProgress(StageCodes.IPA, Jan(5))));
        await using var db = fixture.NewContext();

        var page = EditActualsPage(db, fixture.Actuals(db), "HoD", "actor");
        page.Input = await EditorInputAsync(db, Row(StageCodes.FS, actualStart: Jan(2)));

        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(ProjectId, CancellationToken.None));
        var flash = Assert.IsType<string>(page.TempData["Flash"]);
        Assert.Contains("Note:", flash, StringComparison.Ordinal);
        Assert.Contains("IPA", flash, StringComparison.Ordinal);
        await using var verify = fixture.NewContext();
        Assert.Equal(Jan(2), (await verify.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.FS)).ActualStart);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task RetainedPncRow_OnlyConstrainsCobWhilePncIsApplicable(bool pncApplicable, bool accepted)
    {
        await using var fixture = await Fixture.CreateAsync(
            db => Stages(db,
                Completed(StageCodes.COB, Feb(1), Feb(10)),
                Completed(StageCodes.PNC, Feb(11), Feb(25)),
                InProgress(StageCodes.EAS, Feb(26))),
            project => project.PncApplicable = pncApplicable);
        await using var db = fixture.NewContext();

        var update = UpdateAsync(fixture, db, Row(StageCodes.COB, completedOn: Feb(15)));

        if (accepted)
        {
            Assert.Equal(1, (await update).UpdatedCount);
        }
        else
        {
            await Assert.ThrowsAsync<StageActualsValidationException>(() => update);
            await AssertUnchangedAsync(fixture, StageCodes.COB, Feb(1), Feb(10));
        }
    }

    [Fact]
    public async Task ConfiguredExtensionStageDependency_IsEnforced()
    {
        await using var fixture = await Fixture.CreateAsync(db =>
        {
            db.StageDependencyTemplates.Add(new StageDependencyTemplate
            {
                Version = ProcurementWorkflow.VersionV1,
                FromStageCode = "EXT1",
                DependsOnStageCode = StageCodes.FS
            });
            Stages(db, Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress("EXT1", Jan(12)));
        });
        await using var db = fixture.NewContext();

        var exception = await Assert.ThrowsAsync<StageActualsValidationException>(() =>
            UpdateAsync(fixture, db, Row(StageCodes.FS, completedOn: Jan(15))));

        Assert.Contains(exception.Errors, error => error.StartsWith("EXT1:", StringComparison.Ordinal));
        await AssertUnchangedAsync(fixture, StageCodes.FS, Jan(1), Jan(10));
    }

    [Fact]
    public async Task PublishedArppIpaCompletion_CannotBeOverridden()
    {
        await using var fixture = await Fixture.CreateAsync(
            db => Stages(db, Completed(StageCodes.FS, Jan(1), Jan(10)), Completed(StageCodes.IPA, Jan(11), Jan(25))),
            authority: Authority(Jan(25)));
        await using var db = fixture.NewContext();

        var exception = await Assert.ThrowsAsync<StageActualsValidationException>(() =>
            UpdateAsync(fixture, db, Row(StageCodes.IPA, completedOn: Jan(20))));

        Assert.Contains(exception.Errors, error => error.Contains("published ARPP", StringComparison.Ordinal));
        await AssertUnchangedAsync(fixture, StageCodes.IPA, Jan(11), Jan(25));

        var backfill = await Assert.ThrowsAsync<StageBackfillValidationException>(() =>
            fixture.Backfill(db).ApplyAsync(ProjectId, new[] { new StageBackfillUpdate(StageCodes.IPA, null, Jan(20)) }, TestActors.Hod("actor")));
        Assert.Contains(backfill.Details, detail => detail.Contains("ARPP", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StatusDateMatrixAndIstFutureLimits_AreEnforced()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db,
            InProgress(StageCodes.FS, Mar(1)),
            Completed(StageCodes.BID, Mar(1), Mar(5))));
        await using var db = fixture.NewContext();

        var completion = await Assert.ThrowsAsync<StageActualsValidationException>(() =>
            UpdateAsync(fixture, db, Row(StageCodes.FS, completedOn: Mar(10))));
        Assert.Contains(completion.Errors, error => error.Contains("stage-transition", StringComparison.Ordinal));

        var future = await Assert.ThrowsAsync<StageActualsValidationException>(() =>
            UpdateAsync(fixture, db, Row(StageCodes.BID, completedOn: Today.AddDays(1))));
        Assert.Contains(future.Errors, error => error.Contains("cannot be in the future", StringComparison.Ordinal));

        await AssertUnchangedAsync(fixture, StageCodes.FS, Mar(1), null);
        await AssertUnchangedAsync(fixture, StageCodes.BID, Mar(1), Mar(5));
    }

    [Fact]
    public async Task MixedValidAndInvalidRows_WriteNothing()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db,
            Completed(StageCodes.BID, Feb(1), Feb(5)),
            InProgress(StageCodes.FS, Jan(5))));
        await using var db = fixture.NewContext();

        await Assert.ThrowsAsync<StageActualsValidationException>(() => UpdateAsync(fixture, db,
            Row(StageCodes.BID, actualStart: Feb(2)),
            Row(StageCodes.FS, clearStart: true)));

        await AssertUnchangedAsync(fixture, StageCodes.BID, Feb(1), Feb(5));
        await AssertUnchangedAsync(fixture, StageCodes.FS, Jan(5), null);
    }

    // SECTION: RequiresBackfill and history classification
    [Fact]
    public async Task DateCorrection_OnADatedStageFlaggedForMissingFacts_KeepsTheFlag()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db,
            new ProjectStage { ProjectId = ProjectId, StageCode = StageCodes.BID, Status = StageStatus.Completed, ActualStart = Feb(1), CompletedOn = Feb(5), RequiresBackfill = true }));
        await using var db = fixture.NewContext();

        await UpdateAsync(fixture, db, Row(StageCodes.BID, actualStart: Feb(2)));

        await using var verify = fixture.NewContext();
        Assert.True((await verify.ProjectStages.SingleAsync()).RequiresBackfill);
    }

    [Fact]
    public async Task EditorCompletionOfAStageAwaitingBackfill_ClearsTheFlagAndIsLoggedAsBackfill()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db, AwaitingBackfill(StageCodes.BID, Feb(1))));
        await using var db = fixture.NewContext();

        await UpdateAsync(fixture, db, Row(StageCodes.BID, completedOn: Feb(5)));

        await using var verify = fixture.NewContext();
        var stage = await verify.ProjectStages.SingleAsync();
        Assert.False(stage.RequiresBackfill);
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Equal("Backfill", (await verify.StageChangeLogs.SingleAsync()).Action);
    }

    [Fact]
    public async Task ArppReconciliationOfAStaleUndatedIpaRow_IsNotLoggedAsBackfill()
    {
        await using var fixture = await Fixture.CreateAsync(
            db => Stages(db, Completed(StageCodes.FS, Jan(1), Jan(4)), AwaitingBackfill(StageCodes.IPA, Jan(5))),
            authority: Authority(Jan(25)));
        await using var db = fixture.NewContext();

        await UpdateAsync(fixture, db, Row(StageCodes.IPA, actualStart: Jan(6)));

        await using var verify = fixture.NewContext();
        var ipa = await verify.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(Jan(25), ipa.CompletedOn);
        Assert.False(ipa.RequiresBackfill);
        Assert.Equal("ActualsUpdated", (await verify.StageChangeLogs.SingleAsync()).Action);
    }

    // SECTION: Completion backfill through the shared validator
    [Fact]
    public async Task Backfill_PublishesWithHistory_AndAuditsOnlyAfterCommit()
    {
        var audit = new RecordingAudit();
        await using var fixture = await Fixture.CreateAsync(db => Stages(db, AwaitingBackfill(StageCodes.BID, Feb(1))));
        await using var db = fixture.NewContext();

        var result = await fixture.Backfill(db, audit).ApplyAsync(
            ProjectId,
            new[] { new StageBackfillUpdate(StageCodes.BID, null, Feb(5), Token(db, StageCodes.BID)) },
            TestActors.Hod("actor"),
            baselineVersion: ActualStageToken.BaselineVersion);

        Assert.Equal(1, result.UpdatedCount);
        await using var verify = fixture.NewContext();
        var stage = await verify.ProjectStages.SingleAsync();
        Assert.Equal(Feb(5), stage.CompletedOn);
        Assert.False(stage.RequiresBackfill);
        Assert.Equal("Backfill", (await verify.StageChangeLogs.SingleAsync()).Action);
        Assert.Equal("Projects.StageBackfilled", Assert.Single(audit.Entries).Action);
    }

    [Fact]
    public async Task Backfill_InvalidatingAnUntouchedSuccessor_IsRejectedWithoutWrites()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db,
            AwaitingBackfill(StageCodes.FS, Jan(1)),
            InProgress(StageCodes.IPA, Jan(11))));
        await using var db = fixture.NewContext();

        var exception = await Assert.ThrowsAsync<StageBackfillValidationException>(() =>
            fixture.Backfill(db).ApplyAsync(ProjectId, new[] { new StageBackfillUpdate(StageCodes.FS, null, Jan(20)) }, TestActors.Hod("actor")));

        Assert.Contains(exception.Details, detail => detail.StartsWith("IPA:", StringComparison.Ordinal));
        await using var verify = fixture.NewContext();
        Assert.Null((await verify.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.FS)).CompletedOn);
        Assert.Empty(await verify.StageChangeLogs.ToListAsync());
    }

    // SECTION: Stale editors
    [Fact]
    public async Task StaleEditorToken_IsRejectedWithoutWrites_ThroughThePage()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db,
            Completed(StageCodes.FS, Jan(1), Jan(10)),
            InProgress(StageCodes.IPA, Jan(12))));
        await using var db = fixture.NewContext();
        var input = await EditorInputAsync(db, Row(StageCodes.IPA, actualStart: Jan(13)));
        await ChangeStoredStartAsync(fixture, StageCodes.IPA, Jan(14)); // another actor commits first

        var page = EditActualsPage(db, fixture.Actuals(db), "HoD", "actor");
        page.Input = input;
        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(ProjectId, CancellationToken.None));

        Assert.Contains("changed since you opened", Assert.IsType<string>(page.TempData["Error"]), StringComparison.Ordinal);
        await AssertUnchangedAsync(fixture, StageCodes.IPA, Jan(14), null);
        await using var verify = fixture.NewContext();
        Assert.Empty(await verify.StageChangeLogs.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentChangeToAnUnrelatedStage_DoesNotMakeTheSaveStale()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db,
            Completed(StageCodes.FS, Jan(1), Jan(10)),
            Completed(StageCodes.BID, Feb(1), Feb(5))));
        await using var db = fixture.NewContext();
        var input = await EditorInputAsync(db, Row(StageCodes.FS, actualStart: Jan(2)));
        await ChangeStoredStartAsync(fixture, StageCodes.BID, Feb(2));

        var result = await fixture.Actuals(db).UpdateAsync(input, TestActors.Hod("actor"), "Actor");

        Assert.Equal(1, result.UpdatedCount);
    }

    [Fact]
    public async Task LegacyPostWithoutBaselineMarker_KeepsThePreviousContract()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db, InProgress(StageCodes.FS, Jan(5))));
        await using var db = fixture.NewContext();

        var result = await fixture.Actuals(db).UpdateAsync(
            new ActualsEditInput
            {
                ProjectId = ProjectId,
                Rows = new List<ActualsEditRowInput> { new() { StageCode = StageCodes.FS, ActualStart = Jan(6) } }
            },
            TestActors.Hod("actor"),
            "Actor");

        Assert.Equal(1, result.UpdatedCount);
    }

    [Fact]
    public async Task StaleBackfillToken_ReturnsConflictWithoutWrites_ThroughThePage()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db, AwaitingBackfill(StageCodes.BID, Feb(1))));
        await using var db = fixture.NewContext();
        var token = Token(db, StageCodes.BID);
        await ChangeStoredStartAsync(fixture, StageCodes.BID, Feb(2));

        var page = new BackfillApplyModel(fixture.Backfill(db), db, NullLogger<BackfillApplyModel>.Instance)
        {
            PageContext = PageContextFor("HoD", "actor")
        };

        var result = await page.OnPostAsync(new BackfillApplyModel.BackfillApplyInput
        {
            ProjectId = ProjectId,
            BaselineVersion = ActualStageToken.BaselineVersion,
            Stages = new List<BackfillApplyModel.BackfillStageInput>
            {
                new() { StageCode = StageCodes.BID, CompletedOn = Feb(5), StageToken = token }
            }
        }, CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Contains("\"error\":\"stale\"", JsonSerializer.Serialize(conflict.Value), StringComparison.Ordinal);
        await using var verify = fixture.NewContext();
        Assert.Null((await verify.ProjectStages.SingleAsync()).CompletedOn);
    }

    [Fact]
    public void BackfillForm_PostsTheValuesOfTheSameStoredRowAsItsToken()
    {
        // The timeline read is older than the stored-row read: a concurrent
        // correction moved the start from 1 Feb to 2 Feb in between.
        var timeline = new[]
        {
            new TimelineItemVm { Code = StageCodes.BID, Name = "Bidding", RequiresBackfill = true, ActualStart = Feb(1) }
        };
        var stored = AwaitingBackfill(StageCodes.BID, Feb(2));

        var form = ProjectManagement.Features.Backfill.BackfillViewModel.Create(ProjectId, Today, timeline, new[] { stored });

        var row = Assert.Single(form.Stages);
        Assert.Equal(Feb(2), row.ActualStart);
        Assert.Equal(ActualStageToken.Compute(stored), row.StageToken);
        Assert.Equal(ActualStageToken.BaselineVersion, form.BaselineVersion);
    }

    [Fact]
    public void BackfillForm_KeepsStaleProtection_WhenARowHasNoStoredStage()
    {
        var form = ProjectManagement.Features.Backfill.BackfillViewModel.Create(
            ProjectId,
            Today,
            new[]
            {
                new TimelineItemVm { Code = StageCodes.BID, Name = "Bidding", RequiresBackfill = true },
                new TimelineItemVm { Code = "EXT1", Name = "Extension", RequiresBackfill = true }
            },
            new[] { AwaitingBackfill(StageCodes.BID, Feb(1)) });

        // One tokenless row must not downgrade the whole form to the legacy contract.
        Assert.Equal(ActualStageToken.BaselineVersion, form.BaselineVersion);
        Assert.NotNull(form.Stages.Single(stage => stage.StageCode == StageCodes.BID).StageToken);
        Assert.Null(form.Stages.Single(stage => stage.StageCode == "EXT1").StageToken);
    }

    [Fact]
    public async Task BaselineAwareBackfill_WithAMissingToken_IsRefusedAsStaleWithoutWrites()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db,
            AwaitingBackfill(StageCodes.BID, Feb(1)),
            AwaitingBackfill(StageCodes.TEC, Feb(1))));
        await using var db = fixture.NewContext();

        var stale = await Assert.ThrowsAsync<StageBackfillStaleException>(() => fixture.Backfill(db).ApplyAsync(
            ProjectId,
            new[]
            {
                new StageBackfillUpdate(StageCodes.BID, null, Feb(5), Token(db, StageCodes.BID)),
                new StageBackfillUpdate(StageCodes.TEC, null, Feb(5), StageToken: null)
            },
            TestActors.Hod("actor"),
            baselineVersion: ActualStageToken.BaselineVersion));

        Assert.Equal(new[] { StageCodes.TEC }, stale.StaleStages);
        await using var verify = fixture.NewContext();
        Assert.All(await verify.ProjectStages.ToListAsync(), stage => Assert.Null(stage.CompletedOn));
        await AssertNoHistoryAsync(fixture);
    }

    // SECTION: Rollback and post-commit effects
    [Fact]
    public async Task HistoryWriteFailure_RollsBackTheCorrection_AndSkipsTheAudit()
    {
        var audit = new RecordingAudit();
        await using var fixture = await Fixture.CreateAsync(db => Stages(db, InProgress(StageCodes.FS, Jan(5))));
        await using var db = fixture.NewContext(failStageHistory: true);

        var input = await EditorInputAsync(db, Row(StageCodes.FS, actualStart: Jan(6)));
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Actuals(db, audit).UpdateAsync(input, TestActors.Hod("actor"), "Actor"));

        await AssertUnchangedAsync(fixture, StageCodes.FS, Jan(5), null);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task HistoryWriteFailure_RollsBackTheBackfill()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db, AwaitingBackfill(StageCodes.BID, Feb(1))));
        await using var db = fixture.NewContext(failStageHistory: true);

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Backfill(db).ApplyAsync(
            ProjectId, new[] { new StageBackfillUpdate(StageCodes.BID, null, Feb(5)) }, TestActors.Hod("actor")));

        await using var verify = fixture.NewContext();
        var stage = await verify.ProjectStages.SingleAsync();
        Assert.Null(stage.CompletedOn);
        Assert.True(stage.RequiresBackfill);
    }

    [Fact]
    public async Task PostCommitAuditFailure_DoesNotUndoACommittedCorrection()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db, InProgress(StageCodes.FS, Jan(5))));
        await using var db = fixture.NewContext();

        var result = await fixture.Actuals(db, new ThrowingAudit()).UpdateAsync(
            await EditorInputAsync(db, Row(StageCodes.FS, actualStart: Jan(6))), TestActors.Hod("actor"), "Actor");

        Assert.Equal(1, result.UpdatedCount);
        await AssertUnchangedAsync(fixture, StageCodes.FS, Jan(6), null);
        await using var verify = fixture.NewContext();
        Assert.Single(await verify.StageChangeLogs.ToListAsync());
    }

    // SECTION: Helpers
    private static async Task<StageActualsUpdateResult> UpdateAsync(
        Fixture fixture,
        ApplicationDbContext db,
        params ActualsEditRowInput[] rows) =>
        await fixture.Actuals(db).UpdateAsync(await EditorInputAsync(db, rows), TestActors.Hod("actor"), "Actor");

    // Builds the post the editor would send: the baseline marker plus the token of
    // each stored row as rendered now.
    private static async Task<ActualsEditInput> EditorInputAsync(ApplicationDbContext db, params ActualsEditRowInput[] rows)
    {
        var stages = await db.ProjectStages.AsNoTracking().Where(stage => stage.ProjectId == ProjectId).ToListAsync();
        foreach (var row in rows)
        {
            row.StageToken = stages.SingleOrDefault(stage => stage.StageCode == row.StageCode) is { } stage
                ? ActualStageToken.Compute(stage)
                : null;
        }

        return new ActualsEditInput
        {
            ProjectId = ProjectId,
            BaselineVersion = ActualStageToken.BaselineVersion,
            Rows = rows.ToList()
        };
    }

    private static ProjectTimelineReadService ReadService(ApplicationDbContext db)
    {
        var metadata = new WorkflowStageMetadataProvider();
        return new ProjectTimelineReadService(db, FakeClock.ForIstDate(Today), metadata, new ProjectStageWorkflowPolicy(db, metadata));
    }

    private static string Token(ApplicationDbContext db, string code) =>
        ActualStageToken.Compute(db.ProjectStages.AsNoTracking().Single(stage => stage.StageCode == code));

    private static ActualsEditRowInput Row(
        string code,
        DateOnly? actualStart = null,
        DateOnly? completedOn = null,
        bool clearStart = false) =>
        new() { StageCode = code, ActualStart = actualStart, CompletedOn = completedOn, ClearActualStart = clearStart };

    private static async Task ChangeStoredStartAsync(Fixture fixture, string code, DateOnly start)
    {
        await using var other = fixture.NewContext();
        var stage = await other.ProjectStages.SingleAsync(s => s.StageCode == code);
        stage.ActualStart = start;
        await other.SaveChangesAsync();
    }

    private static async Task ReassignLeadAsync(Fixture fixture, string leadPoUserId)
    {
        await using var other = fixture.NewContext();
        var project = await other.Projects.SingleAsync(p => p.Id == ProjectId);
        project.LeadPoUserId = leadPoUserId;
        await other.SaveChangesAsync();
    }

    private static async Task AssertNoHistoryAsync(Fixture fixture)
    {
        await using var verify = fixture.NewContext();
        Assert.Empty(await verify.StageChangeLogs.ToListAsync());
    }

    private static async Task AssertUnchangedAsync(Fixture fixture, string code, DateOnly? start, DateOnly? completed)
    {
        await using var verify = fixture.NewContext();
        var stage = await verify.ProjectStages.SingleAsync(s => s.StageCode == code);
        Assert.Equal(start, stage.ActualStart);
        Assert.Equal(completed, stage.CompletedOn);
    }

    private static EditActualsModel EditActualsPage(ApplicationDbContext db, StageActualsUpdateService service, string role, string userId)
    {
        var context = PageContextFor(role, userId);
        return new EditActualsModel(service, db, new UserContext(context.HttpContext.User), NullLogger<EditActualsModel>.Instance)
        {
            PageContext = context,
            TempData = new TempDataDictionary(context.HttpContext, new NullTempDataProvider())
        };
    }

    private static PageContext PageContextFor(string roles, string userId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userId)
        };
        claims.AddRange(roles.Split(',').Select(role => new Claim(ClaimTypes.Role, role.Trim())));
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };
        return new PageContext(new ActionContext(http, new RouteData(), new ActionDescriptor()));
    }

    private static void Stages(ApplicationDbContext db, params ProjectStage[] stages) => db.ProjectStages.AddRange(stages);

    private static ProjectStage InProgress(string code, DateOnly start) =>
        new() { ProjectId = ProjectId, StageCode = code, Status = StageStatus.InProgress, ActualStart = start };

    private static ProjectStage Completed(string code, DateOnly? start, DateOnly completed) =>
        new() { ProjectId = ProjectId, StageCode = code, Status = StageStatus.Completed, ActualStart = start, CompletedOn = completed };

    private static ProjectStage AwaitingBackfill(string code, DateOnly? start) =>
        new() { ProjectId = ProjectId, StageCode = code, Status = StageStatus.Completed, ActualStart = start, RequiresBackfill = true };

    private static ArppIpaStageAuthority Authority(DateOnly issueDate) =>
        new(ProjectId, 2025, 7, ArppIssueKind.Original, 0, "ARPP 2025-26", issueDate, 70, "1", "PPP-1");

    private static DateOnly Jan(int day) => new(2026, 1, day);

    private static DateOnly Feb(int day) => new(2026, 2, day);

    private static DateOnly Mar(int day) => new(2026, 3, day);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ArppIpaStageAuthority? _authority;

        private Fixture(SqliteConnection connection, ArppIpaStageAuthority? authority)
        {
            _connection = connection;
            _authority = authority;
        }

        public static async Task<Fixture> CreateAsync(
            Action<ApplicationDbContext> seed,
            Action<Project>? project = null,
            ArppIpaStageAuthority? authority = null)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection, authority);

            await using var db = fixture.NewContext();
            await db.Database.EnsureCreatedAsync();
            // Project user references are foreign keys under SQLite.
            foreach (var userId in new[] { "seed", "assigned-po", "actor", "someone-else" })
            {
                db.Users.Add(new ApplicationUser { Id = userId, UserName = userId });
            }

            var row = new Project
            {
                Id = ProjectId,
                Name = "Direct actuals project",
                CreatedByUserId = "seed",
                LeadPoUserId = "assigned-po",
                WorkflowVersion = ProcurementWorkflow.VersionV1
            };
            project?.Invoke(row);
            db.Projects.Add(row);
            seed(db);
            await db.SaveChangesAsync();
            return fixture;
        }

        public ApplicationDbContext NewContext(bool failStageHistory = false)
        {
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection);
            if (failStageHistory)
            {
                builder.AddInterceptors(new FailStageHistoryInterceptor());
            }

            return new ApplicationDbContext(builder.Options);
        }

        public StageActualsUpdateService Actuals(ApplicationDbContext db, IAuditService? audit = null) =>
            new(db, FakeClock.ForIstDate(Today), audit ?? new RecordingAudit(),
                NullLogger<StageActualsUpdateService>.Instance,
                new StubArppAuthority(_authority),
                StageWorkflowTestFactory.CreatePolicy(db));

        public StageBackfillService Backfill(ApplicationDbContext db, IAuditService? audit = null) =>
            new(db, FakeClock.ForIstDate(Today), new StubArppAuthority(_authority),
                StageWorkflowTestFactory.CreatePolicy(db), audit: audit);

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    /// <summary>Fails the save that carries stage history, after the stage rows were modified.</summary>
    private sealed class FailStageHistoryInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<StageChangeLog>().Any(entry => entry.State == EntityState.Added) == true)
            {
                throw new DbUpdateException("Injected stage-history failure.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class UserContext(ClaimsPrincipal user) : IUserContext
    {
        public ClaimsPrincipal User => user;
        public string? UserId => user.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private sealed class ThrowingAudit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null,
            string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null) =>
            throw new InvalidOperationException("Injected audit failure.");
    }

    private sealed class StubArppAuthority(ArppIpaStageAuthority? authority) : IArppIpaStageAuthorityService
    {
        public Task<ArppIpaStageAuthority?> ResolveAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(authority);

        public Task<IReadOnlyDictionary<int, ArppIpaStageAuthority>> ResolveManyAsync(
            IReadOnlyCollection<int> projectIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, ArppIpaStageAuthority>>(
                authority is null
                    ? new Dictionary<int, ArppIpaStageAuthority>()
                    : new Dictionary<int, ArppIpaStageAuthority> { [authority.ProjectId] = authority });

        public Task<bool> IsManagedAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(authority is not null);

        public Task EnsureManualLifecycleMutationAllowedAsync(int projectId, string stageCode, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
