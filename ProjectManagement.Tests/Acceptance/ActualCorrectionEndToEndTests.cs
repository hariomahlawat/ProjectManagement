using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests.Acceptance;

/// <summary>
/// Phase D-3e end-to-end acceptance: the complete PO → HoD/Admin Actual correction
/// workflow through the real host over HTTP — rendered Overview editor and backfill
/// modal, antiforgery-protected posts, the Decision Centre queue, detail and decide
/// pages, and the resulting Overview — with no JavaScript, so the server-rendered
/// form alone must carry the user's intent. Database assertions read the same store.
/// </summary>
[Collection(ActualCorrectionAcceptanceHost.Collection)]
public sealed class ActualCorrectionEndToEndTests
{
    private const int ProjectId = 1;
    private const string Po = "acceptance-po";
    private const string Hod = "acceptance-hod";
    private const string Admin = "acceptance-admin";
    private const string OtherPo = "acceptance-other-po";
    private const string Overview = "/Projects/Overview/1";

    // SECTION: A + C — submit, reopen, approve, see the official result
    [Fact]
    public async Task PoSubmitsThroughTheEditor_HodApprovesInTheDecisionCentre_OfficialDatesFollow()
    {
        await using var host = await CreateHostAsync();
        var po = host.ClientFor(Po, "Project Officer");

        // A1–A6: open the editor, correct IPA, justify, submit; nothing is published.
        var editor = await EditorAsync(po);
        Assert.Contains("Submit for approval", editor.Body, StringComparison.Ordinal);
        editor.Set($"Input.Rows[{editor.RowOf(StageCodes.IPA)}].ActualStart", "2026-01-11")
            .Set("Input.Note", "Register shows 11 Jan");
        var flash = await PostAndFollowAsync(po, editor);
        Assert.Contains("Submitted for HoD or Admin approval (revision 1)", flash, StringComparison.Ordinal);
        Assert.Contains("Official dates are unchanged", flash, StringComparison.Ordinal);
        await AssertOfficialAsync(host, StageCodes.IPA, start: Jan(12), logs: 0);

        // A7–A9: reopening shows the pending revision with the proposed value prefilled.
        var reopened = await EditorAsync(po);
        Assert.Contains("Revision 1 is awaiting HoD or Admin approval", reopened.Body, StringComparison.Ordinal);
        Assert.Equal("2026-01-11", reopened.Get($"Input.Rows[{reopened.RowOf(StageCodes.IPA)}].ActualStart"));
        Assert.Matches("data-actuals-official-start>Official: 12 Jan 2026<", reopened.Body);

        // C1–C3: the Decision Centre lists it and shows submitter, justification, recorded and proposed values.
        var hod = host.ClientFor(Hod, "HoD");
        Assert.Contains("Actual date correction revision 1: 1 stage.", await hod.GetStringAsync("/Approvals/Pending"), StringComparison.Ordinal);
        var revisionId = await PendingRevisionIdAsync(host);
        var detail = await hod.GetStringAsync($"/Approvals/Pending/ActualCorrection/{revisionId}");
        Assert.Contains("Recorded (official)", detail, StringComparison.Ordinal);
        Assert.Contains("Register shows 11 Jan", detail, StringComparison.Ordinal);
        Assert.Contains(Po, detail, StringComparison.Ordinal);
        Assert.Contains("publish", HtmlForm.Find(detail, "data-approval-form").Body + detail, StringComparison.OrdinalIgnoreCase);

        // C4–C8: approve the exact revision; facts, history and decision metadata commit together.
        var decided = await DecideAsync(hod, detail, "Approve", "Checked against the register");
        Assert.Contains("Official dates were updated for IPA", decided, StringComparison.Ordinal);
        await AssertOfficialAsync(host, StageCodes.IPA, start: Jan(11), logs: 1);
        await using (var db = host.NewContext())
        {
            var revision = await db.ActualCorrectionRevisions.SingleAsync();
            Assert.Equal((ActualCorrectionRevisionStatus.Approved, Hod, "Checked against the register"),
                (revision.Status, revision.ClosedByUserId, revision.DecisionNote));
            var log = await db.StageChangeLogs.SingleAsync();
            Assert.Equal((ActualFactPublisher.ActualsUpdatedAction, Hod, (DateOnly?)Jan(12), (DateOnly?)Jan(11)),
                (log.Action, log.UserId, log.FromActualStart, log.ToActualStart));
        }

        Assert.DoesNotContain("Actual date correction revision 1", await hod.GetStringAsync("/Approvals/Pending"), StringComparison.Ordinal);

        // C9–C10: the Overview shows the official value and no pending revision.
        var after = await EditorAsync(po);
        Assert.DoesNotContain("is awaiting HoD or Admin approval", after.Body, StringComparison.Ordinal);
        Assert.Equal("2026-01-11", after.Get($"Input.Rows[{after.RowOf(StageCodes.IPA)}].ActualStart"));
    }

