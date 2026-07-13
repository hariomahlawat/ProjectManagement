using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Admin;

public sealed record AdminUserListRequest(
    string? Query,
    string? Role,
    string? Status,
    int Page = 1,
    int PageSize = 25);

public sealed record AdminUserSummary(
    int Total,
    int Active,
    int MustChangePassword,
    int TemporarilyLocked,
    int Disabled,
    int PendingDeletion)
{
    public int Restricted => MustChangePassword + TemporarilyLocked + Disabled + PendingDeletion;

    public int CountFor(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "active" => Active,
        "must-change-password" => MustChangePassword,
        "locked" => TemporarilyLocked,
        "disabled" => Disabled,
        "pending-deletion" => PendingDeletion,
        _ => Total
    };
}

public sealed record AdminUserRow(
    string Id,
    string UserName,
    string FullName,
    string Rank,
    IReadOnlyList<string> Roles,
    AdminUserAccountStateInfo AccountState,
    DateTime? LastLoginUtc,
    int LoginCount,
    DateTime CreatedUtc,
    bool CanRequestHardDelete);

public sealed record AdminUserListResult(
    IReadOnlyList<AdminUserRow> Rows,
    IReadOnlyList<string> Roles,
    AdminUserSummary Summary,
    int Total,
    int Page,
    int PageSize)
{
    public int TotalPages => Total == 0 ? 1 : (int)Math.Ceiling(Total / (double)PageSize);
}

public sealed record AdminUserDetails(
    string Id,
    string UserName,
    string FullName,
    string Rank,
    IReadOnlyList<string> Roles,
    AdminUserAccountStateInfo AccountState,
    DateTime CreatedUtc,
    DateTime? LastLoginUtc,
    int LoginCount,
    int AccessFailedCount,
    DateTimeOffset? LockoutEndUtc,
    DateTime? DisabledUtc,
    string? DisabledBy,
    DateTime? DeletionRequestedUtc,
    string? DeletionRequestedBy,
    DateTime? ScheduledPurgeUtc,
    bool MustChangePassword,
    bool CanRequestHardDelete);

public sealed record AdminUserAuthenticationActivity(
    string Event,
    bool Succeeded,
    DateTimeOffset WhenUtc,
    string? Ip,
    string? UserAgent);

public sealed record AdminUserAdministrativeActivity(
    long Id,
    string Level,
    string Action,
    string Message,
    string Actor,
    DateTime WhenUtc);

