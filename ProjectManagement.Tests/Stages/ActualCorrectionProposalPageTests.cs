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
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Features.Backfill;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Pages.Projects.Stages;
using ProjectManagement.Pages.Projects.Timeline;
using ProjectManagement.Services;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests.Stages;

/// <summary>
/// Phase D-3c: the assigned PO-only user's posts from the existing Actual editor and
/// backfill form become proposals through the real page handlers, never official
/// facts; HoD/Admin keep direct publication; and a form rendered for one action is
/// never executed as the other. Service behaviour is covered by
/// <see cref="ActualCorrectionProposalServiceTests"/>.
/// </summary>
public sealed class ActualCorrectionProposalPageTests
{
    private const int ProjectId = 1;
    private const string Lead = "assigned-po";
    private const string Other = "someone-else";
    private static readonly DateOnly Today = new(2026, 3, 31);

    // SECTION: Editor routing
    [Fact]
    public async Task AssignedPoOnly_SubmitForApproval_RecordsAProposalAndSaysNothingWasPublished()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        await using var db = fixture.NewContext();
        var page = fixture.EditActualsPage(db, "Project Officer", Lead);
        page.Input = await ProposalInputAsync(db, null, Row(StageCodes.IPA, start: Jan(11)));
        page.Input.Note = "Register shows 11 Jan";

        var result = await page.OnPostAsync(ProjectId, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        var flash = Assert.IsType<string>(page.TempData["Flash"]);
        Assert.Contains("Submitted for HoD or Admin approval", flash, StringComparison.Ordinal);
        Assert.Contains("Official dates are unchanged", flash, StringComparison.Ordinal);
        Assert.DoesNotContain("updated successfully", flash, StringComparison.OrdinalIgnoreCase);
        await using var verify = fixture.NewContext();
        var revision = await verify.ActualCorrectionRevisions.Include(item => item.Items).SingleAsync();
        Assert.Equal("Register shows 11 Jan", revision.Note);
        Assert.Equal(Jan(11), Assert.Single(revision.Items).ProposedActualStart);
        await fixture.AssertOfficialAsync(StageCodes.IPA, Jan(12), null);
    }

