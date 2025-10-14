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

        public static AuditEvent VisitTypeAdded(Guid visitTypeId, string name, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitTypeId"] = visitTypeId.ToString(),
                ["Name"] = name
            };

            return new AuditEvent("ProjectOfficeReports.VisitTypeAdded", userId, data);
        }

        public static AuditEvent VisitTypeUpdated(Guid visitTypeId, string name, bool isActive, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitTypeId"] = visitTypeId.ToString(),
                ["Name"] = name,
                ["IsActive"] = isActive ? "true" : "false"
            };

            return new AuditEvent("ProjectOfficeReports.VisitTypeUpdated", userId, data);
        }

        public static AuditEvent VisitTypeDeleted(Guid visitTypeId, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitTypeId"] = visitTypeId.ToString()
            };

            return new AuditEvent("ProjectOfficeReports.VisitTypeDeleted", userId, data);
        }

        public static AuditEvent SocialMediaEventTypeAdded(Guid eventTypeId, string name, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["SocialMediaEventTypeId"] = eventTypeId.ToString(),
                ["Name"] = name
            };

            return new AuditEvent("ProjectOfficeReports.SocialMediaEventTypeAdded", userId, data);
        }

        public static AuditEvent SocialMediaEventTypeUpdated(Guid eventTypeId, string name, bool isActive, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["SocialMediaEventTypeId"] = eventTypeId.ToString(),
                ["Name"] = name,
                ["IsActive"] = isActive ? "true" : "false"
            };

            return new AuditEvent("ProjectOfficeReports.SocialMediaEventTypeUpdated", userId, data);
        }

        public static AuditEvent SocialMediaEventTypeDeleted(Guid eventTypeId, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["SocialMediaEventTypeId"] = eventTypeId.ToString()
            };

            return new AuditEvent("ProjectOfficeReports.SocialMediaEventTypeDeleted", userId, data);
        }

        public static AuditEvent VisitCreated(Guid visitId, Guid visitTypeId, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitId"] = visitId.ToString(),
                ["VisitTypeId"] = visitTypeId.ToString()
            };

            return new AuditEvent("ProjectOfficeReports.VisitCreated", userId, data);
        }

        public static AuditEvent VisitUpdated(Guid visitId, Guid visitTypeId, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitId"] = visitId.ToString(),
                ["VisitTypeId"] = visitTypeId.ToString()
            };

            return new AuditEvent("ProjectOfficeReports.VisitUpdated", userId, data);
        }

        public static AuditEvent VisitDeleted(Guid visitId, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitId"] = visitId.ToString()
            };

            return new AuditEvent("ProjectOfficeReports.VisitDeleted", userId, data);
        }

        public static AuditEvent VisitExported(
            string userId,
            Guid? visitTypeId,
            DateOnly? startDate,
            DateOnly? endDate,
            string? query,
            int count)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitTypeId"] = visitTypeId?.ToString(),
                ["StartDate"] = startDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["EndDate"] = endDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Query"] = query,
                ["Count"] = count.ToString(CultureInfo.InvariantCulture)
            };

            return new AuditEvent("ProjectOfficeReports.VisitExported", userId, data);
        }

        public static AuditEvent SocialMediaEventExported(
            string userId,
            Guid? socialMediaEventTypeId,
            DateOnly? startDate,
            DateOnly? endDate,
            string? query,
            string? platform,
            bool onlyActiveEventTypes,
            int count)
        {
            var data = new Dictionary<string, string?>
            {
                ["SocialMediaEventTypeId"] = socialMediaEventTypeId?.ToString(),
                ["StartDate"] = startDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["EndDate"] = endDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Query"] = query,
                ["Platform"] = platform,
                ["OnlyActiveEventTypes"] = onlyActiveEventTypes ? "true" : "false",
                ["Count"] = count.ToString(CultureInfo.InvariantCulture)
            };

            return new AuditEvent("ProjectOfficeReports.SocialMediaEventExported", userId, data);
        }

        public static AuditEvent VisitPhotoAdded(Guid visitId, Guid photoId, string userId, bool isCover)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitId"] = visitId.ToString(),
                ["PhotoId"] = photoId.ToString(),
                ["IsCover"] = isCover ? "true" : "false"
            };

            return new AuditEvent("ProjectOfficeReports.VisitPhotoAdded", userId, data);
        }

        public static AuditEvent VisitPhotoDeleted(Guid visitId, Guid photoId, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitId"] = visitId.ToString(),
                ["PhotoId"] = photoId.ToString()
            };

            return new AuditEvent("ProjectOfficeReports.VisitPhotoDeleted", userId, data);
        }

        public static AuditEvent VisitCoverPhotoChanged(Guid visitId, Guid photoId, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitId"] = visitId.ToString(),
                ["PhotoId"] = photoId.ToString()
            };

            return new AuditEvent("ProjectOfficeReports.VisitCoverPhotoChanged", userId, data);
        }
    }
}

public readonly record struct AuditEvent(string Action, string? UserId, IDictionary<string, string?> Data)
{
    public Task WriteAsync(IAuditService audit, string? message = null, string level = "Info", string? userName = null)
        => audit.LogAsync(Action, message, level, UserId, userName, Data);
}