public interface IAdminUserQueryService
{
    Task<AdminUserListResult> GetAsync(
        AdminUserListRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminUserSummary> GetSummaryAsync(
        AdminUserListRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminUserRow>> GetForExportAsync(
        AdminUserListRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminUserDetails?> GetDetailsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminUserAuthenticationActivity>> GetRecentLoginActivityAsync(
        string userId,
        int limit = 12,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminUserAdministrativeActivity>> GetRecentAdministrativeActivityAsync(
        string userId,
        int limit = 12,
        CancellationToken cancellationToken = default);
}

public sealed class AdminUserQueryService : IAdminUserQueryService
{
    private const int DefaultPageSize = 25;
    private const int MaximumPageSize = 100;
    private const int MaximumExportRows = 100_000;
    private const int MaximumActivityRows = 50;

    private readonly ApplicationDbContext _db;
    private readonly IUserAccountStateResolver _stateResolver;
    private readonly IAdminTimeService _time;
    private readonly UserLifecycleOptions _lifecycle;

    public AdminUserQueryService(
        ApplicationDbContext db,
        IUserAccountStateResolver stateResolver,
        IAdminTimeService time,
        IOptions<UserLifecycleOptions> lifecycle)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _stateResolver = stateResolver ?? throw new ArgumentNullException(nameof(stateResolver));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _lifecycle = lifecycle?.Value ?? throw new ArgumentNullException(nameof(lifecycle));
    }

    public async Task<AdminUserListResult> GetAsync(
        AdminUserListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = Math.Max(1, request.Page);
        var pageSize = NormalizePageSize(request.PageSize);
        var baseQuery = ComposeBaseQuery(request);
        var summary = await BuildSummaryAsync(baseQuery, cancellationToken);
        var filtered = ApplyStatus(baseQuery, request.Status);
        var total = await filtered.CountAsync(cancellationToken);

        page = total > 0
            ? Math.Min(page, (int)Math.Ceiling(total / (double)pageSize))
            : 1;

        var users = await filtered
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.UserName)
            .ThenBy(user => user.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new UserProjection
            {
                Id = user.Id,
                UserName = user.UserName,
                FullName = user.FullName,
                Rank = user.Rank,
                MustChangePassword = user.MustChangePassword,
                LastLoginUtc = user.LastLoginUtc,
                LoginCount = user.LoginCount,
                CreatedUtc = user.CreatedUtc,
                IsDisabled = user.IsDisabled,
                PendingDeletion = user.PendingDeletion,
                LockoutEnd = user.LockoutEnd
            })
            .ToListAsync(cancellationToken);

        var rows = await BuildRowsAsync(users, cancellationToken);
        var roles = await GetAvailableRolesAsync(cancellationToken);

        return new AdminUserListResult(rows, roles, summary, total, page, pageSize);
    }

    public Task<AdminUserSummary> GetSummaryAsync(
        AdminUserListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return BuildSummaryAsync(ComposeBaseQuery(request), cancellationToken);
    }

    public async Task<IReadOnlyList<AdminUserRow>> GetForExportAsync(
        AdminUserListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var users = await ApplyStatus(ComposeBaseQuery(request), request.Status)
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.UserName)
            .ThenBy(user => user.Id)
            .Take(MaximumExportRows)
            .Select(user => new UserProjection
            {
                Id = user.Id,
                UserName = user.UserName,
                FullName = user.FullName,
                Rank = user.Rank,
                MustChangePassword = user.MustChangePassword,
                LastLoginUtc = user.LastLoginUtc,
                LoginCount = user.LoginCount,
                CreatedUtc = user.CreatedUtc,
                IsDisabled = user.IsDisabled,
                PendingDeletion = user.PendingDeletion,
                LockoutEnd = user.LockoutEnd
            })
            .ToListAsync(cancellationToken);

        return await BuildRowsAsync(users, cancellationToken);
    }

    public async Task<AdminUserDetails?> GetDetailsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var projection = await _db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserDetailsProjection
            {
                Id = user.Id,
                UserName = user.UserName,
                FullName = user.FullName,
                Rank = user.Rank,
                MustChangePassword = user.MustChangePassword,
                LastLoginUtc = user.LastLoginUtc,
                LoginCount = user.LoginCount,
                CreatedUtc = user.CreatedUtc,
                AccessFailedCount = user.AccessFailedCount,
                IsDisabled = user.IsDisabled,
                DisabledUtc = user.DisabledUtc,
                DisabledByUserId = user.DisabledByUserId,
                PendingDeletion = user.PendingDeletion,
                DeletionRequestedUtc = user.DeletionRequestedUtc,
                DeletionRequestedByUserId = user.DeletionRequestedByUserId,
                LockoutEnd = user.LockoutEnd
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (projection is null)
        {
            return null;
        }

        var roles = await GetRolesForUserAsync(userId, cancellationToken);
        var relatedUserIds = new[]
            {
                projection.DisabledByUserId,
                projection.DeletionRequestedByUserId
            }
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var relatedUsers = new Dictionary<string, string>(StringComparer.Ordinal);
        if (relatedUserIds.Length > 0)
        {
            var relatedRows = await _db.Users.AsNoTracking()
                .Where(user => relatedUserIds.Contains(user.Id))
                .Select(user => new { user.Id, user.UserName, user.FullName })
                .ToListAsync(cancellationToken);

            relatedUsers = relatedRows.ToDictionary(
                user => user.Id,
                user => string.IsNullOrWhiteSpace(user.FullName)
                    ? user.UserName ?? "Unknown user"
                    : user.FullName,
                StringComparer.Ordinal);
        }

        var nowUtc = _time.UtcNow;
        var createdUtc = EnsureUtc(projection.CreatedUtc);
        var accountAgeHours = (nowUtc.UtcDateTime - createdUtc).TotalHours;
        var canRequestHardDelete = !projection.PendingDeletion
            && accountAgeHours is >= 0
            && accountAgeHours <= _lifecycle.HardDeleteWindowHours;
        DateTime? scheduledPurgeUtc = projection.PendingDeletion && projection.DeletionRequestedUtc.HasValue
            ? EnsureUtc(projection.DeletionRequestedUtc.Value).AddMinutes(_lifecycle.UndoWindowMinutes)
            : null;

        return new AdminUserDetails(
            projection.Id,
            projection.UserName ?? string.Empty,
            projection.FullName,
            projection.Rank,
            roles,
            ResolveState(projection),
            createdUtc,
            projection.LastLoginUtc.HasValue ? EnsureUtc(projection.LastLoginUtc.Value) : null,
            projection.LoginCount,
            projection.AccessFailedCount,
            projection.LockoutEnd,
            projection.DisabledUtc.HasValue ? EnsureUtc(projection.DisabledUtc.Value) : null,
            ResolveRelatedUser(projection.DisabledByUserId, relatedUsers),
            projection.DeletionRequestedUtc.HasValue ? EnsureUtc(projection.DeletionRequestedUtc.Value) : null,
            ResolveRelatedUser(projection.DeletionRequestedByUserId, relatedUsers),
            scheduledPurgeUtc,
            projection.MustChangePassword,
            canRequestHardDelete);
    }

    public async Task<IReadOnlyList<AdminUserAuthenticationActivity>> GetRecentLoginActivityAsync(
        string userId,
        int limit = 12,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<AdminUserAuthenticationActivity>();
        }

        var boundedLimit = Math.Clamp(limit, 1, MaximumActivityRows);
        var userName = await _db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.UserName)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Array.Empty<AdminUserAuthenticationActivity>();
        }

        var successful = await _db.AuthEvents.AsNoTracking()
            .Where(authEvent => authEvent.UserId == userId
                && authEvent.Event == AuthenticationEventNames.LoginSucceeded)
            .OrderByDescending(authEvent => authEvent.WhenUtc)
            .Take(boundedLimit)
            .Select(authEvent => new AdminUserAuthenticationActivity(
                authEvent.Event,
                true,
                authEvent.WhenUtc,
                authEvent.Ip,
                authEvent.UserAgent))
            .ToListAsync(cancellationToken);

        var failed = await _db.AuditLogs.AsNoTracking()
            .Where(audit =>
                (audit.UserId == userId || audit.UserName == userName)
                && (audit.Action == AuthenticationEventNames.AuditLoginFailed
                    || audit.Action == AuthenticationEventNames.AuditLoginLockedOut))
            .OrderByDescending(audit => audit.TimeUtc)
            .Take(boundedLimit)
            .Select(audit => new
            {
                audit.Action,
                audit.TimeUtc,
                audit.Ip,
                audit.UserAgent
            })
            .ToListAsync(cancellationToken);

        return successful
            .Concat(failed.Select(audit => new AdminUserAuthenticationActivity(
                audit.Action,
                false,
                new DateTimeOffset(EnsureUtc(audit.TimeUtc)),
                audit.Ip,
                audit.UserAgent)))
            .OrderByDescending(activity => activity.WhenUtc)
            .Take(boundedLimit)
            .ToArray();
    }

    public async Task<IReadOnlyList<AdminUserAdministrativeActivity>> GetRecentAdministrativeActivityAsync(
        string userId,
        int limit = 12,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<AdminUserAdministrativeActivity>();
        }

        var boundedLimit = Math.Clamp(limit, 1, MaximumActivityRows);
        var entityIdToken = $"\"EntityId\":\"{userId}\"";

        return await _db.AuditLogs.AsNoTracking()
            .Where(audit =>
                audit.Action.StartsWith("AdminUser")
                && ((audit.DataJson != null && audit.DataJson.Contains(entityIdToken))
                    || (audit.DataJson == null && audit.UserId == userId)))
            .OrderByDescending(audit => audit.TimeUtc)
            .ThenByDescending(audit => audit.Id)
            .Take(boundedLimit)
            .Select(audit => new AdminUserAdministrativeActivity(
                audit.Id,
                audit.Level,
                audit.Action,
                audit.Message ?? audit.Action,
                audit.UserName ?? "System",
                audit.TimeUtc))
            .ToListAsync(cancellationToken);
    }

    private IQueryable<ApplicationUser> ComposeBaseQuery(AdminUserListRequest request)
    {
        var query = _db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var term = request.Query.Trim();

            if (_db.Database.IsNpgsql())
            {
                var pattern = $"%{term}%";
                var matchingRoleIds = _db.Roles.AsNoTracking()
                    .Where(role => role.Name != null && EF.Functions.ILike(role.Name, pattern))
                    .Select(role => role.Id);
                var matchingRoleUserIds = _db.UserRoles.AsNoTracking()
                    .Where(userRole => matchingRoleIds.Contains(userRole.RoleId))
                    .Select(userRole => userRole.UserId);

                query = query.Where(user =>
                    (user.UserName != null && EF.Functions.ILike(user.UserName, pattern))
                    || EF.Functions.ILike(user.FullName, pattern)
                    || EF.Functions.ILike(user.Rank, pattern)
                    || matchingRoleUserIds.Contains(user.Id));
            }
            else
            {
                var normalized = term.ToLowerInvariant();
                var matchingRoleIds = _db.Roles.AsNoTracking()
                    .Where(role => role.Name != null && role.Name.ToLower().Contains(normalized))
                    .Select(role => role.Id);
                var matchingRoleUserIds = _db.UserRoles.AsNoTracking()
                    .Where(userRole => matchingRoleIds.Contains(userRole.RoleId))
                    .Select(userRole => userRole.UserId);

                query = query.Where(user =>
                    (user.UserName != null && user.UserName.ToLower().Contains(normalized))
                    || user.FullName.ToLower().Contains(normalized)
                    || user.Rank.ToLower().Contains(normalized)
                    || matchingRoleUserIds.Contains(user.Id));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            var requestedRole = request.Role.Trim().ToUpperInvariant();
            var roleIds = _db.Roles.AsNoTracking()
                .Where(role => role.NormalizedName == requestedRole)
                .Select(role => role.Id);
            var userIds = _db.UserRoles.AsNoTracking()
                .Where(userRole => roleIds.Contains(userRole.RoleId))
                .Select(userRole => userRole.UserId);
            query = query.Where(user => userIds.Contains(user.Id));
        }

        return query;
    }

    private IQueryable<ApplicationUser> ApplyStatus(
        IQueryable<ApplicationUser> query,
        string? status)
    {
        var nowUtc = _time.UtcNow;
        return status?.Trim().ToLowerInvariant() switch
        {
            "active" => query.Where(user =>
                !user.PendingDeletion
                && !user.IsDisabled
                && (!user.LockoutEnd.HasValue || user.LockoutEnd <= nowUtc)
                && !user.MustChangePassword),
            "must-change-password" => query.Where(user =>
                !user.PendingDeletion
                && !user.IsDisabled
                && (!user.LockoutEnd.HasValue || user.LockoutEnd <= nowUtc)
                && user.MustChangePassword),
            "locked" => query.Where(user =>
                !user.PendingDeletion
                && !user.IsDisabled
                && user.LockoutEnd.HasValue
                && user.LockoutEnd > nowUtc),
            "disabled" => query.Where(user => !user.PendingDeletion && user.IsDisabled),
            "pending-deletion" => query.Where(user => user.PendingDeletion),
            _ => query
        };
    }

    private async Task<AdminUserSummary> BuildSummaryAsync(
        IQueryable<ApplicationUser> query,
        CancellationToken cancellationToken)
    {
        var nowUtc = _time.UtcNow;
        var projection = await query
            .GroupBy(_ => 1)
            .Select(group => new SummaryProjection
            {
                Total = group.Count(),
                PendingDeletion = group.Count(user => user.PendingDeletion),
                Disabled = group.Count(user => !user.PendingDeletion && user.IsDisabled),
                TemporarilyLocked = group.Count(user =>
                    !user.PendingDeletion
                    && !user.IsDisabled
                    && user.LockoutEnd.HasValue
                    && user.LockoutEnd > nowUtc),
                MustChangePassword = group.Count(user =>
                    !user.PendingDeletion
                    && !user.IsDisabled
                    && (!user.LockoutEnd.HasValue || user.LockoutEnd <= nowUtc)
                    && user.MustChangePassword),
                Active = group.Count(user =>
                    !user.PendingDeletion
                    && !user.IsDisabled
                    && (!user.LockoutEnd.HasValue || user.LockoutEnd <= nowUtc)
                    && !user.MustChangePassword)
            })
            .SingleOrDefaultAsync(cancellationToken);

        return projection is null
            ? new AdminUserSummary(0, 0, 0, 0, 0, 0)
            : new AdminUserSummary(
                projection.Total,
                projection.Active,
                projection.MustChangePassword,
                projection.TemporarilyLocked,
                projection.Disabled,
                projection.PendingDeletion);
    }

    private async Task<IReadOnlyList<AdminUserRow>> BuildRowsAsync(
        IReadOnlyCollection<UserProjection> users,
        CancellationToken cancellationToken)
    {
        if (users.Count == 0)
        {
            return Array.Empty<AdminUserRow>();
        }

        var userIds = users.Select(user => user.Id).ToArray();
        var rolePairs = await (
                from userRole in _db.UserRoles.AsNoTracking()
                join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                where userIds.Contains(userRole.UserId) && role.Name != null
                select new { userRole.UserId, RoleName = role.Name! })
            .ToListAsync(cancellationToken);

        var roleMap = rolePairs
            .GroupBy(pair => pair.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(pair => pair.RoleName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.Ordinal);

        var nowUtc = _time.UtcNow;
        return users.Select(user =>
        {
            var roles = roleMap.TryGetValue(user.Id, out var resolved)
                ? resolved
                : Array.Empty<string>();
            var createdUtc = EnsureUtc(user.CreatedUtc);
            var accountAgeHours = (nowUtc.UtcDateTime - createdUtc).TotalHours;
            var canDelete = !user.PendingDeletion
                && accountAgeHours is >= 0
                && accountAgeHours <= _lifecycle.HardDeleteWindowHours;

            return new AdminUserRow(
                user.Id,
                user.UserName ?? string.Empty,
                user.FullName,
                user.Rank,
                roles,
                ResolveState(user),
                user.LastLoginUtc.HasValue ? EnsureUtc(user.LastLoginUtc.Value) : null,
                user.LoginCount,
                createdUtc,
                canDelete);
        }).ToArray();
    }

    private async Task<IReadOnlyList<string>> GetRolesForUserAsync(
        string userId,
        CancellationToken cancellationToken) =>
        await (
            from userRole in _db.UserRoles.AsNoTracking()
            join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == userId && role.Name != null
            orderby role.Name
            select role.Name!)
        .Distinct()
        .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<string>> GetAvailableRolesAsync(CancellationToken cancellationToken) =>
        await _db.Roles.AsNoTracking()
            .Where(role => role.Name != null)
            .Select(role => role.Name!)
            .OrderBy(role => role)
            .ToListAsync(cancellationToken);

    private AdminUserAccountStateInfo ResolveState(UserProjection user) =>
        _stateResolver.Resolve(
            new ApplicationUser
            {
                PendingDeletion = user.PendingDeletion,
                IsDisabled = user.IsDisabled,
                LockoutEnd = user.LockoutEnd,
                MustChangePassword = user.MustChangePassword
            },
            _time.UtcNow);

    private AdminUserAccountStateInfo ResolveState(UserDetailsProjection user) =>
        _stateResolver.Resolve(
            new ApplicationUser
            {
                PendingDeletion = user.PendingDeletion,
                IsDisabled = user.IsDisabled,
                LockoutEnd = user.LockoutEnd,
                MustChangePassword = user.MustChangePassword
            },
            _time.UtcNow);

    private static string? ResolveRelatedUser(
        string? userId,
        IReadOnlyDictionary<string, string> users) =>
        string.IsNullOrWhiteSpace(userId)
            ? null
            : users.TryGetValue(userId, out var displayName)
                ? displayName
                : "Unknown user";

    private static int NormalizePageSize(int pageSize) =>
        pageSize is > 0 and <= MaximumPageSize ? pageSize : DefaultPageSize;

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed class SummaryProjection
    {
        public int Total { get; init; }
        public int Active { get; init; }
        public int MustChangePassword { get; init; }
        public int TemporarilyLocked { get; init; }
        public int Disabled { get; init; }
        public int PendingDeletion { get; init; }
    }

    private class UserProjection
    {
        public string Id { get; init; } = string.Empty;
        public string? UserName { get; init; }
        public string FullName { get; init; } = string.Empty;
        public string Rank { get; init; } = string.Empty;
        public bool MustChangePassword { get; init; }
        public DateTime? LastLoginUtc { get; init; }
        public int LoginCount { get; init; }
        public DateTime CreatedUtc { get; init; }
        public bool IsDisabled { get; init; }
        public bool PendingDeletion { get; init; }
        public DateTimeOffset? LockoutEnd { get; init; }
    }

    private sealed class UserDetailsProjection : UserProjection
    {
        public int AccessFailedCount { get; init; }
        public DateTime? DisabledUtc { get; init; }
        public string? DisabledByUserId { get; init; }
        public DateTime? DeletionRequestedUtc { get; init; }
        public string? DeletionRequestedByUserId { get; init; }
    }
}
