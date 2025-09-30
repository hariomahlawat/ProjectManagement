using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
    }
}

public readonly record struct AuditEvent(string Action, string? UserId, IDictionary<string, string?> Data)
{
    public Task WriteAsync(IAuditService audit, string? message = null, string level = "Info", string? userName = null)
        => audit.LogAsync(Action, message, level, UserId, userName, Data);
}
