using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Application.Ipr;

public interface IIprReadService
{
    Task<PagedResult<IprListRowDto>> SearchAsync(IprFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every IPR record, all statuses, in register order. The register view filters these in
    /// the browser; each row carries its status so the counting rule stays in IprStatusPolicy.
    /// </summary>
    Task<IReadOnlyList<IprListRowDto>> ListRegisterAsync(CancellationToken cancellationToken = default);

    Task<IprRecord?> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<IprKpis> GetKpisAsync(IprFilter filter, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IprExportRowDto>> GetExportAsync(IprFilter filter, CancellationToken cancellationToken = default);
}
