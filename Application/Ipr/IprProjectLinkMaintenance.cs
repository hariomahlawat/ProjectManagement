using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Application.Ipr;

/// <summary>
/// Maintains the invariant that Repeat Build projects cannot own IPR records.
/// The IPR record itself is retained; only the invalid project association is removed.
/// </summary>
public static class IprProjectLinkMaintenance
{
    public static async Task<int> DetachLinkedRecordsAsync(
        ApplicationDbContext db,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (projectId <= 0)
        {
            return 0;
        }

        var linkedRecords = await db.IprRecords
            .Where(record => record.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        foreach (var record in linkedRecords)
        {
            record.ProjectId = null;
        }

        return linkedRecords.Count;
    }
}
