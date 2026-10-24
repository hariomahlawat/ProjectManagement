using System.Globalization;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
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
    DateTimeOffset? PendingActualsSinceUtc = null,
    ProcurementEditVm? ProcurementEdit = null,
    PlanEditorVm? PlanEditor = null);

public sealed record BriefingDeskStageInput(string Code, string Name, StageStatus Status, int SortOrder);

public sealed record BriefingDeskIdeaInput(WorkspaceIdeaVm Idea);

public sealed record BriefingDeskTaskInput(
    WorkspaceTaskVm Task,
    DateTime? LastProgressAtUtc,
    string RowVersion);

public static class BriefingDeskBuilder
{
    // SECTION: Rule constants
    // A project update older than this many days leaves the project not brief-ready. It
    // matches the weekly update rhythm in WorkspaceNudgeService.GetUpdateStatus.
    public const int UpdateWindowDays = 7;

    // Task progress follows the same weekly rhythm as project updates.
    public const int TaskProgressWindowDays = 7;

    private const string CurrentStageComponent = "Current-stage timeline (PDC)";

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
    // answered, and a current-stage PDC that is set and not overdue (or submitted to the HoD).
    // Record gaps are offered as improvements and never block readiness. Work the HoD returned
    // is not listed: a return is a finished decision, which the PO learns of from the
    // notification and the editor's banner; the desk shows only what is still pending.
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

        var checks = new List<BriefingDeskCheckVm> { ProjectUpdateCheck(row, conference, directionAwaitingProgress) };

        var planPanel = hasStage && !planDirectSaveRole ? ShortPlanPanel(input, row, anchor) : null;
        if (hasStage)
        {
            checks.Add(ProjectPdcCheck(row, input.PendingPlanSinceUtc, planPanel?.PanelId));
            if (row.IsCurrentStageStartMissing)
            {
                checks.Add(ProjectStartCheck(row, input.PendingActualsSinceUtc));
            }
        }

        var procurementPanelId = input.ProcurementEdit is null ? null : $"{anchor}-procurement";
        var improvements = ProjectImprovements(row.RecordHealth, procurementPanelId, WorkspaceRouteHelper.BriefingDeskItem(anchor));

        return new BriefingDeskItemVm
        {
            Kind = ConferenceItemKind.Project,
            ItemId = row.ProjectId,
            Title = row.ProjectName,
            OpenUrl = row.OpenUrl,
            Facts = ProjectFacts(row, hasStage, input.PendingPlanSinceUtc),
            Strip = ProjectStrip(input.Stages, row.CurrentStageCode),
            Direction = conference?.LatestDirection,
            DirectionAwaitingProgress = directionAwaitingProgress,
            LastUpdateText = row.DaysSinceLastPoRemark switch
            {
                null => "You have not posted an update on this project yet.",
                0 => "Your last update was today.",
                1 => "Your last update was yesterday.",
                var days => $"Your last update was {days} days ago."
            },
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

    private static BriefingDeskCheckVm ProjectUpdateCheck(
        WorkspaceProjectMatrixRowVm row,
        OfficerConferenceItemVm? conference,
        bool directionAwaitingProgress)
    {
        var fresh = row.DaysSinceLastPoRemark is int days && days <= UpdateWindowDays;
        if (directionAwaitingProgress)
        {
            var issued = conference?.LatestDirection?.CreatedAtUtc;
            return new BriefingDeskCheckVm
            {
                Key = "update",
                Label = "Report progress on the direction",
                Detail = issued is DateTime issuedAt
                    ? $"No progress recorded since the direction of {FormatDay(WorkspaceNudgeService.ToIstDate(issuedAt))}."
                    : "No progress recorded since the latest direction.",
                State = BriefingDeskCheckState.Open,
                Route = BriefingDeskRoute.Update
            };
        }

        return new BriefingDeskCheckVm
        {
            Key = "update",
            Label = "This week’s update",
            Detail = row.DaysSinceLastPoRemark switch
            {
                null => $"Required every {UpdateWindowDays} days. No update recorded yet.",
                var value when fresh => value == 0 ? "Posted today." : $"Posted {value} day{Plural(value!.Value)} ago.",
                var value => $"Last update {value} days ago. Required every {UpdateWindowDays} days."
            },
            State = fresh ? BriefingDeskCheckState.Done : BriefingDeskCheckState.Open,
            Route = BriefingDeskRoute.Update
        };
    }

    // SECTION: Current-stage plan form
    // The short form posts only the current stage's row to the plan editor's own handler,
    // which then validates and submits the whole plan exactly as the full editor does. It is
    // offered only when nothing else would ride along: no private draft, no pending or locked
    // submission, and a current stage that is still plannable.
    internal static BriefingDeskPlanPanelVm? ShortPlanPanel(
        BriefingDeskProjectInput input,
        WorkspaceProjectMatrixRowVm row,
        string anchor)
    {
        var editor = input.PlanEditor;
        var needsPdc = row.HasOverdueCurrentStage || row.CurrentStagePdc is null;
        if (editor is null || !needsPdc || input.PendingPlanSinceUtc is not null)
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
            IsOverdue = row.HasOverdueCurrentStage,
            ReturnUrl = WorkspaceRouteHelper.BriefingDeskItem(anchor),
            FullEditorUrl = WorkspaceRouteHelper.ProjectPlanEdit(row.ProjectId, stage.Code)
        };
    }

