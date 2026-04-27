using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public interface IActionTaskService
{
    Task<List<ActionTaskItem>> GetTasksAsync(string userId, string role, CancellationToken cancellationToken = default);
    Task<ActionTaskItem> CreateTaskAsync(ActionTaskItem task, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(int taskId, string status, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default);
    Task SubmitTaskAsync(int taskId, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default);
    Task CloseTaskAsync(int taskId, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default);
    Task<ActionTaskItem?> GetTaskAsync(int taskId, CancellationToken cancellationToken = default);
    Task<List<ActionTaskAuditLog>> GetTaskLogsAsync(int taskId, string userId, string role, CancellationToken cancellationToken = default);
}
