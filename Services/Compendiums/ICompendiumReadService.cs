using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Compendiums;

public interface ICompendiumReadService
{
    Task<CompendiumPdfDataDto> GetProliferationCompendiumAsync(CancellationToken cancellationToken = default);
}
