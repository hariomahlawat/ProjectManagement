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

        public List<IFormFile> Files { get; set; } = new List<IFormFile>();

        public string? RedirectTo { get; set; }
    }

    public class CommentComposerViewModel
    {
        public string FormHandler { get; set; } = "Comment";

        public CommentFormModel Form { get; set; } = new CommentFormModel();

        public IEnumerable<SelectListItem> StageOptions { get; set; } = new List<SelectListItem>();

        public IEnumerable<SelectListItem> TypeOptions { get; set; } = new List<SelectListItem>();

        public string SubmitButtonLabel { get; set; } = "Post";

        public string? Legend { get; set; }

        public bool ShowStagePicker { get; set; } = true;

        public bool ShowPinnedToggle { get; set; } = true;

        public long MaxFileSizeBytes { get; set; }

        public string? StatusMessage { get; set; }

        public string? ErrorMessage { get; set; }
    }

    public class CommentAttachmentViewModel
    {
        public CommentAttachmentViewModel(int id, string fileName, long sizeBytes)
        {
            Id = id;
            FileName = string.IsNullOrWhiteSpace(fileName) ? "file" : fileName;
            SizeBytes = sizeBytes;
        }

        public int Id { get; }

        public string FileName { get; }

        public long SizeBytes { get; }
    }

    public class CommentReplyModel
    {
        public int Id { get; set; }

        public int ProjectId { get; set; }

        public string Body { get; set; } = string.Empty;

        public ProjectCommentType Type { get; set; }

        public DateTime CreatedOn { get; set; }

        public DateTime? EditedOn { get; set; }

        public string AuthorName { get; set; } = string.Empty;

        public string? AuthorId { get; set; }

        public IReadOnlyList<CommentAttachmentViewModel> Attachments { get; set; } = new List<CommentAttachmentViewModel>();

        public bool CanEdit { get; set; }
    }

    public class CommentDisplayModel
    {
        public int Id { get; set; }

        public int ProjectId { get; set; }

        public string Body { get; set; } = string.Empty;

        public ProjectCommentType Type { get; set; }

        public bool Pinned { get; set; }

        public DateTime CreatedOn { get; set; }

        public DateTime? EditedOn { get; set; }

        public string AuthorName { get; set; } = string.Empty;

        public string? AuthorId { get; set; }

        public string? StageCode { get; set; }

        public string? StageName { get; set; }

        public IReadOnlyList<CommentAttachmentViewModel> Attachments { get; set; } = new List<CommentAttachmentViewModel>();

        public IReadOnlyList<CommentReplyModel> Replies { get; set; } = new List<CommentReplyModel>();

        public bool CanEdit { get; set; }

        public bool CanReply { get; set; }
    }
}