    // SECTION: D — reject; the PO can see the outcome
    [Fact]
    public async Task AdminRejects_EvidenceAndFactsAreKept_ThePoSeesTheRejectionAndRemarks()
    {
        await using var host = await CreateHostAsync();
        var po = host.ClientFor(Po, "Project Officer");
        var editor = await EditorAsync(po);
        editor.Set($"Input.Rows[{editor.RowOf(StageCodes.IPA)}].ActualStart", "2026-01-11");
        await PostAndFollowAsync(po, editor);

        var revisionId = await PendingRevisionIdAsync(host);
        var hod = host.ClientFor(Hod, "HoD");
        var hodTab = await hod.GetStringAsync($"/Approvals/Pending/ActualCorrection/{revisionId}");
        var admin = host.ClientFor(Admin, "Admin");
        var detail = await admin.GetStringAsync($"/Approvals/Pending/ActualCorrection/{revisionId}");
        Assert.Contains("Official dates are unchanged", await DecideAsync(admin, detail, "Reject", "Use the signed register date"), StringComparison.Ordinal);

        await AssertOfficialAsync(host, StageCodes.IPA, start: Jan(12), logs: 0);
        await using (var db = host.NewContext())
        {
            var revision = await db.ActualCorrectionRevisions.Include(item => item.Items).SingleAsync();
            Assert.Equal((ActualCorrectionRevisionStatus.Rejected, Admin), (revision.Status, revision.ClosedByUserId));
            Assert.Equal(Jan(11), Assert.Single(revision.Items).ProposedActualStart);
        }

        // The HoD's tab, opened before the rejection, cannot approve the rejected revision afterwards.
        var late = await DecideAsync(hod, hodTab, "Approve", null);
        Assert.Contains("withdrawn, replaced or already decided", late, StringComparison.Ordinal);
        await AssertOfficialAsync(host, StageCodes.IPA, start: Jan(12), logs: 0);

        // The PO reopening the editor learns that the correction was rejected, and why.
        var reopened = await EditorAsync(po);
        Assert.Contains("Your correction revision 1 was rejected", reopened.Body, StringComparison.Ordinal);

        // After a reassignment the new PO sees the outcome attributed to the previous officer.
        await using (var db = host.NewContext())
        {
            (await db.Projects.SingleAsync(project => project.Id == ProjectId)).LeadPoUserId = OtherPo;
            await db.SaveChangesAsync();
        }

        var newLead = await EditorAsync(host.ClientFor(OtherPo, "Project Officer"));
        Assert.Contains("The previous Project Officer's correction revision 1 was rejected", WebUtility.HtmlDecode(newLead.Body), StringComparison.Ordinal);
        Assert.DoesNotContain("Your correction revision 1", newLead.Body, StringComparison.Ordinal);
        Assert.Contains("Use the signed register date", reopened.Body, StringComparison.Ordinal);
        Assert.Equal("2026-01-12", reopened.Get($"Input.Rows[{reopened.RowOf(StageCodes.IPA)}].ActualStart"));
    }

