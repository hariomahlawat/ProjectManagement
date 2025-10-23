using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Application.Ipr;

public interface IIprReadService
{
    Task<PagedResult<IprListRowDto>> SearchAsync(IprFilter filter, CancellationToken cancellationToken = default);

    Task<IprRecord?> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<IprKpis> GetKpisAsync(IprFilter filter, CancellationToken cancellationToken = default);
}