    private static BriefingDeskCheckVm ProjectPdcCheck(
        WorkspaceProjectMatrixRowVm row,
        DateTimeOffset? pendingPlanSinceUtc,
        string? panelId = null)
    {
        var code = row.CurrentStageCode;
        var planUrl = WorkspaceRouteHelper.ProjectPlanEdit(row.ProjectId, code);

        // A plan awaiting approval is the PO's answer to a missing or overdue PDC.
        if (pendingPlanSinceUtc is DateTimeOffset since && (row.HasOverdueCurrentStage || row.CurrentStagePdc is null))
        {
            return new BriefingDeskCheckVm
            {
                Key = "pdc",
                Label = $"{code} planned dates",
                Detail = "Your plan is awaiting HoD approval. Official dates change when it is approved.",
                State = BriefingDeskCheckState.Waiting,
                Route = BriefingDeskRoute.Approval,
                StateText = $"With HoD since {FormatDay(since)}",
                ActionUrl = planUrl
            };
        }

        if (row.HasOverdueCurrentStage && row.CurrentStagePdc is DateOnly overduePdc)
        {
            var overdueDays = Math.Max(0, -(row.DaysUntilCurrentStagePdc ?? 0));
            return new BriefingDeskCheckVm
            {
                Key = "pdc",
                Label = $"Revise {code} PDC",
                Detail = $"PDC was {FormatDay(overduePdc)}, {overdueDays} day{Plural(overdueDays)} ago. Propose a revised date.",
                State = BriefingDeskCheckState.Open,
                Route = BriefingDeskRoute.Approval,
                ActionText = "Propose date",
                ActionUrl = planUrl,
                PanelId = panelId
            };
        }

        if (row.CurrentStagePdc is not DateOnly pdc)
        {
            return new BriefingDeskCheckVm
            {
                Key = "pdc",
                Label = $"Set {code} planned dates",
                Detail = "The current stage has no planned completion date (PDC).",
                State = BriefingDeskCheckState.Open,
                Route = BriefingDeskRoute.Approval,
                ActionText = "Propose dates",
                ActionUrl = planUrl,
                PanelId = panelId
            };
        }

        var remaining = row.DaysUntilCurrentStagePdc ?? 0;
        return new BriefingDeskCheckVm
        {
            Key = "pdc",
            Label = $"{code} PDC {FormatDay(pdc)}",
            Detail = remaining == 0 ? "Due today." : $"{remaining} day{Plural(remaining)} remaining.",
            State = BriefingDeskCheckState.Done,
            Route = BriefingDeskRoute.Approval,
            ActionUrl = planUrl
        };
    }

    private static BriefingDeskCheckVm ProjectStartCheck(WorkspaceProjectMatrixRowVm row, DateTimeOffset? pendingActualsSinceUtc)
    {
        var code = row.CurrentStageCode;
        var actualsUrl = WorkspaceRouteHelper.ProjectTimelineActuals(row.ProjectId);
        if (pendingActualsSinceUtc is DateTimeOffset since)
        {
            return new BriefingDeskCheckVm
            {
                Key = "start",
                Label = $"{code} actual start",
                Detail = "Your date correction is awaiting HoD approval.",
                State = BriefingDeskCheckState.Waiting,
                Route = BriefingDeskRoute.Approval,
                StateText = $"With HoD since {FormatDay(since)}",
                ActionUrl = actualsUrl
            };
        }

        return new BriefingDeskCheckVm
        {
            Key = "start",
            Label = $"Record {code} actual start",
            Detail = "The current stage is in progress but has no start date.",
            State = BriefingDeskCheckState.Open,
            Route = BriefingDeskRoute.Approval,
            ActionText = "Propose date",
            ActionUrl = actualsUrl
        };
    }

    // SECTION: Record-health improvements
    // Gaps are grouped by the editor that fixes them. The current-stage PDC is excluded
    // because the required PDC check above already covers it.
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
            .Where(gap => !string.Equals(gap.Component, CurrentStageComponent, StringComparison.Ordinal))
            .Where(gap => !string.IsNullOrWhiteSpace(gap.ActionUrl))
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
                    Detail = gaps.Count == 1 ? gaps[0].Reason : fields,
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

