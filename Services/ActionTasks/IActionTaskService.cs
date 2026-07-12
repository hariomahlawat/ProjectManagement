using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public interface IActionTaskService
{
    Task<List<ActionTaskItem>> GetTasksAsync(string userId, string role, CancellationToken cancellationToken = default);
    Task<ActionTaskItem> CreateTaskAsync(ActionTaskItem task, CancellationToken cancellationToken = default);
    Task<ActionTaskItem> CreateTaskAsync(ActionTaskItem task, string? auditRemarks, CancellationToken cancellationToken = default);
    Task<ActionTaskItem> CreateBacklogItemAsync(ActionTaskItem task, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(int taskId, byte[] rowVersion, string status, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default);
    Task SubmitTaskAsync(int taskId, byte[] rowVersion, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default);
    Task CloseTaskAsync(int taskId, byte[] rowVersion, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default);
    Task CloseTaskDirectlyAsync(int taskId, byte[] rowVersion, string closureRemarks, string closedByUserId, string role, CancellationToken cancellationToken = default);
    Task UpdateTaskDateAsync(int taskId, byte[] rowVersion, DateTime newDate, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default);
    Task<ActionTaskItem?> GetTaskAsync(int taskId, CancellationToken cancellationToken = default);
    Task<List<ActionTaskAuditLog>> GetTaskLogsAsync(int taskId, string userId, string role, CancellationToken cancellationToken = default);
    Task<Dictionary<int, DateTime?>> GetLastActivityUtcByTaskIdsAsync(IReadOnlyCollection<int> taskIds, CancellationToken cancellationToken = default);
}
