using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Admin;

public sealed record AdminLogQuery(
    string? Level,
    string? Action,
    string? UserName,
    string? UserId,
    string? Ip,
    string? Contains,
    DateOnly? From,
    DateOnly? To,
    int Page = 1,
    int PageSize = 25,
    string Sort = "Time",
    string Direction = "desc");

public sealed record AdminLogRow(
    DateTime TimeUtc,
    string Level,
    string Action,
    string? UserId,
    string? UserName,
    string? Ip,
    string? Message,
    string? DataJson);

public sealed record AdminLogResult(
    IReadOnlyList<AdminLogRow> Rows,
    IReadOnlyList<string> ActionOptions,
    IReadOnlyList<string> SeriesLabels,
    IReadOnlyList<int> SeriesCounts,
    int Total,
    int Page,
    int PageSize)
{
    public int TotalPages => Total == 0 ? 1 : (int)Math.Ceiling(Total / (double)PageSize);
}

public interface IAdminLogQueryService
{
    Task<AdminLogResult> GetAsync(AdminLogQuery request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminLogRow>> GetForExportAsync(AdminLogQuery request, int maximumRows, CancellationToken cancellationToken = default);
}

public sealed class AdminLogQueryService : IAdminLogQueryService
{
    private const int MaximumPageSize = 200;
    private readonly ApplicationDbContext _db;
    private readonly IAdminTimeService _time;

    public AdminLogQueryService(ApplicationDbContext db, IAdminTimeService time)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public async Task<AdminLogResult> GetAsync(
        AdminLogQuery request,
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

        var rows = await ApplySort(filtered, request.Sort, request.Direction)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(log => new AdminLogRow(
                log.TimeUtc,
                log.Level,
                log.Action,
                log.UserId,
                log.UserName,
                log.Ip,
                log.Message,
                log.DataJson))
            .ToListAsync(cancellationToken);

        var actionOptions = await _db.AuditLogs.AsNoTracking()
            .Select(log => log.Action)
            .Distinct()
            .OrderBy(action => action)
            .Take(250)
            .ToListAsync(cancellationToken);

        var seriesTimes = await filtered
            .Select(log => log.TimeUtc)
            .ToListAsync(cancellationToken);

        var perDay = seriesTimes
            .GroupBy(timeUtc => DateOnly.FromDateTime(_time.ToIst(timeUtc)))
            .OrderBy(group => group.Key)
            .Select(group => new { Day = group.Key, Count = group.Count() })
            .ToList();

        return new AdminLogResult(
            rows,
            actionOptions,
            perDay.Select(item => item.Day.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)).ToArray(),
            perDay.Select(item => item.Count).ToArray(),
            total,
            page,
            pageSize);
    }

    public async Task<IReadOnlyList<AdminLogRow>> GetForExportAsync(
        AdminLogQuery request,
        int maximumRows,
        CancellationToken cancellationToken = default)
    {
        var safeMaximum = Math.Clamp(maximumRows, 1, 100_000);
        return await ApplySort(ComposeQuery(request), "Time", "desc")
            .Take(safeMaximum)
            .Select(log => new AdminLogRow(
                log.TimeUtc,
                log.Level,
                log.Action,
                log.UserId,
                log.UserName,
                log.Ip,
                log.Message,
                log.DataJson))
            .ToListAsync(cancellationToken);
    }

    private IQueryable<AuditLog> ComposeQuery(AdminLogQuery request)
    {
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Level))
        {
            query = query.Where(log => log.Level == request.Level);
        }

        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            query = query.Where(log => log.Action == request.Action);
        }

        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            query = query.Where(log => log.UserId == request.UserId);
        }
        else if (!string.IsNullOrWhiteSpace(request.UserName))
        {
            var term = request.UserName.Trim().ToLowerInvariant();
            query = query.Where(log => log.UserName != null && log.UserName.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(request.Ip))
        {
            var term = request.Ip.Trim().ToLowerInvariant();
            query = query.Where(log => log.Ip != null && log.Ip.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(request.Contains))
        {
            var term = request.Contains.Trim().ToLowerInvariant();
            query = query.Where(log =>
                (log.Message != null && log.Message.ToLower().Contains(term))
                || (log.DataJson != null && log.DataJson.ToLower().Contains(term)));
        }

        if (request.From.HasValue)
        {
            var fromUtc = _time.StartOfIstDayUtc(request.From.Value).UtcDateTime;
            query = query.Where(log => log.TimeUtc >= fromUtc);
        }

        if (request.To.HasValue)
        {
            var toUtcExclusive = _time.EndExclusiveOfIstDayUtc(request.To.Value).UtcDateTime;
            query = query.Where(log => log.TimeUtc < toUtcExclusive);
        }

        return query;
    }

    private static IQueryable<AuditLog> ApplySort(
        IQueryable<AuditLog> query,
        string? sort,
        string? direction)
    {
        var ascending = string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase);
        return sort switch
        {
            "Level" => ascending
                ? query.OrderBy(log => log.Level).ThenBy(log => log.TimeUtc)
                : query.OrderByDescending(log => log.Level).ThenByDescending(log => log.TimeUtc),
            "Action" => ascending
                ? query.OrderBy(log => log.Action).ThenBy(log => log.TimeUtc)
                : query.OrderByDescending(log => log.Action).ThenByDescending(log => log.TimeUtc),
            "User" => ascending
                ? query.OrderBy(log => log.UserName).ThenBy(log => log.TimeUtc)
                : query.OrderByDescending(log => log.UserName).ThenByDescending(log => log.TimeUtc),
            "Ip" => ascending
                ? query.OrderBy(log => log.Ip).ThenBy(log => log.TimeUtc)
                : query.OrderByDescending(log => log.Ip).ThenByDescending(log => log.TimeUtc),
            _ => ascending
                ? query.OrderBy(log => log.TimeUtc)
                : query.OrderByDescending(log => log.TimeUtc)
        };
    }
}
