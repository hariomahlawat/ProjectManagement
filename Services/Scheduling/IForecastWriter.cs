using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Scheduling;

public interface IForecastWriter
{
    Task RecomputeAsync(int projectId, string? causeStageCode, string causeType, string? userId, CancellationToken ct = default);
}
