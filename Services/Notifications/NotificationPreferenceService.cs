using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Notifications;

namespace ProjectManagement.Services.Notifications;

public sealed class NotificationPreferenceService : INotificationPreferenceService
{
    private readonly ApplicationDbContext _db;

    public NotificationPreferenceService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<bool> AllowsAsync(
        NotificationKind kind,
        string userId,
        int? projectId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (projectId.HasValue)
        {
            var muted = await _db.UserProjectMutes
                .AsNoTracking()
                .AnyAsync(
                    m => m.UserId == userId && m.ProjectId == projectId.Value,
                    cancellationToken);

            if (muted)
            {
                return false;
            }
        }

        var preference = await _db.UserNotificationPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.Kind == kind)
            .Select(p => (bool?)p.Allow)
            .FirstOrDefaultAsync(cancellationToken);

        if (preference.HasValue)
        {
            return preference.Value;
        }

        var claimType = GetClaimType(kind);
        if (claimType is null)
        {
            return true;
        }

        var hasOptOutClaim = await _db.Set<IdentityUserClaim<string>>()
            .AsNoTracking()
            .AnyAsync(
                c =>
                    c.UserId == userId &&
                    c.ClaimType == claimType &&
                    c.ClaimValue == NotificationClaimTypes.OptOutValue,
                cancellationToken);

        return !hasOptOutClaim;
    }

    private static string? GetClaimType(NotificationKind kind)
        => kind switch
        {
            NotificationKind.RemarkCreated => NotificationClaimTypes.RemarkCreatedOptOut,
            _ => null
        };
}
