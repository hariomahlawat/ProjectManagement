using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels;
using ProjectManagement.ViewModels.Workspace;
using Xunit;

namespace ProjectManagement.Tests.Workspace;

public sealed class BriefingDeskBuilderTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);

    // SECTION: Projects
    [Fact]
    public void Project_WithFreshUpdateAndFuturePdc_IsBriefReady()
    {
        var desk = Build(projects: new[] { Project(daysSinceUpdate: 3, pdcInDays: 30) });

        var item = Assert.Single(desk.Sections.Single().Items);
        Assert.True(item.IsBriefReady);
        Assert.Equal(1, desk.ReadyCount);
        Assert.All(item.Checks, check => Assert.Equal(BriefingDeskCheckState.Done, check.State));
    }

    [Fact]
    public void Project_WithoutAnyUpdate_IsNotReady_AndAsksForTheWeeklyUpdate()
    {
        var item = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: null, pdcInDays: 30) }));

        Assert.False(item.IsBriefReady);
        var update = item.Checks.Single(check => check.Key == "update");
        Assert.Equal(BriefingDeskCheckState.Open, update.State);
        Assert.Equal(BriefingDeskRoute.Update, update.Route);
        Assert.Equal("This week’s update", update.Label);
    }

    [Fact]
    public void Project_UpdateOlderThanTheWeeklyWindow_IsNotReady()
    {
        var item = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: BriefingDeskBuilder.UpdateWindowDays + 1, pdcInDays: 30) }));

        Assert.False(item.IsBriefReady);
    }

    [Fact]
    public void Project_WithUnansweredDirection_AsksForProgressEvenWhenTheUpdateIsFresh()
    {
        var project = Project(daysSinceUpdate: 1, pdcInDays: 30);
        var desk = Build(
            projects: new[] { project },
            pendingDirections: new[] { new WorkspaceConferenceDirectionActionVm { Kind = ConferenceItemKind.Project, ItemId = project.Row.ProjectId } });

        var item = SingleItem(desk);
        Assert.False(item.IsBriefReady);
        Assert.True(item.DirectionAwaitingProgress);
        Assert.Equal("Report progress on the direction", item.Checks.Single(check => check.Key == "update").Label);
    }

    [Fact]
    public void Project_WithMissingPdc_OffersThePlanEditorForTheCurrentStage()
    {
        var item = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: 1, pdcInDays: null) }));

        var pdc = item.Checks.Single(check => check.Key == "pdc");
        Assert.Equal(BriefingDeskCheckState.Open, pdc.State);
        Assert.Equal(BriefingDeskRoute.Approval, pdc.Route);
        Assert.Contains("oc=plan&stage=DEVP", pdc.ActionUrl);
        Assert.False(item.IsBriefReady);
    }

    [Fact]
    public void Project_WithMissingPdcAndAPlanWithTheHod_IsDoneAndCountsAsReady()
    {
        var project = Project(daysSinceUpdate: 2, pdcInDays: null) with
        {
            PendingPlanSinceUtc = new DateTimeOffset(2026, 10, 19, 6, 0, 0, TimeSpan.Zero)
        };

        var item = SingleItem(Build(projects: new[] { project }));

        var pdc = item.Checks.Single(check => check.Key == "pdc");
        Assert.Equal(BriefingDeskCheckState.Submitted, pdc.State);
        Assert.Equal("Sent to HoD on 19 Oct 2026", pdc.StateText);
        Assert.Null(pdc.ActionText);
        Assert.True(item.IsBriefReady);
    }

    [Fact]
    public void Project_WithAPlanWithTheHod_SaysItWasSent_InsteadOfAnAlarm()
    {
        var since = new DateTimeOffset(2026, 10, 19, 6, 0, 0, TimeSpan.Zero);
        var passed = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: 2, pdcInDays: -18) with { PendingPlanSinceUtc = since } }));
        var missing = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: 2, pdcInDays: null) with { PendingPlanSinceUtc = since } }));

        var passedFact = Assert.Single(passed.Facts, fact => fact.Text.Contains("PDC"));
        Assert.Equal("Revised PDC sent to HoD on 19 Oct 2026", passedFact.Text);
        Assert.Equal(BriefingDeskTone.Neutral, passedFact.Tone);
        Assert.DoesNotContain(passed.Checks, check => check.Key == "pdc");

        var missingFact = Assert.Single(missing.Facts, fact => fact.Text.Contains("PDC"));
        Assert.Equal("PDC sent to HoD on 19 Oct 2026", missingFact.Text);
        Assert.Equal(BriefingDeskTone.Neutral, missingFact.Tone);
    }

    [Fact]
    public void Project_WithPassedPdc_IsARedNoteThatNeverBlocksReadiness()
    {
        var item = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: 2, pdcInDays: -18) }));

        Assert.True(item.IsBriefReady);
        Assert.DoesNotContain(item.Checks, check => check.Key == "pdc");
        var fact = Assert.Single(item.Facts, fact => fact.Text.Contains("PDC"));
        Assert.Equal("PDC overdue by 18 days", fact.Text);
        Assert.Equal(BriefingDeskTone.Danger, fact.Tone);
    }

    [Fact]
    public void Project_MissingActualStart_IsNeverAskedFor()
    {
        var item = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: 1, pdcInDays: 30, startMissing: true) }));

        Assert.True(item.IsBriefReady);
        Assert.DoesNotContain(item.Checks, check => check.Key == "start");
    }

    [Fact]
    public void Project_MissingCompletionDates_AreARequiredStep_NotAnImprovement()
    {
        var health = new WorkspaceRecordHealthVm
        {
            ProjectId = 1,
            HealthPercent = 70,
            GapDetails = new[]
            {
                Gap("IPA_ACTUAL_COMPLETION", "Historical stage completion dates", "IPA — Actual Completion", 5m, "/Projects/Overview/1?oc=actuals#timeline"),
                Gap("AON_ACTUAL_COMPLETION", "Historical stage completion dates", "AON — Actual Completion", 5m, "/Projects/Overview/1?oc=actuals#timeline")
            }
        };

        var item = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: 1, pdcInDays: 30, health: health) }));

        Assert.False(item.IsBriefReady);
        var completion = item.Checks.Single(check => check.Key == "completion");
        Assert.True(completion.IsRequired);
        Assert.Equal(BriefingDeskCheckState.Open, completion.State);
        Assert.Equal("2 stage completion dates missing", completion.Label);
        Assert.Contains("IPA, AON", completion.Detail);
        Assert.Equal("/Projects/Overview/1?oc=actuals#timeline", completion.ActionUrl);
        Assert.Empty(item.Improvements);
    }

    [Fact]
    public void Project_MissingCompletionDates_WithACorrectionWithTheHod_AreDone()
    {
        var health = CompletionGaps("IPA");
        var project = Project(daysSinceUpdate: 1, pdcInDays: 30, health: health) with
        {
            PendingCorrection = Correction("IPA")
        };

        var item = SingleItem(Build(projects: new[] { project }));

        var completion = item.Checks.Single(check => check.Key == "completion");
        Assert.Equal(BriefingDeskCheckState.Submitted, completion.State);
        Assert.Equal("Sent to HoD on 20 Oct 2026", completion.StateText);
        Assert.True(item.IsBriefReady);
    }

    [Fact]
    public void Project_OneMissingCompletionDate_IsNamedInTheStep()
    {
        var item = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: 1, pdcInDays: 30, health: CompletionGaps("IPA")) }));

        var completion = item.Checks.Single(check => check.Key == "completion");
        Assert.Equal("IPA completion date missing", completion.Label);
        Assert.Equal(BriefingDeskCheckState.Open, completion.State);
    }

    [Fact]
    public void Project_ACorrectionForOtherStages_DoesNotAnswerAMissingCompletionDate()
    {
        var project = Project(daysSinceUpdate: 1, pdcInDays: 30, health: CompletionGaps("IPA")) with
        {
            // Proposes only DEVP's completion; IPA is still missing.
            PendingCorrection = Correction("DEVP")
        };

        var item = SingleItem(Build(projects: new[] { project }));

        var completion = item.Checks.Single(check => check.Key == "completion");
        Assert.Equal(BriefingDeskCheckState.Open, completion.State);
        Assert.Equal("IPA completion date missing", completion.Label);
        Assert.Contains("does not include IPA", completion.Detail);
        Assert.False(item.IsBriefReady);
    }

    [Fact]
    public void Project_ACorrectionCoveringSomeStages_ListsOnlyTheRest()
    {
        var project = Project(daysSinceUpdate: 1, pdcInDays: 30, health: CompletionGaps("IPA", "AON", "BID")) with
        {
            PendingCorrection = Correction("IPA")
        };

        var completion = SingleItem(Build(projects: new[] { project })).Checks.Single(check => check.Key == "completion");

        Assert.Equal(BriefingDeskCheckState.Open, completion.State);
        Assert.Equal("2 stage completion dates missing", completion.Label);
        Assert.Contains("AON, BID", completion.Detail);
        Assert.DoesNotContain("IPA", completion.Detail);
    }

    [Fact]
    public void Project_IpaCompletionFromArpp_IsNeverAStep()
    {
        var project = Project(daysSinceUpdate: 1, pdcInDays: 30, health: CompletionGaps("IPA", "AON")) with
        {
            IpaCompletionFromArpp = true
        };

        var completion = SingleItem(Build(projects: new[] { project })).Checks.Single(check => check.Key == "completion");

        // The ARPP record supplies IPA's date and the editor locks it; only AON is asked for.
        Assert.Equal("AON completion date missing", completion.Label);
    }

    [Fact]
    public void Project_IpaCompletionFromArpp_AloneLeavesTheProjectReady()
    {
        var project = Project(daysSinceUpdate: 1, pdcInDays: 30, health: CompletionGaps("IPA")) with
        {
            IpaCompletionFromArpp = true
        };

        var item = SingleItem(Build(projects: new[] { project }));

        Assert.DoesNotContain(item.Checks, check => check.Key == "completion");
        Assert.True(item.IsBriefReady);
    }

    [Fact]
    public void Project_ACorrectionNeedingResubmission_AsksThePoToReviewIt()
    {
        var project = Project(daysSinceUpdate: 1, pdcInDays: 30, health: CompletionGaps("IPA")) with
        {
            PendingCorrection = Correction("IPA") with { NeedsResubmission = true }
        };

        var item = SingleItem(Build(projects: new[] { project }));

        var completion = item.Checks.Single(check => check.Key == "completion");
        Assert.Equal(BriefingDeskCheckState.Open, completion.State);
        Assert.Equal("Review your date correction", completion.Label);
        Assert.Equal("Review", completion.ActionText);
        Assert.Equal("Opens the date editor", completion.RouteHint);
        Assert.False(item.IsBriefReady);
    }

    [Fact]
    public void Project_ACorrectionNeedingResubmission_IsAStepEvenWithNoMissingCompletionDate()
    {
        // For example the PO proposed a different date for a recorded completion, then the
        // HoD published another date directly: no gap, but the correction cannot be approved.
        var project = Project(daysSinceUpdate: 1, pdcInDays: 30) with
        {
            PendingCorrection = Correction("IPA") with { NeedsResubmission = true }
        };

        var item = SingleItem(Build(projects: new[] { project }));

        var review = item.Checks.Single(check => check.Key == "completion");
        Assert.Equal("Review your date correction", review.Label);
        Assert.Equal(BriefingDeskCheckState.Open, review.State);
        Assert.Equal("/Projects/Overview/1?oc=actuals#timeline", review.ActionUrl);
        Assert.False(item.IsBriefReady);
    }

    [Fact]
    public void Project_ACurrentCorrectionWithNoMissingCompletionDate_AddsNoStep()
    {
        var project = Project(daysSinceUpdate: 1, pdcInDays: 30) with { PendingCorrection = Correction("IPA") };

        var item = SingleItem(Build(projects: new[] { project }));

        Assert.DoesNotContain(item.Checks, check => check.Key == "completion");
        Assert.True(item.IsBriefReady);
    }

    [Fact]
    public void Project_PendingPlanWithAFuturePdc_StaysDoneWithThePdc()
    {
        var project = Project(daysSinceUpdate: 1, pdcInDays: 30) with
        {
            PendingPlanSinceUtc = new DateTimeOffset(2026, 10, 19, 6, 0, 0, TimeSpan.Zero)
        };

        var item = SingleItem(Build(projects: new[] { project }));

        var pdc = item.Checks.Single(check => check.Key == "pdc");
        Assert.Equal(BriefingDeskCheckState.Done, pdc.State);
        Assert.Equal("DEVP PDC 20 Nov 2026", pdc.Label);
        Assert.Contains(item.Facts, fact => fact.Text == "PDC 20 Nov 2026" && fact.Tone == BriefingDeskTone.Neutral);
    }

    [Fact]
    public void Project_PassedPdcNote_LinksToThePlanEditor()
    {
        var item = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: 2, pdcInDays: -18) }));

        var fact = Assert.Single(item.Facts, fact => fact.Text.StartsWith("PDC overdue", StringComparison.Ordinal));
        Assert.Contains("oc=plan&stage=DEVP", fact.Url);
        Assert.All(item.Facts.Where(other => other != fact), other => Assert.Null(other.Url));
    }

    [Fact]
    public void Project_StageTag_NamesTheStage()
    {
        var item = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: 1, pdcInDays: 30) }));

        Assert.Equal("DEVP · Development · 40 days", item.Facts[0].Text);
        Assert.Equal("DEVP", BriefingDeskBuilder.StageLabel("DEVP", "DEVP"));
        Assert.Equal("DEVP", BriefingDeskBuilder.StageLabel("DEVP", null));
    }

    [Fact]
    public void Project_TagsThatAStepRepeats_AreMarkedCovered()
    {
        var withPdc = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: 1, pdcInDays: 30) }));
        var passed = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: 1, pdcInDays: -5) }));

        // The PDC and update tags repeat the PDC and update steps; the stage tag has no step.
        Assert.True(withPdc.Facts.Single(fact => fact.Text.StartsWith("PDC", StringComparison.Ordinal)).CoveredByStep);
        Assert.True(withPdc.Facts.Single(fact => fact.Text.StartsWith("Updated", StringComparison.Ordinal)).CoveredByStep);
        Assert.False(withPdc.Facts[0].CoveredByStep);

        // A passed PDC has no step, so its red note stays on the open card.
        Assert.False(passed.Facts.Single(fact => fact.Text.StartsWith("PDC overdue", StringComparison.Ordinal)).CoveredByStep);
    }

    [Fact]
    public void Idea_DocumentCount_ShowsOnlyWhenThereAreDocuments()
    {
        var none = SingleItem(Build(ideas: new[] { new BriefingDeskIdeaInput(Idea(1, needsUpdate: false)) }));
        var some = Idea(2, needsUpdate: false);
        some.DocumentCount = 3;
        var withDocs = SingleItem(Build(ideas: new[] { new BriefingDeskIdeaInput(some) }));

        Assert.DoesNotContain(none.Facts, fact => fact.Text.Contains("document", StringComparison.Ordinal));
        Assert.Contains(withDocs.Facts, fact => fact.Text == "3 documents");
    }

    [Fact]
    public void Task_OverdueDueTag_IsCoveredByTheOverdueStep_UnlessBlocked()
    {
        var overdue = SingleItem(Build(tasks: new[] { TaskInput(ActionTaskStatuses.InProgress, overdueDays: 4, progressDaysAgo: 1) }));
        var blocked = SingleItem(Build(tasks: new[] { TaskInput(ActionTaskStatuses.Blocked, overdueDays: 4, progressDaysAgo: 1) }));

        Assert.True(overdue.Facts.Single(fact => fact.Text.StartsWith("Due", StringComparison.Ordinal)).CoveredByStep);
        Assert.False(blocked.Facts.Single(fact => fact.Text.StartsWith("Due", StringComparison.Ordinal)).CoveredByStep);
    }

    [Fact]
    public void Project_RecordHealth_IsNotAHeaderTag()
    {
        var item = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: 1, pdcInDays: 30) }));

        Assert.DoesNotContain(item.Facts, fact => fact.Text.StartsWith("Records", StringComparison.Ordinal));
        Assert.Equal(100, item.RecordHealthPercent);
    }

    [Fact]
    public void Project_UpdateBox_IsDueOnlyWhenTheUpdateIsOpen()
    {
        var fresh = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: 2, pdcInDays: 30) }));
        var stale = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: 9, pdcInDays: 30) }));

        Assert.False(fresh.IsUpdateDue);
        Assert.True(stale.IsUpdateDue);
    }

    [Fact]
    public void UpdateBox_IsDueForADirection_AStaleIdea_AndStaleTaskProgress_ButNotASubmittedTask()
    {
        var project = Project(daysSinceUpdate: 1, pdcInDays: 30);
        var withDirection = SingleItem(Build(
            projects: new[] { project },
            pendingDirections: new[] { new WorkspaceConferenceDirectionActionVm { Kind = ConferenceItemKind.Project, ItemId = project.Row.ProjectId } }));
        Assert.True(withDirection.IsUpdateDue);

        var ideas = Build(ideas: new[] { new BriefingDeskIdeaInput(Idea(1, needsUpdate: true)), new BriefingDeskIdeaInput(Idea(2, needsUpdate: false)) })
            .Sections.Single().Items;
        Assert.True(ideas.Single(item => item.ItemId == 1).IsUpdateDue);
        Assert.False(ideas.Single(item => item.ItemId == 2).IsUpdateDue);

        Assert.True(SingleItem(Build(tasks: new[] { TaskInput(ActionTaskStatuses.InProgress, overdueDays: 0, progressDaysAgo: 12) })).IsUpdateDue);
        Assert.False(SingleItem(Build(tasks: new[] { TaskInput(ActionTaskStatuses.InProgress, overdueDays: 0, progressDaysAgo: 2) })).IsUpdateDue);
        Assert.False(SingleItem(Build(tasks: new[] { TaskInput(ActionTaskStatuses.Submitted, overdueDays: 0, progressDaysAgo: null) })).IsUpdateDue);
    }

    [Fact]
    public void Project_RecordGaps_AreImprovementsThatNeverBlockReadiness()
    {
        var health = new WorkspaceRecordHealthVm
        {
            ProjectId = 1,
            HealthPercent = 33,
            GapDetails = new[]
            {
                Gap("L1_COST", "Procurement at a glance", "L1 Cost", 4.17m, "/Projects/Overview/1?oc=procurement#procurement"),
                Gap("PNC_COST", "Procurement at a glance", "PNC Cost", 4.17m, "/Projects/Overview/1?oc=procurement#procurement"),
                Gap("DOCS", "Published project documents", "1 additional project document", 5m, "/Projects/1/Documents"),
                Gap("CURRENT_STAGE_PDC", "Current-stage timeline (PDC)", "DEVP — PDC", 9m, "/Projects/Overview/1?oc=actuals#timeline")
            }
        };
        var project = Project(daysSinceUpdate: 1, pdcInDays: 30, health: health);

        var item = SingleItem(Build(projects: new[] { project }));

        Assert.True(item.IsBriefReady);
        Assert.Equal(2, item.Improvements.Count);
        var procurement = item.Improvements.Single(check => check.Route == BriefingDeskRoute.Direct);
        Assert.Equal("2 procurement entries missing", procurement.Label);
        Assert.Equal(8, procurement.Points);
        Assert.Contains(item.Improvements, check => check.Route == BriefingDeskRoute.Approval && check.Label == "1 additional project document");
        Assert.DoesNotContain(item.Improvements, check => check.Label.Contains("PDC"));
        Assert.All(item.Improvements, check => Assert.False(check.IsRequired));
    }

    [Fact]
    public void Project_MediaGaps_LinkWithTheDeskAsTheReturnAddress()
    {
        var health = new WorkspaceRecordHealthVm
        {
            ProjectId = 1,
            HealthPercent = 50,
            GapDetails = new[]
            {
                Gap("PHOTOS", "Supporting media", "3 additional project photos", 6m, "/Projects/1/Photos"),
                Gap("VIDEO", "Supporting media", "Project video", 4m, "/Projects/1/Videos"),
                Gap("DOCS", "Published project documents", "1 additional project document", 5m, "/Projects/1/Documents")
            }
        };
        var project = Project(daysSinceUpdate: 1, pdcInDays: 30, health: health);

        var item = SingleItem(Build(projects: new[] { project }));

        var returnUrl = Uri.EscapeDataString(WorkspaceRouteHelper.BriefingDeskItem("desk-project-1"));
        Assert.Equal($"/Projects/1/Photos?returnUrl={returnUrl}", item.Improvements.Single(check => check.Label == "3 additional project photos").ActionUrl);
        Assert.Equal($"/Projects/1/Videos?returnUrl={returnUrl}", item.Improvements.Single(check => check.Label == "Project video").ActionUrl);
        Assert.Equal("/Projects/1/Documents", item.Improvements.Single(check => check.Label == "1 additional project document").ActionUrl);
        Assert.All(
            item.Improvements.Where(check => check.ActionUrl.Contains("returnUrl=")),
            check => Assert.Equal(BriefingDeskRoute.Direct, check.Route));
    }

    [Fact]
    public void Project_ProcurementGap_OpensTheDeskPanelWhenTheEditorIsLoaded()
    {
        var health = new WorkspaceRecordHealthVm
        {
            ProjectId = 1,
            HealthPercent = 60,
            GapDetails = new[]
            {
                Gap("L1_COST", "Procurement at a glance", "L1 Cost", 4.17m, "/Projects/Overview/1?oc=procurement#procurement"),
                Gap("DOCS", "Published project documents", "1 additional project document", 5m, "/Projects/1/Documents")
            }
        };
        var project = Project(daysSinceUpdate: 1, pdcInDays: 30, health: health) with
        {
            ProcurementEdit = new ProjectManagement.ViewModels.ProcurementEditVm()
        };

        var item = SingleItem(Build(projects: new[] { project }));

        Assert.Equal("desk-project-1-procurement", item.ProcurementPanelId);
        Assert.Equal("desk-project-1-procurement", item.Improvements.Single(check => check.Route == BriefingDeskRoute.Direct).PanelId);
        Assert.Null(item.Improvements.Single(check => check.Route == BriefingDeskRoute.Approval).PanelId);
    }

    [Fact]
    public void Project_ProcurementGap_FallsBackToTheLinkWithoutAnEditor()
    {
        var health = new WorkspaceRecordHealthVm
        {
            ProjectId = 1,
            GapDetails = new[] { Gap("L1_COST", "Procurement at a glance", "L1 Cost", 4.17m, "/Projects/Overview/1?oc=procurement#procurement") }
        };

        var item = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: 1, pdcInDays: 30, health: health) }));

        Assert.Null(item.ProcurementPanelId);
        Assert.Null(Assert.Single(item.Improvements).PanelId);
    }

    // SECTION: Current-stage plan form
    [Fact]
    public void Project_MissingPdc_OffersTheShortPlanForm_WhenThePlanIsClean()
    {
        var project = Project(daysSinceUpdate: 1, pdcInDays: null) with { PlanEditor = Editor() };

        var item = SingleItem(Build(projects: new[] { project }));

        var panel = Assert.IsType<BriefingDeskPlanPanelVm>(item.PlanPanel);
        Assert.Equal("desk-project-1-plan", panel.PanelId);
        Assert.Equal("DEVP", panel.StageCode);
        Assert.Equal("token-1", panel.EditToken);
        Assert.StartsWith("/Workspace?", panel.ReturnUrl);
        Assert.Equal("desk-project-1-plan", item.Checks.Single(check => check.Key == "pdc").PanelId);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("pending")]
    [InlineData("locked")]
    [InlineData("blocked")]
    public void Project_ShortPlanForm_IsWithheldWhenOtherChangesWouldRideAlong(string condition)
    {
        var state = condition switch
        {
            "draft" => new PlanEditorStateVm { HasMyDraft = true, EditToken = "t" },
            "pending" => new PlanEditorStateVm { HasPendingSubmission = true, EditToken = "t" },
            "locked" => new PlanEditorStateVm { IsLocked = true, EditToken = "t" },
            _ => new PlanEditorStateVm { CanSubmit = false, EditToken = "t" }
        };
        var project = Project(daysSinceUpdate: 1, pdcInDays: null) with { PlanEditor = Editor(state) };

        var item = SingleItem(Build(projects: new[] { project }));

        Assert.Null(item.PlanPanel);
        var pdc = item.Checks.Single(check => check.Key == "pdc");
        Assert.Null(pdc.PanelId);
        Assert.Contains("oc=plan", pdc.ActionUrl);
    }

    [Fact]
    public void Project_ShortPlanForm_IsWithheldForDirectSaveRoles()
    {
        var project = Project(daysSinceUpdate: 1, pdcInDays: null) with { PlanEditor = Editor() };

        var desk = BriefingDeskBuilder.Build(new BriefingDeskInput(
            Today,
            new[] { project },
            Array.Empty<BriefingDeskIdeaInput>(),
            Array.Empty<BriefingDeskTaskInput>(),
            Array.Empty<WorkspaceConferenceDirectionActionVm>(),
            null,
            Array.Empty<BriefingDeskWaitingVm>(),
            PlanDirectSaveRole: true));

        var item = SingleItem(desk);
        Assert.Null(item.PlanPanel);
        var pdc = item.Checks.Single(check => check.Key == "pdc");
        Assert.Null(pdc.PanelId);
        Assert.Contains("oc=plan", pdc.ActionUrl);
    }

    [Fact]
    public void Project_PassedPdc_HasNoPlanForm()
    {
        var project = Project(daysSinceUpdate: 1, pdcInDays: -5) with { PlanEditor = Editor() };

        var item = SingleItem(Build(projects: new[] { project }));

        Assert.Null(item.PlanPanel);
    }

    [Fact]
    public void Project_FuturePdc_HasNoPlanForm()
    {
        var project = Project(daysSinceUpdate: 1, pdcInDays: 30) with { PlanEditor = Editor() };

        var item = SingleItem(Build(projects: new[] { project }));

        Assert.Null(item.PlanPanel);
    }

    private static PlanEditorVm Editor(PlanEditorStateVm? state = null)
        => new()
        {
            State = state ?? new PlanEditorStateVm { EditToken = "token-1" },
            Exact = new PlanEditVm
            {
                ProjectId = 1,
                Rows = new()
                {
                    new PlanEditVm.PlanEditRow { Code = "SO", Name = "Supply Order", Status = StageStatus.Completed },
                    new PlanEditVm.PlanEditRow { Code = "DEVP", Name = "Development", Status = StageStatus.InProgress, PlannedStart = new DateOnly(2026, 2, 1) }
                }
            }
        };

    [Fact]
    public void Project_StageStrip_MarksPastCurrentAndFutureStages()
    {
        var item = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: 1, pdcInDays: 30) }));

        Assert.Equal(new[] { "SO", "DEVP", "ATP" }, item.Strip.Select(step => step.Label));
        Assert.Equal(
            new[] { BriefingDeskStepState.Past, BriefingDeskStepState.Current, BriefingDeskStepState.Future },
            item.Strip.Select(step => step.State));
    }

    // SECTION: Ideas
    [Fact]
    public void Idea_NeedingUpdate_IsNotReady_ButARecentIdeaIs()
    {
        var desk = Build(ideas: new[]
        {
            new BriefingDeskIdeaInput(Idea(1, needsUpdate: true)),
            new BriefingDeskIdeaInput(Idea(2, needsUpdate: false))
        });

        var section = desk.Sections.Single();
        Assert.Equal(ConferenceItemKind.ProjectIdea, section.Kind);
        Assert.False(section.Items.Single(item => item.ItemId == 1).IsBriefReady);
        Assert.True(section.Items.Single(item => item.ItemId == 2).IsBriefReady);
        Assert.Equal(1, section.Items.First().ItemId);
    }

    // SECTION: Tasks
    [Fact]
    public void Task_OverdueAndNotBlocked_NeedsSubmitOrBlock()
    {
        var item = SingleItem(Build(tasks: new[] { TaskInput(ActionTaskStatuses.InProgress, overdueDays: 3, progressDaysAgo: 1) }));

        Assert.False(item.IsBriefReady);
        Assert.Equal(BriefingDeskCheckState.Open, item.Checks.Single(check => check.Key == "due").State);
        Assert.True(item.CanSubmitTask);
        Assert.True(item.CanFlagTaskBlocked);
    }

    [Fact]
    public void Task_FlaggedBlockedWithRecentProgress_IsReady()
    {
        var item = SingleItem(Build(tasks: new[] { TaskInput(ActionTaskStatuses.Blocked, overdueDays: 3, progressDaysAgo: 2) }));

        Assert.True(item.IsBriefReady);
        Assert.False(item.CanFlagTaskBlocked);
        Assert.Equal(BriefingDeskStepState.Blocked, item.Strip[1].State);
    }

    [Fact]
    public void Task_Submitted_WaitsOnTheAssignerAndIsReady()
    {
        var item = SingleItem(Build(tasks: new[] { TaskInput(ActionTaskStatuses.Submitted, overdueDays: 0, progressDaysAgo: null) }));

        Assert.True(item.IsBriefReady);
        var closure = Assert.Single(item.Checks);
        Assert.Equal(BriefingDeskCheckState.Submitted, closure.State);
        Assert.Equal("Sent to assigner on 20 Oct 2026", closure.StateText);
        Assert.False(item.CanSubmitTask);
    }

    [Fact]
    public void Task_WithoutRecentProgress_IsNotReady()
    {
        var item = SingleItem(Build(tasks: new[] { TaskInput(ActionTaskStatuses.Assigned, overdueDays: 0, progressDaysAgo: 12) }));

        Assert.False(item.IsBriefReady);
        Assert.Equal(BriefingDeskCheckState.Open, item.Checks.Single(check => check.Key == "update").State);
    }

    // SECTION: Desk composition
    [Fact]
    public void Sections_FollowConferenceOrder_AndEmptySectionsAreOmitted()
    {
        var desk = Build(
            projects: new[] { Project(daysSinceUpdate: 1, pdcInDays: 30) },
            tasks: new[] { TaskInput(ActionTaskStatuses.Assigned, overdueDays: 0, progressDaysAgo: 1) });

        Assert.Equal(new[] { ConferenceItemKind.Project, ConferenceItemKind.ActionTask }, desk.Sections.Select(section => section.Kind));
        Assert.Equal(2, desk.TotalCount);
    }

    [Fact]
    public void ConferenceItem_IsAttachedForTheComdtPreview()
    {
        var project = Project(daysSinceUpdate: 1, pdcInDays: 30);
        var conference = new OfficerConferenceVm
        {
            Sections = new[]
            {
                new OfficerConferenceSectionVm
                {
                    Kind = ConferenceItemKind.Project,
                    Items = new[]
                    {
                        new OfficerConferenceItemVm
                        {
                            Kind = ConferenceItemKind.Project,
                            ItemId = project.Row.ProjectId,
                            CurrentStateName = "Development",
                            LatestDirection = new ConferenceDirectionVm { Body = "Start trials", CreatedAtUtc = new DateTime(2026, 10, 8, 5, 0, 0, DateTimeKind.Utc) }
                        }
                    }
                }
            }
        };

        var item = SingleItem(Build(projects: new[] { project }, conference: conference));

        Assert.NotNull(item.Conference);
        Assert.Equal("Start trials", item.Direction?.Body);
    }

    // SECTION: Recently completed projects
    [Fact]
    public void RecentlyCompletedProjects_AreListedApart_AndNeverCountTowardsReadiness()
    {
        var conference = new OfficerConferenceVm
        {
            Sections = new[]
            {
                new OfficerConferenceSectionVm
                {
                    Kind = ConferenceItemKind.Project,
                    Items = new[]
                    {
                        new OfficerConferenceItemVm { Kind = ConferenceItemKind.Project, ItemId = 1, Title = "Active project" },
                        new OfficerConferenceItemVm
                        {
                            Kind = ConferenceItemKind.Project, ItemId = 40, Title = "VROP Myanmar 2025", IsRecentlyCompleted = true,
                            CurrentContext = "Completed in 2026",
                            LatestDirection = new ConferenceDirectionVm { Body = "Share lessons learnt", CreatedAtUtc = new DateTime(2026, 10, 1, 5, 0, 0, DateTimeKind.Utc) }
                        },
                        new OfficerConferenceItemVm { Kind = ConferenceItemKind.Project, ItemId = 41, Title = "VROP Sri Lanka 2025", IsRecentlyCompleted = true }
                    }
                }
            }
        };

        var desk = Build(projects: new[] { Project(daysSinceUpdate: 1, pdcInDays: 30) }, conference: conference);

        Assert.Equal(1, desk.TotalCount);
        Assert.Equal(2, desk.RecentlyCompleted.Count);
        var withDirection = desk.RecentlyCompleted[0];
        Assert.Equal(40, withDirection.ItemId);
        Assert.False(withDirection.IsBriefReady);
        Assert.Equal("Report progress on the direction", Assert.Single(withDirection.Checks).Label);
        Assert.Contains(withDirection.Facts, fact => fact.Text == "Completed in 2026");
        Assert.True(desk.RecentlyCompleted[1].IsBriefReady);
        Assert.Empty(desk.RecentlyCompleted[1].Checks);
    }

    [Fact]
    public void RecentlyCompletedProject_WithProgressAfterTheDirection_IsSettled()
    {
        var conference = new OfficerConferenceVm
        {
            Sections = new[]
            {
                new OfficerConferenceSectionVm
                {
                    Kind = ConferenceItemKind.Project,
                    Items = new[]
                    {
                        new OfficerConferenceItemVm
                        {
                            Kind = ConferenceItemKind.Project, ItemId = 40, Title = "VROP Myanmar 2025", IsRecentlyCompleted = true,
                            LatestDirection = new ConferenceDirectionVm { Body = "Share lessons learnt" },
                            ProgressEntries = new[] { new ConferenceProgressEntryVm { Label = "Project Officer", Body = "Lessons learnt shared.", ActivityAtUtc = new DateTime(2026, 10, 15, 5, 0, 0, DateTimeKind.Utc) } }
                        }
                    }
                }
            }
        };

        var desk = Build(conference: conference);

        Assert.True(Assert.Single(desk.RecentlyCompleted).IsBriefReady);
        Assert.Empty(desk.Sections);
    }

    [Theory]
    [InlineData("placeholder")]
    [InlineData("mco-only")]
    public void RecentlyCompletedProject_PlaceholderOrMcoEntry_StillAwaitsThePo(string entries)
    {
        var placeholder = new ConferenceProgressEntryVm
        {
            Label = "Project Officer",
            EmptyText = "Progress update awaited. No Project Officer remark has been recorded since the direction was issued."
        };
        var mco = new ConferenceProgressEntryVm { Label = "MCO", Body = "Vendor paperwork filed.", ActivityAtUtc = new DateTime(2026, 10, 15, 5, 0, 0, DateTimeKind.Utc) };
        var conference = new OfficerConferenceVm
        {
            Sections = new[]
            {
                new OfficerConferenceSectionVm
                {
                    Kind = ConferenceItemKind.Project,
                    Items = new[]
                    {
                        new OfficerConferenceItemVm
                        {
                            Kind = ConferenceItemKind.Project, ItemId = 40, Title = "VROP Myanmar 2025", IsRecentlyCompleted = true,
                            LatestDirection = new ConferenceDirectionVm { Body = "Share lessons learnt" },
                            ProgressEntries = entries == "placeholder" ? new[] { placeholder } : new[] { placeholder, mco }
                        }
                    }
                }
            }
        };

        var item = Assert.Single(Build(conference: conference).RecentlyCompleted);

        Assert.False(item.IsBriefReady);
        Assert.True(item.DirectionAwaitingProgress);
        Assert.Equal("Report progress on the direction", Assert.Single(item.Checks).Label);
    }

    // SECTION: Builders
    private static BriefingDeskVm Build(
        IReadOnlyList<BriefingDeskProjectInput>? projects = null,
        IReadOnlyList<BriefingDeskIdeaInput>? ideas = null,
        IReadOnlyList<BriefingDeskTaskInput>? tasks = null,
        IReadOnlyList<WorkspaceConferenceDirectionActionVm>? pendingDirections = null,
        OfficerConferenceVm? conference = null)
        => BriefingDeskBuilder.Build(new BriefingDeskInput(
            Today,
            projects ?? Array.Empty<BriefingDeskProjectInput>(),
            ideas ?? Array.Empty<BriefingDeskIdeaInput>(),
            tasks ?? Array.Empty<BriefingDeskTaskInput>(),
            pendingDirections ?? Array.Empty<WorkspaceConferenceDirectionActionVm>(),
            conference,
            Array.Empty<BriefingDeskWaitingVm>()));

    private static BriefingDeskItemVm SingleItem(BriefingDeskVm desk)
        => Assert.Single(Assert.Single(desk.Sections).Items);

    private static BriefingDeskProjectInput Project(int? daysSinceUpdate, int? pdcInDays, WorkspaceRecordHealthVm? health = null, bool startMissing = false)
    {
        var row = new WorkspaceProjectMatrixRowVm
        {
            ProjectId = 1,
            ProjectName = "VR Based BFSR",
            CurrentStageCode = "DEVP",
            CurrentStageName = "Development",
            DaysInCurrentStage = 40,
            CurrentStagePdc = pdcInDays is int days ? Today.AddDays(days) : null,
            DaysUntilCurrentStagePdc = pdcInDays,
            DaysSinceLastPoRemark = daysSinceUpdate,
            HasOverdueCurrentStage = pdcInDays is < 0,
            IsCurrentStagePdcMissing = pdcInDays is null,
            IsCurrentStageStartMissing = startMissing,
            RecordHealthPercent = health?.HealthPercent ?? 100,
            RecordHealth = health ?? new WorkspaceRecordHealthVm { ProjectId = 1, HealthPercent = 100 },
            OpenUrl = "/Projects/Overview/1"
        };
        var stages = new[]
        {
            new BriefingDeskStageInput("SO", "Supply Order", StageStatus.Completed, 1),
            new BriefingDeskStageInput("DEVP", "Development", StageStatus.InProgress, 2),
            new BriefingDeskStageInput("ATP", "Acceptance Testing", StageStatus.NotStarted, 3)
        };
        return new BriefingDeskProjectInput(row, stages);
    }

    private static BriefingDeskPendingCorrection Correction(params string[] completionStages)
        => new(
            new DateTimeOffset(2026, 10, 20, 6, 0, 0, TimeSpan.Zero),
            completionStages.ToHashSet(StringComparer.OrdinalIgnoreCase),
            NeedsResubmission: false);

    // Completion-date gaps as ProjectRecordHealthService produces them.
    private static WorkspaceRecordHealthVm CompletionGaps(params string[] stages)
        => new()
        {
            ProjectId = 1,
            HealthPercent = 80,
            GapDetails = stages
                .Select(stage => Gap($"{stage}_ACTUAL_COMPLETION", "Historical stage completion dates", $"{stage} — Actual Completion", 5m, "/Projects/Overview/1?oc=actuals#timeline", stage))
                .ToArray()
        };

    private static WorkspaceRecordGapVm Gap(string code, string component, string label, decimal points, string url, string? stageCode = null)
        => new()
        {
            StageCode = stageCode,
            Code = code,
            Component = component,
            FieldLabel = label,
            Reason = $"{label} is missing.",
            MaximumPoints = points,
            ActionText = "Fix",
            ActionUrl = url
        };

    private static WorkspaceIdeaVm Idea(int id, bool needsUpdate)
        => new()
        {
            IdeaId = id,
            Title = $"Idea {id}",
            Status = "Active",
            NeedsUpdate = needsUpdate,
            LastActivityAtUtc = needsUpdate ? new DateTime(2026, 9, 20, 6, 0, 0, DateTimeKind.Utc) : new DateTime(2026, 10, 18, 6, 0, 0, DateTimeKind.Utc),
            OpenUrl = $"/ProjectIdeas/Details/{id}"
        };

    private static BriefingDeskTaskInput TaskInput(string status, int overdueDays, int? progressDaysAgo)
        => new(
            new WorkspaceTaskVm
            {
                TaskId = 7,
                Title = "Prepare TEC report",
                Status = status,
                Priority = "High",
                DueDate = Today.AddDays(overdueDays > 0 ? -overdueDays : 5),
                IsOverdue = overdueDays > 0,
                DaysOverdue = overdueDays > 0 ? overdueDays : null,
                OpenUrl = "/ActionTasks?taskId=7",
                SubmittedOnUtc = status == ActionTaskStatuses.Submitted ? new DateTime(2026, 10, 20, 6, 0, 0, DateTimeKind.Utc) : null
            },
            progressDaysAgo is int days ? new DateTime(2026, 10, 21, 6, 0, 0, DateTimeKind.Utc).AddDays(-days) : null,
            "AAAAAAAAB9E=");
}
