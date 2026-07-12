using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Admin;

public sealed record AdminUserListRequest(
    string? Query,
    string? Role,
    string? Status,
    int Page = 1,
    int PageSize = 25);

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
    int Total,
    int Page,
    int PageSize)
{
    public int TotalPages => Total == 0 ? 1 : (int)Math.Ceiling(Total / (double)PageSize);
}

public interface IAdminUserQueryService
{
    Task<AdminUserListResult> GetAsync(
        AdminUserListRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminUserRow>> GetForExportAsync(
        AdminUserListRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AdminUserQueryService : IAdminUserQueryService
{
    private const int MaximumPageSize = 100;
    private const int MaximumExportRows = 100_000;

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
        var page = Math.Max(1, request.Page);
        var pageSize = request.PageSize is > 0 and <= MaximumPageSize ? request.PageSize : 25;
        var filtered = ComposeQuery(request);
        var total = await filtered.CountAsync(cancellationToken);

        if (total > 0)
        {
            page = Math.Min(page, (int)Math.Ceiling(total / (double)pageSize));
        }

        var users = await filtered
            .OrderBy(user => user.UserName)
            .ThenBy(user => user.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var rows = await BuildRowsAsync(users, cancellationToken);
        var roles = await _db.Roles.AsNoTracking()
            .Where(role => role.Name != null)
            .Select(role => role.Name!)
            .OrderBy(role => role)
            .ToListAsync(cancellationToken);

        return new(rows, roles, total, page, pageSize);
    }

    public async Task<IReadOnlyList<AdminUserRow>> GetForExportAsync(
        AdminUserListRequest request,
        CancellationToken cancellationToken = default)
    {
        var users = await ComposeQuery(request)
            .OrderBy(user => user.UserName)
            .ThenBy(user => user.Id)
            .Take(MaximumExportRows)
            .ToListAsync(cancellationToken);

        return await BuildRowsAsync(users, cancellationToken);
    }

    private IQueryable<ApplicationUser> ComposeQuery(AdminUserListRequest request)
    {
        var query = _db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var term = request.Query.Trim().ToLowerInvariant();
            var matchingRoleIds = _db.Roles.AsNoTracking()
                .Where(role => role.Name != null && role.Name.ToLower().Contains(term))
                .Select(role => role.Id);
            var matchingRoleUserIds = _db.UserRoles.AsNoTracking()
                .Where(userRole => matchingRoleIds.Contains(userRole.RoleId))
                .Select(userRole => userRole.UserId);

            query = query.Where(user =>
                (user.UserName != null && user.UserName.ToLower().Contains(term))
                || user.FullName.ToLower().Contains(term)
                || user.Rank.ToLower().Contains(term)
                || matchingRoleUserIds.Contains(user.Id));
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

        var nowUtc = _time.UtcNow;
        query = request.Status?.Trim().ToLowerInvariant() switch
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

        return query;
    }

    private async Task<IReadOnlyList<AdminUserRow>> BuildRowsAsync(
        IReadOnlyCollection<ApplicationUser> users,
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
                    .OrderBy(role => role)
                    .ToArray(),
                StringComparer.Ordinal);

        var nowUtc = _time.UtcNow;
        return users.Select(user =>
        {
            var roles = roleMap.TryGetValue(user.Id, out var resolved)
                ? resolved
                : Array.Empty<string>();
            var createdUtc = DateTime.SpecifyKind(user.CreatedUtc, DateTimeKind.Utc);
            var canDelete = (nowUtc.UtcDateTime - createdUtc).TotalHours <= _lifecycle.HardDeleteWindowHours;

            return new AdminUserRow(
                user.Id,
                user.UserName ?? string.Empty,
                user.FullName,
                user.Rank,
                roles,
                _stateResolver.Resolve(user, nowUtc),
                user.LastLoginUtc,
                user.LoginCount,
                createdUtc,
                canDelete);
        }).ToArray();
    }
}
