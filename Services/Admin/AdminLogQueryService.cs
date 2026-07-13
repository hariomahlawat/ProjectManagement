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
    string Direction = "desc",
    string? Category = null,
    string? EntityType = null);

public sealed record AdminLogRow(
    DateTime TimeUtc,
    string Level,
    string Action,
    string? UserId,
    string? UserName,
    string? Ip,
    string? Message,
    string? DataJson,
    long Id = 0,
    string? UserAgent = null,
    AdminAuditPayload? Payload = null);

public sealed record AdminLogSummary(
    int Total,
    int Information,
    int Warnings,
    int Errors,
    int Actors,
    int AffectedRecords);

public sealed record AdminLogResult(
    IReadOnlyList<AdminLogRow> Rows,
    IReadOnlyList<string> ActionOptions,
    IReadOnlyList<string> SeriesLabels,
    IReadOnlyList<int> SeriesCounts,
    int Total,
    int Page,
    int PageSize,
    AdminLogSummary? Summary = null)
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
    private const int MaximumActionOptions = 250;
    private const int IstOffsetMinutes = 330;

    private readonly ApplicationDbContext _db;
    private readonly IAdminTimeService _time;
    private readonly IAdminAuditPayloadParser _payloads;

    public AdminLogQueryService(
        ApplicationDbContext db,
        IAdminTimeService time,
        IAdminAuditPayloadParser? payloads = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _payloads = payloads ?? new AdminAuditPayloadParser();
    }

    public async Task<AdminLogResult> GetAsync(
        AdminLogQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = Math.Max(1, request.Page);
        var pageSize = request.PageSize is > 0 and <= MaximumPageSize ? request.PageSize : 25;
        var filtered = ComposeQuery(request);

        var total = await filtered.CountAsync(cancellationToken);
        if (total > 0)
        {
            page = Math.Min(page, (int)Math.Ceiling(total / (double)pageSize));
        }

        var entities = await ApplySort(filtered, request.Sort, request.Direction)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var rows = entities.Select(Map).ToArray();

        var actionOptions = await _db.AuditLogs.AsNoTracking()
            .Select(log => log.Action)
            .Distinct()
            .OrderBy(action => action)
            .Take(MaximumActionOptions)
            .ToListAsync(cancellationToken);

        // Grouping remains in the database. The fixed offset converts the stored UTC
        // timestamp to the IST calendar date before aggregation.
        var perDay = await filtered
            .GroupBy(log => log.TimeUtc.AddMinutes(IstOffsetMinutes).Date)
            .Select(group => new { Day = group.Key, Count = group.Count() })
            .OrderBy(row => row.Day)
            .ToListAsync(cancellationToken);

        // Severity cards remain comparable while another severity is selected: all
        // non-severity filters are retained, and only the Level filter is removed.
        var summaryQuery = string.IsNullOrWhiteSpace(request.Level)
            ? filtered
            : ComposeQuery(request with { Level = null });
        var summaryTotal = await summaryQuery.CountAsync(cancellationToken);
        var levelCounts = await summaryQuery
            .GroupBy(log => log.Level)
            .Select(group => new { Level = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var information = levelCounts
            .Where(item => string.Equals(item.Level, "Info", StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.Count);
        var warnings = levelCounts
            .Where(item => string.Equals(item.Level, "Warning", StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.Count);
        var errors = levelCounts
            .Where(item => string.Equals(item.Level, "Error", StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.Count);
        var actors = await summaryQuery
            .Where(log => log.UserName != null && log.UserName != string.Empty)
            .Select(log => log.UserName)
            .Distinct()
            .CountAsync(cancellationToken);
        var affectedRecords = await summaryQuery.CountAsync(
            log => log.DataJson != null && log.DataJson.Contains("\"EntityId\""),
            cancellationToken);

        return new AdminLogResult(
            rows,
            actionOptions,
            perDay.Select(item => DateOnly.FromDateTime(item.Day).ToString("dd MMM yyyy", CultureInfo.InvariantCulture)).ToArray(),
            perDay.Select(item => item.Count).ToArray(),
            total,
            page,
            pageSize,
            new AdminLogSummary(summaryTotal, information, warnings, errors, actors, affectedRecords));
    }

    public async Task<IReadOnlyList<AdminLogRow>> GetForExportAsync(
        AdminLogQuery request,
        int maximumRows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var safeMaximum = Math.Clamp(maximumRows, 1, 100_000);
        var entities = await ApplySort(ComposeQuery(request), "Time", "desc")
            .Take(safeMaximum)
            .ToListAsync(cancellationToken);
        return entities.Select(Map).ToArray();
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

        query = ApplyCategory(query, request.Category);

        if (!string.IsNullOrWhiteSpace(request.EntityType))
        {
            var entityToken = $"\"EntityType\":\"{request.EntityType.Trim()}\"";
            query = query.Where(log => log.DataJson != null && log.DataJson.Contains(entityToken));
        }

        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            query = query.Where(log => log.UserId == request.UserId);
        }
        else if (!string.IsNullOrWhiteSpace(request.UserName))
        {
            var term = request.UserName.Trim();
            if (_db.Database.IsNpgsql())
            {
                var pattern = $"%{term}%";
                query = query.Where(log => log.UserName != null && EF.Functions.ILike(log.UserName, pattern));
            }
            else
            {
                var normalized = term.ToLowerInvariant();
                query = query.Where(log => log.UserName != null && log.UserName.ToLower().Contains(normalized));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Ip))
        {
            var term = request.Ip.Trim();
            if (_db.Database.IsNpgsql())
            {
                var pattern = $"%{term}%";
                query = query.Where(log => log.Ip != null && EF.Functions.ILike(log.Ip, pattern));
            }
            else
            {
                var normalized = term.ToLowerInvariant();
                query = query.Where(log => log.Ip != null && log.Ip.ToLower().Contains(normalized));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Contains))
        {
            var term = request.Contains.Trim();
            if (_db.Database.IsNpgsql())
            {
                var pattern = $"%{term}%";
                query = query.Where(log =>
                    (log.Message != null && EF.Functions.ILike(log.Message, pattern))
                    || (log.DataJson != null && EF.Functions.ILike(log.DataJson, pattern))
                    || EF.Functions.ILike(log.Action, pattern));
            }
            else
            {
                var normalized = term.ToLowerInvariant();
                query = query.Where(log =>
                    (log.Message != null && log.Message.ToLower().Contains(normalized))
                    || (log.DataJson != null && log.DataJson.ToLower().Contains(normalized))
                    || log.Action.ToLower().Contains(normalized));
            }
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

    private static IQueryable<AuditLog> ApplyCategory(IQueryable<AuditLog> query, string? category)
    {
        return category?.Trim().ToLowerInvariant() switch
        {
            "authentication" => query.Where(log => log.Action.StartsWith("Login")),
            "access" or "access-security" => query.Where(log => log.Action.StartsWith("AdminUser")),
            "projects" => query.Where(log => log.Action.Contains("Project")),
            "documents" => query.Where(log => log.Action.Contains("Document")),
            "calendar" => query.Where(log => log.Action.Contains("Calendar")),
            "recovery" => query.Where(log =>
                log.Action.Contains("Restore")
                || log.Action.Contains("Trash")
                || log.Action.Contains("Purge")
                || log.Action.Contains("Recycle")),
            "master-data" => query.Where(log => log.Action.StartsWith("MasterData")),
            _ => query
        };
    }

    private AdminLogRow Map(AuditLog log) => new(
        log.TimeUtc,
        log.Level,
        log.Action,
        log.UserId,
        log.UserName,
        log.Ip,
        log.Message,
        log.DataJson,
        log.Id,
        log.UserAgent,
        _payloads.Parse(log.DataJson));

    private static IQueryable<AuditLog> ApplySort(
        IQueryable<AuditLog> query,
        string? sort,
        string? direction)
    {
        var ascending = string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase);
        return sort switch
        {
            "Level" => ascending
                ? query.OrderBy(log => log.Level).ThenBy(log => log.TimeUtc).ThenBy(log => log.Id)
                : query.OrderByDescending(log => log.Level).ThenByDescending(log => log.TimeUtc).ThenByDescending(log => log.Id),
            "Action" => ascending
                ? query.OrderBy(log => log.Action).ThenBy(log => log.TimeUtc).ThenBy(log => log.Id)
                : query.OrderByDescending(log => log.Action).ThenByDescending(log => log.TimeUtc).ThenByDescending(log => log.Id),
            "User" => ascending
                ? query.OrderBy(log => log.UserName).ThenBy(log => log.TimeUtc).ThenBy(log => log.Id)
                : query.OrderByDescending(log => log.UserName).ThenByDescending(log => log.TimeUtc).ThenByDescending(log => log.Id),
            "Ip" => ascending
                ? query.OrderBy(log => log.Ip).ThenBy(log => log.TimeUtc).ThenBy(log => log.Id)
                : query.OrderByDescending(log => log.Ip).ThenByDescending(log => log.TimeUtc).ThenByDescending(log => log.Id),
            _ => ascending
                ? query.OrderBy(log => log.TimeUtc).ThenBy(log => log.Id)
                : query.OrderByDescending(log => log.TimeUtc).ThenByDescending(log => log.Id)
        };
    }
}
