using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Services;

namespace ProjectManagement.Pages.Projects;

[Authorize(Roles = "Project Officer,HoD,Admin,MCO,Comdt")]
public class StagesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly StageRulesService _rules;
    private readonly IClock _clock;
    private readonly ProjectCommentService _commentService;

    private static readonly string[] CommentRoles = new[] { "Admin", "HoD", "Project Officer", "MCO", "Comdt" };

    public StagesModel(ApplicationDbContext db, StageRulesService rules, IClock clock, ProjectCommentService commentService)
    {
        _db = db;
        _rules = rules;
        _clock = clock;
        _commentService = commentService;
    }

    public record StageRow(
        string Code,
        string Name,
        DateOnly? PlannedStart,
        DateOnly? PlannedDue,
        StageStatus Status,
        DateOnly? ActualStart,
        DateOnly? CompletedOn,
        int SlipDays,
        StageGuardResult StartGuard,
        StageGuardResult CompleteGuard,
        StageGuardResult SkipGuard);

    public int ProjectId { get; private set; }
    public string ProjectName { get; private set; } = string.Empty;
    public List<StageRow> Stages { get; private set; } = new();
    public List<StageSlipSummary> StageSlips { get; private set; } = new();
    public ProjectRagStatus ProjectRag { get; private set; } = ProjectRagStatus.Green;
    public bool CanManageStages { get; private set; }
    public bool CanComment { get; private set; }
    public List<SelectListItem> CommentStageOptions { get; private set; } = new();
    public List<CommentDisplayModel> StageComments { get; private set; } = new();
    public CommentComposerViewModel CommentComposer { get; private set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [TempData]
    public string? CommentStatusMessage { get; set; }

    [TempData]
    public string? CommentErrorMessage { get; set; }

    [BindProperty(Name = "Form")]
    public CommentFormModel CommentInput { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? CommentStageId { get; set; }

    [BindProperty(SupportsGet = true, Name = "commentParentId")]
    public int? CommentParentId { get; set; }

    [BindProperty(SupportsGet = true, Name = "commentEditId")]
    public int? CommentEditId { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var cancellationToken = HttpContext.RequestAborted;
        return await LoadAsync(id, cancellationToken);
    }

    public async Task<IActionResult> OnPostStartAsync(int projectId, string stage, CancellationToken cancellationToken)
    {
        var stageCode = NormalizeStageCode(stage);
        if (stageCode == null)
        {
            ErrorMessage = "Stage code is required.";
            return RedirectToPage(new { id = projectId });
        }

        var (result, ctx) = await LoadForMutationAsync(projectId, cancellationToken);
        if (result != null)
        {
            return result;
        }

        if (ctx is null)
        {
            ErrorMessage = "Unable to load stage data.";
            return RedirectToPage(new { id = projectId });
        }

        var stageEntity = ctx.Stages.FirstOrDefault(s => s.StageCode.Equals(stageCode, StringComparison.OrdinalIgnoreCase));
        if (stageEntity == null)
        {
            return NotFound();
        }

        var context = await _rules.BuildContextAsync(ctx.Stages, cancellationToken);
        var guard = _rules.CanStart(context, stageCode);
        if (!guard.Allowed)
        {
            ErrorMessage = guard.Reason ?? $"Stage {stageCode} cannot be started.";
            return RedirectToPage(new { id = projectId });
        }

        var today = Today();
        stageEntity.ActualStart ??= today;
        stageEntity.Status = StageStatus.InProgress;

        await _db.SaveChangesAsync(cancellationToken);

        StatusMessage = $"Stage {stageCode} started.";
        return RedirectToPage(new { id = projectId });
    }

    public async Task<IActionResult> OnPostCompleteAsync(int projectId, string stage, CancellationToken cancellationToken)
    {
        var stageCode = NormalizeStageCode(stage);
        if (stageCode == null)
        {
            ErrorMessage = "Stage code is required.";
            return RedirectToPage(new { id = projectId });
        }

        var (result, ctx) = await LoadForMutationAsync(projectId, cancellationToken);
        if (result != null)
        {
            return result;
        }

        if (ctx is null)
        {
            ErrorMessage = "Unable to load stage data.";
            return RedirectToPage(new { id = projectId });
        }

        var stageEntity = ctx.Stages.FirstOrDefault(s => s.StageCode.Equals(stageCode, StringComparison.OrdinalIgnoreCase));
        if (stageEntity == null)
        {
            return NotFound();
        }

        var context = await _rules.BuildContextAsync(ctx.Stages, cancellationToken);
        var guard = _rules.CanComplete(context, stageCode);
        if (!guard.Allowed)
        {
            ErrorMessage = guard.Reason ?? $"Stage {stageCode} cannot be completed.";
            return RedirectToPage(new { id = projectId });
        }

        var today = Today();
        stageEntity.CompletedOn = today;
        stageEntity.ActualStart ??= today;
        stageEntity.Status = StageStatus.Completed;

        await _db.SaveChangesAsync(cancellationToken);

        StatusMessage = $"Stage {stageCode} completed.";
        return RedirectToPage(new { id = projectId });
    }

    public async Task<IActionResult> OnPostSkipAsync(int projectId, string stage, string? reason, CancellationToken cancellationToken)
    {
        var stageCode = NormalizeStageCode(stage);
        if (stageCode == null)
        {
            ErrorMessage = "Stage code is required.";
            return RedirectToPage(new { id = projectId });
        }

        reason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length is < 3 or > 200)
        {
            ErrorMessage = "Provide a reason between 3 and 200 characters to skip PNC.";
            return RedirectToPage(new { id = projectId });
        }

        var (result, ctx) = await LoadForMutationAsync(projectId, cancellationToken);
        if (result != null)
        {
            return result;
        }

        if (ctx is null)
        {
            ErrorMessage = "Unable to load stage data.";
            return RedirectToPage(new { id = projectId });
        }

        var stageEntity = ctx.Stages.FirstOrDefault(s => s.StageCode.Equals(stageCode, StringComparison.OrdinalIgnoreCase));
        if (stageEntity == null)
        {
            return NotFound();
        }

        var context = await _rules.BuildContextAsync(ctx.Stages, cancellationToken);
        var guard = _rules.CanSkip(context, stageCode);
        if (!guard.Allowed)
        {
            ErrorMessage = guard.Reason ?? $"Stage {stageCode} cannot be skipped.";
            return RedirectToPage(new { id = projectId });
        }

        stageEntity.Status = StageStatus.Skipped;
        stageEntity.ActualStart = null;
        stageEntity.CompletedOn = null;

        await _db.SaveChangesAsync(cancellationToken);

        StatusMessage = $"Stage {stageCode} skipped.";
        return RedirectToPage(new { id = projectId });
    }

    public async Task<IActionResult> OnPostStageCommentAsync(int projectId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null || !UserCanComment())
        {
            return Forbid();
        }

        CommentInput.ProjectId = projectId;
        CommentStageId = CommentInput.StageId ?? CommentStageId;

        if (!CommentInput.StageId.HasValue && !CommentInput.ParentCommentId.HasValue)
        {
            CommentErrorMessage = "Select a stage before adding a remark.";
            return await LoadAsync(projectId, cancellationToken);
        }

        if (!ModelState.IsValid)
        {
            return await LoadAsync(projectId, cancellationToken);
        }

        try
        {
            if (CommentInput.EditingCommentId.HasValue)
            {
                var updated = await _commentService.UpdateAsync(CommentInput.EditingCommentId.Value, userId, CommentInput.Body, CommentInput.Type, CommentInput.Pinned, CommentInput.Files, cancellationToken);
                if (updated == null)
                {
                    CommentErrorMessage = "Unable to edit the remark.";
                }
                else
                {
                    CommentStatusMessage = "Remark updated.";
                }
            }
            else
            {
                await _commentService.CreateAsync(projectId, CommentInput.StageId, CommentInput.ParentCommentId, CommentInput.Body, CommentInput.Type, CommentInput.Pinned, userId, CommentInput.Files, cancellationToken);
                CommentStatusMessage = "Remark added.";
            }
        }
        catch (Exception ex)
        {
            CommentErrorMessage = ex.Message;
        }

        var redirect = CommentInput.RedirectTo ?? Url.Page("/Projects/Stages", new
        {
            id = projectId,
            commentStageId = CommentInput.StageId ?? CommentStageId
        });

        if (!string.IsNullOrEmpty(redirect))
        {
            return Redirect(redirect!);
        }

        return RedirectToPage(new
        {
            id = projectId,
            commentStageId = CommentInput.StageId ?? CommentStageId
        });
    }

    public async Task<IActionResult> OnPostDeleteCommentAsync(int projectId, int commentId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null || !UserCanComment())
        {
            return Forbid();
        }

        var comment = await _db.ProjectComments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == commentId && c.ProjectId == projectId, cancellationToken);
        if (comment == null)
        {
            CommentErrorMessage = "Remark not found.";
            return RedirectToPage(new { id = projectId, commentStageId = CommentStageId });
        }

        var ok = await _commentService.SoftDeleteAsync(commentId, userId, cancellationToken);
        if (ok)
        {
            CommentStatusMessage = "Remark deleted.";
        }
        else
        {
            CommentErrorMessage = "Unable to delete remark.";
        }

        return RedirectToPage(new { id = projectId, commentStageId = comment.ProjectStageId ?? CommentStageId });
    }

    public async Task<IActionResult> OnGetDownloadAttachmentAsync(int projectId, int commentId, int attachmentId, CancellationToken cancellationToken)
    {
        var result = await _commentService.OpenAttachmentAsync(projectId, commentId, attachmentId, cancellationToken);
        if (result == null)
        {
            return NotFound();
        }

        var (attachment, stream) = result.Value;
        return File(stream, attachment.ContentType, attachment.FileName);
    }

    private async Task<IActionResult> LoadAsync(int id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null)
        {
            return Challenge();
        }

        var project = await _db.Projects
            .Where(p => p.Id == id)
            .Select(p => new { p.Id, p.Name, p.LeadPoUserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        ProjectId = project.Id;
        ProjectName = project.Name;

        var canManage = UserCanManage(project.LeadPoUserId, userId);
        if (!canManage && User.IsInRole("Project Officer"))
        {
            return Forbid();
        }

        CanManageStages = canManage;

        var templates = await _db.StageTemplates
            .AsNoTracking()
            .Where(t => t.Version == PlanConstants.StageTemplateVersion)
            .OrderBy(t => t.Sequence)
            .Select(t => new { t.Code, t.Name })
            .ToListAsync(cancellationToken);

        var projectStages = await _db.ProjectStages
            .AsNoTracking()
            .Where(ps => ps.ProjectId == id)
            .ToListAsync(cancellationToken);

        var stageLookup = projectStages
            .ToDictionary(ps => ps.StageCode, ps => ps, StringComparer.OrdinalIgnoreCase);

        var context = await _rules.BuildContextAsync(projectStages, cancellationToken);
        var today = Today();
        var health = StageHealthCalculator.Compute(projectStages, today);

        StageSlips = templates
            .Select(t => new StageSlipSummary(
                t.Code,
                health.SlipByStage.TryGetValue(t.Code, out var slip) ? slip : 0))
            .ToList();
        ProjectRag = health.Rag;

        Stages = templates
            .Select(template =>
            {
                stageLookup.TryGetValue(template.Code, out var projectStage);

                var status = projectStage?.Status ?? StageStatus.NotStarted;

                return new StageRow(
                    template.Code,
                    template.Name,
                    projectStage?.PlannedStart,
                    projectStage?.PlannedDue,
                    status,
                    projectStage?.ActualStart,
                    projectStage?.CompletedOn,
                    health.SlipByStage.TryGetValue(template.Code, out var slip) ? slip : 0,
                    _rules.CanStart(context, template.Code),
                    _rules.CanComplete(context, template.Code),
                    _rules.CanSkip(context, template.Code));
            })
            .ToList();

        var stageNameMap = templates.ToDictionary(t => t.Code, t => t.Name, StringComparer.OrdinalIgnoreCase);
        await LoadStageRemarksAsync(id, projectStages, stageNameMap, cancellationToken);

        return Page();
    }

    private async Task LoadStageRemarksAsync(int projectId, List<ProjectStage> projectStages, IDictionary<string, string> stageNameMap, CancellationToken cancellationToken)
    {
        CanComment = UserCanComment();

        var stageById = projectStages.ToDictionary(ps => ps.Id);
        CommentStageOptions = projectStages
            .OrderBy(ps => ps.StageCode)
            .Select(ps =>
            {
                var label = stageNameMap.TryGetValue(ps.StageCode, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? $"{ps.StageCode} — {name}"
                    : ps.StageCode;
                return new SelectListItem(label, ps.Id.ToString(), CommentStageId.HasValue && CommentStageId.Value == ps.Id);
            })
            .ToList();

        if (!CommentStageId.HasValue && CommentStageOptions.Count > 0)
        {
            if (int.TryParse(CommentStageOptions[0].Value, out var firstId))
            {
                CommentStageId = firstId;
            }
        }

        var stageId = CommentStageId;
        if (stageId.HasValue && !stageById.ContainsKey(stageId.Value))
        {
            var first = stageById.Keys.FirstOrDefault();
            if (first != 0)
            {
                stageId = CommentStageId = first;
            }
            else
            {
                stageId = null;
                CommentStageId = null;
            }
        }

        if (!CommentInput.StageId.HasValue && stageId.HasValue)
        {
            CommentInput.StageId = stageId;
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!stageId.HasValue)
        {
            StageComments = new List<CommentDisplayModel>();
            await PrepareStageComposerAsync(null, null, cancellationToken);
            return;
        }

        var stageCode = stageById.TryGetValue(stageId.Value, out var stageEntity) ? stageEntity.StageCode : null;

        var baseQuery = _db.ProjectComments
            .AsNoTracking()
            .Where(c => c.ProjectId == projectId && !c.IsDeleted && c.ParentCommentId == null && c.ProjectStageId == stageId);

        var rows = await baseQuery
            .OrderByDescending(c => c.Pinned)
            .ThenByDescending(c => c.CreatedOn)
            .Take(20)
            .Select(c => new
            {
                Comment = c,
                Author = c.CreatedByUser,
                Attachments = c.Attachments.OrderBy(a => a.FileName).Select(a => new { a.Id, a.FileName, a.SizeBytes }).ToList(),
                Replies = c.Replies
                    .Where(r => !r.IsDeleted)
                    .OrderBy(r => r.CreatedOn)
                    .Select(r => new
                    {
                        Reply = r,
                        Author = r.CreatedByUser,
                        Attachments = r.Attachments.OrderBy(a => a.FileName).Select(a => new { a.Id, a.FileName, a.SizeBytes }).ToList()
                    }).ToList()
            })
            .ToListAsync(cancellationToken);

        StageComments = rows.Select(c => new CommentDisplayModel
        {
            Id = c.Comment.Id,
            ProjectId = c.Comment.ProjectId,
            Body = c.Comment.Body,
            Type = c.Comment.Type,
            Pinned = c.Comment.Pinned,
            CreatedOn = c.Comment.CreatedOn,
            EditedOn = c.Comment.EditedOn,
            AuthorId = c.Comment.CreatedByUserId,
            AuthorName = BuildAuthorName(c.Author, c.Comment.CreatedByUserId),
            StageCode = stageCode,
            StageName = stageCode != null && stageNameMap.TryGetValue(stageCode, out var name) ? name : stageCode,
            Attachments = c.Attachments.Select(a => new CommentAttachmentViewModel(a.Id, a.FileName, a.SizeBytes)).ToList(),
            Replies = c.Replies.Select(r => new CommentReplyModel
            {
                Id = r.Reply.Id,
                ProjectId = r.Reply.ProjectId,
                Body = r.Reply.Body,
                Type = r.Reply.Type,
                CreatedOn = r.Reply.CreatedOn,
                EditedOn = r.Reply.EditedOn,
                AuthorId = r.Reply.CreatedByUserId,
                AuthorName = BuildAuthorName(r.Author, r.Reply.CreatedByUserId),
                Attachments = r.Attachments.Select(a => new CommentAttachmentViewModel(a.Id, a.FileName, a.SizeBytes)).ToList(),
                CanEdit = CanComment && currentUserId != null && string.Equals(r.Reply.CreatedByUserId, currentUserId, StringComparison.Ordinal)
            }).ToList(),
            CanEdit = CanComment && currentUserId != null && string.Equals(c.Comment.CreatedByUserId, currentUserId, StringComparison.Ordinal),
            CanReply = CanComment
        }).ToList();

        await PrepareStageComposerAsync(stageId, stageCode, cancellationToken);
    }

    private async Task PrepareStageComposerAsync(int? stageId, string? stageCode, CancellationToken cancellationToken)
    {
        CommentInput.StageId = stageId;
        CommentInput.ParentCommentId = CommentParentId;
        CommentInput.EditingCommentId = CommentEditId;

        if (CommentParentId.HasValue)
        {
            var parent = await _db.ProjectComments
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == CommentParentId.Value && c.ProjectId == ProjectId && !c.IsDeleted, cancellationToken);

            if (parent != null)
            {
                CommentInput.StageId = parent.ProjectStageId;
            }
        }

        if (CommentEditId.HasValue)
        {
            var comment = await _db.ProjectComments
                .AsNoTracking()
                .Where(c => c.Id == CommentEditId.Value && c.ProjectId == ProjectId && !c.IsDeleted)
                .Select(c => new { c.Body, c.Type, c.Pinned, c.ProjectStageId, c.CreatedByUserId })
                .FirstOrDefaultAsync(cancellationToken);

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (comment != null && userId != null && string.Equals(comment.CreatedByUserId, userId, StringComparison.Ordinal))
            {
                CommentInput.Body = comment.Body;
                CommentInput.Type = comment.Type;
                CommentInput.Pinned = comment.Pinned;
                CommentInput.StageId = comment.ProjectStageId;
            }
            else
            {
                CommentEditId = null;
                CommentInput.EditingCommentId = null;
            }
        }

        var typeOptions = Enum.GetValues<ProjectCommentType>()
            .Select(t => new SelectListItem(t.ToString(), t.ToString()))
            .ToList();

        var legend = stageCode != null ? $"Stage {stageCode} remarks" : "Stage remarks";

        CommentComposer = new CommentComposerViewModel
        {
            FormHandler = "StageComment",
            Form = CommentInput,
            StageOptions = CommentStageOptions,
            TypeOptions = typeOptions,
            SubmitButtonLabel = CommentEditId.HasValue ? "Save" : CommentParentId.HasValue ? "Reply" : "Post",
            Legend = legend,
            ShowStagePicker = false,
            ShowPinnedToggle = true,
            MaxFileSizeBytes = ProjectCommentService.MaxAttachmentSizeBytes,
            StatusMessage = CommentStatusMessage,
            ErrorMessage = CommentErrorMessage
        };

        CommentInput.RedirectTo = Url.Page("/Projects/Stages", new
        {
            id = ProjectId,
            commentStageId = CommentInput.StageId
        });
    }

    private async Task<(IActionResult? Result, MutationContext? Context)> LoadForMutationAsync(int projectId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null)
        {
            return (Challenge(), null);
        }

        var project = await _db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Id, p.LeadPoUserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null)
        {
            return (NotFound(), null);
        }

        if (!UserCanManage(project.LeadPoUserId, userId))
        {
            return (Forbid(), null);
        }

        var stages = await _db.ProjectStages
            .Where(ps => ps.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        return (null, new MutationContext(stages));
    }

    private static string? NormalizeStageCode(string? stage)
    {
        if (string.IsNullOrWhiteSpace(stage))
        {
            return null;
        }

        return stage.Trim().ToUpperInvariant();
    }

    private DateOnly Today() => DateOnly.FromDateTime(_clock.UtcNow.DateTime);

    private bool UserCanComment()
    {
        foreach (var role in CommentRoles)
        {
            if (User.IsInRole(role))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildAuthorName(ApplicationUser? user, string userId)
    {
        if (user == null)
        {
            return string.IsNullOrEmpty(userId) ? "Unknown" : "Former user";
        }

        var display = string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : $"{user.Rank} {user.FullName}";
        return string.IsNullOrWhiteSpace(display) ? user.UserName ?? "User" : display.Trim();
    }

    private bool UserCanManage(string? leadPoUserId, string? currentUserId)
    {
        if (User.IsInRole("Admin") || User.IsInRole("HoD"))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(currentUserId) && string.Equals(leadPoUserId, currentUserId, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private sealed record MutationContext(List<ProjectStage> Stages);
}