    // SECTION: B — revise without JavaScript; clear intent and backfill items survive
    [Fact]
    public async Task ReopenedEditorWithoutJavaScript_KeepsAPendingClearAndBackfillItem()
    {
        await using var host = await CreateHostAsync();
        var po = host.ClientFor(Po, "Project Officer");

        // The PO clears FS's recorded start (actuals-edit.js sets the explicit clear flag).
        var editor = await EditorAsync(po);
        var fs = editor.RowOf(StageCodes.FS);
        editor.Set($"Input.Rows[{fs}].ActualStart", string.Empty).Set($"Input.Rows[{fs}].ClearActualStart", "true");
        await PostAndFollowAsync(po, editor);

        // Then supplies BID's missing completion through the backfill modal (JSON).
        var backfill = await BackfillAsync(po, await po.GetStringAsync(Overview), StageCodes.BID, "2026-02-10");
        Assert.True(backfill.GetProperty("pendingApproval").GetBoolean());
        Assert.Contains("Official dates are unchanged", backfill.GetProperty("message").GetString(), StringComparison.Ordinal);

        // Reopen and change only IPA, posting the rendered form as-is (no script runs).
        var reopened = await EditorAsync(po);
        reopened.Set($"Input.Rows[{reopened.RowOf(StageCodes.IPA)}].ActualStart", "2026-01-11");
        Assert.Contains("Submitted for HoD or Admin approval (revision 3)", await PostAndFollowAsync(po, reopened), StringComparison.Ordinal);

        await using var db = host.NewContext();
        var items = (await db.ActualCorrectionRevisions.Include(item => item.Items)
                .SingleAsync(item => item.Status == ActualCorrectionRevisionStatus.Pending))
            .Items.ToDictionary(item => item.StageCode);
        Assert.Equal(new[] { StageCodes.BID, StageCodes.FS, StageCodes.IPA }, items.Keys.OrderBy(code => code).ToArray());
        Assert.True(items[StageCodes.FS].ClearsActualStart);
        Assert.Equal((ActualCorrectionItemKind.Backfill, (DateOnly?)Feb(10)), (items[StageCodes.BID].Kind, items[StageCodes.BID].ProposedCompletedOn));
        Assert.Equal(0, await db.StageChangeLogs.CountAsync());
    }

