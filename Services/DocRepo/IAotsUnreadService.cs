using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.DocRepo;

public interface IAotsUnreadService
{
    // SECTION: Unread AOTS aggregate count
    Task<int> GetUnreadCountAsync(string? userId, CancellationToken cancellationToken = default);
}
