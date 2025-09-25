using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Services;

namespace ProjectManagement.Pages.Projects
{
    [Authorize]
    public class ViewModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IClock _clock;

        public ViewModel(ApplicationDbContext db, IClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public record ItemModel(int Id, string Name, string? Description, string? Hod, string? Po, DateTime CreatedAt, int? ActivePlanVersionNo);

        public record ActivitySummary(int CommentId, int? StageId, string? StageLabel, string AuthorName, string BodyPreview, ProjectCommentType Type, DateTime CreatedOn);

        public ItemModel Item { get; private set; } = null!;
        public List<StageSlipSummary> StageSlips { get; private set; } = new();
        public ProjectRagStatus ProjectRag { get; private set; } = ProjectRagStatus.Green;
        public bool HasApprovedPlan { get; private set; }
        public bool IsPlanPendingApproval { get; private set; }
        public int? ActivePlanVersionNo { get; private set; }
        public string NextDueText { get; private set; } = string.Empty;
        public List<ActivitySummary> RecentActivity { get; private set; } = new();

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var cancellationToken = HttpContext.RequestAborted;

            var item = await _db.Projects
                .Include(p => p.HodUser)
                .Include(p => p.LeadPoUser)
                .Where(p => p.Id == id)
                .Select(p => new ItemModel(
                    p.Id,
                    p.Name,
                    p.Description,
                    p.HodUser == null ? null : $"{p.HodUser.Rank} {p.HodUser.FullName}",
                    p.LeadPoUser == null ? null : $"{p.LeadPoUser.Rank} {p.LeadPoUser.FullName}",
                    p.CreatedAt,
                    p.ActivePlanVersionNo))
                .FirstOrDefaultAsync(cancellationToken);

            if (item == null)
            {
                return NotFound();
            }

            Item = item;
            ActivePlanVersionNo = item.ActivePlanVersionNo;
            HasApprovedPlan = item.ActivePlanVersionNo.HasValue;

            await LoadStageHealthAsync(id);
            await LoadPlanInsightsAsync(id, cancellationToken);
            return Page();
        }

        private async Task LoadStageHealthAsync(int projectId)
        {
            var cancellationToken = HttpContext.RequestAborted;

            var templates = await _db.StageTemplates
                .AsNoTracking()
                .Where(t => t.Version == PlanConstants.StageTemplateVersion)
                .OrderBy(t => t.Sequence)
                .Select(t => t.Code)
                .ToListAsync(cancellationToken);

            var stages = await _db.ProjectStages
                .AsNoTracking()
                .Where(ps => ps.ProjectId == projectId)
                .ToListAsync(cancellationToken);

            var health = StageHealthCalculator.Compute(stages, DateOnly.FromDateTime(_clock.UtcNow.DateTime));

            StageSlips = templates
                .Select(code => new StageSlipSummary(code, health.SlipByStage.TryGetValue(code, out var slip) ? slip : 0))
                .ToList();

            ProjectRag = health.Rag;
        }

        private async Task LoadPlanInsightsAsync(int projectId, CancellationToken cancellationToken)
        {
            IsPlanPendingApproval = await _db.PlanVersions
                .AsNoTracking()
                .AnyAsync(p => p.ProjectId == projectId && p.Status == PlanVersionStatus.PendingApproval, cancellationToken);

            var stageNames = await _db.StageTemplates
                .AsNoTracking()
                .Where(t => t.Version == PlanConstants.StageTemplateVersion)
                .ToDictionaryAsync(t => t.Code, t => t.Name, StringComparer.OrdinalIgnoreCase, cancellationToken);

            if (!HasApprovedPlan || !ActivePlanVersionNo.HasValue)
            {
                NextDueText = "Baseline not approved.";
                await LoadRecentActivityAsync(projectId, stageNames, cancellationToken);
                return;
            }

            var plan = await _db.PlanVersions
                .AsNoTracking()
                .Include(p => p.StagePlans)
                .FirstOrDefaultAsync(p => p.ProjectId == projectId && p.VersionNo == ActivePlanVersionNo.Value, cancellationToken);

            IReadOnlyCollection<StagePlan>? stagePlans = plan?.StagePlans;
            IReadOnlyDictionary<string, ProjectStage>? stageStatus = null;

            if (plan != null)
            {
                stageStatus = await _db.ProjectStages
                    .AsNoTracking()
                    .Where(ps => ps.ProjectId == projectId)
                    .ToDictionaryAsync(ps => ps.StageCode, StringComparer.OrdinalIgnoreCase, cancellationToken);
            }

            NextDueText = BuildNextDueText(stagePlans, stageStatus, stageNames);

            await LoadRecentActivityAsync(projectId, stageNames, cancellationToken);
        }

