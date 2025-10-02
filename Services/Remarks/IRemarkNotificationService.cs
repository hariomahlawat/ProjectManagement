using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.Remarks;

namespace ProjectManagement.Services.Remarks;

public interface IRemarkNotificationService
{
    Task NotifyRemarkCreatedAsync(
        Remark remark,
        RemarkActorContext actor,
        RemarkProjectInfo project,
        CancellationToken cancellationToken = default);
}

public sealed record RemarkProjectInfo(int ProjectId, string ProjectName, string? LeadPoUserId, string? HodUserId);
