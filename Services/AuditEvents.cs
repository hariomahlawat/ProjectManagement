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
    }
}

public readonly record struct AuditEvent(string Action, string? UserId, IDictionary<string, string?> Data)
{
    public Task WriteAsync(IAuditService audit, string? message = null, string level = "Info", string? userName = null)
        => audit.LogAsync(Action, message, level, UserId, userName, Data);
}