    private static IReadOnlyList<BriefingDeskFactVm> ProjectFacts(
        WorkspaceProjectMatrixRowVm row,
        bool hasStage,
        DateTimeOffset? pendingPlanSinceUtc = null)
    {
        var facts = new List<BriefingDeskFactVm>();
        if (!hasStage)
        {
            facts.Add(new BriefingDeskFactVm("No current stage", BriefingDeskTone.Warning));
        }
        else if (row.IsCurrentStageNotStarted)
        {
            facts.Add(new BriefingDeskFactVm($"{row.CurrentStageCode} not started", BriefingDeskTone.Warning));
        }
        else if (row.DaysInCurrentStage is int days)
        {
            facts.Add(new BriefingDeskFactVm($"{days} day{Plural(days)} in {row.CurrentStageCode}", BriefingDeskTone.Neutral));
        }
        else
        {
            facts.Add(new BriefingDeskFactVm(row.CurrentStageCode, BriefingDeskTone.Neutral));
        }

        // A revised plan with the HoD is the PO's answer to a missing or overdue PDC (the PDC
        // check counts it as done), so the header says so instead of raising an alarm.
        var planWithHod = pendingPlanSinceUtc is DateTimeOffset since
            && (row.HasOverdueCurrentStage || row.CurrentStagePdc is null)
            ? $"with HoD since {FormatDay(since)}"
            : null;

        if (hasStage)
        {
            if (planWithHod is not null)
            {
                facts.Add(new BriefingDeskFactVm(
                    row.CurrentStagePdc is null ? $"PDC {planWithHod}" : $"Revised PDC {planWithHod}",
                    BriefingDeskTone.Neutral));
            }
            else if (row.HasOverdueCurrentStage && row.CurrentStagePdc is DateOnly overdue)
            {
                var overdueDays = Math.Max(0, -(row.DaysUntilCurrentStagePdc ?? 0));
                facts.Add(new BriefingDeskFactVm($"PDC {FormatDay(overdue)} · overdue {overdueDays} day{Plural(overdueDays)}", BriefingDeskTone.Danger));
            }
            else if (row.CurrentStagePdc is DateOnly pdc)
            {
                facts.Add(new BriefingDeskFactVm($"PDC {FormatDay(pdc)}", BriefingDeskTone.Neutral));
            }
            else
            {
                facts.Add(new BriefingDeskFactVm("PDC not set", BriefingDeskTone.Warning));
            }
        }

        facts.Add(row.DaysSinceLastPoRemark switch
        {
            null => new BriefingDeskFactVm("No update yet", BriefingDeskTone.Danger),
            0 => new BriefingDeskFactVm("Updated today", BriefingDeskTone.Good),
            var value when value <= UpdateWindowDays => new BriefingDeskFactVm($"Updated {value} day{Plural(value!.Value)} ago", BriefingDeskTone.Good),
            var value => new BriefingDeskFactVm($"Updated {value} days ago", value > 10 ? BriefingDeskTone.Danger : BriefingDeskTone.Warning)
        });

        facts.Add(new BriefingDeskFactVm(
            $"Records {row.RecordHealthPercent}%",
            row.RecordHealthPercent >= 80 ? BriefingDeskTone.Good : BriefingDeskTone.Neutral));

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
        if (conference.RecordHealth is { } health)
        {
            facts.Add(new BriefingDeskFactVm($"Records {health.HealthPercent}%", health.HealthPercent >= 80 ? BriefingDeskTone.Good : BriefingDeskTone.Neutral));
        }

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
    // Brief-ready = recent activity (the existing idea staleness rule) and the latest
    // conference direction answered with a note.
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
        var open = directionAwaitingProgress || idea.NeedsUpdate;
        var check = new BriefingDeskCheckVm
        {
            Key = "update",
            Label = directionAwaitingProgress ? "Report progress on the direction" : "Note on progress",
            Detail = directionAwaitingProgress
                ? "No note since the latest direction."
                : idea.NeedsUpdate
                    ? $"Last activity {activityText}. Ideas need a note every 15 days."
                    : $"Last activity {activityText}.",
            State = open ? BriefingDeskCheckState.Open : BriefingDeskCheckState.Done,
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
                new BriefingDeskFactVm($"Last activity {activityText}", idea.NeedsUpdate ? BriefingDeskTone.Warning : BriefingDeskTone.Neutral),
                new BriefingDeskFactVm($"{idea.DocumentCount} document{Plural(idea.DocumentCount)}", BriefingDeskTone.Neutral)
            },
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
                Detail = "The assigner closes the task or returns it for further action.",
                State = BriefingDeskCheckState.Waiting,
                Route = BriefingDeskRoute.Approval,
                StateText = "With assigner"
            });
        }
        else
        {
            var progressFresh = daysSinceProgress is int days && days <= TaskProgressWindowDays;
            checks.Add(new BriefingDeskCheckVm
            {
                Key = "update",
                Label = directionAwaitingProgress ? "Report progress on the direction" : "Progress update",
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
        var dueFact = task.IsOverdue && !isSubmitted
            ? new BriefingDeskFactVm($"Due {FormatDay(task.DueDate)} · overdue {task.DaysOverdue ?? 0} day{Plural(task.DaysOverdue ?? 0)}", BriefingDeskTone.Danger)
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