        private async Task LoadRecentActivityAsync(int projectId, IReadOnlyDictionary<string, string> stageNames, CancellationToken cancellationToken)
        {
            var comments = await _db.ProjectComments
                .AsNoTracking()
                .Where(c => c.ProjectId == projectId && !c.IsDeleted && c.ParentCommentId == null)
                .OrderByDescending(c => c.Pinned)
                .ThenByDescending(c => c.CreatedOn)
                .ThenByDescending(c => c.Id)
                .Take(3)
                .Select(c => new
                {
                    c.Id,
                    c.ProjectStageId,
                    StageCode = c.ProjectStage != null ? c.ProjectStage.StageCode : null,
                    c.Body,
                    c.Type,
                    c.CreatedOn,
                    Author = c.CreatedByUser,
                    c.CreatedByUserId
                })
                .ToListAsync(cancellationToken);

            RecentActivity = comments
                .Select(c => new ActivitySummary(
                    c.Id,
                    c.ProjectStageId,
                    string.IsNullOrWhiteSpace(c.StageCode) ? null : BuildStageLabel(c.StageCode!, stageNames),
                    BuildAuthorName(c.Author, c.CreatedByUserId),
                    BuildPreview(c.Body),
                    c.Type,
                    c.CreatedOn))
                .ToList();
        }

        private static string BuildNextDueText(IReadOnlyCollection<StagePlan>? stagePlans, IReadOnlyDictionary<string, ProjectStage>? stageStatus, IReadOnlyDictionary<string, string> stageNames)
        {
            if (stagePlans == null || stagePlans.Count == 0)
            {
                return "Schedule not configured.";
            }

            var ordered = stagePlans
                .Where(sp => sp.PlannedDue.HasValue || sp.PlannedStart.HasValue)
                .OrderBy(sp => sp.PlannedDue ?? sp.PlannedStart)
                .ToList();

            if (!ordered.Any())
            {
                return "No planned dates.";
            }

            foreach (var plan in ordered)
            {
                var status = stageStatus != null && stageStatus.TryGetValue(plan.StageCode, out var stage)
                    ? stage.Status
                    : StageStatus.NotStarted;

                if (status is StageStatus.Completed or StageStatus.Skipped)
                {
                    continue;
                }

                var label = BuildStageLabel(plan.StageCode, stageNames);
                var date = plan.PlannedDue ?? plan.PlannedStart;
                return date.HasValue ? $"{label} — {date.Value:dd MMM yyyy}" : label;
            }

            return "All stages completed.";
        }

        private static string BuildStageLabel(string? stageCode, IReadOnlyDictionary<string, string> stageNames)
        {
            if (string.IsNullOrWhiteSpace(stageCode))
            {
                return "Project";
            }

            return stageNames.TryGetValue(stageCode, out var name) && !string.IsNullOrWhiteSpace(name)
                ? $"{stageCode} — {name}"
                : stageCode;
        }

        private static string BuildAuthorName(ApplicationUser? user, string? userId)
        {
            if (user == null)
            {
                return string.IsNullOrEmpty(userId) ? "Unknown" : "Former user";
            }

            var display = string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : $"{user.Rank} {user.FullName}";
            return string.IsNullOrWhiteSpace(display) ? user.UserName ?? "User" : display.Trim();
        }

        private static string BuildPreview(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return "(No details provided)";
            }

            var trimmed = body.Trim();
            var normalized = trimmed.Replace("\r", " ").Replace("\n", " ");
            const int maxLength = 140;
            if (normalized.Length <= maxLength)
            {
                return normalized;
            }

            var shortened = normalized.Substring(0, Math.Min(normalized.Length, maxLength)).TrimEnd();
            return shortened + "…";
        }
    }
}
