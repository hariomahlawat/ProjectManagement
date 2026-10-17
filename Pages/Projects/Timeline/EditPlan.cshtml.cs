using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Scheduling;
using ProjectManagement.Services.Stages;
using ProjectManagement.Utilities;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects.Timeline;

[Authorize(Roles = "Admin,Project Officer,HoD")]
[AutoValidateAntiforgeryToken]
public class EditPlanModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly PlanGenerationService _planGeneration;
    private readonly PlanDraftService _planDraft;
    private readonly PlanApprovalService _planApproval;
    private readonly ILogger<EditPlanModel> _logger;
    private readonly IUserContext _userContext;
    private readonly IOfficeCalendarService? _officeCalendar;
    private readonly IPlannedTimelineValidationService _timelineValidation;

    public EditPlanModel(
        ApplicationDbContext db,
        IAuditService audit,
        PlanGenerationService planGeneration,
        PlanDraftService planDraft,
        PlanApprovalService planApproval,
        ILogger<EditPlanModel> logger,
        IUserContext userContext,
        IOfficeCalendarService? officeCalendar = null,
        IPlannedTimelineValidationService? timelineValidation = null)
    {
        _db = db;
        _audit = audit;
        _planGeneration = planGeneration;
        _planDraft = planDraft;
        _planApproval = planApproval;
        _logger = logger;
        _userContext = userContext;
        _officeCalendar = officeCalendar;
        _timelineValidation = timelineValidation ?? new PlannedTimelineValidationService(
            db,
            new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider()),
            officeCalendar ?? new OfficeCalendarService(db));
    }

    [BindProperty]
    public PlanEditInput Input { get; set; } = new();

    public IActionResult OnGet(int id) => NotFound();

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (IsAutosaveRequest && !string.Equals(Input.Mode, PlanEditorModes.Durations, StringComparison.OrdinalIgnoreCase))
        {
            return AutosaveFailure("Autosave is only supported for Duration working inputs.", 400);
        }

        if (id != Input.ProjectId)
        {
            return RejectPlanRequest(id, "Unable to process the request. Please reload and try again.");
        }

        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return IsAutosaveRequest ? AutosaveFailure("You are not authorised to edit this plan.", 403) : Forbid();
        }

        var principal = _userContext.User;
        var isAdmin = principal.IsInRole(RoleNames.Admin);
        var isHoD = principal.IsInRole(RoleNames.HoD);
        var canDirectApply = isAdmin || isHoD;

        var project = await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return IsAutosaveRequest ? AutosaveFailure("The project was not found.", 404) : NotFound();
        }

        var workflowVersion = project.WorkflowVersion;

        var isProjectsHod = isHoD;
        var isProjectsPo = string.Equals(project.LeadPoUserId, userId, StringComparison.OrdinalIgnoreCase);

        if (!isAdmin && !isProjectsPo && !isProjectsHod)
        {
            _logger.LogWarning("User {UserId} attempted to edit plan for project {ProjectId} without permission.", userId, id);
            return IsAutosaveRequest ? AutosaveFailure("You are not authorised to edit this plan.", 403) : Forbid();
        }

        // Validate request intent before either handler can retrieve/create a draft
        // or change working inputs, official dates, or approval state.
        var intent = ParsePlanIntent(Input.Action);
        if (intent is null || !IsAllowedPlanIntent(Input.Mode, canDirectApply, intent.Value))
        {
            return RejectPlanRequest(id, "Choose a valid planner action for your role and mode.");
        }

        try
        {
            if (string.Equals(Input.Mode, PlanEditorModes.Durations, StringComparison.OrdinalIgnoreCase))
            {
                return await HandleDurationsAsync(id, userId, cancellationToken, workflowVersion, intent.Value);
            }

            return await HandleExactAsync(id, userId, principal, cancellationToken, workflowVersion, canDirectApply, intent.Value);
        }
        catch (DbUpdateConcurrencyException ex) when (intent == PlanIntent.SaveChanges)
        {
            // Handler transaction scopes have rolled back before control reaches
            // this boundary, including any earlier generation/draft/audit writes.
            _db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Concurrent plan publication rejected for Project {ProjectId}.", id);
            TempData[PlanEditorRecovery.TempDataKey] = PlanEditorRecovery.Serialize(Input, userId);
            return RejectPlanRequest(id, "The project plan changed while you were saving. Review the current plan and try again.", 409);
        }
    }

    public async Task<IActionResult> OnGetValidateAsync(int id, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var project = await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        var principal = _userContext.User;
        var isAdmin = principal.IsInRole("Admin");
        var isHoD = principal.IsInRole("HoD");
        var isProjectsHod = isHoD;
        var isProjectsPo = string.Equals(project.LeadPoUserId, userId, StringComparison.OrdinalIgnoreCase);

        if (!isAdmin && !isProjectsPo && !isProjectsHod)
        {
            return Forbid();
        }

        var draft = await _planDraft.GetMyDraftAsync(id, cancellationToken);
        if (draft is null)
        {
            return new JsonResult(new { ok = true, errors = Array.Empty<string>() });
        }

        var errors = await _planApproval.GetValidationErrorsAsync(draft.Id, cancellationToken);
        return new JsonResult(new { ok = errors.Count == 0, errors });
    }

    public async Task<IActionResult> OnPostDeleteDraftAsync(int id, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var project = await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        var principal = _userContext.User;
        var isAdmin = principal.IsInRole("Admin");
        var isHoD = principal.IsInRole("HoD");
        var isProjectsHod = isHoD;
        var isProjectsPo = string.Equals(project.LeadPoUserId, userId, StringComparison.OrdinalIgnoreCase);

        if (!isAdmin && !isProjectsPo && !isProjectsHod)
        {
            _logger.LogWarning("User {UserId} attempted to delete plan draft for project {ProjectId} without permission.", userId, id);
            return Forbid();
        }

        var result = await _planDraft.DeleteDraftAsync(id, cancellationToken);

        switch (result)
        {
            case PlanDraftDeleteResult.Success:
                TempData["Flash"] = "Draft discarded.";
                break;
            case PlanDraftDeleteResult.NotFound:
                TempData["Error"] = "No draft to discard.";
                break;
            case PlanDraftDeleteResult.Conflict:
                TempData["Error"] = "Draft already submitted. Nothing to discard.";
                break;
        }

        return RedirectToPage("/Projects/Overview", new { id });
    }

    private async Task<IActionResult> HandleExactAsync(int id, string userId, ClaimsPrincipal principal, CancellationToken cancellationToken, string? workflowVersion, bool canDirectApply, PlanIntent intent)
    {
        var rows = Input.Rows ??= new List<PlanEditInputRow>();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Code))
            {
                continue;
            }

            if (row.PlannedStart.HasValue && row.PlannedDue.HasValue &&
                row.PlannedStart.Value > row.PlannedDue.Value)
            {
                var name = string.IsNullOrWhiteSpace(row.Name) ? row.Code : row.Name;
                ModelState.AddModelError(string.Empty, $"For {name}, Start date cannot be after Due date.");
            }
        }

        if (!ModelState.IsValid)
        {
            return RedirectWithValidationErrors(id);
        }

        if (canDirectApply)
        {
            return await HandleExactDirectSaveAsync(id, userId, principal, rows, cancellationToken);
        }

        var submitForApproval = intent == PlanIntent.Submit;

        PlanVersion draft;
        try
        {
            draft = await _planDraft.CreateOrGetDraftAsync(id, cancellationToken);
        }
        catch (PlanDraftLockedException ex)
        {
            TempData["Error"] = ex.Message;
            TempData["OpenOffcanvas"] = "plan-edit";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        await _planDraft.UpgradeLegacyDraftForExactAsync(draft, cancellationToken);
        var workingCalendar = await BuildWorkingCalendarAsync(id, rows, cancellationToken, draft);
        var userName = principal.Identity?.Name;
        var stageOrderLookup = ProcurementWorkflow.BuildOrderLookup(workflowVersion);
        var nextSortOrder = Math.Max(stageOrderLookup.Count, draft.StagePlans.Select(stage => stage.SortOrder + 1).DefaultIfEmpty(0).Max());

        var stageMap = draft.StagePlans
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .ToDictionary(stage => stage.StageCode!, stage => stage, StringComparer.OrdinalIgnoreCase);

        var changes = new List<StageChange>();
        var durationsChanged = false;

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Code))
            {
                continue;
            }

            if (!stageMap.TryGetValue(row.Code, out var stagePlan))
            {
                stagePlan = new StagePlan
                {
                    PlanVersionId = draft.Id,
                    StageCode = row.Code,
                    SortOrder = StageOrder(row.Code, stageOrderLookup, ref nextSortOrder)
                };
                draft.StagePlans.Add(stagePlan);
                stageMap[row.Code] = stagePlan;
            }

            var previousStart = stagePlan.PlannedStart;
            var previousDue = stagePlan.PlannedDue;
            // Legacy Exact dates already belonged to the proposal, but its
            // duration is rebuilt from the accepted calendar during cutover.
            var duration = CalculateWorkingDuration(workingCalendar, row.PlannedStart, row.PlannedDue);
            if (stagePlan.DurationDays != duration)
            {
                stagePlan.DurationDays = duration;
                durationsChanged = true;
            }

            if (previousStart == row.PlannedStart && previousDue == row.PlannedDue)
            {
                continue;
            }

            stagePlan.PlannedStart = row.PlannedStart;
            stagePlan.PlannedDue = row.PlannedDue;
            changes.Add(new StageChange(row.Code, row.Name, previousStart, previousDue, row.PlannedStart, row.PlannedDue));
        }

        var pncApplicable = stageMap.TryGetValue(StageCodes.PNC, out var pncStage) &&
            pncStage.PlannedStart is DateOnly && pncStage.PlannedDue is DateOnly;

        var pncChanged = draft.PncApplicable != pncApplicable;
        draft.PncApplicable = pncApplicable;
        if (changes.Count > 0 || durationsChanged || pncChanged)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (changes.Count > 0)
        {
            _logger.LogInformation(
                "Exact timeline saved for Project {ProjectId}. ConnHash: {ConnHash}",
                id,
                ConnectionStringHasher.Hash(
                    _db.Database.IsRelational() ? _db.Database.GetConnectionString() : null));

            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = id.ToString(CultureInfo.InvariantCulture),
                ["ChangedStages"] = string.Join(";", changes.Select(change => change.Code))
            };

            for (var index = 0; index < changes.Count; index++)
            {
                var change = changes[index];
                var prefix = $"Stage[{index}]";
                data[$"{prefix}.Code"] = change.Code;
                data[$"{prefix}.Name"] = change.Name;
                data[$"{prefix}.Start.Before"] = FormatDate(change.PreviousStart);
                data[$"{prefix}.Start.After"] = FormatDate(change.NewStart);
                data[$"{prefix}.Due.Before"] = FormatDate(change.PreviousDue);
                data[$"{prefix}.Due.After"] = FormatDate(change.NewDue);
            }

            await _audit.LogAsync(
                "Projects.PlanUpdated",
                userId: userId,
                userName: userName,
                data: data);
        }

        if (submitForApproval)
        {
            try
            {
                await _planApproval.SubmitForApprovalAsync(id, userId, cancellationToken);
            }
            catch (PlanApprovalValidationException ex)
            {
                TempData["Error"] = ex.Errors.Count > 0 ? string.Join(" ", ex.Errors) : ex.Message;
                TempData["OpenOffcanvas"] = "plan-edit";
                _logger.LogWarning(ex, "Draft submission failed validation for project {ProjectId} by user {UserId}.", id, userId);
                return RedirectToPage("/Projects/Overview", new { id });
            }
            catch (DomainException ex)
            {
                TempData["Error"] = ex.Message;
                TempData["OpenOffcanvas"] = "plan-edit";
                _logger.LogWarning(ex, "Draft submission blocked for project {ProjectId} by user {UserId}.", id, userId);
                return RedirectToPage("/Projects/Overview", new { id });
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                TempData["OpenOffcanvas"] = "plan-edit";
                _logger.LogWarning(ex, "Draft submission failed for project {ProjectId} by user {UserId}.", id, userId);
                return RedirectToPage("/Projects/Overview", new { id });
            }

            TempData["Flash"] = "Plan submitted for HoD review.";
        }
        else
        {
            TempData["OpenOffcanvas"] = "plan-edit";

            if (changes.Count > 0)
            {
                TempData["Flash"] = "Draft saved.";
            }
            else
            {
                TempData["Flash"] = "Draft saved. No changes detected.";
            }
        }

        return RedirectToPage("/Projects/Overview", new { id });
    }

    private async Task<IActionResult> HandleDurationsAsync(int id, string userId, CancellationToken ct, string? workflowVersion, PlanIntent intent)
    {
        var submitForApproval = intent == PlanIntent.Submit;
        var action = NormalizeAction(Input.Action);

        // SECTION: Optional stage aggregation
        var optionalStageQuery = _db.StageTemplates
            .AsNoTracking()
            .Where(template => template.Optional);

        if (!string.IsNullOrWhiteSpace(workflowVersion))
        {
            optionalStageQuery = optionalStageQuery.Where(template => template.Version == workflowVersion);
        }

        var optionalStageCodes = await optionalStageQuery
            .Select(template => template.Code)
            .ToListAsync(ct);

        var skippedStageCodes = await _db.ProjectStages
            .AsNoTracking()
            .Where(stage => stage.ProjectId == id && stage.Status == StageStatus.Skipped)
            .Select(stage => stage.StageCode)
            .ToListAsync(ct);

        var optionalStages = new HashSet<string>(optionalStageCodes, StringComparer.OrdinalIgnoreCase);
        optionalStages.UnionWith(skippedStageCodes);

        if (Input.AnchorStart is null)
        {
            ModelState.AddModelError(nameof(Input.AnchorStart), "Please provide the anchor start date.");
        }

        if (!NextStageStartPolicies.IsValid(Input.NextStageStartPolicy))
        {
            ModelState.AddModelError(nameof(Input.NextStageStartPolicy), "Choose a valid next stage start policy.");
        }

        var rows = Input.Rows ??= new List<PlanEditInputRow>();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Code))
            {
                continue;
            }

            if (optionalStages.Contains(row.Code))
            {
                if (!row.DurationDays.HasValue || row.DurationDays.Value <= 0)
                {
                    row.DurationDays = null;
                }

                continue;
            }

            if (!row.DurationDays.HasValue || row.DurationDays.Value <= 0)
            {
                var name = string.IsNullOrWhiteSpace(row.Name) ? row.Code : row.Name;
                ModelState.AddModelError(string.Empty, $"Duration for {name} must be a positive number of days.");
            }
        }

        var anyDurationProvided = rows.Any(r =>
            !string.IsNullOrWhiteSpace(r.Code) &&
            r.DurationDays.HasValue &&
            r.DurationDays.Value > 0);

        if (submitForApproval && !anyDurationProvided)
        {
            ModelState.AddModelError(string.Empty, "Please provide at least one stage duration before submitting.");
        }

        if (!ModelState.IsValid)
        {
            return RedirectWithValidationErrors(id);
        }

        if (intent == PlanIntent.SaveChanges)
        {
            await using var publication = await RelationalTransactionScope.CreateAsync(_db.Database, ct);
            var baseline = await PublishedPlanningBaseline.LoadAsync(_db, id, ct);
            var publishedProject = await _db.Projects.SingleAsync(project => project.Id == id, ct);
            var settings = await _db.ProjectScheduleSettings.SingleOrDefaultAsync(s => s.ProjectId == id, ct);
            if (settings is null)
            {
                settings = new ProjectScheduleSettings
                {
                    ProjectId = id
                };
                _db.ProjectScheduleSettings.Add(settings);
            }

            settings.AnchorStart = Input.AnchorStart;
            settings.IncludeWeekends = Input.IncludeWeekends;
            settings.SkipHolidays = Input.SkipHolidays;
            settings.NextStageStartPolicy = NextStageStartPolicies.IsValid(Input.NextStageStartPolicy)
                ? Input.NextStageStartPolicy
                : NextStageStartPolicies.NextWorkingDay;

            var durationRows = await PlanningCompatibilityCatalog.PrepareDurationRowsForPublicationAsync(
                _db,
                id,
                workflowVersion,
                baseline.IsTrusted,
                rows.Select(row => row.Code),
                ct);
            publishedProject.PublishedPlanningInputVersion = PlanningInputVersions.Current;

            var durationMap = durationRows
                .Where(d => !string.IsNullOrWhiteSpace(d.StageCode))
                .ToDictionary(d => d.StageCode!, StringComparer.OrdinalIgnoreCase);

            var stageOrderLookup = ProcurementWorkflow.BuildOrderLookup(workflowVersion);
            var extraSortStart = Math.Max(stageOrderLookup.Count, durationRows.Select(duration => duration.SortOrder + 1).DefaultIfEmpty(0).Max());

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Code))
                {
                    continue;
                }

                durationMap.TryGetValue(row.Code, out var duration);
                var sortOrder = stageOrderLookup.TryGetValue(row.Code, out var knownOrder)
                    ? knownOrder
                    : duration?.SortOrder ?? extraSortStart++;

                if (duration is null)
                {
                    duration = new ProjectPlanDuration
                    {
                        ProjectId = id,
                        StageCode = row.Code,
                        SortOrder = sortOrder
                    };
                    _db.ProjectPlanDurations.Add(duration);
                    durationMap[row.Code] = duration;
                }

                duration.DurationDays = row.DurationDays;
                duration.SortOrder = sortOrder;
            }

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Durations saved for Project {ProjectId}. ConnHash: {ConnHash}",
                id,
                ConnectionStringHasher.Hash(
                    _db.Database.IsRelational() ? _db.Database.GetConnectionString() : null));

            await EnsureProjectStagesExistAsync(id, rows.Select(r => r.Code), ct);
            // Clearing ambiguous legacy inputs does not make omitted stages an
            // explicit date-clear request. Only the submitted inputs generate.
            var publishedStageCodes = baseline.IsTrusted ? null : rows
                .Where(row => !string.IsNullOrWhiteSpace(row.Code))
                .Select(row => row.Code).ToArray();
            await _planGeneration.GenerateAsync(id, ct, publishedStageCodes);

            // Validate the complete projected publication after generation so
            // retained manual/extension stages participate in chronology. The
            // enclosing relational transaction makes rejection all-or-nothing.
            var publishedPnc = rows.LastOrDefault(row =>
                string.Equals(row.Code, StageCodes.PNC, StringComparison.OrdinalIgnoreCase));
            var candidatePncApplicable = publishedPnc is null
                ? publishedProject.PncApplicable
                : publishedPnc.DurationDays.GetValueOrDefault() > 0;
            var projectedStages = await _db.ProjectStages
                .Where(stage => stage.ProjectId == id && !string.IsNullOrWhiteSpace(stage.StageCode))
                .Select(stage => new PlannedTimelineStage(
                    stage.StageCode,
                    stage.PlannedStart,
                    stage.PlannedDue,
                    stage.Status == StageStatus.Skipped,
                    stage.SortOrder))
                .ToListAsync(ct);
            var directAnchor = ResolveDirectPublicationAnchor(workflowVersion, rows);
            var validation = await _timelineValidation.ValidateAsync(
                new PlannedTimelineCandidate(
                    id,
                    directAnchor,
                    settings.NextStageStartPolicy == NextStageStartPolicies.SameDay
                        ? PlanTransitionRule.SameDay
                        : PlanTransitionRule.NextWorkingDay,
                    settings.IncludeWeekends,
                    settings.SkipHolidays,
                    candidatePncApplicable,
                    projectedStages),
                ct);
            if (!validation.IsValid)
            {
                var errors = validation.Errors.ToArray();
                return RejectPlanRequest(id, string.Join(" ", errors), errors: errors);
            }

            // The explicit submitted publication decision is authoritative. Shared
            // working duration rows and retained stage dates never supply this value.
            bool? previousPncApplicable = null;
            if (publishedPnc is not null)
            {
                previousPncApplicable = publishedProject.PncApplicable;
                publishedProject.PncApplicable = publishedPnc.DurationDays.GetValueOrDefault() > 0;
                // An explicit decision belongs to this publication even if its
                // value equals the original. Include the existing RowVersion check.
                _db.Entry(publishedProject).Property(project => project.PncApplicable).IsModified = true;
                await _db.SaveChangesAsync(ct);
            }

            // Refresh only an existing actor-owned draft from the published
            // baseline. Direct publication never creates a private revision.
            var workingDraft = await _planDraft.GetMyDraftAsync(id, ct);
            if (workingDraft is not null)
            {
                await SynchronizeDurationDraftAsync(id, workingDraft, settings, durationMap, ct);
                await _db.SaveChangesAsync(ct);
            }

            var publicationData = new Dictionary<string, string?>
            {
                ["ProjectId"] = id.ToString(CultureInfo.InvariantCulture),
                ["Action"] = PlanEditActions.SaveChanges,
                ["SaveMode"] = "Direct"
            };
            if (previousPncApplicable.HasValue)
            {
                publicationData["PncApplicable.Before"] = previousPncApplicable.Value.ToString();
                publicationData["PncApplicable.After"] = (publishedPnc!.DurationDays.GetValueOrDefault() > 0).ToString();
            }

            await _audit.LogAsync(
                "Projects.PlanGeneratedFromDurations",
                userId: userId,
                data: publicationData);

            await publication.CommitAsync(ct);

            TempData["Flash"] = "Timeline plan updated.";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        PlanVersion draft;
        try
        {
            draft = await _planDraft.CreateOrGetDraftAsync(id, ct);
        }
        catch (PlanDraftLockedException ex)
        {
            return RejectPlanRequest(id, ex.Message, 409);
        }

        await _planDraft.UpgradeLegacyDraftForDurationAsync(draft, ct);
        SetDurationProposalInputs(draft, rows, workflowVersion);
        await _db.SaveChangesAsync(ct);
        await _planGeneration.GenerateDraftAsync(id, draft.Id, ct);

        if (submitForApproval)
        {
            try
            {
                await _planApproval.SubmitForApprovalAsync(id, userId, ct);
            }
            catch (PlanApprovalValidationException ex)
            {
                TempData["Error"] = ex.Errors.Count > 0 ? string.Join(" ", ex.Errors) : ex.Message;
                TempData["OpenOffcanvas"] = "plan-edit";
                _logger.LogWarning(ex, "Draft submission failed validation for project {ProjectId} by user {UserId}.", id, userId);
                return RedirectToPage("/Projects/Overview", new { id });
            }
            catch (DomainException ex)
            {
                TempData["Error"] = ex.Message;
                TempData["OpenOffcanvas"] = "plan-edit";
                _logger.LogWarning(ex, "Draft submission blocked for project {ProjectId} by user {UserId}.", id, userId);
                return RedirectToPage("/Projects/Overview", new { id });
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                TempData["OpenOffcanvas"] = "plan-edit";
                _logger.LogWarning(ex, "Draft submission failed for project {ProjectId} by user {UserId}.", id, userId);
                return RedirectToPage("/Projects/Overview", new { id });
            }
        }

        await _audit.LogAsync(
            "Projects.PlanGeneratedFromDurations",
            userId: userId,
            data: new Dictionary<string, string?>
            {
                ["ProjectId"] = id.ToString(CultureInfo.InvariantCulture),
                ["Action"] = action
            });

        if (IsAutosaveRequest)
        {
            return new JsonResult(new { ok = true, savedAt = DateTimeOffset.UtcNow });
        }

        if (submitForApproval)
        {
            TempData["Flash"] = "Plan submitted for HoD review.";
        }
        else
        {
            TempData["OpenOffcanvas"] = "plan-edit";

            if (intent is PlanIntent.SaveDraft or PlanIntent.Autosave)
            {
                TempData["Flash"] = "Draft saved.";
            }
            else if (intent == PlanIntent.Calculate)
            {
                TempData["Flash"] = "Draft recalculated.";
            }
            else
            {
                TempData["Flash"] = "Draft updated.";
            }
        }

        return RedirectToPage("/Projects/Overview", new { id });
    }

    private void SetDurationProposalInputs(PlanVersion draft, IReadOnlyCollection<PlanEditInputRow> rows, string? workflowVersion)
    {
        draft.AnchorDate = Input.AnchorStart;
        draft.SkipWeekends = !Input.IncludeWeekends;
        draft.SkipHolidays = Input.SkipHolidays;
        draft.TransitionRule = Input.NextStageStartPolicy == NextStageStartPolicies.SameDay
            ? PlanTransitionRule.SameDay
            : PlanTransitionRule.NextWorkingDay;

        var stageMap = draft.StagePlans.Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .ToDictionary(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase);
        var stageOrderLookup = ProcurementWorkflow.BuildOrderLookup(workflowVersion);
        var nextSortOrder = Math.Max(stageOrderLookup.Count, draft.StagePlans.Select(stage => stage.SortOrder + 1).DefaultIfEmpty(0).Max());
        foreach (var row in rows.Where(row => !string.IsNullOrWhiteSpace(row.Code)))
        {
            if (!stageMap.TryGetValue(row.Code, out var stage))
            {
                stage = new StagePlan { PlanVersionId = draft.Id, StageCode = row.Code,
                    SortOrder = StageOrder(row.Code, stageOrderLookup, ref nextSortOrder) };
                draft.StagePlans.Add(stage);
                stageMap[row.Code] = stage;
            }
            if (stageOrderLookup.TryGetValue(row.Code, out var knownOrder)) stage.SortOrder = knownOrder;
            stage.DurationDays = row.DurationDays.GetValueOrDefault();
        }

        // An omitted PNC row leaves the proposal's existing decision intact.
        // An explicit row (including a clear) supplies the new proposal value.
        var submittedPnc = rows.LastOrDefault(row => string.Equals(row.Code, StageCodes.PNC, StringComparison.OrdinalIgnoreCase));
        if (submittedPnc is not null) draft.PncApplicable = submittedPnc.DurationDays.GetValueOrDefault() > 0;
    }

    private async Task SynchronizeDurationDraftAsync(
        int projectId,
        PlanVersion draft,
        ProjectScheduleSettings settings,
        IReadOnlyDictionary<string, ProjectPlanDuration> durations,
        CancellationToken cancellationToken)
    {
        draft.PlanningInputVersion = PlanningInputVersions.Current;
        draft.AnchorDate = settings.AnchorStart;
        draft.SkipWeekends = !settings.IncludeWeekends;
        draft.SkipHolidays = settings.SkipHolidays;
        draft.TransitionRule = settings.NextStageStartPolicy == NextStageStartPolicies.SameDay
            ? PlanTransitionRule.SameDay
            : PlanTransitionRule.NextWorkingDay;
        draft.PncApplicable = await _db.Projects.Where(project => project.Id == projectId)
            .Select(project => project.PncApplicable).SingleAsync(cancellationToken);

        var stages = await _db.ProjectStages.Where(stage => stage.ProjectId == projectId)
            .ToDictionaryAsync(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var authoritativeCodes = new HashSet<string>(
            durations.Keys.Concat(stages.Keys),
            StringComparer.OrdinalIgnoreCase);
        var obsoleteDraftStages = draft.StagePlans
            .Where(stage => !authoritativeCodes.Contains(stage.StageCode))
            .ToList();
        if (obsoleteDraftStages.Count > 0)
        {
            _db.StagePlans.RemoveRange(obsoleteDraftStages);
            foreach (var stage in obsoleteDraftStages) draft.StagePlans.Remove(stage);
        }

        var draftStages = draft.StagePlans.ToDictionary(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase);
        foreach (var code in authoritativeCodes)
        {
            if (!draftStages.TryGetValue(code, out var draftStage))
            {
                draftStage = new StagePlan { PlanVersionId = draft.Id, StageCode = code };
                draft.StagePlans.Add(draftStage);
            }
            durations.TryGetValue(code, out var duration);
            stages.TryGetValue(code, out var stage);
            draftStage.DurationDays = duration?.DurationDays.GetValueOrDefault() ?? 0;
            draftStage.SortOrder = duration?.SortOrder ?? stage?.SortOrder ?? draftStage.SortOrder;
            draftStage.PlannedStart = stage?.PlannedStart;
            draftStage.PlannedDue = stage?.PlannedDue;
        }
        NormalizeNonApplicablePnc(draft);
    }

    private static void NormalizeNonApplicablePnc(PlanVersion draft)
    {
        if (draft.PncApplicable) return;
        foreach (var stage in draft.StagePlans.Where(stage => string.Equals(stage.StageCode, StageCodes.PNC, StringComparison.OrdinalIgnoreCase)))
        {
            stage.DurationDays = 0;
            stage.PlannedStart = null;
            stage.PlannedDue = null;
        }
    }

    private IActionResult RedirectWithValidationErrors(int projectId)
    {
        var messages = ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => error.ErrorMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToArray();

        return RejectPlanRequest(projectId, messages.Length > 0
            ? string.Join(" ", messages)
            : "Please correct the highlighted errors and try again.", errors: messages);
    }

    private bool IsAutosaveRequest =>
        string.Equals(NormalizeAction(Input.Action), PlanEditActions.Autosave, StringComparison.OrdinalIgnoreCase) ||
        // Keep cached clients using AJAX SaveDraft safe during rollout as well.
        (string.Equals(Input.Mode, PlanEditorModes.Durations, StringComparison.OrdinalIgnoreCase) &&
         string.Equals(NormalizeAction(Input.Action), PlanEditActions.SaveDraft, StringComparison.OrdinalIgnoreCase) &&
         string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase));

    private static JsonResult AutosaveFailure(string message, int statusCode, string[]? errors = null) =>
        new(new { ok = false, message, errors = errors ?? new[] { message } }) { StatusCode = statusCode };

    private IActionResult RejectPlanRequest(int projectId, string message, int statusCode = 400, string[]? errors = null)
    {
        if (IsAutosaveRequest)
        {
            return AutosaveFailure(message, statusCode, errors);
        }
        TempData["Error"] = message;
        TempData["OpenOffcanvas"] = "plan-edit";
        return RedirectToPage("/Projects/Overview", new { id = projectId });
    }

    private enum PlanIntent { Autosave, SaveDraft, Calculate, Submit, SaveChanges }

    private static PlanIntent? ParsePlanIntent(string? action) => NormalizeAction(action).ToUpperInvariant() switch
    {
        "AUTOSAVE" => PlanIntent.Autosave,
        "SAVEDRAFT" => PlanIntent.SaveDraft,
        "CALCULATE" => PlanIntent.Calculate,
        "SUBMIT" => PlanIntent.Submit,
        "SAVECHANGES" => PlanIntent.SaveChanges,
        _ => null
    };

    private static bool IsAllowedPlanIntent(string? mode, bool canDirectApply, PlanIntent intent)
    {
        if (string.Equals(mode, PlanEditorModes.Durations, StringComparison.OrdinalIgnoreCase))
        {
            return intent is PlanIntent.Autosave or PlanIntent.SaveDraft or PlanIntent.Calculate ||
                intent == (canDirectApply ? PlanIntent.SaveChanges : PlanIntent.Submit);
        }

        return string.Equals(mode, PlanEditorModes.Exact, StringComparison.OrdinalIgnoreCase) &&
            (canDirectApply
                ? intent == PlanIntent.SaveChanges
                : intent is PlanIntent.SaveDraft or PlanIntent.Submit);
    }

    private static string NormalizeAction(string? action) => string.IsNullOrWhiteSpace(action)
        ? string.Empty
        : action.Trim();

    private static int CalculateWorkingDuration(WorkingCalendar calendar, DateOnly? start, DateOnly? due)
    {
        if (!start.HasValue || !due.HasValue || due.Value < start.Value)
        {
            return 0;
        }

        return calendar.CountWorkingDaysInclusive(start.Value, due.Value);
    }

    private async Task<WorkingCalendar> BuildWorkingCalendarAsync(
        int projectId,
        IReadOnlyCollection<PlanEditInputRow> rows,
        CancellationToken cancellationToken,
        PlanVersion? proposal = null,
        PublishedPlanningBaseline? acceptedBaseline = null)
    {
        var settings = proposal is null
            ? (acceptedBaseline ?? await PublishedPlanningBaseline.LoadAsync(_db, projectId, cancellationToken)).Settings
            : null;

        var includeWeekends = proposal is not null ? !proposal.SkipWeekends : settings?.IncludeWeekends ?? false;
        var skipHolidays = proposal?.SkipHolidays ?? settings?.SkipHolidays ?? true;

        var dates = rows
            .SelectMany(row => new[] { row.PlannedStart, row.PlannedDue })
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .ToArray();

        var rangeStart = dates.Length > 0 ? dates.Min().AddDays(-7) : DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1);
        var rangeEndExclusive = dates.Length > 0 ? dates.Max().AddDays(8) : DateOnly.FromDateTime(DateTime.UtcNow).AddYears(2);

        IReadOnlyCollection<DateOnly> holidays;
        if (_officeCalendar is not null)
        {
            holidays = (await _officeCalendar.GetNonWorkingDatesAsync(
                rangeStart,
                rangeEndExclusive,
                cancellationToken)).ToArray();
        }
        else
        {
            holidays = await _db.Holidays
                .AsNoTracking()
                .Where(holiday =>
                    holiday.Date >= rangeStart
                    && holiday.Date < rangeEndExclusive
                    && (holiday.Type == HolidayType.Gazetted || holiday.IsObservedAsOfficeHoliday))
                .Select(holiday => holiday.Date)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        return new WorkingCalendar(holidays, includeWeekends, skipHolidays);
    }

    private static string? FormatDate(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // SECTION: Direct-save helpers for HoD/Admin
    private async Task<IActionResult> HandleExactDirectSaveAsync(
        int projectId,
        string userId,
        ClaimsPrincipal principal,
        IReadOnlyCollection<PlanEditInputRow> rows,
        CancellationToken cancellationToken)
    {
        await using var publication = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        var baseline = await PublishedPlanningBaseline.LoadAsync(_db, projectId, cancellationToken);
        var workingCalendar = await BuildWorkingCalendarAsync(projectId, rows, cancellationToken, acceptedBaseline: baseline);
        var publishedProject = await _db.Projects.SingleAsync(project => project.Id == projectId, cancellationToken);
        var stageRows = await _db.ProjectStages
            .Where(stage => stage.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        var publishedPnc = rows.LastOrDefault(row =>
            string.Equals(row.Code, StageCodes.PNC, StringComparison.OrdinalIgnoreCase));
        var submittedPncApplicable = publishedPnc is not null &&
            publishedPnc.PlannedStart.HasValue &&
            publishedPnc.PlannedDue.HasValue;
        var candidatePncApplicable = publishedPnc is null
            ? publishedProject.PncApplicable
            : submittedPncApplicable;
        var projectedStages = stageRows
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .ToDictionary(
                stage => stage.StageCode,
                stage => new PlannedTimelineStage(
                    stage.StageCode,
                    stage.PlannedStart,
                    stage.PlannedDue,
                    stage.Status == StageStatus.Skipped,
                    stage.SortOrder),
                StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Where(row => !string.IsNullOrWhiteSpace(row.Code)))
        {
            projectedStages[row.Code] = new PlannedTimelineStage(
                row.Code,
                row.PlannedStart,
                row.PlannedDue,
                projectedStages.TryGetValue(row.Code, out var existing) && existing.IsSkipped,
                existing?.SortOrder ?? ProcurementWorkflow.OrderOf(baseline.WorkflowVersion, row.Code));
        }

        var acceptedSettings = baseline.Settings;
        var directAnchor = ResolveDirectPublicationAnchor(baseline.WorkflowVersion, rows);
        var validation = await _timelineValidation.ValidateAsync(
            new PlannedTimelineCandidate(
                projectId,
                directAnchor,
                acceptedSettings?.NextStageStartPolicy == NextStageStartPolicies.SameDay
                    ? PlanTransitionRule.SameDay
                    : PlanTransitionRule.NextWorkingDay,
                acceptedSettings?.IncludeWeekends ?? false,
                acceptedSettings?.SkipHolidays ?? true,
                candidatePncApplicable,
                projectedStages.Values.ToArray()),
            cancellationToken);
        if (!validation.IsValid)
        {
            var errors = validation.Errors.ToArray();
            return RejectPlanRequest(projectId, string.Join(" ", errors), errors: errors);
        }

        ProjectScheduleSettings? establishedSettings = null;
        if (!baseline.IsTrusted)
        {
            establishedSettings = await _db.ProjectScheduleSettings.SingleOrDefaultAsync(item => item.ProjectId == projectId, cancellationToken);
            if (establishedSettings is null)
            {
                establishedSettings = new ProjectScheduleSettings { ProjectId = projectId };
                _db.ProjectScheduleSettings.Add(establishedSettings);
            }
            establishedSettings.AnchorStart = baseline.Settings?.AnchorStart;
            establishedSettings.IncludeWeekends = baseline.Settings?.IncludeWeekends ?? false;
            establishedSettings.SkipHolidays = baseline.Settings?.SkipHolidays ?? true;
            establishedSettings.NextStageStartPolicy = baseline.Settings?.NextStageStartPolicy ?? NextStageStartPolicies.NextWorkingDay;
            publishedProject.PublishedPlanningInputVersion = PlanningInputVersions.Current;
        }

        var stageLookup = stageRows
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .ToDictionary(stage => stage.StageCode!, StringComparer.OrdinalIgnoreCase);

        var durationRows = await PlanningCompatibilityCatalog.PrepareDurationRowsForPublicationAsync(
            _db,
            projectId,
            baseline.WorkflowVersion,
            baseline.IsTrusted,
            rows.Select(row => row.Code),
            cancellationToken);
        var durationLookup = durationRows
            .Where(item => !string.IsNullOrWhiteSpace(item.StageCode))
            .ToDictionary(item => item.StageCode!, StringComparer.OrdinalIgnoreCase);

        var stageOrderLookup = ProcurementWorkflow.BuildOrderLookup(await _db.Projects
            .AsNoTracking()
            .Where(project => project.Id == projectId)
            .Select(project => project.WorkflowVersion)
            .SingleOrDefaultAsync(cancellationToken));

        var nextSortOrder = stageRows.Count > 0
            ? stageRows.Max(stage => stage.SortOrder) + 1
            : stageOrderLookup.Count;

        var changes = new List<StageChange>();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Code))
            {
                continue;
            }

            if (!stageLookup.TryGetValue(row.Code, out var stage))
            {
                stage = new ProjectStage
                {
                    ProjectId = projectId,
                    StageCode = row.Code,
                    Status = StageStatus.NotStarted,
                    SortOrder = StageOrder(row.Code, stageOrderLookup, ref nextSortOrder)
                };
                _db.ProjectStages.Add(stage);
                stageLookup[row.Code] = stage;
            }

            var previousStart = stage.PlannedStart;
            var previousDue = stage.PlannedDue;
            var datesChanged = previousStart != row.PlannedStart || previousDue != row.PlannedDue;
            if (!datesChanged && baseline.IsTrusted)
            {
                continue;
            }

            stage.PlannedStart = row.PlannedStart;
            stage.PlannedDue = row.PlannedDue;

            if (!durationLookup.TryGetValue(row.Code, out var duration))
            {
                duration = new ProjectPlanDuration
                {
                    ProjectId = projectId,
                    StageCode = row.Code,
                    SortOrder = stage.SortOrder
                };
                _db.ProjectPlanDurations.Add(duration);
                durationLookup[row.Code] = duration;
            }

            duration.SortOrder = stage.SortOrder;
            duration.DurationDays = row.PlannedStart.HasValue && row.PlannedDue.HasValue
                ? CalculateWorkingDuration(workingCalendar, row.PlannedStart, row.PlannedDue)
                : null;

            if (datesChanged)
            {
                changes.Add(new StageChange(row.Code, row.Name, previousStart, previousDue, row.PlannedStart, row.PlannedDue));
            }
        }

        bool? previousPncApplicable = null;
        if (publishedPnc is not null)
        {
            previousPncApplicable = publishedProject.PncApplicable;
            publishedProject.PncApplicable = submittedPncApplicable;
            // Keep the canonical Project RowVersion in the explicit decision's write-set.
            _db.Entry(publishedProject).Property(project => project.PncApplicable).IsModified = true;
        }

        var workingDraft = await _planDraft.GetMyDraftAsync(projectId, cancellationToken);
        if (changes.Count > 0 || workingDraft is not null || publishedPnc is not null || !baseline.IsTrusted)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        if (workingDraft is not null)
        {
            if (!baseline.IsTrusted || workingDraft.PlanningInputVersion < PlanningInputVersions.Current)
            {
                // Direct publication completes only an existing actor-owned draft.
                var settings = establishedSettings ?? baseline.Settings ?? new ProjectScheduleSettings { ProjectId = projectId };
                await SynchronizeDurationDraftAsync(projectId, workingDraft, settings, durationLookup, cancellationToken);
            }
            else
            {
                var proposalCalendar = await BuildWorkingCalendarAsync(projectId, rows, cancellationToken, workingDraft);
                foreach (var row in rows.Where(row => !string.IsNullOrWhiteSpace(row.Code)))
                {
                    var draftStage = workingDraft.StagePlans.FirstOrDefault(stage =>
                        string.Equals(stage.StageCode, row.Code, StringComparison.OrdinalIgnoreCase));
                    if (draftStage is null)
                    {
                        draftStage = new StagePlan { PlanVersionId = workingDraft.Id, StageCode = row.Code };
                        workingDraft.StagePlans.Add(draftStage);
                    }
                    draftStage.PlannedStart = row.PlannedStart;
                    draftStage.PlannedDue = row.PlannedDue;
                    draftStage.DurationDays = CalculateWorkingDuration(proposalCalendar, row.PlannedStart, row.PlannedDue);
                    if (stageLookup.TryGetValue(row.Code, out var publishedStage)) draftStage.SortOrder = publishedStage.SortOrder;
                }
                workingDraft.PncApplicable = publishedProject.PncApplicable && workingDraft.StagePlans.Any(stage =>
                    string.Equals(stage.StageCode, StageCodes.PNC, StringComparison.OrdinalIgnoreCase) &&
                    stage.PlannedStart.HasValue && stage.PlannedDue.HasValue);
                NormalizeNonApplicablePnc(workingDraft);
            }
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (changes.Count > 0 || (previousPncApplicable.HasValue && previousPncApplicable.Value != submittedPncApplicable))
        {
            var userName = principal.Identity?.Name;

            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                ["ChangedStages"] = string.Join(";", changes.Select(change => change.Code)),
                ["SaveMode"] = "Direct"
            };

            if (previousPncApplicable.HasValue)
            {
                data["PncApplicable.Before"] = previousPncApplicable.Value.ToString();
                data["PncApplicable.After"] = submittedPncApplicable.ToString();
            }

            for (var index = 0; index < changes.Count; index++)
            {
                var change = changes[index];
                var prefix = $"Stage[{index}]";
                data[$"{prefix}.Code"] = change.Code;
                data[$"{prefix}.Name"] = change.Name;
                data[$"{prefix}.Start.Before"] = FormatDate(change.PreviousStart);
                data[$"{prefix}.Start.After"] = FormatDate(change.NewStart);
                data[$"{prefix}.Due.Before"] = FormatDate(change.PreviousDue);
                data[$"{prefix}.Due.After"] = FormatDate(change.NewDue);
            }

            await _audit.LogAsync(
                "Projects.PlanUpdated",
                userId: userId,
                userName: userName,
                data: data);
        }

        await publication.CommitAsync(cancellationToken);

        TempData["Flash"] = changes.Count > 0
            ? "Timeline plan updated."
            : "Planned dates saved successfully.";
        return RedirectToPage("/Projects/Overview", new { id = projectId });
    }

    // SECTION: Ensure timeline stages exist for direct generation
    private async Task EnsureProjectStagesExistAsync(int projectId, IEnumerable<string?> stageCodes, CancellationToken cancellationToken)
    {
        var codes = stageCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (codes.Length == 0)
        {
            return;
        }

        var existingCodes = await _db.ProjectStages
            .AsNoTracking()
            .Where(stage => stage.ProjectId == projectId && stage.StageCode != null)
            .Select(stage => stage.StageCode!)
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
        var stageOrderLookup = ProcurementWorkflow.BuildOrderLookup(await _db.Projects
            .AsNoTracking()
            .Where(project => project.Id == projectId)
            .Select(project => project.WorkflowVersion)
            .SingleOrDefaultAsync(cancellationToken));
        var extraSortStart = stageOrderLookup.Count;

        foreach (var code in codes)
        {
            if (existingSet.Contains(code))
            {
                continue;
            }

            _db.ProjectStages.Add(new ProjectStage
            {
                ProjectId = projectId,
                StageCode = code,
                SortOrder = StageOrder(code, stageOrderLookup, ref extraSortStart),
                Status = StageStatus.NotStarted
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    // SECTION: Stage ordering helpers
    private static int StageOrder(string stageCode, IReadOnlyDictionary<string, int> stageOrderLookup, ref int extraSortStart)
    {
        if (stageOrderLookup.TryGetValue(stageCode, out var index))
        {
            return index;
        }

        return extraSortStart++;
    }

    private static string ResolveDirectPublicationAnchor(
        string? workflowVersion,
        IEnumerable<PlanEditInputRow> rows)
    {
        var defaultOrder = ProcurementWorkflow.OrderOf(
            workflowVersion,
            PlanConstants.DefaultAnchorStageCode);
        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Code))
            .Select(row => new
            {
                row.Code,
                Order = ProcurementWorkflow.OrderOf(workflowVersion, row.Code)
            })
            .Where(item => item.Order < defaultOrder)
            .OrderBy(item => item.Order)
            .Select(item => item.Code)
            .FirstOrDefault() ?? PlanConstants.DefaultAnchorStageCode;
    }

    private sealed record StageChange(
        string Code,
        string Name,
        DateOnly? PreviousStart,
        DateOnly? PreviousDue,
        DateOnly? NewStart,
        DateOnly? NewDue);
}
