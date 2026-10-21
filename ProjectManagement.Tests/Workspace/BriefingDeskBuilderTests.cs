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
    public void Project_WithOverduePdcAndAPendingPlan_ShowsWithHodAndCountsAsReady()
    {
        var project = Project(daysSinceUpdate: 2, pdcInDays: -18) with
        {
            PendingPlanSinceUtc = new DateTimeOffset(2026, 10, 19, 6, 0, 0, TimeSpan.Zero)
        };

        var item = SingleItem(Build(projects: new[] { project }));

        var pdc = item.Checks.Single(check => check.Key == "pdc");
        Assert.Equal(BriefingDeskCheckState.Waiting, pdc.State);
        Assert.Equal("With HoD since 19 Oct 2026", pdc.StateText);
        Assert.True(item.IsBriefReady);
    }

    [Fact]
    public void Project_WithOverduePdcAndNoPlan_AsksForARevisedDate()
    {
        var item = SingleItem(Build(projects: new[] { Project(daysSinceUpdate: 2, pdcInDays: -18) }));

        var pdc = item.Checks.Single(check => check.Key == "pdc");
        Assert.Equal("Revise DEVP PDC", pdc.Label);
        Assert.Equal(BriefingDeskCheckState.Open, pdc.State);
        Assert.Contains(item.Facts, fact => fact.Tone == BriefingDeskTone.Danger && fact.Text.Contains("overdue 18 days"));
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
                Gap("DEVP_PDC", "Current-stage timeline (PDC)", "DEVP — PDC", 9m, "/Projects/Overview/1?oc=actuals#timeline")
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

    // SECTION: Returned items and the current-stage plan form
    [Fact]
    public void Project_ReturnedItem_IsTheFirstCheck_WithTheHodNote_AndBlocksReadiness()
    {
        var returned = new WorkspaceAttentionItemVm
        {
            ProjectId = 1,
            Type = "Timeline",
            Detail = "Timeline plan returned for correction",
            ActionText = "Correct",
            ActionUrl = "/Projects/Overview/1?oc=plan#timeline",
            DueOrEventDateUtc = new DateTime(2026, 10, 19, 6, 0, 0, DateTimeKind.Utc),
            ReviewerNote = "  Align DEVP with the vendor schedule. "
        };

        var desk = BriefingDeskBuilder.Build(new BriefingDeskInput(
            Today,
            new[] { Project(daysSinceUpdate: 1, pdcInDays: 30) },
            Array.Empty<BriefingDeskIdeaInput>(),
            Array.Empty<BriefingDeskTaskInput>(),
            Array.Empty<WorkspaceConferenceDirectionActionVm>(),
            null,
            Array.Empty<BriefingDeskWaitingVm>(),
            new[] { returned }));

        var item = SingleItem(desk);
        var first = item.Checks[0];
        Assert.Equal("Timeline plan returned for correction", first.Label);
        Assert.Equal("Returned on 19 Oct 2026. HoD: \u201cAlign DEVP with the vendor schedule.\u201d", first.Detail);
        Assert.Equal("Correct and resubmit", first.ActionText);
        Assert.Equal(BriefingDeskCheckState.Open, first.State);
        Assert.False(item.IsBriefReady);
    }

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
            Array.Empty<WorkspaceAttentionItemVm>(),
            PlanDirectSaveRole: true));

        var item = SingleItem(desk);
        Assert.Null(item.PlanPanel);
        var pdc = item.Checks.Single(check => check.Key == "pdc");
        Assert.Null(pdc.PanelId);
        Assert.Contains("oc=plan", pdc.ActionUrl);
    }

    [Fact]
    public void Project_EveryReturnedItemForTheProject_BecomesACheck()
    {
        var returned = Enumerable.Range(1, 7)
            .Select(index => new WorkspaceAttentionItemVm
            {
                ProjectId = 1,
                Type = $"Type{index}",
                Detail = $"Returned item {index}",
                ActionUrl = "/Projects/Overview/1",
                DueOrEventDateUtc = new DateTime(2026, 10, index, 6, 0, 0, DateTimeKind.Utc)
            })
            .ToList();

        var desk = BriefingDeskBuilder.Build(new BriefingDeskInput(
            Today,
            new[] { Project(daysSinceUpdate: 1, pdcInDays: 30) },
            Array.Empty<BriefingDeskIdeaInput>(),
            Array.Empty<BriefingDeskTaskInput>(),
            Array.Empty<WorkspaceConferenceDirectionActionVm>(),
            null,
            Array.Empty<BriefingDeskWaitingVm>(),
            returned));

        var item = SingleItem(desk);
        Assert.Equal(7, item.Checks.Count(check => check.Key.StartsWith("returned:", StringComparison.Ordinal)));
        Assert.Equal("Returned item 7", item.Checks[0].Label);
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
        Assert.Equal(BriefingDeskCheckState.Waiting, Assert.Single(item.Checks).State);
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
            Array.Empty<BriefingDeskWaitingVm>(),
            Array.Empty<WorkspaceAttentionItemVm>()));

    private static BriefingDeskItemVm SingleItem(BriefingDeskVm desk)
        => Assert.Single(Assert.Single(desk.Sections).Items);

    private static BriefingDeskProjectInput Project(int? daysSinceUpdate, int? pdcInDays, WorkspaceRecordHealthVm? health = null)
    {
        var row = new WorkspaceProjectMatrixRowVm
        {
            ProjectId = 1,
            ProjectName = "VR Based BFSR",
            CurrentStageCode = "DEVP",
            DaysInCurrentStage = 40,
            CurrentStagePdc = pdcInDays is int days ? Today.AddDays(days) : null,
            DaysUntilCurrentStagePdc = pdcInDays,
            DaysSinceLastPoRemark = daysSinceUpdate,
            HasOverdueCurrentStage = pdcInDays is < 0,
            IsCurrentStagePdcMissing = pdcInDays is null,
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

    private static WorkspaceRecordGapVm Gap(string code, string component, string label, decimal points, string url)
        => new()
        {
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
                OpenUrl = "/ActionTasks?taskId=7"
            },
            progressDaysAgo is int days ? new DateTime(2026, 10, 21, 6, 0, 0, DateTimeKind.Utc).AddDays(-days) : null,
            "AAAAAAAAB9E=");
}
