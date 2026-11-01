using System.Globalization;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.ActionTasks;
using ProjectManagement.ViewModels;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

// SECTION: Briefing desk inputs
// Everything the desk needs is resolved by ProjectOfficerWorkspaceService; the builder only
// applies the brief-ready rules, so the rules can be tested without a database.
public sealed record BriefingDeskInput(
    DateOnly Today,
    IReadOnlyList<BriefingDeskProjectInput> Projects,
    IReadOnlyList<BriefingDeskIdeaInput> Ideas,
    IReadOnlyList<BriefingDeskTaskInput> Tasks,
    IReadOnlyList<WorkspaceConferenceDirectionActionVm> PendingDirections,
    OfficerConferenceVm? Conference,
    IReadOnlyList<BriefingDeskWaitingVm> WithOthers,
    // HoD and Admin save plans directly; the plan editor refuses a Submit from them.
    bool PlanDirectSaveRole = false);

public sealed record BriefingDeskProjectInput(
    WorkspaceProjectMatrixRowVm Row,
    IReadOnlyList<BriefingDeskStageInput> Stages,
    DateTimeOffset? PendingPlanSinceUtc = null,
    BriefingDeskPendingCorrection? PendingCorrection = null,
    ProcurementEditVm? ProcurementEdit = null,
    PlanEditorVm? PlanEditor = null,
    // A published ARPP record supplies the IPA completion date (the date editor locks it).
    bool IpaCompletionFromArpp = false);

// The latest pending actual-date correction on a project: when it was sent, the stages whose
// completion date it proposes, and whether official dates moved since, so the HoD cannot approve
// it until the PO reviews and resubmits it.
public sealed record BriefingDeskPendingCorrection(
    DateTimeOffset SubmittedOn,
    IReadOnlySet<string> ProposedCompletionStageCodes,
    bool NeedsResubmission,
    string? SubmittedByUserId = null);

public sealed record BriefingDeskStageInput(string Code, string Name, StageStatus Status, int SortOrder);

public sealed record BriefingDeskIdeaInput(WorkspaceIdeaVm Idea);

public sealed record BriefingDeskTaskInput(
    WorkspaceTaskVm Task,
    DateTime? LastProgressAtUtc,
    string RowVersion);

public static class BriefingDeskBuilder
{
    // SECTION: Rule constants
    // A project remark this many days old or older leaves the project not ready for conference
    // (red). From UpdateDueSoonDays it is still ready but shown in amber, so the PO posts before
    // it turns red. The same window bounds the stage confirmation a remark carries.
    public const int UpdateWindowDays = 10;

    public const int UpdateDueSoonDays = 7;

    // Task progress follows the same weekly rhythm as project updates.
    public const int TaskProgressWindowDays = 7;

    // Current-stage gaps (PDC, planned start, actual start) are recognised by the code prefix
    // ProjectRecordHealthService gives them, not by the component's display label.
    internal const string CurrentStageGapPrefix = "CURRENT_STAGE_";

    public static bool IsCurrentStageGap(WorkspaceRecordGapVm gap)
        => gap.Code.StartsWith(CurrentStageGapPrefix, StringComparison.Ordinal);

    // Completion dates of completed stages are required on the desk, not offered as improvements.
    // Actual start dates are optional and never asked for here. The gaps are recognised by the
    // code ProjectRecordHealthService gives them ("<STAGE>_ACTUAL_COMPLETION"), not by a label.
    internal const string CompletionDateGapSuffix = "_ACTUAL_COMPLETION";

    public static bool IsCompletionDateGap(WorkspaceRecordGapVm gap)
        => gap.Code.EndsWith(CompletionDateGapSuffix, StringComparison.Ordinal);

    public static BriefingDeskVm Build(BriefingDeskInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var conferenceItems = (input.Conference?.Sections ?? Array.Empty<OfficerConferenceSectionVm>())
            .SelectMany(section => section.Items)
            .GroupBy(item => (item.Kind, item.ItemId))
            .ToDictionary(group => group.Key, group => group.First());
        var pendingDirections = input.PendingDirections
            .Select(direction => (direction.Kind, direction.ItemId))
            .ToHashSet();

        OfficerConferenceItemVm? ConferenceItem(ConferenceItemKind kind, int id)
            => conferenceItems.GetValueOrDefault((kind, id));

        var projects = input.Projects
            .Select(project => BuildProject(
                project,
                input.Today,
                ConferenceItem(ConferenceItemKind.Project, project.Row.ProjectId),
                pendingDirections.Contains((ConferenceItemKind.Project, project.Row.ProjectId)),
                input.PlanDirectSaveRole))
            .ToList();
        var ideas = input.Ideas
            .Select(idea => BuildIdea(
                idea.Idea,
                input.Today,
                ConferenceItem(ConferenceItemKind.ProjectIdea, idea.Idea.IdeaId),
                pendingDirections.Contains((ConferenceItemKind.ProjectIdea, idea.Idea.IdeaId))))
            .ToList();
        var tasks = input.Tasks
            .Select(task => BuildTask(
                task,
                input.Today,
                ConferenceItem(ConferenceItemKind.ActionTask, task.Task.TaskId),
                pendingDirections.Contains((ConferenceItemKind.ActionTask, task.Task.TaskId))))
            .ToList();

        // Same order as the Conference view, so preparing here rehearses the brief there.
        var sections = new[]
            {
                Section(ConferenceItemKind.Project, "Projects", projects),
                Section(ConferenceItemKind.ProjectIdea, "Ideas", ideas),
                Section(ConferenceItemKind.ActionTask, "Tasks", tasks)
            }
            .Where(section => section.Items.Count > 0)
            .ToList();
        var all = sections.SelectMany(section => section.Items).ToList();

        // Recently completed projects still appear in conference review, so the PO may be
        // asked about them. They are listed apart and never count towards brief-ready.
        var recentlyCompleted = conferenceItems.Values
            .Where(item => item.Kind == ConferenceItemKind.Project && item.IsRecentlyCompleted)
            .Select(BuildCompletedProject)
            .OrderBy(item => item.IsBriefReady)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new BriefingDeskVm
        {
            Sections = sections,
            RecentlyCompleted = recentlyCompleted,
            ReadyCount = all.Count(item => item.IsBriefReady),
            TotalCount = all.Count,
            WithOthers = input.WithOthers.OrderBy(item => item.SinceUtc).ToList()
        };
    }

