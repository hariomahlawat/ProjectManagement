using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.DocRepo;

public interface IAotsUnreadService
{
    // SECTION: Unread AOTS aggregate count
    Task<int> GetUnreadCountAsync(string? userId, CancellationToken cancellationToken = default);

    // SECTION: AOTS read-state tracking
    Task<bool> MarkAsReadAsync(Guid documentId, string? userId, CancellationToken cancellationToken = default);
}
