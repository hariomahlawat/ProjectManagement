using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Search;

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
    AdminAuditPayload? Payload = null,
    string? ActorDisplayName = null)
{
    /// <summary>
    /// The name shown as the actor: the person's full name (then user name) from the user
    /// directory, or "PRISM" for events the application wrote by itself.
    /// </summary>
    public string Actor => ActorDisplayName
        ?? AuditActorNames.Resolve(
            Payload?.ActorUserId ?? UserId,
            Payload?.ActorName ?? UserName,
            EmptyDirectory,
            EmptyDirectory);

    private static readonly IReadOnlyDictionary<string, AuditActorDirectoryEntry> EmptyDirectory =
        new Dictionary<string, AuditActorDirectoryEntry>();
}

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
        var rows = await MapAsync(entities, cancellationToken);

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
        // A person is counted once whether their rows kept the user id, the user name or
        // both; events PRISM wrote by itself are not people.
        var actors = await summaryQuery
            .Where(log => (log.UserId != null && log.UserId != string.Empty
                    && log.UserId != "System" && log.UserId != "PRISM system")
                || (log.UserName != null && log.UserName != string.Empty
                    && log.UserName != "System" && log.UserName != "PRISM system" && log.UserName != "PRISM"))
            // A row that kept only the user name (a failed sign-in, say) is keyed by the
            // matching account's id, so it joins that person's other rows.
            .Select(log => log.UserId != null && log.UserId != string.Empty
                ? log.UserId
                : _db.Users
                    .Where(user => user.NormalizedUserName != null
                        && user.NormalizedUserName == log.UserName!.ToUpper())
                    .Select(user => user.Id)
                    .FirstOrDefault() ?? log.UserName)
            .Distinct()
            .CountAsync(cancellationToken);
        var affectedRecords = await CountDistinctRecordsAsync(summaryQuery, cancellationToken);

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
        return await MapAsync(entities, cancellationToken);
    }

    private const string EntityIdToken = "\"EntityId\":\"";
    private const string EntityTypeToken = "\"EntityType\":\"";

    /// <summary>
    /// Counts the distinct records the events touched: one per entity type and id, however many
    /// events name it. The details are compact JSON written by <see cref="AuditService"/>, so
    /// the values are read with plain string functions that run in the database. Rows without
    /// an entity id are not records.
    /// </summary>
    internal static Task<int> CountDistinctRecordsAsync(
        IQueryable<AuditLog> query,
        CancellationToken cancellationToken) =>
        DistinctRecordKeys(query).CountAsync(cancellationToken);

    /// <summary>One key per record ("type|id"); see <see cref="CountDistinctRecordsAsync"/>.</summary>
    internal static IQueryable<string> DistinctRecordKeys(IQueryable<AuditLog> query) =>
        query
            .Where(log => log.DataJson != null && log.DataJson.Contains(EntityIdToken))
            .Select(log => new
            {
                IdTail = log.DataJson!.Substring(log.DataJson.IndexOf(EntityIdToken) + EntityIdToken.Length),
                TypeTail = log.DataJson.Contains(EntityTypeToken)
                    ? log.DataJson.Substring(log.DataJson.IndexOf(EntityTypeToken) + EntityTypeToken.Length)
                    : "\""
            })
            .Select(item => new
            {
                Type = item.TypeTail.Substring(0, item.TypeTail.IndexOf("\"")),
                Id = item.IdTail.Substring(0, item.IdTail.IndexOf("\""))
            })
            .Where(record => record.Id != string.Empty)
            .Select(record => record.Type + "|" + record.Id)
            .Distinct();

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
            // The Actor box matches the stored sign-in name and the person's full name, so a
            // search for "Hari" also finds rows that kept only the user id.
            var term = request.UserName.Trim();
            if (_db.Database.IsNpgsql())
            {
                var pattern = SearchLikePattern.Contains(term);
                var matchingIds = _db.Users
                    .Where(user => EF.Functions.ILike(user.FullName, pattern)
                        || (user.UserName != null && EF.Functions.ILike(user.UserName, pattern)))
                    .Select(user => user.Id);
                var matchingNames = _db.Users
                    .Where(user => EF.Functions.ILike(user.FullName, pattern)
                        && user.NormalizedUserName != null)
                    .Select(user => user.NormalizedUserName);
                query = query.Where(log =>
                    (log.UserName != null && EF.Functions.ILike(log.UserName, pattern))
                    || (log.UserName != null && matchingNames.Contains(log.UserName.ToUpper()))
                    || (log.UserId != null && matchingIds.Contains(log.UserId)));
            }
            else
            {
                var normalized = term.ToLowerInvariant();
                var matchingIds = _db.Users
                    .Where(user => user.FullName.ToLower().Contains(normalized)
                        || (user.UserName != null && user.UserName.ToLower().Contains(normalized)))
                    .Select(user => user.Id);
                var matchingNames = _db.Users
                    .Where(user => user.FullName.ToLower().Contains(normalized)
                        && user.NormalizedUserName != null)
                    .Select(user => user.NormalizedUserName);
                query = query.Where(log =>
                    (log.UserName != null && log.UserName.ToLower().Contains(normalized))
                    || (log.UserName != null && matchingNames.Contains(log.UserName.ToUpper()))
                    || (log.UserId != null && matchingIds.Contains(log.UserId)));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Ip))
        {
            var term = request.Ip.Trim();
            if (_db.Database.IsNpgsql())
            {
                var pattern = SearchLikePattern.Contains(term);
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
                var pattern = SearchLikePattern.Contains(term);
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
            // Maintenance runs log one action per stage (Started/Completed/Failed/Cancelled,
            // Committed), so their "Audit history" links filter by prefix, not exact action.
            "pdf-ingestion" => query.Where(log => log.Action.StartsWith("PdfIngestion")),
            "legacy-import" => query.Where(log => log.Action.StartsWith("LegacyImport")),
            _ => query
        };
    }

    private async Task<IReadOnlyList<AdminLogRow>> MapAsync(
        IReadOnlyList<AuditLog> logs,
        CancellationToken cancellationToken)
    {
        var parsed = logs
            .Select(log => (Log: log, Payload: _payloads.Parse(log.DataJson)))
            .ToArray();

        var (byId, byUserName) = await AuditActorNames.LoadDirectoryAsync(
            _db,
            parsed.SelectMany(item => new[] { item.Payload.ActorUserId, item.Log.UserId }),
            parsed.SelectMany(item => new[] { item.Payload.ActorName, item.Log.UserName }),
            cancellationToken);

        return parsed
            .Select(item => new AdminLogRow(
                item.Log.TimeUtc,
                item.Log.Level,
                item.Log.Action,
                item.Log.UserId,
                item.Log.UserName,
                item.Log.Ip,
                item.Log.Message,
                item.Log.DataJson,
                item.Log.Id,
                item.Log.UserAgent,
                item.Payload,
                AuditActorNames.Resolve(
                    FirstPresent(item.Payload.ActorUserId, item.Log.UserId),
                    FirstPresent(item.Payload.ActorName, item.Log.UserName),
                    byId,
                    byUserName)))
            .ToArray();
    }

    private static string? FirstPresent(string? first, string? second) =>
        string.IsNullOrWhiteSpace(first) ? second : first;

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
