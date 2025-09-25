using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Pages.Projects;
using ProjectManagement.Services;

namespace ProjectManagement.Pages.Projects
{
    [Authorize]
    public class ActivityModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ProjectCommentService _commentService;

        private const int PageSize = 20;

        public ActivityModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ProjectCommentService commentService)
        {
            _db = db;
            _userManager = userManager;
            _commentService = commentService;
        }

        public int ProjectId { get; private set; }
        public string ProjectName { get; private set; } = string.Empty;
        public List<CommentDisplayModel> Comments { get; private set; } = new();
        public CommentComposerViewModel Composer { get; private set; } = new();
        public List<SelectListItem> StageOptions { get; private set; } = new();
        public List<SelectListItem> TypeOptions { get; private set; } = new();
        public List<SelectListItem> AuthorOptions { get; private set; } = new();
        public int CurrentPage { get; private set; } = 1;
        public int TotalPages { get; private set; } = 1;
        public bool CanComment { get; private set; }

        [TempData]
        public string? StatusMessage { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        [BindProperty(Name = "Form")]
        public CommentFormModel Input { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public ProjectCommentType? Type { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? AuthorId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? StageId { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateOnly? From { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateOnly? To { get; set; }

        [BindProperty(SupportsGet = true, Name = "Page")]
        public int PageIndex { get; set; } = 1;

        [BindProperty(SupportsGet = true, Name = "parentId")]
        public int? ReplyTo { get; set; }

        [BindProperty(SupportsGet = true, Name = "editId")]
        public int? EditId { get; set; }

        private static readonly string[] CommentRoles = new[] { "Admin", "HoD", "Project Officer", "MCO", "Comdt" };

        public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
        {
            var project = await _db.Projects
                .AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => new { p.Id, p.Name })
                .FirstOrDefaultAsync(cancellationToken);

            if (project == null)
            {
                return NotFound();
            }

            ProjectId = project.Id;
            ProjectName = project.Name;
            CanComment = UserCanComment();

            await LoadSelectListsAsync(id, cancellationToken);
            await PrepareComposerAsync(cancellationToken);
            await LoadCommentsAsync(id, cancellationToken);

            return Page();
        }

        public async Task<IActionResult> OnPostCommentAsync(int id, CancellationToken cancellationToken)
        {
            ProjectId = id;
            CanComment = UserCanComment();
            if (!CanComment)
            {
                return Forbid();
            }

            Input.ProjectId = id;

            var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (project == null)
            {
                return NotFound();
            }

            await LoadSelectListsAsync(id, cancellationToken);

            if (!ModelState.IsValid)
            {
                await PrepareComposerAsync(cancellationToken);
                await LoadCommentsAsync(id, cancellationToken);
                return Page();
            }

            var userId = _userManager.GetUserId(User)!;

            try
            {
                if (Input.EditingCommentId.HasValue)
                {
                    var updated = await _commentService.UpdateAsync(Input.EditingCommentId.Value, userId, Input.Body, Input.Type, Input.Pinned, Input.Files, cancellationToken);
                    if (updated == null)
                    {
                        ErrorMessage = "Unable to edit the remark.";
                    }
                    else
                    {
                        StatusMessage = "Remark updated.";
                    }
                }
                else
                {
                    await _commentService.CreateAsync(id, Input.StageId, Input.ParentCommentId, Input.Body, Input.Type, Input.Pinned, userId, Input.Files, cancellationToken);
                    StatusMessage = "Remark added.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }

            var redirect = Input.RedirectTo ?? Url.Page("/Projects/Activity", new
            {
                id,
                Type,
                AuthorId,
                StageId,
                From,
                To,
                Page = PageIndex
            });

            if (!string.IsNullOrEmpty(redirect))
            {
                return Redirect(redirect);
            }

            return RedirectToPage(new { id, Type, AuthorId, StageId, From, To, Page = PageIndex });
        }

        public async Task<IActionResult> OnPostDeleteCommentAsync(int id, int commentId, CancellationToken cancellationToken)
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null || !UserCanComment())
            {
                return Forbid();
            }

            var ok = await _commentService.SoftDeleteAsync(commentId, userId, cancellationToken);
            StatusMessage = ok ? "Remark deleted." : "Unable to delete remark.";

            return RedirectToPage(new { id, Type, AuthorId, StageId, From, To, Page = PageIndex });
        }

        public async Task<IActionResult> OnGetDownloadAttachmentAsync(int id, int commentId, int attachmentId, CancellationToken cancellationToken)
        {
            var result = await _commentService.OpenAttachmentAsync(id, commentId, attachmentId, cancellationToken);
            if (result == null)
            {
                return NotFound();
            }

            var (attachment, stream) = result.Value;
            return File(stream, attachment.ContentType, attachment.FileName);
        }
        private async Task LoadSelectListsAsync(int projectId, CancellationToken cancellationToken)
        {
            var stages = await _db.ProjectStages
                .AsNoTracking()
                .Where(s => s.ProjectId == projectId)
                .OrderBy(s => s.StageCode)
                .Select(s => new { s.Id, s.StageCode })
                .ToListAsync(cancellationToken);

            StageOptions = stages
                .Select(s => new SelectListItem($"{s.StageCode}", s.Id.ToString()))
                .ToList();

            TypeOptions = Enum.GetValues<ProjectCommentType>()
                .Select(t => new SelectListItem(t.ToString(), t.ToString()))
                .ToList();

            var authorData = await _db.ProjectComments
                .AsNoTracking()
                .Where(c => c.ProjectId == projectId && !c.IsDeleted)
                .Select(c => new
                {
                    c.CreatedByUserId,
                    Rank = c.CreatedByUser != null ? c.CreatedByUser.Rank : null,
                    FullName = c.CreatedByUser != null ? c.CreatedByUser.FullName : null,
                    UserName = c.CreatedByUser != null ? c.CreatedByUser.UserName : null
                })
                .Distinct()
                .ToListAsync(cancellationToken);

            AuthorOptions = authorData
                .Where(a => !string.IsNullOrEmpty(a.CreatedByUserId))
                .Select(a =>
                {
                    var display = string.IsNullOrWhiteSpace(a.FullName)
                        ? a.UserName
                        : $"{a.Rank} {a.FullName}";

                    if (string.IsNullOrWhiteSpace(display))
                    {
                        display = "Former user";
                    }
                    else
                    {
                        display = display.Trim();
                    }

                    return new SelectListItem(display ?? "Former user", a.CreatedByUserId!);
                })
                .OrderBy(a => a.Text)
                .ToList();
        }

        private async Task PrepareComposerAsync(CancellationToken cancellationToken)
        {
            var composerForm = new CommentFormModel
            {
                ProjectId = ProjectId,
                StageId = StageId,
                ParentCommentId = ReplyTo,
                EditingCommentId = EditId,
                Body = Input.Body,
                Type = Input.Type,
                Pinned = Input.Pinned,
                RedirectTo = Url.Page("/Projects/Activity", new
                {
                    id = ProjectId,
                    Type,
                    AuthorId,
                    StageId,
                    From,
                    To,
                    Page = PageIndex
                })
            };

            if (ReplyTo.HasValue)
            {
                var parent = await _db.ProjectComments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == ReplyTo.Value && c.ProjectId == ProjectId && !c.IsDeleted, cancellationToken);

                if (parent != null)
                {
                    composerForm.StageId ??= parent.ProjectStageId;
                }
            }

            if (EditId.HasValue)
            {
                var comment = await _db.ProjectComments
                    .AsNoTracking()
                    .Where(c => c.Id == EditId.Value && c.ProjectId == ProjectId && !c.IsDeleted)
                    .Select(c => new { c.Body, c.Type, c.Pinned, c.ProjectStageId, c.CreatedByUserId })
                    .FirstOrDefaultAsync(cancellationToken);

                var userId = _userManager.GetUserId(User);
                if (comment != null && userId != null && string.Equals(comment.CreatedByUserId, userId, StringComparison.Ordinal))
                {
                    composerForm.Body = comment.Body;
                    composerForm.Type = comment.Type;
                    composerForm.Pinned = comment.Pinned;
                    composerForm.StageId = comment.ProjectStageId;
                }
                else
                {
                    EditId = null;
                    composerForm.EditingCommentId = null;
                }
            }

            Composer = new CommentComposerViewModel
            {
                FormHandler = "Comment",
                Form = composerForm,
                StageOptions = StageOptions,
                TypeOptions = TypeOptions,
                SubmitButtonLabel = EditId.HasValue ? "Save" : ReplyTo.HasValue ? "Reply" : "Post",
                Legend = EditId.HasValue ? "Edit remark" : ReplyTo.HasValue ? "Reply to remark" : "Add a remark",
                ShowStagePicker = !ReplyTo.HasValue,
                ShowPinnedToggle = true,
                MaxFileSizeBytes = ProjectCommentService.MaxAttachmentSizeBytes,
                StatusMessage = StatusMessage,
                ErrorMessage = ErrorMessage
            };
        }

        private async Task LoadCommentsAsync(int projectId, CancellationToken cancellationToken)
        {
            CurrentPage = PageIndex < 1 ? 1 : PageIndex;
            var userId = _userManager.GetUserId(User);
            var canComment = UserCanComment();

            var baseQuery = _db.ProjectComments
                .AsNoTracking()
                .Where(c => c.ProjectId == projectId && !c.IsDeleted && c.ParentCommentId == null);

            if (Type.HasValue)
            {
                baseQuery = baseQuery.Where(c => c.Type == Type.Value);
            }

            if (!string.IsNullOrEmpty(AuthorId))
            {
                baseQuery = baseQuery.Where(c => c.CreatedByUserId == AuthorId);
            }

            if (StageId.HasValue)
            {
                baseQuery = baseQuery.Where(c => c.ProjectStageId == StageId);
            }

            if (From.HasValue)
            {
                var fromDate = From.Value.ToDateTime(TimeOnly.MinValue);
                baseQuery = baseQuery.Where(c => c.CreatedOn >= fromDate);
            }

            if (To.HasValue)
            {
                var toDate = To.Value.ToDateTime(TimeOnly.MaxValue);
                baseQuery = baseQuery.Where(c => c.CreatedOn <= toDate);
            }

            var total = await baseQuery.CountAsync(cancellationToken);
            TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            if (CurrentPage > TotalPages)
            {
                CurrentPage = TotalPages;
            }

            var skip = (CurrentPage - 1) * PageSize;

            var topLevel = await baseQuery
                .OrderByDescending(c => c.Pinned)
                .ThenByDescending(c => c.CreatedOn)
                .Skip(skip)
                .Take(PageSize)
                .Select(c => new
                {
                    Comment = c,
                    Author = c.CreatedByUser,
                    Stage = c.ProjectStage,
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

            Comments = topLevel.Select(c =>
            {
                var comment = c.Comment;
                var authorName = BuildAuthorName(c.Author, comment.CreatedByUserId);
                return new CommentDisplayModel
                {
                    Id = comment.Id,
                    ProjectId = comment.ProjectId,
                    Body = comment.Body,
                    Type = comment.Type,
                    Pinned = comment.Pinned,
                    CreatedOn = comment.CreatedOn,
                    EditedOn = comment.EditedOn,
                    AuthorId = comment.CreatedByUserId,
                    AuthorName = authorName,
                    StageCode = c.Stage?.StageCode,
                    StageName = c.Stage?.StageCode,
                    Attachments = c.Attachments
                        .Select(a => new CommentAttachmentViewModel(a.Id, a.FileName, a.SizeBytes))
                        .ToList(),
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
                        CanEdit = canComment && userId != null && string.Equals(r.Reply.CreatedByUserId, userId, StringComparison.Ordinal)
                    }).ToList(),
                    CanEdit = canComment && userId != null && string.Equals(comment.CreatedByUserId, userId, StringComparison.Ordinal),
                    CanReply = canComment
                };
            }).ToList();
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
    }
}
