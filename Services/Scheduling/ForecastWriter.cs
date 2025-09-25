using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Scheduling;

public class ForecastWriter : IForecastWriter
{
    public Task RecomputeAsync(int projectId, string? causeStageCode, string causeType, string? userId, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}