    // C7.2: chronology is decided here, not in the browser. A refused proposal reopens the
    // editor with the PO's entries and note, and the corrected resubmission is recorded.
    [Fact]
    public async Task RefusedProposal_ReopensTheEditorWithTheEntries_AndTheCorrectedResubmissionIsRecorded()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(11)));
        await using var db = fixture.NewContext();
        var page = fixture.EditActualsPage(db, "Project Officer", Lead);
        page.Input = await ProposalInputAsync(db, null, Row(StageCodes.FS, completed: Jan(12)), Row(StageCodes.IPA, start: Jan(11)));
        page.Input.Note = "Per signed register";

        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(ProjectId, CancellationToken.None));
        Assert.StartsWith("IPA:", Assert.IsType<string>(page.TempData["Error"]), StringComparison.Ordinal);
        await using (var verify = fixture.NewContext())
        {
            Assert.Empty(await verify.ActualCorrectionRevisions.ToListAsync());
        }

        var payload = Assert.IsType<string>(page.TempData[ActualsEditorRecovery.TempDataKey]);
        var editor = await fixture.RenderEditorAsync(db);
        var reopened = ActualsEditorRecovery.Restore(editor, payload, ProjectId, Lead, ActualsSubmissionMode.Proposal);
        Assert.True(reopened.RestoredSubmission);
        Assert.Contains("Correct IPA in the same update", Assert.Single(reopened.SubmissionErrors), StringComparison.Ordinal);
        Assert.Equal(Jan(12), reopened.EnteredValues[StageCodes.FS].CompletedOn);
        Assert.Equal("Per signed register", reopened.EnteredNote);
        // A form rendered for direct publication never receives a proposal's entries.
        Assert.Same(editor, ActualsEditorRecovery.Restore(editor, payload, ProjectId, Lead, ActualsSubmissionMode.Direct));

        var resubmit = fixture.EditActualsPage(db, "Project Officer", Lead);
        resubmit.Input = await ProposalInputAsync(db, null, Row(StageCodes.FS, completed: Jan(12)), Row(StageCodes.IPA, start: Jan(12)));
        Assert.IsType<RedirectToPageResult>(await resubmit.OnPostAsync(ProjectId, CancellationToken.None));
        Assert.Contains("Submitted for HoD or Admin approval", Assert.IsType<string>(resubmit.TempData["Flash"]), StringComparison.Ordinal);
        await fixture.AssertOfficialAsync(StageCodes.FS, Jan(1), Jan(10));
    }

    [Theory]
    [InlineData("HoD", Other)]
    [InlineData("Admin", Other)]
    [InlineData("HoD,Project Officer", Lead)]
    [InlineData("Admin,Project Officer", Lead)]
    public async Task ProposalForm_PostedByAUserWithDirectAuthority_IsNeitherPublishedNorSubmitted(string roles, string userId)
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        await using var db = fixture.NewContext();
        var page = fixture.EditActualsPage(db, roles, userId);
        page.Input = await ProposalInputAsync(db, null, Row(StageCodes.IPA, start: Jan(11)));

        var result = await page.OnPostAsync(ProjectId, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(EditActualsModel.AccessChangedMessage, page.TempData["Error"]);
        await fixture.AssertNothingWrittenAsync(StageCodes.IPA, Jan(12), null);
    }

    [Theory]
    [InlineData("Project Officer", Other)]
    [InlineData("Viewer", Lead)]
    public async Task ProposalForm_PostedByAnUnassignedPoOrViewer_IsForbidden(string roles, string userId)
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        await using var db = fixture.NewContext();
        var page = fixture.EditActualsPage(db, roles, userId);
        page.Input = await ProposalInputAsync(db, null, Row(StageCodes.IPA, start: Jan(11)));

        Assert.IsType<ForbidResult>(await page.OnPostAsync(ProjectId, CancellationToken.None));
        await fixture.AssertNothingWrittenAsync(StageCodes.IPA, Jan(12), null);
    }

    [Fact]
    public async Task HodDirectSave_IsUnchangedAndLeavesAPendingProposalAlone()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)), InProgress(StageCodes.BID, Feb(1)));
        await fixture.SubmitAsync(Row(StageCodes.BID, start: Feb(2)));
        await using var db = fixture.NewContext();
        var page = fixture.EditActualsPage(db, "HoD", Other);
        page.Input = await DirectInputAsync(db, Row(StageCodes.IPA, start: Jan(11)));

        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(ProjectId, CancellationToken.None));

        Assert.Equal("Actual dates updated successfully.", page.TempData["Flash"]);
        await using var verify = fixture.NewContext();
        Assert.Equal(Jan(11), (await verify.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA)).ActualStart);
        Assert.Equal(ActualCorrectionRevisionStatus.Pending, (await verify.ActualCorrectionRevisions.SingleAsync()).Status);
    }

    // SECTION: Complete-proposal resubmission from the rendered editor
    [Fact]
    public async Task ReopenedEditor_ResubmitsEveryPendingItemIncludingBackfillAndClearIntent()
    {
        await using var fixture = await Fixture.CreateAsync(
            Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)), AwaitingBackfill(StageCodes.BID, Feb(1)));
        // Pending: a cleared FS start (editor) and a BID completion (backfill form).
        await fixture.SubmitAsync(Row(StageCodes.FS, clear: true));
        await fixture.SubmitBackfillAsync(StageCodes.BID, Feb(10));

        // The PO reopens the editor and changes only IPA.
        await using var db = fixture.NewContext();
        var editor = await fixture.RenderEditorAsync(db);
        var fs = editor.Rows.Single(row => row.StageCode == StageCodes.FS);
        Assert.True(fs.HasProposal);
        Assert.Null(fs.EditStart);
        Assert.Equal(Jan(1), fs.ActualStart);
        var page = fixture.EditActualsPage(db, "Project Officer", Lead);
        page.Input = PostFrom(editor, (StageCodes.IPA, Jan(11)));

        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(ProjectId, CancellationToken.None));

        await using var verify = fixture.NewContext();
        var current = await verify.ActualCorrectionRevisions.Include(item => item.Items)
            .SingleAsync(item => item.Status == ActualCorrectionRevisionStatus.Pending);
        Assert.Equal(3, current.RevisionNo);
        var items = current.Items.ToDictionary(item => item.StageCode);
        Assert.Equal(new[] { StageCodes.BID, StageCodes.FS, StageCodes.IPA }, items.Keys.OrderBy(code => code).ToArray());
        Assert.True(items[StageCodes.FS].ClearsActualStart);
        Assert.Equal(Feb(10), items[StageCodes.BID].ProposedCompletedOn);
        Assert.Equal(ActualCorrectionItemKind.Backfill, items[StageCodes.BID].Kind);
        Assert.Equal(Jan(11), items[StageCodes.IPA].ProposedActualStart);
        await fixture.AssertOfficialAsync(StageCodes.FS, Jan(1), Jan(10));
        await fixture.AssertOfficialAsync(StageCodes.BID, Feb(1), null);
    }

    [Fact]
    public async Task ReopenedEditor_PostedUnchanged_KeepsThePendingRevision()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        await fixture.SubmitAsync(Row(StageCodes.IPA, start: Jan(11)));
        await using var db = fixture.NewContext();
        var page = fixture.EditActualsPage(db, "Project Officer", Lead);
        page.Input = PostFrom(await fixture.RenderEditorAsync(db));

        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(ProjectId, CancellationToken.None));

        Assert.Contains("No changes since your pending submission (revision 1)", Assert.IsType<string>(page.TempData["Flash"]), StringComparison.Ordinal);
        await using var verify = fixture.NewContext();
        Assert.Equal(1, await verify.ActualCorrectionRevisions.CountAsync());
    }

    [Fact]
    public async Task ReopenedEditor_RevertingTheOnlyCorrection_PointsToWithdrawWithoutPublishing()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        await fixture.SubmitAsync(Row(StageCodes.IPA, start: Jan(11)));
        await using var db = fixture.NewContext();
        var page = fixture.EditActualsPage(db, "Project Officer", Lead);
        page.Input = PostFrom(await fixture.RenderEditorAsync(db), (StageCodes.IPA, Jan(12)));

        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(ProjectId, CancellationToken.None));

        Assert.Contains("Use Withdraw", Assert.IsType<string>(page.TempData["Error"]), StringComparison.Ordinal);
        await fixture.AssertOfficialAsync(StageCodes.IPA, Jan(12), null);
        await using var verify = fixture.NewContext();
        Assert.Equal(ActualCorrectionRevisionStatus.Pending, (await verify.ActualCorrectionRevisions.SingleAsync()).Status);
    }

    // SECTION: Stale and malformed posts
    [Fact]
    public async Task StaleTokenAndStaleBaseline_GiveReloadGuidanceAndWriteNothing()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        await using var db = fixture.NewContext();
        var staleToken = await ProposalInputAsync(db, null, Row(StageCodes.IPA, start: Jan(11)));
        await fixture.ChangeStoredStartAsync(StageCodes.IPA, Jan(13));
        var tokenPage = fixture.EditActualsPage(db, "Project Officer", Lead);
        tokenPage.Input = staleToken;
        await tokenPage.OnPostAsync(ProjectId, CancellationToken.None);

        await fixture.SubmitAsync(Row(StageCodes.IPA, start: Jan(11)));
        var baselinePage = fixture.EditActualsPage(db, "Project Officer", Lead);
        baselinePage.Input = await ProposalInputAsync(db, null, Row(StageCodes.IPA, start: Jan(10)));
        await baselinePage.OnPostAsync(ProjectId, CancellationToken.None);

        Assert.Contains("changed since you opened the page: IPA", Assert.IsType<string>(tokenPage.TempData["Error"]), StringComparison.Ordinal);
        Assert.Equal("actuals-edit", tokenPage.TempData["OpenOffcanvas"]);
        Assert.Contains("changed in another window", Assert.IsType<string>(baselinePage.TempData["Error"]), StringComparison.Ordinal);
        await using var verify = fixture.NewContext();
        Assert.Equal(1, await verify.ActualCorrectionRevisions.CountAsync());
    }

    [Fact]
    public async Task MalformedBaselineEpoch_IsNeverReadAsNone()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        await using var db = fixture.NewContext();
        var page = fixture.EditActualsPage(db, "Project Officer", Lead);
        page.Input = await ProposalInputAsync(db, null, Row(StageCodes.IPA, start: Jan(11)));
        page.ModelState.AddModelError("Input.BasedOnSubmittedOn", "The value 'yesterday' is not valid.");

        await page.OnPostAsync(ProjectId, CancellationToken.None);

        Assert.Equal(ActualProposalMessages.OutOfDate, page.TempData["Error"]);
        await fixture.AssertNothingWrittenAsync(StageCodes.IPA, Jan(12), null);
    }

    // SECTION: Withdrawal
    [Fact]
    public async Task Withdraw_ClosesTheExactPendingRevisionAndRefusesStaleOrUnauthorisedAttempts()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        var pending = await fixture.SubmitAsync(Row(StageCodes.IPA, start: Jan(11)));
        await using var db = fixture.NewContext();

        var hod = fixture.EditActualsPage(db, "HoD", Other);
        await hod.OnPostWithdrawAsync(ProjectId, pending.Id, pending.SubmittedOn, CancellationToken.None);
        var otherPo = fixture.EditActualsPage(db, "Project Officer", Other);
        var forbidden = await otherPo.OnPostWithdrawAsync(ProjectId, pending.Id, pending.SubmittedOn, CancellationToken.None);
        var wrongEpoch = fixture.EditActualsPage(db, "Project Officer", Lead);
        await wrongEpoch.OnPostWithdrawAsync(ProjectId, pending.Id, pending.SubmittedOn.AddSeconds(-1), CancellationToken.None);
        var withdraw = fixture.EditActualsPage(db, "Project Officer", Lead);
        await withdraw.OnPostWithdrawAsync(ProjectId, pending.Id, pending.SubmittedOn, CancellationToken.None);
        var again = fixture.EditActualsPage(db, "Project Officer", Lead);
        await again.OnPostWithdrawAsync(ProjectId, pending.Id, pending.SubmittedOn, CancellationToken.None);

        Assert.Equal(EditActualsModel.AccessChangedMessage, hod.TempData["Error"]);
        Assert.IsType<ForbidResult>(forbidden);
        Assert.Contains("already withdrawn, replaced or decided", Assert.IsType<string>(wrongEpoch.TempData["Error"]), StringComparison.Ordinal);
        Assert.Contains("was withdrawn. Official dates are unchanged.", Assert.IsType<string>(withdraw.TempData["Flash"]), StringComparison.Ordinal);
        Assert.Contains("already withdrawn, replaced or decided", Assert.IsType<string>(again.TempData["Error"]), StringComparison.Ordinal);
        await using var verify = fixture.NewContext();
        var revision = await verify.ActualCorrectionRevisions.Include(item => item.Items).SingleAsync();
        Assert.Equal(ActualCorrectionRevisionStatus.Withdrawn, revision.Status);
        Assert.Equal(Lead, revision.ClosedByUserId);
        Assert.Single(revision.Items);
        await fixture.AssertOfficialAsync(StageCodes.IPA, Jan(12), null);
    }

    // SECTION: Backfill form
    [Fact]
    public async Task BackfillForm_ByAssignedPoOnly_SubmitsAProposalWithAnExplicitPendingContract()
    {
        await using var fixture = await Fixture.CreateAsync(AwaitingBackfill(StageCodes.BID, Feb(1)));
        await using var db = fixture.NewContext();
        var page = fixture.BackfillPage(db, "Project Officer", Lead);

        var result = await page.OnPostAsync(await BackfillInputAsync(db, null, StageCodes.BID, Feb(10)), CancellationToken.None);

        var body = JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(result).Value);
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.True(body.GetProperty("pendingApproval").GetBoolean());
        Assert.Equal("submitted", body.GetProperty("outcome").GetString());
        Assert.Equal(1, body.GetProperty("revisionNo").GetInt32());
        Assert.Contains("Official dates are unchanged", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        // The modal reloads the page; the confirmation survives the reload (C2.3).
        Assert.Equal(body.GetProperty("message").GetString(), page.TempData["Flash"]);
        await fixture.AssertOfficialAsync(StageCodes.BID, Feb(1), null);
        await using var verify = fixture.NewContext();
        Assert.True((await verify.ProjectStages.SingleAsync()).RequiresBackfill);
        Assert.Equal(ActualCorrectionItemKind.Backfill, (await verify.ActualCorrectionItems.SingleAsync()).Kind);
    }

    [Fact]
    public async Task BackfillForm_CarriesPendingEditorCorrectionsForward()
    {
        await using var fixture = await Fixture.CreateAsync(
            Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)), AwaitingBackfill(StageCodes.BID, Feb(1)));
        var pending = await fixture.SubmitAsync(Row(StageCodes.IPA, start: Jan(11)));
        await using var db = fixture.NewContext();
        var page = fixture.BackfillPage(db, "Project Officer", Lead);

        var result = await page.OnPostAsync(await BackfillInputAsync(db, pending, StageCodes.BID, Feb(10)), CancellationToken.None);

        Assert.IsType<JsonResult>(result);
        await using var verify = fixture.NewContext();
        var current = await verify.ActualCorrectionRevisions.Include(item => item.Items)
            .SingleAsync(item => item.Status == ActualCorrectionRevisionStatus.Pending);
        Assert.Equal(new[] { StageCodes.BID, StageCodes.IPA }, current.Items.Select(item => item.StageCode).OrderBy(code => code).ToArray());
    }

    [Theory]
    [InlineData("HoD", Other)]
    [InlineData("Admin,Project Officer", Lead)]
    public async Task BackfillProposalForm_PostedWithDirectAuthority_IsRefusedWithoutWrites(string roles, string userId)
    {
        await using var fixture = await Fixture.CreateAsync(AwaitingBackfill(StageCodes.BID, Feb(1)));
        await using var db = fixture.NewContext();
        var page = fixture.BackfillPage(db, roles, userId);

        var result = await page.OnPostAsync(await BackfillInputAsync(db, null, StageCodes.BID, Feb(10)), CancellationToken.None);

        var refusal = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, refusal.StatusCode);
        Assert.Equal("reload-required", JsonSerializer.SerializeToElement(refusal.Value).GetProperty("error").GetString());
        Assert.False(page.TempData.ContainsKey("Flash"));
        await fixture.AssertNothingWrittenAsync(StageCodes.BID, Feb(1), null);
    }

    [Fact]
    public async Task BackfillForm_InvalidConflictingAndDuplicatePosts_AreReportedWithoutPublishing()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), AwaitingBackfill(StageCodes.BID, Feb(1)));
        await using var db = fixture.NewContext();

        var future = await fixture.BackfillPage(db, "Project Officer", Lead)
            .OnPostAsync(await BackfillInputAsync(db, null, StageCodes.BID, new DateOnly(2026, 4, 2)), CancellationToken.None);
        var notAwaiting = await fixture.BackfillPage(db, "Project Officer", Lead)
            .OnPostAsync(await BackfillInputAsync(db, null, StageCodes.FS, Jan(9)), CancellationToken.None);
        var first = await BackfillInputAsync(db, null, StageCodes.BID, Feb(10));
        var accepted = await fixture.BackfillPage(db, "Project Officer", Lead).OnPostAsync(first, CancellationToken.None);
        // A second click or tab replaying the same "none" baseline cannot create another revision.
        var duplicate = await fixture.BackfillPage(db, "Project Officer", Lead).OnPostAsync(first, CancellationToken.None);

        Assert.Equal(422, Assert.IsType<UnprocessableEntityObjectResult>(future).StatusCode);
        Assert.Equal("conflict", Body(notAwaiting).GetProperty("error").GetString());
        Assert.IsType<JsonResult>(accepted);
        Assert.Equal(409, Assert.IsType<ObjectResult>(duplicate).StatusCode);
        Assert.Equal("stale", Body(duplicate).GetProperty("error").GetString());
        await using var verify = fixture.NewContext();
        Assert.Equal(1, await verify.ActualCorrectionRevisions.CountAsync());
        await fixture.AssertOfficialAsync(StageCodes.BID, Feb(1), null);
    }

    [Fact]
    public async Task BackfillForm_ResubmittedOverAPendingStartClear_KeepsTheClear()
    {
        await using var fixture = await Fixture.CreateAsync(AwaitingBackfill(StageCodes.BID, Feb(1)), AwaitingBackfill(StageCodes.AON, null));
        // The editor proposes clearing BID's start together with its completion.
        await fixture.SubmitAsync(Row(StageCodes.BID, completed: Feb(10), clear: true));
        await using var db = fixture.NewContext();
        var pending = await fixture.Proposals(db).GetPendingAsync(ProjectId);
        var modal = new BackfillViewModel
        {
            ProjectId = ProjectId,
            Stages = new[]
            {
                new BackfillStageViewModel { StageCode = StageCodes.BID, ActualStart = Feb(1) },
                new BackfillStageViewModel { StageCode = StageCodes.AON }
            }
        }.WithPendingProposal(pending, Lead);
        Assert.True(modal.Stages[0].IsCarriedForward);

        // The modal posts only AON (overview.js skips carried rows).
        var input = await BackfillInputAsync(db,
            new ActualCorrectionRevisionIdentity(pending!.Id, pending.RevisionNo, pending.SubmittedOn), StageCodes.AON, Feb(5));
        Assert.IsType<JsonResult>(await fixture.BackfillPage(db, "Project Officer", Lead).OnPostAsync(input, CancellationToken.None));

        await using var verify = fixture.NewContext();
        var current = await verify.ActualCorrectionRevisions.Include(item => item.Items)
            .SingleAsync(item => item.Status == ActualCorrectionRevisionStatus.Pending);
        var bid = current.Items.Single(item => item.StageCode == StageCodes.BID);
        Assert.True(bid.ClearsActualStart);
        Assert.Null(bid.ProposedActualStart);
        Assert.Contains(current.Items, item => item.StageCode == StageCodes.AON);
    }

    // SECTION: Rendered read models
    [Fact]
    public void EditorViewModel_PrefillsProposedValuesAndReportsStagesItCannotShow()
    {
        var editor = new ActualsEditorVm
        {
            ProjectId = ProjectId,
            Rows = new[]
            {
                new ActualsEditorRowVm { StageCode = StageCodes.FS, Status = StageStatus.Completed, ActualStart = Jan(1), CompletedOn = Jan(10) },
                new ActualsEditorRowVm { StageCode = StageCodes.IPA, Status = StageStatus.InProgress, ActualStart = Jan(12) }
            }
        };
        var revision = new ActualCorrectionRevision
        {
            Id = 7,
            RevisionNo = 2,
            SubmittedByUserId = Lead,
            SubmittedOn = new DateTimeOffset(2026, 3, 30, 4, 5, 6, TimeSpan.Zero).AddTicks(1230),
            Items =
            {
                new ActualCorrectionItem { StageCode = StageCodes.FS, ClearsActualStart = true, ProposedCompletedOn = Jan(10) },
                new ActualCorrectionItem { StageCode = StageCodes.PNC, ProposedActualStart = Jan(20) }
            }
        };

        var prefilled = editor.WithPendingProposal(revision, Lead);
        var notice = editor.WithPendingProposal(revision, Other, prefill: false);

        var fs = prefilled.Rows[0];
        Assert.True(fs.HasProposal);
        Assert.Null(fs.EditStart);
        Assert.Equal(Jan(1), fs.ActualStart);
        Assert.False(prefilled.Rows[1].HasProposal);
        Assert.True(prefilled.PendingProposal!.SubmittedByCurrentUser);
        Assert.Equal(revision.SubmittedOn, DateTimeOffset.Parse(prefilled.PendingProposal.SubmittedOnRoundTrip, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(new[] { StageCodes.PNC }, prefilled.UnrenderedProposalStages(revision));
        Assert.False(notice.Rows[0].HasProposal);
        Assert.Equal(2, notice.PendingProposal!.RevisionNo);
    }

    [Fact]
    public void BackfillViewModel_PrefillsOnlyRowsCoveredByThePendingProposal()
    {
        var backfill = new BackfillViewModel
        {
            ProjectId = ProjectId,
            Stages = new[]
            {
                new BackfillStageViewModel { StageCode = StageCodes.BID, ActualStart = Feb(1), StageToken = "T1" },
                new BackfillStageViewModel { StageCode = StageCodes.AON, StageToken = "T2" }
            }
        };
        var revision = new ActualCorrectionRevision
        {
            Id = 3,
            RevisionNo = 1,
            SubmittedByUserId = Lead,
            Items = { new ActualCorrectionItem { StageCode = StageCodes.BID, ProposedActualStart = Feb(1), ProposedCompletedOn = Feb(10) } }
        };

        var prefilled = backfill.WithPendingProposal(revision, Lead);

        Assert.True(prefilled.Stages[0].HasProposal);
        Assert.Equal(Feb(10), prefilled.Stages[0].CompletedOn);
        Assert.Equal("T1", prefilled.Stages[0].StageToken);
        Assert.False(prefilled.Stages[1].HasProposal);
        Assert.Equal(3, prefilled.PendingProposal!.RevisionId);
        // AON still needs a date, so the banner keeps asking for action.
        Assert.False(prefilled.AwaitingApproval);
        Assert.False(backfill.AwaitingApproval);
    }

    [Fact]
    public void BackfillViewModel_AwaitsApprovalOnlyWhenThePendingProposalCoversEveryStage()
    {
        var backfill = new BackfillViewModel
        {
            ProjectId = ProjectId,
            Stages = new[]
            {
                new BackfillStageViewModel { StageCode = StageCodes.BID, ActualStart = Feb(1) },
                new BackfillStageViewModel { StageCode = StageCodes.AON }
            }
        };
        var revision = new ActualCorrectionRevision
        {
            Id = 4,
            RevisionNo = 2,
            SubmittedByUserId = Lead,
            Items =
            {
                new ActualCorrectionItem { StageCode = StageCodes.BID, ProposedActualStart = Feb(1), ProposedCompletedOn = Feb(10) },
                new ActualCorrectionItem { StageCode = StageCodes.AON, ProposedCompletedOn = Feb(12) }
            }
        };

        Assert.True(backfill.WithPendingProposal(revision, Lead).AwaitingApproval);
        Assert.False(BackfillViewModel.Empty.AwaitingApproval);

        // A HoD/Admin changed an official fact after submission: approval would refuse
        // the revision (SourceChanged), so the PO must act again rather than wait.
        Assert.False(backfill.WithPendingProposal(revision, Lead, changedStageCodes: new[] { StageCodes.AON }).AwaitingApproval);
    }

    [Fact]
    public void BackfillBanner_ReportsASubmissionInsteadOfActionNeeded()
    {
        var banner = ReadRepoFile("Pages", "Projects", "_ProjectOperationalState.cshtml");

        Assert.Contains("Model.Backfill.AwaitingApproval", banner, StringComparison.Ordinal);
        Assert.Contains("Submitted for approval.", banner, StringComparison.Ordinal);
        Assert.Contains("Official dates update when a HoD or Admin approves.", banner, StringComparison.Ordinal);
        Assert.Contains("data-backfill-banner", banner, StringComparison.Ordinal);
    }

    [Fact]
    public void Partials_RenderTheProposalActionsAndTheModeMarker()
    {
        var editor = ReadRepoFile("Pages", "Projects", "Timeline", "_EditActualsForm.cshtml");
        var modal = ReadRepoFile("Pages", "Projects", "_BackfillModal.cshtml");
        var hosts = ReadRepoFile("Pages", "Projects", "_ProjectWorkspaceHosts.cshtml");

        Assert.Contains("Submit for approval", editor, StringComparison.Ordinal);
        Assert.Contains("Input.SubmissionMode", editor, StringComparison.Ordinal);
        Assert.Contains("Input.BasedOnSubmittedOn", editor, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"Withdraw\"", editor, StringComparison.Ordinal);
        Assert.Contains("data-official-start", editor, StringComparison.Ordinal);
        Assert.Contains("Submit for approval", modal, StringComparison.Ordinal);
        Assert.Contains("data-backfill-mode", modal, StringComparison.Ordinal);
        Assert.Equal(2, hosts.Split("[\"ActualsProposalSubmission\"] = Model.CanSubmitActualProposals").Length - 1);
    }

    // SECTION: Helpers
    private static JsonElement Body(IActionResult result) =>
        JsonSerializer.SerializeToElement(Assert.IsAssignableFrom<ObjectResult>(result).Value);

    private static ActualsEditRowInput Row(string code, DateOnly? start = null, DateOnly? completed = null, bool clear = false) =>
        new() { StageCode = code, ActualStart = start, CompletedOn = completed, ClearActualStart = clear };

    private static async Task<ActualsEditInput> ProposalInputAsync(
        ApplicationDbContext db,
        ActualCorrectionRevisionIdentity? basedOn,
        params ActualsEditRowInput[] rows)
    {
        var input = await DirectInputAsync(db, rows);
        input.SubmissionMode = ActualsSubmissionMode.Proposal;
        input.BasedOnRevisionId = basedOn?.Id;
        input.BasedOnSubmittedOn = basedOn?.SubmittedOn;
        return input;
    }

    // The post a rendered direct editor sends: the marker plus each stored row's token.
    private static async Task<ActualsEditInput> DirectInputAsync(ApplicationDbContext db, params ActualsEditRowInput[] rows)
    {
        var stages = await db.ProjectStages.AsNoTracking().Where(stage => stage.ProjectId == ProjectId).ToListAsync();
        foreach (var row in rows)
        {
            row.StageToken = ActualStageToken.Compute(stages.Single(stage => stage.StageCode == row.StageCode));
        }

        return new ActualsEditInput
        {
            ProjectId = ProjectId,
            BaselineVersion = ActualStageToken.BaselineVersion,
            SubmissionMode = ActualsSubmissionMode.Direct,
            Rows = rows.ToList()
        };
    }

    /// <summary>
    /// What the proposal-mode partial and actuals-edit.js post for a rendered editor:
    /// each row's prefilled value from enabled inputs only (disabled inputs are not
    /// posted), clear intent relative to the official start, and the pending identity.
    /// The enablement rules mirror _EditActualsForm. <paramref name="edits"/> sets new starts.
    /// </summary>
    private static ActualsEditInput PostFrom(ActualsEditorVm editor, params (string Code, DateOnly Start)[] edits)
    {
        var changed = edits.ToDictionary(edit => edit.Code, edit => edit.Start);
        return new ActualsEditInput
        {
            ProjectId = editor.ProjectId,
            BaselineVersion = editor.BaselineVersion,
            SubmissionMode = ActualsSubmissionMode.Proposal,
            BasedOnRevisionId = editor.PendingProposal?.RevisionId,
            BasedOnSubmittedOn = editor.PendingProposal is null
                ? null
                : DateTimeOffset.Parse(editor.PendingProposal.SubmittedOnRoundTrip, System.Globalization.CultureInfo.InvariantCulture),
            Note = editor.PendingProposal?.Note,
            Rows = editor.Rows
                .Select(row =>
                {
                    var canEditStart = row.IsEditable || row.HasProposal;
                    var canEditCompletion = canEditStart && row.Status == StageStatus.Completed && !row.IsArppManaged;
                    var start = changed.TryGetValue(row.StageCode, out var edited) ? edited : row.EditStart;
                    return new ActualsEditRowInput
                    {
                        StageCode = row.StageCode,
                        ActualStart = canEditStart ? start : null,
                        CompletedOn = canEditCompletion ? row.EditCompletedOn : null,
                        ClearActualStart = canEditStart && row.ActualStart.HasValue && !start.HasValue,
                        StageToken = row.StageToken
                    };
                })
                .ToList()
        };
    }

    private static async Task<BackfillApplyModel.BackfillApplyInput> BackfillInputAsync(
        ApplicationDbContext db,
        ActualCorrectionRevisionIdentity? basedOn,
        string code,
        DateOnly completed)
    {
        var stage = await db.ProjectStages.AsNoTracking().SingleAsync(item => item.StageCode == code);
        return new BackfillApplyModel.BackfillApplyInput
        {
            ProjectId = ProjectId,
            BaselineVersion = ActualStageToken.BaselineVersion,
            SubmissionMode = ActualsSubmissionMode.Proposal,
            BasedOnRevisionId = basedOn?.Id,
            BasedOnSubmittedOn = basedOn?.SubmittedOn,
            Stages = new List<BackfillApplyModel.BackfillStageInput>
            {
                new() { StageCode = code, ActualStart = stage.ActualStart, CompletedOn = completed, StageToken = ActualStageToken.Compute(stage) }
            }
        };
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "ProjectManagement.csproj")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return System.IO.File.ReadAllText(System.IO.Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
    }

    private static ProjectStage InProgress(string code, DateOnly start) =>
        new() { ProjectId = ProjectId, StageCode = code, Status = StageStatus.InProgress, ActualStart = start };

    private static ProjectStage Completed(string code, DateOnly start, DateOnly completed) =>
        new() { ProjectId = ProjectId, StageCode = code, Status = StageStatus.Completed, ActualStart = start, CompletedOn = completed };

    private static ProjectStage AwaitingBackfill(string code, DateOnly? start) =>
        new() { ProjectId = ProjectId, StageCode = code, Status = StageStatus.Completed, ActualStart = start, RequiresBackfill = true };

    private static DateOnly Jan(int day) => new(2026, 1, day);

    private static DateOnly Feb(int day) => new(2026, 2, day);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly FakeClock _clock = FakeClock.ForIstDate(Today);

        private Fixture(SqliteConnection connection) => _connection = connection;

        public static async Task<Fixture> CreateAsync(params ProjectStage[] stages)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection);

            await using var db = fixture.NewContext();
            await db.Database.EnsureCreatedAsync();
            foreach (var userId in new[] { "seed", Lead, Other })
            {
                db.Users.Add(new ApplicationUser { Id = userId, UserName = userId });
            }

            db.Projects.Add(new Project
            {
                Id = ProjectId,
                Name = "Actual proposal page project",
                CreatedByUserId = "seed",
                LeadPoUserId = Lead,
                WorkflowVersion = ProcurementWorkflow.VersionV1
            });
            db.ProjectStages.AddRange(stages);
            await db.SaveChangesAsync();
            return fixture;
        }

        public ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);

        public ActualCorrectionProposalService Proposals(ApplicationDbContext db)
        {
            // Each submission gets a distinct epoch, as real submissions do.
            _clock.Set(_clock.UtcNow.AddSeconds(1));
            return new ActualCorrectionProposalService(db, _clock, workflowPolicy: StageWorkflowTestFactory.CreatePolicy(db), audit: new RecordingAudit());
        }

        public EditActualsModel EditActualsPage(ApplicationDbContext db, string roles, string userId)
        {
            var context = PageContextFor(roles, userId);
            var direct = new StageActualsUpdateService(db, _clock, new RecordingAudit(), NullLogger<StageActualsUpdateService>.Instance,
                workflowPolicy: StageWorkflowTestFactory.CreatePolicy(db));
            return new EditActualsModel(direct, db, new UserContext(context.HttpContext.User), NullLogger<EditActualsModel>.Instance, Proposals(db))
            {
                PageContext = context,
                TempData = new TempDataDictionary(context.HttpContext, new NullTempDataProvider())
            };
        }

        public BackfillApplyModel BackfillPage(ApplicationDbContext db, string roles, string userId) =>
            new(new StageBackfillService(db, _clock, workflowPolicy: StageWorkflowTestFactory.CreatePolicy(db)), db,
                NullLogger<BackfillApplyModel>.Instance, Proposals(db))
            {
                PageContext = PageContextFor(roles, userId),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempDataProvider())
            };

        // A proposal submitted earlier from the editor, based on the current pending revision.
        public async Task<ActualCorrectionRevisionIdentity> SubmitAsync(params ActualsEditRowInput[] rows)
        {
            await using var db = NewContext();
            var pending = await Proposals(db).GetPendingAsync(ProjectId);
            var input = await ProposalInputAsync(db,
                pending is null ? null : new ActualCorrectionRevisionIdentity(pending.Id, pending.RevisionNo, pending.SubmittedOn), rows);
            var result = await Proposals(db).SubmitAsync(
                new ActualCorrectionSubmission(ProjectId, ActualChangeEntryPoint.Editor,
                    input.Rows.Select(row => new ActualCorrectionProposalRow(row.StageCode, row.ActualStart, row.ClearActualStart, row.CompletedOn, row.StageToken)).ToArray(),
                    new ActualCorrectionBaseline(input.BaselineVersion, input.BasedOnRevisionId, input.BasedOnSubmittedOn)),
                TestActors.ProjectOfficer(Lead));
            Assert.Equal(ActualCorrectionSubmissionOutcome.Submitted, result.Outcome);
            return result.Revision!;
        }

        public async Task SubmitBackfillAsync(string code, DateOnly completed)
        {
            await using var db = NewContext();
            var pending = await Proposals(db).GetPendingAsync(ProjectId);
            var input = await BackfillInputAsync(db,
                pending is null ? null : new ActualCorrectionRevisionIdentity(pending.Id, pending.RevisionNo, pending.SubmittedOn), code, completed);
            Assert.IsType<JsonResult>(await BackfillPage(db, "Project Officer", Lead).OnPostAsync(input, CancellationToken.None));
        }

        /// <summary>The editor model as Overview builds it for the assigned PO-only user.</summary>
        public async Task<ActualsEditorVm> RenderEditorAsync(ApplicationDbContext db)
        {
            var stages = await db.ProjectStages.AsNoTracking().Where(stage => stage.ProjectId == ProjectId).ToListAsync();
            var editor = new ActualsEditorVm
            {
                ProjectId = ProjectId,
                Today = Today,
                BaselineVersion = ActualStageToken.BaselineVersion,
                Rows = stages.Select(stage => new ActualsEditorRowVm
                {
                    StageCode = stage.StageCode,
                    Status = stage.Status,
                    IsEditable = stage.Status is StageStatus.InProgress or StageStatus.Completed,
                    ActualStart = stage.ActualStart,
                    CompletedOn = stage.CompletedOn,
                    RequiresBackfill = stage.RequiresBackfill,
                    StageToken = ActualStageToken.Compute(stage)
                }).ToArray()
            };
            return editor.WithPendingProposal(await Proposals(db).GetPendingAsync(ProjectId), Lead);
        }

        public async Task ChangeStoredStartAsync(string code, DateOnly start)
        {
            await using var db = NewContext();
            var stage = await db.ProjectStages.SingleAsync(item => item.StageCode == code);
            stage.ActualStart = start;
            await db.SaveChangesAsync();
        }

        public async Task AssertOfficialAsync(string code, DateOnly? start, DateOnly? completed)
        {
            await using var verify = NewContext();
            var stage = await verify.ProjectStages.AsNoTracking().SingleAsync(item => item.StageCode == code);
            Assert.Equal(start, stage.ActualStart);
            Assert.Equal(completed, stage.CompletedOn);
            Assert.False(await verify.StageChangeLogs.AnyAsync());
        }

        public async Task AssertNothingWrittenAsync(string code, DateOnly? start, DateOnly? completed)
        {
            await AssertOfficialAsync(code, start, completed);
            await using var verify = NewContext();
            Assert.False(await verify.ActualCorrectionRevisions.AnyAsync());
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    private static PageContext PageContextFor(string roles, string userId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userId)
        };
        claims.AddRange(roles.Split(',').Select(role => new Claim(ClaimTypes.Role, role.Trim())));
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) };
        return new PageContext(new ActionContext(http, new RouteData(), new ActionDescriptor()));
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
}
