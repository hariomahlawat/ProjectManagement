using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Storage;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services
{
    public class ProjectCommentService
    {
        private readonly ApplicationDbContext _db;
        private readonly IClock _clock;
        private readonly IAuditService _audit;
        private readonly ILogger<ProjectCommentService> _logger;
        private readonly string _basePath;

        public const long MaxAttachmentSizeBytes = 25 * 1024 * 1024; // 25 MB per file

        private static readonly HashSet<string> AllowedAttachmentContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf",
            "image/png",
            "image/jpeg",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };

        public ProjectCommentService(ApplicationDbContext db,
                                     IClock clock,
                                     IAuditService audit,
                                     IUploadRootProvider uploadRootProvider,
                                     ILogger<ProjectCommentService> logger)
        {
            _db = db;
            _clock = clock;
            _audit = audit;
            _logger = logger;
            if (uploadRootProvider == null)
            {
                throw new ArgumentNullException(nameof(uploadRootProvider));
            }

            _basePath = uploadRootProvider.RootPath;
        }

        public async Task<ProjectComment> CreateAsync(int projectId,
                                                      int? projectStageId,
                                                      int? parentCommentId,
                                                      string body,
                                                      ProjectCommentType type,
                                                      bool pinned,
                                                      string userId,
                                                      IEnumerable<IFormFile> attachments,
                                                      CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                throw new ArgumentException("Comment body is required.", nameof(body));
            }

            await EnsureStageBelongsToProject(projectId, projectStageId, cancellationToken);
            var parent = await GetParentCommentForProjectAsync(projectId, parentCommentId, cancellationToken);

            if (parent != null)
            {
                if (parent.ProjectStageId.HasValue)
                {
                    if (projectStageId.HasValue && parent.ProjectStageId.Value != projectStageId.Value)
                    {
                        throw new InvalidOperationException("Replies must stay within the same stage.");
                    }

                    projectStageId = parent.ProjectStageId;
                }
                else if (projectStageId.HasValue)
                {
                    throw new InvalidOperationException("Project-level remarks cannot target a stage.");
                }
            }

            var now = _clock.UtcNow.UtcDateTime;

            using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

            var comment = new ProjectComment
            {
                ProjectId = projectId,
                ProjectStageId = projectStageId,
                ParentCommentId = parentCommentId,
                Body = body.Trim(),
                Type = type,
                Pinned = pinned,
                IsDeleted = false,
                CreatedByUserId = userId,
                CreatedOn = now
            };

            _db.ProjectComments.Add(comment);
            await _db.SaveChangesAsync(cancellationToken);

            var saved = await SaveAttachmentsAsync(projectId, comment, attachments, userId, now, cancellationToken);
            if (saved.Count > 0)
            {
                await _db.ProjectCommentAttachments.AddRangeAsync(saved, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);

            comment.Attachments = saved;

            await _audit.LogAsync("Comments.CommentAdded",
                userId: userId,
                data: new Dictionary<string, string?>
                {
                    ["ProjectId"] = projectId.ToString(),
                    ["CommentId"] = comment.Id.ToString(),
                    ["StageId"] = projectStageId?.ToString(),
                    ["ParentCommentId"] = parentCommentId?.ToString(),
                    ["AttachmentCount"] = saved.Count.ToString()
                });

            return comment;
        }

        public async Task<ProjectComment?> UpdateAsync(int commentId,
                                                       string userId,
                                                       string body,
                                                       ProjectCommentType type,
                                                       bool pinned,
                                                       IEnumerable<IFormFile> newAttachments,
                                                       CancellationToken cancellationToken)
        {
            var comment = await _db.ProjectComments
                .Include(c => c.Attachments)
                .FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken);

            if (comment == null || comment.IsDeleted)
            {
                return null;
            }

            if (!string.Equals(comment.CreatedByUserId, userId, StringComparison.Ordinal))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                throw new ArgumentException("Comment body is required.", nameof(body));
            }

            var now = _clock.UtcNow.UtcDateTime;

            comment.Body = body.Trim();
            comment.Type = type;
            comment.Pinned = pinned;
            comment.EditedByUserId = userId;
            comment.EditedOn = now;

            var saved = await SaveAttachmentsAsync(comment.ProjectId, comment, newAttachments, userId, now, cancellationToken);
            if (saved.Count > 0)
            {
                await _db.ProjectCommentAttachments.AddRangeAsync(saved, cancellationToken);
            }

            await _db.SaveChangesAsync(cancellationToken);

            if (saved.Count > 0)
            {
                comment.Attachments = comment.Attachments.Concat(saved).ToList();
            }

            await _audit.LogAsync("Comments.CommentEdited",
                userId: userId,
                data: new Dictionary<string, string?>
                {
                    ["CommentId"] = comment.Id.ToString(),
                    ["ProjectId"] = comment.ProjectId.ToString(),
                    ["AttachmentCount"] = saved.Count > 0 ? saved.Count.ToString() : "0"
                });

            return comment;
        }

        public async Task<bool> SoftDeleteAsync(int commentId, string userId, CancellationToken cancellationToken)
        {
            var comment = await _db.ProjectComments.FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken);
            if (comment == null || comment.IsDeleted)
            {
                return false;
            }

            if (!string.Equals(comment.CreatedByUserId, userId, StringComparison.Ordinal))
            {
                return false;
            }

            comment.IsDeleted = true;
            comment.EditedByUserId = userId;
            comment.EditedOn = _clock.UtcNow.UtcDateTime;

            await _db.SaveChangesAsync(cancellationToken);

            await _audit.LogAsync("Comments.CommentDeleted",
                userId: userId,
                data: new Dictionary<string, string?>
                {
                    ["CommentId"] = comment.Id.ToString(),
                    ["ProjectId"] = comment.ProjectId.ToString()
                });

            return true;
        }

        public async Task<(ProjectCommentAttachment attachment, Stream stream)?> OpenAttachmentAsync(int projectId, int commentId, int attachmentId, CancellationToken cancellationToken)
        {
            var attachment = await _db.ProjectCommentAttachments
                .Include(a => a.Comment)
                .FirstOrDefaultAsync(a => a.Id == attachmentId && a.CommentId == commentId, cancellationToken);

            if (attachment == null || attachment.Comment.IsDeleted || attachment.Comment.ProjectId != projectId)
            {
                return null;
            }

            if (!File.Exists(attachment.StoragePath))
            {
                _logger.LogWarning("Attachment file missing at path {Path} for attachment {AttachmentId}", attachment.StoragePath, attachmentId);
                return null;
            }

            var stream = new FileStream(attachment.StoragePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return (attachment, stream);
        }

        private async Task EnsureStageBelongsToProject(int projectId, int? stageId, CancellationToken cancellationToken)
        {
            if (stageId == null)
            {
                return;
            }

            var exists = await _db.ProjectStages
                .AsNoTracking()
                .AnyAsync(s => s.Id == stageId.Value && s.ProjectId == projectId, cancellationToken);

            if (!exists)
            {
                throw new InvalidOperationException("Stage not found for project.");
            }
        }

        private async Task<ProjectComment?> GetParentCommentForProjectAsync(int projectId, int? parentId, CancellationToken cancellationToken)
        {
            if (parentId == null)
            {
                return null;
            }

            var parent = await _db.ProjectComments
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == parentId.Value && c.ProjectId == projectId && !c.IsDeleted, cancellationToken);

            if (parent == null)
            {
                throw new InvalidOperationException("Parent comment not found.");
            }

            return parent;
        }

        private async Task<List<ProjectCommentAttachment>> SaveAttachmentsAsync(int projectId,
                                                                                ProjectComment comment,
                                                                                IEnumerable<IFormFile> files,
                                                                                string userId,
                                                                                DateTime timestamp,
                                                                                CancellationToken cancellationToken)
        {
            var list = new List<ProjectCommentAttachment>();
            if (files == null)
            {
                return list;
            }

            foreach (var file in files)
            {
                if (file == null || file.Length <= 0)
                {
                    continue;
                }

                if (file.Length > MaxAttachmentSizeBytes)
                {
                    throw new InvalidOperationException($"Attachment '{file.FileName}' exceeds the maximum size of {MaxAttachmentSizeBytes / (1024 * 1024)} MB.");
                }

                if (string.IsNullOrWhiteSpace(file.ContentType) || !AllowedAttachmentContentTypes.Contains(file.ContentType))
                {
                    throw new InvalidOperationException($"File type '{file.ContentType}' is not allowed.");
                }

                var originalName = file.FileName ?? string.Empty;
                if (originalName.Length > 260)
                {
                    originalName = originalName[..260];
                }

                var safeFileName = FileNameSanitizer.Sanitize(originalName);
                var storedName = $"{Guid.NewGuid():N}_{safeFileName}";
                var directory = BuildCommentDirectory(projectId, comment.Id);
                Directory.CreateDirectory(directory);
                var fullPath = Path.Combine(directory, storedName);

                await using (var target = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await file.CopyToAsync(target, cancellationToken);
                }

                list.Add(new ProjectCommentAttachment
                {
                    CommentId = comment.Id,
                    StoredFileName = storedName,
                    OriginalFileName = originalName,
                    ContentType = file.ContentType,
                    SizeBytes = file.Length,
                    StoragePath = fullPath,
                    UploadedByUserId = userId,
                    UploadedOn = timestamp
                });
            }

            return list;
        }

        private string BuildCommentDirectory(int projectId, int commentId)
        {
            return Path.Combine(_basePath, "projects", projectId.ToString(), "comments", commentId.ToString());
        }
    }
}
