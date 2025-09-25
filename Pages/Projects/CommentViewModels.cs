using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Models;

namespace ProjectManagement.Pages.Projects
{
    public class CommentFormModel
    {
        [Required]
        public int ProjectId { get; set; }

        public int? StageId { get; set; }

        public int? ParentCommentId { get; set; }

        public int? EditingCommentId { get; set; }

        [Required]
        [StringLength(2000, MinimumLength = 4)]
        public string Body { get; set; } = string.Empty;

        [Required]
        public ProjectCommentType Type { get; set; } = ProjectCommentType.Update;

        public bool Pinned { get; set; }

        public List<IFormFile> Files { get; set; } = new();

        public string? RedirectTo { get; set; }
    }

    public class CommentComposerViewModel
    {
        public string FormHandler { get; init; } = "Comment";

        public CommentFormModel Form { get; init; } = new();

        public IEnumerable<SelectListItem> StageOptions { get; init; } = new List<SelectListItem>();

        public IEnumerable<SelectListItem> TypeOptions { get; init; } = new List<SelectListItem>();

        public string SubmitButtonLabel { get; init; } = "Post";

        public string? Legend { get; init; }

        public bool ShowStagePicker { get; init; } = true;

        public bool ShowPinnedToggle { get; init; } = true;

        public long MaxFileSizeBytes { get; init; }

        public string? StatusMessage { get; init; }

        public string? ErrorMessage { get; init; }
    }

    public record CommentAttachmentViewModel(int Id, string FileName, long SizeBytes);

    public class CommentReplyModel
    {
        public int Id { get; init; }

        public int ProjectId { get; init; }

        public string Body { get; init; } = string.Empty;

        public ProjectCommentType Type { get; init; }

        public DateTime CreatedOn { get; init; }

        public DateTime? EditedOn { get; init; }

        public string AuthorName { get; init; } = string.Empty;

        public string? AuthorId { get; init; }

        public IReadOnlyList<CommentAttachmentViewModel> Attachments { get; init; } = new List<CommentAttachmentViewModel>();

        public bool CanEdit { get; init; }
    }

    public class CommentDisplayModel
    {
        public int Id { get; init; }

        public int ProjectId { get; init; }

        public string Body { get; init; } = string.Empty;

        public ProjectCommentType Type { get; init; }

        public bool Pinned { get; init; }

        public DateTime CreatedOn { get; init; }

        public DateTime? EditedOn { get; init; }

        public string AuthorName { get; init; } = string.Empty;

        public string? AuthorId { get; init; }

        public string? StageCode { get; init; }

        public string? StageName { get; init; }

        public IReadOnlyList<CommentAttachmentViewModel> Attachments { get; init; } = new List<CommentAttachmentViewModel>();

        public IReadOnlyList<CommentReplyModel> Replies { get; init; } = new List<CommentReplyModel>();

        public bool CanEdit { get; init; }

        public bool CanReply { get; init; }
    }
}