    private static BriefingDeskSectionVm Section(
        ConferenceItemKind kind,
        string title,
        IReadOnlyList<BriefingDeskItemVm> items)
        => new()
        {
            Kind = kind,
            Title = title,
            Items = items
                .OrderBy(item => item.IsBriefReady)
                .ThenByDescending(item => item.Facts.Count(fact => fact.Tone == BriefingDeskTone.Danger))
                .ThenByDescending(item => item.OpenCount)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

    // SECTION: Projects
    // Brief-ready = a PO update within the weekly window, the latest conference direction
    // answered, a current-stage PDC that is set, and every completed stage carrying its
    // completion date. Anything submitted for approval is the PO's part done. A PDC that has
    // passed is shown as a red note and never blocks readiness; actual start dates are optional
    // and not asked for. Other record gaps are offered as improvements and never block
    // readiness. Work the HoD returned is not listed: a return is a finished decision, which the
    // PO learns of from the notification and the editor's banner.
    private static BriefingDeskItemVm BuildProject(
        BriefingDeskProjectInput input,
        DateOnly today,
        OfficerConferenceItemVm? conference,
        bool directionAwaitingProgress,
        bool planDirectSaveRole)
    {
        var row = input.Row;
        var hasStage = !string.IsNullOrWhiteSpace(row.CurrentStageCode) && row.CurrentStageCode != "—";
        var anchor = $"desk-{BriefingDeskLabels.KindKey(ConferenceItemKind.Project)}-{row.ProjectId}";

        var projectTab = ProjectManagement.Infrastructure.ProjectTab.Name(row.ProjectId);
        // The current stage can come from the workflow without a saved stage row (for example a
        // project with only FS recorded). A remark can only be tagged with a saved stage
        // (RemarkService checks it), so such a stage is not offered for confirmation: the PO
        // records it on the project page first.
        // With no stage rows at all, RemarkService accepts any stage of the workflow.
        var stageIsRecorded = hasStage && (input.Stages.Count == 0 || input.Stages.Any(stage =>
            string.Equals(stage.Code, row.CurrentStageCode, StringComparison.OrdinalIgnoreCase)));
        var stageToConfirm = stageIsRecorded
            ? new BriefingDeskStageVm(row.CurrentStageCode, StageLabel(row.CurrentStageCode, row.CurrentStageName), row.OpenUrl, projectTab)
            : null;

        // In the order the PO works through them: the remark (which also confirms the stage), the
        // stage's PDC, then procurement for the stages already completed. A stage that a remark
        // cannot confirm is a step of its own, done on the project page.
        var checks = new List<BriefingDeskCheckVm>
        {
            ProjectUpdateCheck(row, conference, directionAwaitingProgress, stageToConfirm, today)
        };
        if (ProjectStageCheck(row, hasStage, stageToConfirm) is { } stageCheck)
        {
            checks.Add(stageCheck);
        }

        var planPanel = hasStage && !planDirectSaveRole ? ShortPlanPanel(input, row, anchor) : null;
        if (hasStage && ProjectPdcCheck(row, input.PendingPlanSinceUtc, planPanel?.PanelId) is { } pdcCheck)
        {
            checks.Add(pdcCheck);
        }

        var procurementPanelId = input.ProcurementEdit is null ? null : $"{anchor}-procurement";
        if (ProjectProcurementCheck(row.RecordHealth, procurementPanelId) is { } procurementCheck)
        {
            checks.Add(procurementCheck);
        }

        // Missing completion dates of earlier stages are a gap to fill when convenient, listed
        // first among the improvements; they never block readiness.
        var improvements = new List<BriefingDeskCheckVm>();
        if (ProjectCompletionDatesCheck(row, input.PendingCorrection, input.IpaCompletionFromArpp) is { } completionCheck)
        {
            improvements.Add(completionCheck);
        }

        improvements.AddRange(ProjectImprovements(row.RecordHealth, procurementPanelId, WorkspaceRouteHelper.BriefingDeskItem(anchor)));

        return new BriefingDeskItemVm
        {
            Kind = ConferenceItemKind.Project,
            ItemId = row.ProjectId,
            Title = row.ProjectName,
            OpenUrl = row.OpenUrl,
            Facts = ProjectFacts(row, hasStage, input.PendingPlanSinceUtc, pdcCovered: checks.Any(check => check.Key == "pdc")),
            Strip = ProjectStrip(input.Stages, row.CurrentStageCode),
            Direction = conference?.LatestDirection,
            DirectionAwaitingProgress = directionAwaitingProgress,
            LastUpdateText = row.DaysSinceLastPoRemark switch
            {
                null => "You have not posted a remark on this project yet.",
                0 => "Your last remark was today.",
                1 => "Your last remark was yesterday.",
                var days => $"Your last remark was {days} days ago."
            },
            StageToConfirm = stageToConfirm,
            Checks = checks,
            Improvements = improvements,
            ProcurementEdit = input.ProcurementEdit,
            ProcurementPanelId = procurementPanelId,
            PlanPanel = planPanel,
            ImprovementPoints = improvements.Sum(item => item.Points),
            RecordHealthPercent = row.RecordHealthPercent,
            Conference = conference
        };
    }

    // One step for the remark and the stage it confirms, since one post does both: the remark box
    // carries "Stage is still …", ticked by default. The step is done while a PO remark is under
    // UpdateWindowDays old and the current stage was confirmed on one in the same window. It
    // turns amber (due soon) from UpdateDueSoonDays, counted from the older of the two.
    private static BriefingDeskCheckVm ProjectUpdateCheck(
        WorkspaceProjectMatrixRowVm row,
        OfficerConferenceItemVm? conference,
        bool directionAwaitingProgress,
        BriefingDeskStageVm? stage,
        DateOnly today)
    {
        var remarkDays = row.DaysSinceLastPoRemark;
        int? confirmedDays = row.CurrentStageConfirmedAtUtc is DateTime confirmedAt
            ? Math.Max(0, today.DayNumber - WorkspaceNudgeService.ToIstDate(confirmedAt).DayNumber)
            : null;
        var fresh = remarkDays is int days && days < UpdateWindowDays;
        var stageConfirmed = stage is null || confirmedDays is int confirmed && confirmed < UpdateWindowDays;
        var withTick = stage is null ? string.Empty : ", with the stage ticked";

        // The wrong-stage link travels with the step; with the remark box open it sits beside the
        // tick instead (bd-card--stage-in-box).
        var secondaryText = stage is null ? null : "Wrong stage? Change it on the project page";

        if (directionAwaitingProgress)
        {
            var issued = conference?.LatestDirection?.CreatedAtUtc;
            return new BriefingDeskCheckVm
            {
                Key = "update",
                Label = "Report progress on the direction",
                NextText = "Direction",
                Detail = (issued is DateTime issuedAt
                    ? $"No progress recorded since the direction of {FormatDay(WorkspaceNudgeService.ToIstDate(issuedAt))}."
                    : "No progress recorded since the latest direction.")
                    + (stageConfirmed ? string.Empty : " Post it with the stage ticked."),
                State = BriefingDeskCheckState.Open,
                Route = BriefingDeskRoute.Update,
                SecondaryText = secondaryText,
                SecondaryUrl = stage?.ProjectPageUrl,
                SecondaryTarget = stage?.ProjectTabName
            };
        }

        if (!fresh)
        {
            return new BriefingDeskCheckVm
            {
                Key = "update",
                Label = "Remark",
                Detail = remarkDays is int stale
                    ? $"Last remark {stale} days ago. Post one at least every {UpdateWindowDays} days{withTick}."
                    : $"No remark yet. Post one at least every {UpdateWindowDays} days{withTick}.",
                State = BriefingDeskCheckState.Open,
                Route = BriefingDeskRoute.Update,
                SecondaryText = secondaryText,
                SecondaryUrl = stage?.ProjectPageUrl,
                SecondaryTarget = stage?.ProjectTabName
            };
        }

        if (!stageConfirmed)
        {
            return new BriefingDeskCheckVm
            {
                Key = "update",
                Label = $"Confirm stage {stage!.Code}",
                NextText = "Confirm stage",
                Detail = $"Your last remark, {DaysAgo(remarkDays!.Value)}, did not confirm the stage. Post a remark with “Stage is still {stage.Label}” ticked.",
                State = BriefingDeskCheckState.Open,
                Route = BriefingDeskRoute.Update,
                SecondaryText = secondaryText,
                SecondaryUrl = stage.ProjectPageUrl,
                SecondaryTarget = stage.ProjectTabName
            };
        }

        // The step lapses with the older of the remark and the confirmation.
        var oldest = Math.Max(remarkDays!.Value, confirmedDays ?? 0);
        var dueSoon = oldest >= UpdateDueSoonDays;
        var posted = stage is null
            ? $"Posted {DaysAgo(remarkDays.Value)}."
            : confirmedDays == remarkDays
                ? $"Posted {DaysAgo(remarkDays.Value)}, stage confirmed."
                : $"Posted {DaysAgo(remarkDays.Value)}, stage confirmed {DaysAgo(confirmedDays!.Value)}.";
        var left = UpdateWindowDays - oldest;

        return new BriefingDeskCheckVm
        {
            Key = "update",
            Label = "Remark",
            Detail = dueSoon ? $"{posted} Post again within {left} day{Plural(left)}." : posted,
            State = BriefingDeskCheckState.Done,
            IsDueSoon = dueSoon,
            Route = BriefingDeskRoute.Update,
            SecondaryText = secondaryText,
            SecondaryUrl = stage?.ProjectPageUrl,
            SecondaryTarget = stage?.ProjectTabName
        };
    }

    private static string DaysAgo(int days) => days switch
    {
        0 => "today",
        1 => "yesterday",
        _ => $"{days} days ago"
    };

    // A current stage that a remark cannot confirm: none at all, or one the workflow supplies
    // without a saved stage row. Either is fixed on the project page, which opens in the
    // project's own tab. Otherwise the stage is confirmed on the remark (ProjectUpdateCheck).
    private static BriefingDeskCheckVm? ProjectStageCheck(
        WorkspaceProjectMatrixRowVm row,
        bool hasStage,
        BriefingDeskStageVm? stage)
    {
        if (stage is not null)
        {
            return null;
        }

        if (hasStage)
        {
            return new BriefingDeskCheckVm
            {
                Key = "stage",
                Label = $"Record stage {row.CurrentStageCode}",
                NextText = "Record stage",
                Detail = "This stage is not recorded on the project yet, so a remark cannot confirm it. Update the stage on the project page.",
                State = BriefingDeskCheckState.Open,
                Route = BriefingDeskRoute.Direct,
                SecondaryText = "Update it on the project page",
                SecondaryUrl = row.OpenUrl,
                SecondaryTarget = ProjectManagement.Infrastructure.ProjectTab.Name(row.ProjectId)
            };
        }

        return new BriefingDeskCheckVm
        {
            Key = "stage",
            Label = "Set the current stage",
            NextText = "Set stage",
            Detail = "The project has no current stage. Set it on the project page.",
            State = BriefingDeskCheckState.Open,
            Route = BriefingDeskRoute.Direct,
            SecondaryText = "Set it on the project page",
            SecondaryUrl = row.OpenUrl,
            SecondaryTarget = ProjectManagement.Infrastructure.ProjectTab.Name(row.ProjectId)
        };
    }

    // Procurement figures that the completed stages make applicable (an AoN cost once AoN is
    // done, and so on: ProjectRecordHealthService decides which). Missing ones block readiness;
    // the PO fills them in the procurement panel on the desk.
    private static BriefingDeskCheckVm? ProjectProcurementCheck(
        WorkspaceRecordHealthVm? health,
        string? procurementPanelId)
    {
        var gaps = (health?.GapDetails ?? Array.Empty<WorkspaceRecordGapVm>())
            .Where(gap => !string.IsNullOrWhiteSpace(gap.ActionUrl) && IsProcurementEditor(gap.ActionUrl!))
            .OrderBy(gap => gap.Priority)
            .ThenBy(gap => gap.FieldLabel)
            .ToList();
        if (gaps.Count == 0)
        {
            return null;
        }

        var fields = string.Join(", ", gaps.Take(4).Select(gap => gap.FieldLabel));
        if (gaps.Count > 4)
        {
            fields += $" and {gaps.Count - 4} more";
        }

        return new BriefingDeskCheckVm
        {
            Key = "procurement",
            NextText = "Procurement",
            Label = gaps.Count == 1 ? $"{gaps[0].FieldLabel} missing" : $"{gaps.Count} procurement entries missing",
            Detail = gaps.Count == 1 ? gaps[0].Reason : $"Needed for the stages already completed: {fields}.",
            State = BriefingDeskCheckState.Open,
            Route = BriefingDeskRoute.Direct,
            ActionText = "Update procurement",
            ActionUrl = gaps[0].ActionUrl,
            PanelId = procurementPanelId
        };
    }

    // SECTION: Current-stage plan form
    // Offered only when the current-stage PDC is missing. The short form posts only the current
    // stage's row to the plan editor's own handler, which then validates and submits the whole
    // plan exactly as the full editor does. It is offered only when nothing else would ride
    // along: no private draft, no pending or locked submission, and a current stage that is
    // still plannable.
    internal static BriefingDeskPlanPanelVm? ShortPlanPanel(
        BriefingDeskProjectInput input,
        WorkspaceProjectMatrixRowVm row,
        string anchor)
    {
        var editor = input.PlanEditor;
        if (editor is null || row.CurrentStagePdc is not null || input.PendingPlanSinceUtc is not null)
        {
            return null;
        }

        var state = editor.State;
        if (state.IsLocked || !state.CanSubmit || state.HasMyDraft || state.HasPendingSubmission)
        {
            return null;
        }

        var stage = editor.Exact.Rows.FirstOrDefault(candidate =>
            string.Equals(candidate.Code, row.CurrentStageCode, StringComparison.OrdinalIgnoreCase));
        if (stage is null || stage.Status is StageStatus.Completed or StageStatus.Skipped)
        {
            return null;
        }

        return new BriefingDeskPlanPanelVm
        {
            PanelId = $"{anchor}-plan",
            ProjectId = row.ProjectId,
            ProjectName = row.ProjectName,
            EditToken = state.EditToken,
            StageCode = stage.Code,
            StageName = stage.Name,
            PlannedStart = stage.PlannedStart,
            PlannedDue = stage.PlannedDue,
            ReturnUrl = WorkspaceRouteHelper.BriefingDeskItem(anchor),
            FullEditorUrl = WorkspaceRouteHelper.ProjectPlanEdit(row.ProjectId, stage.Code),
            StageLabel = StageLabel(row.CurrentStageCode, row.CurrentStageName),
            ProjectPageUrl = row.OpenUrl,
            ProjectTabName = ProjectManagement.Infrastructure.ProjectTab.Name(row.ProjectId)
        };
    }

    // Only a missing PDC asks anything of the PO. A plan already with the HoD is the PO's part
    // done; a PDC that has passed is a red note in the header, not a step.
    private static BriefingDeskCheckVm? ProjectPdcCheck(
        WorkspaceProjectMatrixRowVm row,
        DateTimeOffset? pendingPlanSinceUtc,
        string? panelId = null)
    {
        var code = row.CurrentStageCode;
        var planUrl = WorkspaceRouteHelper.ProjectPlanEdit(row.ProjectId, code);

        if (row.CurrentStagePdc is DateOnly pdc)
        {
            // A PDC that has passed is no concern on the desk: it is shown as a plain date.
            var remaining = row.DaysUntilCurrentStagePdc ?? 0;
            return new BriefingDeskCheckVm
            {
                Key = "pdc",
                Label = $"{code} PDC {FormatDay(pdc)}",
                Detail = remaining < 0
                    ? "The date has passed. Revise the plan only if it has changed."
                    : remaining == 0 ? "Due today." : $"{remaining} day{Plural(remaining)} remaining.",
                State = BriefingDeskCheckState.Done,
                Route = BriefingDeskRoute.Approval,
                ActionUrl = planUrl
            };
        }

        if (pendingPlanSinceUtc is DateTimeOffset since)
        {
            return new BriefingDeskCheckVm
            {
                Key = "pdc",
                Label = $"{code} PDC",
                Detail = "The plan is with the HoD. Nothing more is needed from you.",
                State = BriefingDeskCheckState.Submitted,
                Route = BriefingDeskRoute.Approval,
                StateText = $"Sent to HoD on {FormatDay(since)}",
                ActionUrl = planUrl
            };
        }

        return new BriefingDeskCheckVm
        {
            Key = "pdc",
            Label = $"Set {code} PDC",
            Detail = "No planned completion date for the current stage. You confirm the stage first.",
            State = BriefingDeskCheckState.Open,
            Route = BriefingDeskRoute.Approval,
            ActionText = "Set PDC",
            ActionUrl = planUrl,
            PanelId = panelId
        };
    }

    // Completed stages must carry their completion date. A pending date correction is the PO's
    // part done only for the stages whose completion date it proposes, and only while the HoD
    // can still approve it; one the HoD cannot approve is a step even with no gap.
    private static BriefingDeskCheckVm? ProjectCompletionDatesCheck(
        WorkspaceProjectMatrixRowVm row,
        BriefingDeskPendingCorrection? pending,
        bool ipaCompletionFromArpp = false)
    {
        // Record health reads the stored date; when an ARPP record supplies the IPA date the PO
        // cannot enter one, so that gap is never a step.
        var gaps = (row.RecordHealth?.GapDetails ?? Array.Empty<WorkspaceRecordGapVm>())
            .Where(IsCompletionDateGap)
            .Where(gap => !(ipaCompletionFromArpp && string.Equals(gap.StageCode, StageCodes.IPA, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var actionUrl = gaps.Select(gap => gap.ActionUrl).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url))
            ?? WorkspaceRouteHelper.ProjectTimelineActuals(row.ProjectId);

        // A correction the HoD cannot approve needs the PO whatever it proposes, including a
        // change to a date that is already recorded, so it is checked before the gaps.
        if (pending is { NeedsResubmission: true })
        {
            return new BriefingDeskCheckVm
            {
                Key = "completion",
                IsRequired = false,
                Label = "Review your date correction",
                Detail = "Official dates changed after you sent it. The HoD cannot approve it until you review it and send it again.",
                State = BriefingDeskCheckState.Open,
                Route = BriefingDeskRoute.Approval,
                ActionText = "Review",
                ActionUrl = actionUrl,
                RouteHint = "Opens the date editor"
            };
        }

        if (gaps.Count == 0)
        {
            return null;
        }

        var missing = pending is null
            ? gaps
            : gaps.Where(gap => gap.StageCode is not { } code || !pending.ProposedCompletionStageCodes.Contains(code)).ToList();

        if (missing.Count == 0)
        {
            return new BriefingDeskCheckVm
            {
                Key = "completion",
                IsRequired = false,
                Label = "Stage completion dates",
                Detail = "The date correction is with the HoD. Nothing more is needed from you.",
                State = BriefingDeskCheckState.Submitted,
                Route = BriefingDeskRoute.Approval,
                StateText = $"Sent to HoD on {FormatDay(pending!.SubmittedOn)}",
                ActionUrl = actionUrl
            };
        }

        var stages = string.Join(", ", missing.Select(ShortFieldLabel));
        return new BriefingDeskCheckVm
        {
            Key = "completion",
            IsRequired = false,
            Label = missing.Count == 1
                ? $"{ShortFieldLabel(missing[0])} completion date missing"
                : $"{missing.Count} stage completion dates missing",
            Detail = pending is not null
                ? $"Your date correction with the HoD does not include {stages}. Add {(missing.Count == 1 ? "it" : "them")} to it."
                : missing.Count == 1
                    ? "The stage is completed but its completion date is not recorded."
                    : $"Completed stages without a completion date: {stages}.",
            State = BriefingDeskCheckState.Open,
            Route = BriefingDeskRoute.Approval,
            ActionText = "Add dates",
            ActionUrl = actionUrl
        };
    }

    // SECTION: Record-health improvements
    // Gaps are grouped by the editor that fixes them. The current-stage PDC and stage completion
    // dates are excluded because the required checks above already cover them.
    internal static IReadOnlyList<BriefingDeskCheckVm> ProjectImprovements(
        WorkspaceRecordHealthVm? health,
        string? procurementPanelId = null,
        string? deskReturnUrl = null)
    {
        if (health is null)
        {
            return Array.Empty<BriefingDeskCheckVm>();
        }

        return health.GapDetails
            .Where(gap => !IsCurrentStageGap(gap))
            .Where(gap => !IsCompletionDateGap(gap))
            .Where(gap => !string.IsNullOrWhiteSpace(gap.ActionUrl))
            // Procurement is a required step of its own (ProjectProcurementCheck).
            .Where(gap => !IsProcurementEditor(gap.ActionUrl!))
            .GroupBy(gap => gap.ActionUrl, StringComparer.Ordinal)
            .Select(group =>
            {
                var gaps = group.OrderBy(gap => gap.Priority).ThenBy(gap => gap.FieldLabel).ToList();
                var points = (int)Math.Round(gaps.Sum(gap => Math.Max(0m, gap.MaximumPoints - gap.EarnedPoints)), MidpointRounding.AwayFromZero);
                var route = IsDirectEditor(group.Key) ? BriefingDeskRoute.Direct : BriefingDeskRoute.Approval;
                var fields = string.Join(", ", gaps.Take(4).Select(gap => ShortFieldLabel(gap)));
                if (gaps.Count > 4)
                {
                    fields += $" and {gaps.Count - 4} more";
                }

                return new BriefingDeskCheckVm
                {
                    Key = $"gap:{gaps[0].Code}",
                    Label = gaps.Count == 1 ? gaps[0].FieldLabel : $"{gaps.Count} {PluralComponent(gaps[0].Component)}",
                    Detail = gaps.Count == 1 ? AsSuggestion(gaps[0].Reason) : fields,
                    State = BriefingDeskCheckState.Open,
                    Route = route,
                    IsRequired = false,
                    ActionText = gaps[0].ActionText,
                    // Photo and video uploads come back to the desk when given its address.
                    ActionUrl = IsMediaEditor(group.Key) ? WithReturnUrl(group.Key, deskReturnUrl) : group.Key,
                    // Procurement opens in a panel on the desk when its editor model is loaded.
                    PanelId = IsProcurementEditor(group.Key) ? procurementPanelId : null,
                    Points = points
                };
            })
            .OrderByDescending(item => item.Points)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Record health explains each gap as a requirement of the record ("… are required."). On the
    // desk these gaps are optional, so the same reason is put as a recommendation.
    internal static string AsSuggestion(string reason) => reason
        .Replace(" are required", " are recommended", StringComparison.Ordinal)
        .Replace(" is required", " is recommended", StringComparison.Ordinal)
        .Replace("Required for", "Recommended for", StringComparison.Ordinal)
        .Replace(" must ", " should ", StringComparison.Ordinal);

    // Procurement figures and project media are edited directly by the assigned PO; other
    // editors (project details, documents, historical dates) submit for HoD approval.
    internal static bool IsProcurementEditor(string actionUrl)
        => actionUrl.Contains("oc=procurement", StringComparison.OrdinalIgnoreCase);

    internal static bool IsMediaEditor(string actionUrl)
        => actionUrl.Contains("/Photos", StringComparison.OrdinalIgnoreCase)
            || actionUrl.Contains("/Videos", StringComparison.OrdinalIgnoreCase);

    internal static bool IsDirectEditor(string actionUrl)
        => IsProcurementEditor(actionUrl) || IsMediaEditor(actionUrl);

    private static string WithReturnUrl(string url, string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl))
        {
            return url;
        }

        var hash = url.IndexOf('#');
        var path = hash < 0 ? url : url[..hash];
        var fragment = hash < 0 ? string.Empty : url[hash..];
        var separator = path.Contains('?') ? '&' : '?';
        return $"{path}{separator}returnUrl={Uri.EscapeDataString(returnUrl)}{fragment}";
    }

    private static string ShortFieldLabel(WorkspaceRecordGapVm gap)
        => gap.FieldLabel.EndsWith(" — Actual Completion", StringComparison.Ordinal)
            ? gap.FieldLabel[..^" — Actual Completion".Length]
            : gap.FieldLabel;

    private static string PluralComponent(string component) => component switch
    {
        "Procurement at a glance" => "procurement entries missing",
        "Historical stage completion dates" => "stage completion dates missing",
        "Supporting media" => "media items missing",
        "Published project documents" => "document items missing",
        _ => $"{component.ToLowerInvariant()} items missing"
    };

    // The stage tag names the stage as well as its code, since a code alone ("PNC") means
    // little on a phone, where there is no hover title.
    internal static string StageLabel(string code, string? name)
        => string.IsNullOrWhiteSpace(name) || string.Equals(name, code, StringComparison.OrdinalIgnoreCase) || name == "Not started"
            ? code
            : $"{code} · {name}";

    private static IReadOnlyList<BriefingDeskFactVm> ProjectFacts(
        WorkspaceProjectMatrixRowVm row,
        bool hasStage,
        DateTimeOffset? pendingPlanSinceUtc = null,
        bool pdcCovered = false)
    {
        var facts = new List<BriefingDeskFactVm>();
        var stage = StageLabel(row.CurrentStageCode, row.CurrentStageName);
        if (!hasStage)
        {
            facts.Add(new BriefingDeskFactVm("No current stage", BriefingDeskTone.Warning, Key: BriefingDeskFactKeys.Stage));
        }
        else if (row.IsCurrentStageNotStarted)
        {
            facts.Add(new BriefingDeskFactVm($"{stage} · not started", BriefingDeskTone.Warning, Key: BriefingDeskFactKeys.Stage));
        }
        else if (row.DaysInCurrentStage is int days)
        {
            facts.Add(new BriefingDeskFactVm($"{stage} · {days} day{Plural(days)}", BriefingDeskTone.Neutral, Key: BriefingDeskFactKeys.Stage));
        }
        else
        {
            facts.Add(new BriefingDeskFactVm(stage, BriefingDeskTone.Neutral, Key: BriefingDeskFactKeys.Stage));
        }

        // A plan with the HoD is the PO's answer to a missing or passed PDC, so the header says
        // it was sent instead of raising an alarm.
        var planWithHod = pendingPlanSinceUtc is DateTimeOffset since
            && (row.HasOverdueCurrentStage || row.CurrentStagePdc is null)
            ? $"sent to HoD on {FormatDay(since)}"
            : null;

        if (hasStage)
        {
            if (planWithHod is not null)
            {
                facts.Add(new BriefingDeskFactVm(
                    row.CurrentStagePdc is null ? $"PDC {planWithHod}" : $"Revised PDC {planWithHod}",
                    BriefingDeskTone.Neutral,
                    CoveredByStep: pdcCovered, Key: BriefingDeskFactKeys.Pdc));
            }
            else if (row.CurrentStagePdc is DateOnly pdc)
            {
                facts.Add(new BriefingDeskFactVm($"PDC {FormatDay(pdc)}", BriefingDeskTone.Neutral, CoveredByStep: pdcCovered, Key: BriefingDeskFactKeys.Pdc));
            }
            else
            {
                facts.Add(new BriefingDeskFactVm("PDC not set", BriefingDeskTone.Warning, CoveredByStep: pdcCovered, Key: BriefingDeskFactKeys.Pdc));
            }
        }

        // Green under 7 days, amber from 7, red from 10 (not ready for conference).
        facts.Add(row.DaysSinceLastPoRemark switch
        {
            null => new BriefingDeskFactVm("No remark yet", BriefingDeskTone.Danger),
            0 => new BriefingDeskFactVm("Remark today", BriefingDeskTone.Good),
            var value when value < UpdateDueSoonDays => new BriefingDeskFactVm($"Remark {value} day{Plural(value!.Value)} ago", BriefingDeskTone.Good),
            var value when value < UpdateWindowDays => new BriefingDeskFactVm($"Remark {value} days ago", BriefingDeskTone.Warning),
            var value => new BriefingDeskFactVm($"Remark {value} days ago", BriefingDeskTone.Danger)
        } with { CoveredByStep = true, Key = BriefingDeskFactKeys.Remark }); // the update step and the box repeat it

        // Record health is optional, so it is not a header tag: "Improve the record" shows it
        // ("48% now") with the gaps that would raise it.
        return facts;
    }

    private static IReadOnlyList<BriefingDeskStepVm> ProjectStrip(
        IReadOnlyList<BriefingDeskStageInput> stages,
        string currentStageCode)
        => stages
            .OrderBy(stage => stage.SortOrder)
            .Select(stage =>
            {
                var isCurrent = string.Equals(stage.Code, currentStageCode, StringComparison.OrdinalIgnoreCase);
                var state = isCurrent
                    ? stage.Status == StageStatus.Blocked ? BriefingDeskStepState.Blocked : BriefingDeskStepState.Current
                    : stage.Status is StageStatus.Completed or StageStatus.Skipped
                        ? BriefingDeskStepState.Past
                        : BriefingDeskStepState.Future;
                return new BriefingDeskStepVm(stage.Code, stage.Name, state);
            })
            .ToList();

    // SECTION: Recently completed projects
    // Only an unanswered conference direction asks for anything; the weekly update and
    // timeline rules stop once a project is complete.
    private static BriefingDeskItemVm BuildCompletedProject(OfficerConferenceItemVm conference)
    {
        // The Conference view always adds a "Project Officer" entry once there is a direction,
        // holding only placeholder text until the PO replies; only a real remark answers it.
        var directionAwaitingProgress = conference.LatestDirection is not null
            && !conference.ProgressEntries.Any(IsProjectOfficerProgress);
        var checks = directionAwaitingProgress
            ? new[]
            {
                new BriefingDeskCheckVm
                {
                    Key = "update",
                    Label = "Report progress on the direction",
                    Detail = "No progress recorded since the latest direction.",
                    State = BriefingDeskCheckState.Open,
                    Route = BriefingDeskRoute.Update
                }
            }
            : Array.Empty<BriefingDeskCheckVm>();

        var facts = new List<BriefingDeskFactVm>
        {
            new(string.IsNullOrWhiteSpace(conference.CurrentContext) ? "Completed" : conference.CurrentContext!, BriefingDeskTone.Good)
        };
        return new BriefingDeskItemVm
        {
            Kind = ConferenceItemKind.Project,
            ItemId = conference.ItemId,
            Title = conference.Title,
            OpenUrl = conference.OpenUrl,
            Facts = facts,
            Direction = conference.LatestDirection,
            DirectionAwaitingProgress = directionAwaitingProgress,
            LastUpdateText = "Completed projects stay in your conference review for a while after completion.",
            Checks = checks,
            RecordHealthPercent = conference.RecordHealth?.HealthPercent,
            Conference = conference
        };
    }

    internal const string ProjectOfficerProgressLabel = "Project Officer";

    private static bool IsProjectOfficerProgress(ConferenceProgressEntryVm entry)
        => string.Equals(entry.Label, ProjectOfficerProgressLabel, StringComparison.Ordinal)
            && (entry.ActivityAtUtc.HasValue || !string.IsNullOrWhiteSpace(entry.Body));

    // SECTION: Ideas
    // Ideas matter less than projects and tasks: an idea needs the PO only when a conference
    // direction is waiting for a note. Otherwise a note is optional and there is no time limit.
    private static BriefingDeskItemVm BuildIdea(
        WorkspaceIdeaVm idea,
        DateOnly today,
        OfficerConferenceItemVm? conference,
        bool directionAwaitingProgress)
    {
        var daysSinceActivity = Math.Max(0, today.DayNumber - WorkspaceNudgeService.ToIstDate(idea.LastActivityAtUtc).DayNumber);
        var activityText = daysSinceActivity switch
        {
            0 => "today",
            1 => "yesterday",
            _ => $"{daysSinceActivity} days ago"
        };
        var check = new BriefingDeskCheckVm
        {
            Key = "update",
            Label = directionAwaitingProgress ? "Reply to the conference direction" : "Note on progress",
            NextText = directionAwaitingProgress ? "Direction" : "Note",
            Detail = directionAwaitingProgress
                ? "No note since the latest direction. Your note is the reply."
                : $"Optional. Last activity {activityText}.",
            State = directionAwaitingProgress ? BriefingDeskCheckState.Open : BriefingDeskCheckState.Done,
            Route = BriefingDeskRoute.Update
        };

        return new BriefingDeskItemVm
        {
            Kind = ConferenceItemKind.ProjectIdea,
            ItemId = idea.IdeaId,
            Title = idea.Title,
            OpenUrl = idea.OpenUrl,
            Facts = new[]
            {
                new BriefingDeskFactVm(idea.Status, idea.Status == "On Hold" ? BriefingDeskTone.Warning : BriefingDeskTone.Neutral),
                new BriefingDeskFactVm($"Last activity {activityText}", BriefingDeskTone.Neutral, CoveredByStep: true),
                // "0 documents" says nothing, so the count shows only when there are some.
                idea.DocumentCount > 0 ? new BriefingDeskFactVm($"{idea.DocumentCount} document{Plural(idea.DocumentCount)}", BriefingDeskTone.Neutral) : null
            }
            .OfType<BriefingDeskFactVm>()
            .ToList(),
            Direction = conference?.LatestDirection,
            DirectionAwaitingProgress = directionAwaitingProgress,
            LastUpdateText = $"Last activity on this idea was {activityText}.",
            Checks = new[] { check },
            Conference = conference
        };
    }

    // SECTION: Tasks
    // Brief-ready = progress within the weekly window, the latest direction answered, and an
    // overdue task either submitted for closure or flagged blocked with a reason.
    private static BriefingDeskItemVm BuildTask(
        BriefingDeskTaskInput input,
        DateOnly today,
        OfficerConferenceItemVm? conference,
        bool directionAwaitingProgress)
    {
        var task = input.Task;
        var status = task.Status;
        var isSubmitted = Is(status, ActionTaskStatuses.Submitted);
        var isBlocked = Is(status, ActionTaskStatuses.Blocked);
        int? daysSinceProgress = input.LastProgressAtUtc is DateTime last
            ? Math.Max(0, today.DayNumber - WorkspaceNudgeService.ToIstDate(last).DayNumber)
            : null;
        var checks = new List<BriefingDeskCheckVm>();

        if (isSubmitted)
        {
            checks.Add(new BriefingDeskCheckVm
            {
                Key = "closure",
                Label = "Submitted for closure",
                Detail = "The assigner closes the task or returns it. Nothing more is needed from you.",
                State = BriefingDeskCheckState.Submitted,
                Route = BriefingDeskRoute.Approval,
                StateText = task.SubmittedOnUtc is DateTime submittedOn
                    ? $"Sent to assigner on {FormatDay(new DateTimeOffset(DateTime.SpecifyKind(submittedOn, DateTimeKind.Utc)))}"
                    : "Sent to assigner"
            });
        }
        else
        {
            var progressFresh = daysSinceProgress is int days && days <= TaskProgressWindowDays;
            checks.Add(new BriefingDeskCheckVm
            {
                Key = "update",
                Label = directionAwaitingProgress ? "Report progress on the direction" : "Progress update",
                NextText = directionAwaitingProgress ? "Direction" : "Progress",
                Detail = directionAwaitingProgress
                    ? "No progress recorded since the latest direction."
                    : daysSinceProgress switch
                    {
                        null => $"Required every {TaskProgressWindowDays} days. No progress recorded yet.",
                        0 => "Posted today.",
                        var value when progressFresh => $"Posted {value} day{Plural(value!.Value)} ago.",
                        var value => $"Last progress {value} days ago. Required every {TaskProgressWindowDays} days."
                    },
                State = !directionAwaitingProgress && progressFresh ? BriefingDeskCheckState.Done : BriefingDeskCheckState.Open,
                Route = BriefingDeskRoute.Update
            });

            if (isBlocked)
            {
                checks.Add(new BriefingDeskCheckVm
                {
                    Key = "due",
                    Label = "Flagged blocked",
                    Detail = "The blocker is shown in conference. Submit it once the blocker clears.",
                    State = BriefingDeskCheckState.Done,
                    Route = BriefingDeskRoute.Direct
                });
            }
            else if (task.IsOverdue)
            {
                var overdueDays = task.DaysOverdue ?? 0;
                checks.Add(new BriefingDeskCheckVm
                {
                    Key = "due",
                    Label = $"Overdue by {overdueDays} day{Plural(overdueDays)}",
                    Detail = "Submit it for closure, or flag it blocked with the reason.",
                    State = BriefingDeskCheckState.Open,
                    Route = BriefingDeskRoute.Direct
                });
            }
        }

        var dueDays = task.DueDate.DayNumber - today.DayNumber;
        // An overdue task that is not blocked has an "Overdue by" step, so the open card shows it once.
        var dueFact = task.IsOverdue && !isSubmitted
            ? new BriefingDeskFactVm($"Due {FormatDay(task.DueDate)} · overdue {task.DaysOverdue ?? 0} day{Plural(task.DaysOverdue ?? 0)}", BriefingDeskTone.Danger, CoveredByStep: !isBlocked)
            : dueDays is >= 0 and <= 3 && !isSubmitted
                ? new BriefingDeskFactVm(dueDays == 0 ? $"Due today" : $"Due {FormatDay(task.DueDate)} · in {dueDays} day{Plural(dueDays)}", BriefingDeskTone.Warning)
                : new BriefingDeskFactVm($"Due {FormatDay(task.DueDate)}", BriefingDeskTone.Neutral);
        var isHighPriority = task.Priority.Equals("High", StringComparison.OrdinalIgnoreCase)
            || task.Priority.Equals("Urgent", StringComparison.OrdinalIgnoreCase)
            || task.Priority.Equals("Critical", StringComparison.OrdinalIgnoreCase);

        return new BriefingDeskItemVm
        {
            Kind = ConferenceItemKind.ActionTask,
            ItemId = task.TaskId,
            Title = task.Title,
            OpenUrl = task.OpenUrl,
            Facts = new[]
            {
                dueFact,
                new BriefingDeskFactVm($"{task.Priority} priority", isHighPriority ? BriefingDeskTone.Warning : BriefingDeskTone.Neutral)
            },
            Strip = TaskStrip(status),
            Direction = conference?.LatestDirection,
            DirectionAwaitingProgress = directionAwaitingProgress,
            LastUpdateText = daysSinceProgress switch
            {
                null => "You have not posted progress on this task yet.",
                0 => "Your last progress was today.",
                1 => "Your last progress was yesterday.",
                var value => $"Your last progress was {value} days ago."
            },
            Checks = checks,
            TaskRowVersion = input.RowVersion,
            CanSubmitTask = ActionTaskStatusWorkflow.CanSubmitFromStatus(status),
            CanFlagTaskBlocked = ActionTaskStatusWorkflow.IsAllowedTransition(status, ActionTaskStatuses.Blocked) && !isBlocked,
            Conference = conference
        };
    }

    private static IReadOnlyList<BriefingDeskStepVm> TaskStrip(string status)
    {
        var flow = new[] { ActionTaskStatuses.Assigned, ActionTaskStatuses.InProgress, ActionTaskStatuses.Submitted, ActionTaskStatuses.Closed };
        var isBlocked = Is(status, ActionTaskStatuses.Blocked);
        var currentIndex = isBlocked ? 1 : Array.FindIndex(flow, step => Is(step, status));

        return flow
            .Select((step, index) =>
            {
                if (index == currentIndex)
                {
                    return isBlocked
                        ? new BriefingDeskStepVm("Blocked", "Blocked", BriefingDeskStepState.Blocked)
                        : new BriefingDeskStepVm(step, step, BriefingDeskStepState.Current);
                }

                return new BriefingDeskStepVm(step, step, index < currentIndex ? BriefingDeskStepState.Past : BriefingDeskStepState.Future);
            })
            .ToList();
    }

    // SECTION: Formatting helpers
    private static bool Is(string value, string expected)
        => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static string Plural(int value) => value == 1 ? string.Empty : "s";

    internal static string FormatDay(DateOnly date)
        => date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    private static string FormatDay(DateTimeOffset value)
        => FormatDay(DateOnly.FromDateTime(ProjectManagement.Infrastructure.IstClock.ToIst(value.UtcDateTime)));
}
