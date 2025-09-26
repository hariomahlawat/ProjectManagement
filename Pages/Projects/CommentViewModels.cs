using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Models;

namespace ProjectManagement.Pages.Projects
{
    public class CommentComposerViewModel
    {
        public string? Legend { get; set; }

        public string? StatusMessage { get; set; }

        public string? ErrorMessage { get; set; }

        public CommentComposerFormModel Form { get; set; } = new CommentComposerFormModel();

        public IEnumerable<SelectListItem> StageOptions { get; set; } = Array.Empty<SelectListItem>();

        public IEnumerable<SelectListItem> TypeOptions { get; set; } = Array.Empty<SelectListItem>();

        public bool ShowStagePicker { get; set; }

        public bool ShowPinnedToggle { get; set; }

        public string FormHandler { get; set; } = string.Empty;

        public string SubmitButtonLabel { get; set; } = "Submit";

        public long MaxFileSizeBytes { get; set; } = 0;
    }

    public class CommentComposerFormModel
    {
        [Required]
        public int ProjectId { get; set; }

        public int? StageId { get; set; }

        public int? ParentCommentId { get; set; }

        public int? EditingCommentId { get; set; }

        public string? RedirectTo { get; set; }

        [Required]
        public ProjectCommentType Type { get; set; }

        [Required]
        [MaxLength(2000)]
        [MinLength(4)]
        public string Body { get; set; } = string.Empty;

        public bool Pinned { get; set; }

        public List<IFormFile> Files { get; set; } = new List<IFormFile>();
    }

    public class CommentDisplayModel
    {
        public int Id { get; set; }

        public ProjectCommentType Type { get; set; }

        public bool Pinned { get; set; }

        public DateTime CreatedOn { get; set; }

        public string AuthorName { get; set; } = string.Empty;

        public string? StageCode { get; set; }

        public string? StageName { get; set; }

        public string? Body { get; set; }

        public DateTime? EditedOn { get; set; }

        public bool CanReply { get; set; }

        public bool CanEdit { get; set; }

        public List<CommentAttachmentDisplayModel> Attachments { get; set; } = new List<CommentAttachmentDisplayModel>();

        public List<CommentDisplayModel> Replies { get; set; } = new List<CommentDisplayModel>();
    }

    public class CommentAttachmentDisplayModel
    {
        public int Id { get; set; }

        public string FileName { get; set; } = string.Empty;

        public long SizeBytes { get; set; }
    }
}