    /// <summary>
    /// Cases B and D: without JavaScript, the PO types a new start on the stage whose
    /// pending proposal clears it. The rendered clear flag is still "true", so the post
    /// carries a date and a clear for one stage. The shared validator refuses that
    /// combination (ConflictingStartIntent) before any write, so neither the typed date
    /// nor the clear is silently applied and the pending revision is untouched.
    /// </summary>
    [Fact]
    public async Task DateTypedOverAPendingClearWithoutJavaScript_IsRefusedAndThePendingRevisionIsKept()
    {
        await using var host = await CreateHostAsync();
        var po = host.ClientFor(Po, "Project Officer");
        var editor = await EditorAsync(po);
        var fs = editor.RowOf(StageCodes.FS);
        editor.Set($"Input.Rows[{fs}].ActualStart", string.Empty).Set($"Input.Rows[{fs}].ClearActualStart", "true");
        await PostAndFollowAsync(po, editor);
        var before = await RevisionSnapshotAsync(host);

        // The rendered form carries the pending clear; the PO types a date over it.
        var reopened = await EditorAsync(po);
        var row = reopened.RowOf(StageCodes.FS);
        Assert.Equal(string.Empty, reopened.Get($"Input.Rows[{row}].ActualStart"));
        Assert.Equal("true", reopened.Get($"Input.Rows[{row}].ClearActualStart"));
        reopened.Set($"Input.Rows[{row}].ActualStart", "2026-01-02");

        var page = await PostAndFollowAsync(po, reopened);

        // The reopened editor shows the refusal itself; the page alert behind it is not repeated.
        Assert.Contains("<li>FS: Supply a start date or explicitly clear it, not both.</li>", page, StringComparison.Ordinal);
        Assert.Contains("Nothing was submitted.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("FS: Supply a start date or explicitly clear it, not both. Nothing was submitted.", page, StringComparison.Ordinal);
        Assert.Equal(before, await RevisionSnapshotAsync(host));
        await AssertOfficialAsync(host, StageCodes.FS, start: Jan(1), logs: 0);
    }

    [Fact]
    public async Task ApprovalOfAMixedCorrectionAndBackfill_PublishesBothWithTheirHistory()
    {
        await using var host = await CreateHostAsync();
        var po = host.ClientFor(Po, "Project Officer");
        var editor = await EditorAsync(po);
        editor.Set($"Input.Rows[{editor.RowOf(StageCodes.IPA)}].ActualStart", "2026-01-11");
        await PostAndFollowAsync(po, editor);
        await BackfillAsync(po, await po.GetStringAsync(Overview), StageCodes.BID, "2026-02-10");

        var hod = host.ClientFor(Hod, "HoD");
        var detail = await hod.GetStringAsync($"/Approvals/Pending/ActualCorrection/{await PendingRevisionIdAsync(host)}");
        Assert.Contains("Completion backfill", detail, StringComparison.Ordinal);
        await DecideAsync(hod, detail, "Approve", null);

        await using var db = host.NewContext();
        var bid = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.BID);
        Assert.Equal((Feb(10), false), (bid.CompletedOn, bid.RequiresBackfill));
        Assert.Equal(Jan(11), (await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA)).ActualStart);
        Assert.Equal(new[] { ActualFactPublisher.ActualsUpdatedAction, ActualFactPublisher.BackfillAction },
            (await db.StageChangeLogs.ToListAsync()).OrderBy(log => log.StageCode == StageCodes.BID).Select(log => log.Action).ToArray());
    }

    // SECTION: E — roles, self-decision and forged requests over HTTP
    [Fact]
    public async Task SubmitterPromotedToHod_CannotDecide_AndPoOnlyUsersCannotReachTheDecisionCentre()
    {
        await using var host = await CreateHostAsync();
        var po = host.ClientFor(Po, "Project Officer");
        var editor = await EditorAsync(po);
        editor.Set($"Input.Rows[{editor.RowOf(StageCodes.IPA)}].ActualStart", "2026-01-11");
        await PostAndFollowAsync(po, editor);
        var revisionId = await PendingRevisionIdAsync(host);

        // The submitter, now also HoD, sees both actions disabled, and a forced post is forbidden.
        var promoted = po;
        ActualCorrectionAcceptanceHost.SetRoles(promoted, "HoD", "Project Officer");
        var detail = await promoted.GetStringAsync($"/Approvals/Pending/ActualCorrection/{revisionId}");
        Assert.Contains("You submitted this correction", detail, StringComparison.Ordinal);
        var form = HtmlForm.Find(detail, "data-approval-form");
        Assert.Matches(new Regex(@"value=""Approve""[^>]*disabled"), form.Body);
        Assert.Matches(new Regex(@"value=""Reject""[^>]*disabled"), form.Body);
        var forced = await promoted.PostAsync("/Approvals/Pending/Decide", form.Add("Input.Decision", "Approve").Add("Input.Remarks", "Mine").Content());
        Assert.Equal(HttpStatusCode.Forbidden, forced.StatusCode);

        // A PO-only user cannot open the Decision Centre or post a decision.
        ActualCorrectionAcceptanceHost.SetRoles(po, "Project Officer");
        Assert.NotEqual(HttpStatusCode.OK, (await po.GetAsync("/Approvals/Pending")).StatusCode);
        Assert.NotEqual(HttpStatusCode.Redirect, (await po.PostAsync("/Approvals/Pending/Decide", form.Content())).StatusCode);

        await AssertOfficialAsync(host, StageCodes.IPA, start: Jan(12), logs: 0);
        await using var db = host.NewContext();
        Assert.Equal(ActualCorrectionRevisionStatus.Pending, (await db.ActualCorrectionRevisions.SingleAsync()).Status);
    }

    [Fact]
    public async Task CrossedFormsUnassignedUsersAndForgedDecisions_FailSafely()
    {
        await using var host = await CreateHostAsync();
        var po = host.ClientFor(Po, "Project Officer");
        var hod = host.ClientFor(Hod, "HoD");

        // The PO's proposal form, posted after the same user was promoted to HoD, neither
        // publishes nor submits; a form rendered while they held HoD, posted after demotion,
        // is refused the same way.
        var poForm = await EditorAsync(po);
        poForm.Set($"Input.Rows[{poForm.RowOf(StageCodes.IPA)}].ActualStart", "2026-01-11");
        ActualCorrectionAcceptanceHost.SetRoles(po, "HoD", "Project Officer");
        Assert.Contains("Your access to this project changed", await PostAndFollowAsync(po, poForm), StringComparison.Ordinal);
        var directForm = await EditorAsync(po);
        Assert.Contains("Save changes", directForm.Body, StringComparison.Ordinal);
        directForm.Set($"Input.Rows[{directForm.RowOf(StageCodes.IPA)}].ActualStart", "2026-01-11");
        ActualCorrectionAcceptanceHost.SetRoles(po, "Project Officer");
        Assert.Contains("Your access to this project changed", await PostAndFollowAsync(po, directForm), StringComparison.Ordinal);
        await AssertNoRevisionAsync(host);

        // An unassigned PO gets a read-only editor and cannot submit,
        // Even when that user forges proposal mode and a date into their own read-only form.
        var unassigned = host.ClientFor(OtherPo, "Project Officer");
        var readOnly = await EditorAsync(unassigned);
        Assert.DoesNotContain("Submit for approval", readOnly.Body, StringComparison.Ordinal);
        // Its disabled fieldset posts no rows, so the forgery supplies one.
        readOnly.Set("Input.SubmissionMode", "proposal")
            .Add("Input.Rows[0].StageCode", StageCodes.IPA)
            .Add("Input.Rows[0].ActualStart", "2026-01-11")
            .Add("Input.Rows[0].StageToken", poForm.Get($"Input.Rows[{poForm.RowOf(StageCodes.IPA)}].StageToken")!);
        Assert.Equal(HttpStatusCode.Forbidden, (await unassigned.PostAsync(readOnly.Action, readOnly.Content())).StatusCode);
        await AssertNoRevisionAsync(host);

        // A forged epoch or revision id on the Decision Centre form is refused without effect.
        await PostAndFollowAsync(po, poForm);
        var detail = await hod.GetStringAsync($"/Approvals/Pending/ActualCorrection/{await PendingRevisionIdAsync(host)}");
        var stale = HtmlForm.Find(detail, "data-approval-form").Set("Input.SubmittedOn", "2001-01-01T00:00:00.0000000+00:00").Add("Input.Decision", "Approve");
        Assert.Contains("withdrawn, replaced or already decided", await FollowAsync(hod, await hod.PostAsync("/Approvals/Pending/Decide", stale.Content())), StringComparison.Ordinal);
        var forged = HtmlForm.Find(detail, "data-approval-form").Set("Input.RequestId", "999").Add("Input.Decision", "Approve");
        // A revision id that does not exist is refused and its detail page is not found.
        var forgedResponse = await hod.PostAsync("/Approvals/Pending/Decide", forged.Content());
        Assert.Equal(HttpStatusCode.Redirect, forgedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await hod.GetAsync(forgedResponse.Headers.Location)).StatusCode);

        await AssertOfficialAsync(host, StageCodes.IPA, start: Jan(12), logs: 0);
    }

    // SECTION: F/H — official facts change after submission
    [Fact]
    public async Task HodDirectSaveAfterSubmission_MakesTheProposalStaleForBothSides()
    {
        await using var host = await CreateHostAsync();
        var po = host.ClientFor(Po, "Project Officer");
        var editor = await EditorAsync(po);
        editor.Set($"Input.Rows[{editor.RowOf(StageCodes.IPA)}].ActualStart", "2026-01-11");
        await PostAndFollowAsync(po, editor);

        // The HoD corrects IPA directly with the same editor (direct publication).
        var hod = host.ClientFor(Hod, "HoD");
        var direct = await EditorAsync(hod);
        Assert.Contains("Save changes", direct.Body, StringComparison.Ordinal);
        Assert.Contains("A Project Officer's correction (revision 1", direct.Body, StringComparison.Ordinal);
        direct.Set($"Input.Rows[{direct.RowOf(StageCodes.IPA)}].ActualStart", "2026-01-13");
        Assert.Contains("Actual dates updated successfully.", await PostAndFollowAsync(hod, direct), StringComparison.Ordinal);

        // The reviewer cannot approve the stale revision; rejection stays possible.
        var detail = await hod.GetStringAsync($"/Approvals/Pending/ActualCorrection/{await PendingRevisionIdAsync(host)}");
        Assert.Contains("Official dates changed after this correction was submitted", detail, StringComparison.Ordinal);
        var form = HtmlForm.Find(detail, "data-approval-form");
        Assert.Matches(new Regex(@"value=""Approve""[^>]*disabled"), form.Body);
        Assert.DoesNotMatch(new Regex(@"value=""Reject""[^>]*disabled"), form.Body);

        // The PO is told the official dates moved and the update must be revised.
        var reopened = await EditorAsync(po);
        Assert.Contains("Official dates changed for IPA after you submitted", reopened.Body, StringComparison.Ordinal);
        // The form is marked resubmittable, so actuals-edit.js enables Submit without edits.
        Assert.Contains("data-actuals-resubmit=\"true\"", await po.GetStringAsync(Overview), StringComparison.Ordinal);

        // A newly assigned PO sees the same warning without being named as the submitter.
        await using (var db = host.NewContext())
        {
            (await db.Projects.SingleAsync(project => project.Id == ProjectId)).LeadPoUserId = OtherPo;
            await db.SaveChangesAsync();
        }

        var newLeadClient = host.ClientFor(OtherPo, "Project Officer");
        var newLead = await EditorAsync(newLeadClient);
        Assert.Contains("Official dates changed for IPA after it was submitted", newLead.Body, StringComparison.Ordinal);
        Assert.Contains("by the previous Project Officer", newLead.Body, StringComparison.Ordinal);

        // Recovery: the PO resubmits the proposal unchanged. It is recorded again against the
        // new official dates (a new revision, not "Unchanged") and becomes approvable.
        Assert.Contains("Submitted for HoD or Admin approval (revision 2)", await PostAndFollowAsync(newLeadClient, newLead), StringComparison.Ordinal);
        await using (var db = host.NewContext())
        {
            var rebased = await db.ActualCorrectionRevisions.Include(item => item.Items)
                .SingleAsync(item => item.Status == ActualCorrectionRevisionStatus.Pending);
            var item = Assert.Single(rebased.Items);
            Assert.Equal((2, (DateOnly?)Jan(13), (DateOnly?)Jan(11)), (rebased.RevisionNo, item.BeforeActualStart, item.ProposedActualStart));
        }

        var fresh = await hod.GetStringAsync($"/Approvals/Pending/ActualCorrection/{await PendingRevisionIdAsync(host)}");
        Assert.DoesNotMatch(new Regex(@"value=""Approve""[^>]*disabled"), HtmlForm.Find(fresh, "data-approval-form").Body);
        Assert.Contains("Official dates were updated for IPA", await DecideAsync(hod, fresh, "Approve", null), StringComparison.Ordinal);
        await AssertOfficialAsync(host, StageCodes.IPA, start: Jan(11), logs: 2);
    }

    /// <summary>
    /// A HoD publishes exactly the dates the PO proposed. Resubmitting would change nothing,
    /// so the editor does not offer it; it directs the PO to withdraw, and that succeeds.
    /// </summary>
    [Fact]
    public async Task OfficialDatesConvergedOnTheProposal_DirectThePoToWithdraw()
    {
        await using var host = await CreateHostAsync();
        var po = host.ClientFor(Po, "Project Officer");
        var editor = await EditorAsync(po);
        editor.Set($"Input.Rows[{editor.RowOf(StageCodes.IPA)}].ActualStart", "2026-01-11");
        await PostAndFollowAsync(po, editor);

        var hod = host.ClientFor(Hod, "HoD");
        var direct = await EditorAsync(hod);
        direct.Set($"Input.Rows[{direct.RowOf(StageCodes.IPA)}].ActualStart", "2026-01-11");
        Assert.Contains("Actual dates updated successfully.", await PostAndFollowAsync(hod, direct), StringComparison.Ordinal);

        var page = await po.GetStringAsync(Overview);
        Assert.Contains("The official dates now match every date in this update", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-actuals-resubmit=\"true\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("cannot approve this update until you review the dates below and submit again", page, StringComparison.Ordinal);

        var withdraw = HtmlForm.Find(page, "id=\"actuals-withdraw-form\"");
        Assert.Contains("was withdrawn. Official dates are unchanged.", await PostAndFollowAsync(po, withdraw), StringComparison.Ordinal);
        await using var db = host.NewContext();
        Assert.Equal(ActualCorrectionRevisionStatus.Withdrawn, (await db.ActualCorrectionRevisions.SingleAsync()).Status);
        await AssertOfficialAsync(host, StageCodes.IPA, start: Jan(11), logs: 1);
    }

    // SECTION: Helpers
    private static async Task<ActualCorrectionAcceptanceHost> CreateHostAsync() =>
        await ActualCorrectionAcceptanceHost.CreateAsync(async db =>
        {
            foreach (var userId in new[] { Po, Hod, Admin, OtherPo })
            {
                db.Users.Add(new ApplicationUser { Id = userId, UserName = userId, FullName = userId, MustChangePassword = false });
            }

            db.Projects.Add(new Project
            {
                Id = ProjectId,
                Name = "Acceptance project",
                CreatedByUserId = Po,
                LeadPoUserId = Po,
                WorkflowVersion = ProcurementWorkflow.VersionV1
            });
            // Every workflow stage has a row, as a project created through PRISM does. AON is
            // skipped so that BID (awaiting a backfilled completion) has a resolved predecessor.
            var order = 0;
            foreach (var definition in ProcurementWorkflow.V1Stages)
            {
                var stage = new ProjectStage { ProjectId = ProjectId, StageCode = definition.Code, SortOrder = ++order, Status = StageStatus.NotStarted };
                switch (definition.Code)
                {
                    case StageCodes.FS:
                        (stage.Status, stage.ActualStart, stage.CompletedOn) = (StageStatus.Completed, Jan(1), Jan(10));
                        break;
                    case StageCodes.IPA:
                        (stage.Status, stage.ActualStart) = (StageStatus.InProgress, Jan(12));
                        break;
                    case StageCodes.SOW:
                    case StageCodes.AON:
                        stage.Status = StageStatus.Skipped;
                        break;
                    case StageCodes.BID:
                        (stage.Status, stage.ActualStart, stage.RequiresBackfill) = (StageStatus.Completed, Feb(1), true);
                        break;
                }

                db.ProjectStages.Add(stage);
            }
            await db.SaveChangesAsync();
        });

    private static async Task<HtmlForm> EditorAsync(HttpClient client)
    {
        var response = await client.GetAsync(Overview);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return HtmlForm.Find(await response.Content.ReadAsStringAsync(), "data-actuals-form");
    }

    /// <summary>Posts a form, follows the redirect and returns the page, which renders the flash.</summary>
    private static async Task<string> PostAndFollowAsync(HttpClient client, HtmlForm form) =>
        await FollowAsync(client, await client.PostAsync(form.Action, form.Content()));

    private static async Task<string> FollowAsync(HttpClient client, HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return await client.GetStringAsync(response.Headers.Location);
    }

    private static async Task<string> DecideAsync(HttpClient client, string detail, string decision, string? remarks)
    {
        var form = HtmlForm.Find(detail, "data-approval-form").Add("Input.Decision", decision);
        if (remarks is not null)
        {
            form.Set("Input.Remarks", remarks);
        }

        return await FollowAsync(client, await client.PostAsync("/Approvals/Pending/Decide", form.Content()));
    }

    private static async Task<System.Text.Json.JsonElement> BackfillAsync(HttpClient client, string overview, string stageCode, string completedOn)
    {
        var modal = HtmlForm.Find(overview, "data-backfill-form");
        var row = Regex.Match(overview, $@"data-backfill-row data-stage-code=""{stageCode}"" data-stage-token=""(?<token>[0-9A-F]+)""");
        Assert.True(row.Success, "The backfill modal did not render the stage.");
        var basedOn = modal.Get("basedOnRevisionId");
        var epoch = modal.Get("basedOnSubmittedOn");
        var request = new HttpRequestMessage(HttpMethod.Post, "/Projects/Stages/BackfillApply")
        {
            // The payload overview.js builds from the modal.
            Content = JsonContent.Create(new
            {
                projectId = ProjectId,
                baselineVersion = int.Parse(modal.Get("baselineVersion")!),
                submissionMode = modal.Get("submissionMode"),
                basedOnRevisionId = string.IsNullOrEmpty(basedOn) ? (int?)null : int.Parse(basedOn),
                basedOnSubmittedOn = string.IsNullOrEmpty(epoch) ? null : epoch,
                stages = new[] { new { stageCode, stageToken = row.Groups["token"].Value, actualStart = (string?)null, completedOn } }
            })
        };
        request.Headers.Add("X-CSRF-TOKEN", modal.Get("__RequestVerificationToken"));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
    }

    private static async Task<int> PendingRevisionIdAsync(ActualCorrectionAcceptanceHost host)
    {
        await using var db = host.NewContext();
        return await db.ActualCorrectionRevisions.Where(item => item.Status == ActualCorrectionRevisionStatus.Pending).Select(item => item.Id).SingleAsync();
    }

    private static async Task AssertOfficialAsync(ActualCorrectionAcceptanceHost host, string stageCode, DateOnly? start, int logs)
    {
        await using var db = host.NewContext();
        Assert.Equal(start, (await db.ProjectStages.AsNoTracking().SingleAsync(stage => stage.StageCode == stageCode)).ActualStart);
        Assert.Equal(logs, await db.StageChangeLogs.CountAsync());
    }

    // Every revision with its closure state and items, for an exact before/after comparison.
    private static async Task<string> RevisionSnapshotAsync(ActualCorrectionAcceptanceHost host)
    {
        await using var db = host.NewContext();
        var revisions = await db.ActualCorrectionRevisions.AsNoTracking().Include(item => item.Items).OrderBy(item => item.Id).ToListAsync();
        return string.Join(";", revisions.Select(revision =>
            $"{revision.Id}|{revision.RevisionNo}|{revision.Status}|{revision.SubmittedOn:O}|{revision.ContentHash}|{revision.ClosedOn}|" +
            string.Join(",", revision.Items.OrderBy(item => item.StageCode).Select(item =>
                $"{item.StageCode}:{item.ProposedActualStart}:{item.ProposedCompletedOn}:{item.ClearsActualStart}"))));
    }

    private static async Task AssertNoRevisionAsync(ActualCorrectionAcceptanceHost host)
    {
        await using var db = host.NewContext();
        Assert.False(await db.ActualCorrectionRevisions.AnyAsync());
    }

    private static DateOnly Jan(int day) => new(2026, 1, day);

    private static DateOnly Feb(int day) => new(2026, 2, day);
}
