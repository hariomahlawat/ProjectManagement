using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services;

public static class Audit
{
    public static class Events
    {
        public static AuditEvent DraftDeleted(int projectId, int planVersionId, string userId, DateTimeOffset deletedAt)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["PlanVersionId"] = planVersionId.ToString(),
                ["DeletedOnUtc"] = deletedAt.UtcDateTime.ToString("O")
            };

            return new AuditEvent("Plan.DraftDeleted", userId, data);
        }

        public static AuditEvent ProjectPhotoAdded(int projectId, int photoId, string userId, bool isCover)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["PhotoId"] = photoId.ToString(),
                ["IsCover"] = isCover ? "true" : "false"
            };

            return new AuditEvent("Project.PhotoAdded", userId, data);
        }

        public static AuditEvent ProjectPhotoUpdated(int projectId, int photoId, string userId, string changeType)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["PhotoId"] = photoId.ToString(),
                ["ChangeType"] = changeType
            };

            return new AuditEvent("Project.PhotoUpdated", userId, data);
        }

        public static AuditEvent ProjectPhotoRemoved(int projectId, int photoId, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["PhotoId"] = photoId.ToString()
            };

            return new AuditEvent("Project.PhotoRemoved", userId, data);
        }

        public static AuditEvent ProjectPhotoReordered(int projectId, string userId, IEnumerable<int> orderedPhotoIds)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["Order"] = string.Join(',', orderedPhotoIds)
            };

            return new AuditEvent("Project.PhotoReordered", userId, data);
        }

        public static AuditEvent ProjectLifecycleMarkedCompleted(int projectId, string userId, int? completionYear)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["CompletionYear"] = completionYear?.ToString(CultureInfo.InvariantCulture)
            };

            return new AuditEvent("Project.LifecycleCompleted", userId, data);
        }

        public static AuditEvent ProjectLifecycleCompletionEndorsed(int projectId, string userId, DateOnly completionDate, int? completionYear)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["CompletionDate"] = completionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["CompletionYear"] = completionYear?.ToString(CultureInfo.InvariantCulture)
            };

            return new AuditEvent("Project.LifecycleCompletionEndorsed", userId, data);
        }

        public static AuditEvent ProjectLifecycleCancelled(int projectId, string userId, DateOnly cancelledOn, string reason)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["CancelledOn"] = cancelledOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Reason"] = reason
            };

            return new AuditEvent("Project.LifecycleCancelled", userId, data);
        }

        public static AuditEvent ProjectDocumentRequested(int projectId, int requestId, string userId, ProjectDocumentRequestType type, int? documentId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["RequestId"] = requestId.ToString(),
                ["Type"] = type.ToString(),
                ["DocumentId"] = documentId?.ToString()
            };

            return new AuditEvent("Project.DocumentRequested", userId, data);
        }

        public static AuditEvent ProjectDocumentApproved(int projectId, int requestId, string userId, ProjectDocumentRequestType type, int? documentId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["RequestId"] = requestId.ToString(),
                ["Type"] = type.ToString(),
                ["DocumentId"] = documentId?.ToString()
            };

            return new AuditEvent("Project.DocumentApproved", userId, data);
        }

        public static AuditEvent ProjectDocumentRejected(int projectId, int requestId, string userId, ProjectDocumentRequestType type, int? documentId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["RequestId"] = requestId.ToString(),
                ["Type"] = type.ToString(),
                ["DocumentId"] = documentId?.ToString()
            };

            return new AuditEvent("Project.DocumentRejected", userId, data);
        }

        public static AuditEvent ProjectDocumentPublished(int projectId, int documentId, string? userId, int fileStamp)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["DocumentId"] = documentId.ToString(),
                ["FileStamp"] = fileStamp.ToString()
            };

            return new AuditEvent("Project.DocumentPublished", userId, data);
        }

        public static AuditEvent ProjectDocumentReplaced(int projectId, int documentId, string? userId, int fileStamp)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["DocumentId"] = documentId.ToString(),
                ["FileStamp"] = fileStamp.ToString()
            };

            return new AuditEvent("Project.DocumentReplaced", userId, data);
        }

        public static AuditEvent ProjectDocumentRemoved(int projectId, int documentId, string? userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["DocumentId"] = documentId.ToString()
            };

            return new AuditEvent("Project.DocumentRemoved", userId, data);
        }

        public static AuditEvent ProjectDocumentRestored(int projectId, int documentId, string? userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["DocumentId"] = documentId.ToString()
            };

            return new AuditEvent("Project.DocumentRestored", userId, data);
        }

        public static AuditEvent ProjectDocumentHardDeleted(int projectId, int documentId, string? userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["DocumentId"] = documentId.ToString()
            };

            return new AuditEvent("Project.DocumentHardDeleted", userId, data);
        }

        public static AuditEvent ProjectDocumentsRestoredBulk(string? userId, IReadOnlyCollection<int> documentIds)
        {
            var data = new Dictionary<string, string?>
            {
                ["Count"] = documentIds.Count.ToString(CultureInfo.InvariantCulture),
                ["DocumentIds"] = string.Join(',', documentIds)
            };

            return new AuditEvent("Project.DocumentRestoredBulk", userId, data);
        }

        public static AuditEvent ProjectDocumentsHardDeletedBulk(string? userId, IReadOnlyCollection<int> documentIds)
        {
            var data = new Dictionary<string, string?>
            {
                ["Count"] = documentIds.Count.ToString(CultureInfo.InvariantCulture),
                ["DocumentIds"] = string.Join(',', documentIds)
            };

            return new AuditEvent("Project.DocumentHardDeletedBulk", userId, data);
        }
    }
}

public readonly record struct AuditEvent(string Action, string? UserId, IDictionary<string, string?> Data)
{
    public Task WriteAsync(IAuditService audit, string? message = null, string level = "Info", string? userName = null)
        => audit.LogAsync(Action, message, level, UserId, userName, Data);
}
