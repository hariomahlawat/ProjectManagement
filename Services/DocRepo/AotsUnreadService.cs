using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Services.DocRepo;

public sealed class AotsUnreadService : IAotsUnreadService
{
    // SECTION: Dependencies
    private readonly ApplicationDbContext _db;

    // SECTION: Request-level cache (scoped lifetime)
    private readonly Dictionary<string, int> _countByUserId = new(StringComparer.Ordinal);

    public AotsUnreadService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    // SECTION: Unread AOTS aggregate count
    public async Task<int> GetUnreadCountAsync(string? userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return 0;
        }

        if (_countByUserId.TryGetValue(userId, out var cachedCount))
        {
            return cachedCount;
        }

        var unreadCount = await _db.Documents
            .AsNoTracking()
            .Where(document =>
                document.IsAots &&
                !document.IsDeleted &&
                !document.IsExternal &&
                document.IsActive &&
                !_db.DocRepoAotsViews.Any(view => view.DocumentId == document.Id && view.UserId == userId))
            .CountAsync(cancellationToken);

        _countByUserId[userId] = unreadCount;

        return unreadCount;
    }
}
