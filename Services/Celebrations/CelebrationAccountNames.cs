using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Services.Celebrations;

/// <summary>
/// The accounts a celebration can belong to, by id: people who can sign in today, each as
/// "Col R Sharma". The one place that decides who counts, for linking and for the name a linked
/// celebration is shown under (CelebrationHelpers.DisplayName).
/// </summary>
public static class CelebrationAccountNames
{
    public static async Task<IReadOnlyDictionary<string, string>> LoadAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken = default)
    {
        var users = await db.Users.AsNoTracking()
            .Where(user => user.AccountKind == UserAccountKind.Human && !user.IsDisabled && !user.PendingDeletion)
            .Select(user => new { user.Id, user.Rank, user.FullName, user.UserName })
            .ToListAsync(cancellationToken);

        return users.ToDictionary(
            user => user.Id,
            user => CelebrationUserMatcher.Label(user.Rank, user.FullName, user.UserName),
            StringComparer.Ordinal);
    }
}
