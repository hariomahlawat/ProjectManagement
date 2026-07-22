using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Admin.Recovery;

public sealed record DocumentRecoveryQuery(
    string? Search = null,
    int? ProjectId = null,
    string? StageCode = null,
    DateOnly? DeletedFrom = null,
    DateOnly? DeletedTo = null,
    string? DeletedBy = null,
    int Page = 1,
    int PageSize = 25);

public sealed record DocumentRecoveryRow(
    int DocumentId,
    int ProjectId,
    string ProjectName,
    string? StageCode,
    string StageDisplayName,
    string Title,
    string OriginalFileName,
    string ContentType,
    long FileSize,
    DateTimeOffset? DeletedAtUtc,
    string? DeletedByDisplay);

public sealed record DocumentRecoverySelectionSummary(
    int RequestedCount,
    int EligibleCount,
    long TotalBytes,
    IReadOnlyList<string> Titles);

public sealed record DocumentRecoveryPage(
    IReadOnlyList<DocumentRecoveryRow> Items,
    int TotalCount,
    long TotalBytes,
    int Page,
    int PageSize,
    IReadOnlyList<SelectListItem> Projects,
    IReadOnlyList<SelectListItem> Stages)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public interface IDocumentRecoveryQueryService
{
    Task<DocumentRecoveryPage> QueryAsync(
        DocumentRecoveryQuery query,
        CancellationToken cancellationToken = default);

    Task<DocumentRecoverySelectionSummary> GetSelectionSummaryAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default);
}

public sealed class DocumentRecoveryQueryService : IDocumentRecoveryQueryService
{
    private readonly ApplicationDbContext _db;
    private readonly IAdminTimeService _time;
    private readonly AdminRecoveryOptions _options;

    public DocumentRecoveryQueryService(
        ApplicationDbContext db,
        IAdminTimeService time,
        IOptions<AdminRecoveryOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<DocumentRecoveryPage> QueryAsync(
        DocumentRecoveryQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(
            request.PageSize <= 0 ? _options.DefaultPageSize : request.PageSize,
            10,
            _options.MaximumPageSize);
        var search = Normalize(request.Search, 200);
        var deletedBy = Normalize(request.DeletedBy, 160);
        var stageCode = Normalize(request.StageCode, 32);

        var query = _db.ProjectDocuments.AsNoTracking()
            .Where(document => document.Status == ProjectDocumentStatus.SoftDeleted);

        if (search is not null)
        {
            var pattern = $"%{search}%";
            query = query.Where(document =>
                EF.Functions.ILike(document.Title, pattern)
                || EF.Functions.ILike(document.OriginalFileName, pattern)
                || EF.Functions.ILike(document.Project.Name, pattern));
        }
        if (request.ProjectId.HasValue)
            query = query.Where(document => document.ProjectId == request.ProjectId.Value);
        if (stageCode is not null)
            query = query.Where(document => document.Stage != null && document.Stage.StageCode == stageCode);
        if (request.DeletedFrom.HasValue)
        {
            var from = _time.StartOfIstDayUtc(request.DeletedFrom.Value);
            query = query.Where(document => document.ArchivedAtUtc >= from);
        }
        if (request.DeletedTo.HasValue)
        {
            var to = _time.EndExclusiveOfIstDayUtc(request.DeletedTo.Value);
            query = query.Where(document => document.ArchivedAtUtc < to);
        }
        if (deletedBy is not null)
        {
            var pattern = $"%{deletedBy}%";
            query = query.Where(document =>
                (document.ArchivedByUser != null && document.ArchivedByUser.FullName != null
                    && EF.Functions.ILike(document.ArchivedByUser.FullName, pattern))
                || (document.ArchivedByUser != null && document.ArchivedByUser.UserName != null
                    && EF.Functions.ILike(document.ArchivedByUser.UserName, pattern))
                || (document.ArchivedByUserId != null && EF.Functions.ILike(document.ArchivedByUserId, pattern)));
        }

        // A scoped DbContext cannot execute concurrent database operations. Keep
        // these bounded aggregate queries sequential and cancellation-aware.
        var totalCount = await query.CountAsync(cancellationToken);
        var totalBytes = await query.SumAsync(document => (long?)document.FileSize, cancellationToken) ?? 0;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        var items = await query
            .OrderByDescending(document => document.ArchivedAtUtc)
            .ThenBy(document => document.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(document => new DocumentRecoveryRow(
                document.Id,
                document.ProjectId,
                document.Project.Name,
                document.Stage == null ? null : document.Stage.StageCode,
                document.Stage == null ? "—" : document.Stage.StageCode,
                document.Title,
                document.OriginalFileName,
                document.ContentType,
                document.FileSize,
                document.ArchivedAtUtc,
                document.ArchivedByUser == null
                    ? document.ArchivedByUserId
                    : document.ArchivedByUser.FullName ?? document.ArchivedByUser.UserName ?? document.ArchivedByUserId))
            .ToListAsync(cancellationToken);

        var projectRows = await _db.ProjectDocuments.AsNoTracking()
            .Where(document => document.Status == ProjectDocumentStatus.SoftDeleted)
            .Select(document => new { document.ProjectId, document.Project.Name })
            .Distinct()
            .OrderBy(row => row.Name)
            .ToListAsync(cancellationToken);

        var displayItems = items
            .Select(item => item with
            {
                StageDisplayName = string.IsNullOrWhiteSpace(item.StageCode)
                    ? "—"
                    : StageCodes.DisplayNameOf(item.StageCode)
            })
            .ToArray();
        var stages = StageCodes.All
            .Select(code => new SelectListItem(StageCodes.DisplayNameOf(code), code))
            .ToArray();

        return new DocumentRecoveryPage(
            displayItems,
            totalCount,
            totalBytes,
            page,
            pageSize,
            projectRows
                .Select(row => new SelectListItem(row.Name, row.ProjectId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .ToArray(),
            stages);
    }

    public async Task<DocumentRecoverySelectionSummary> GetSelectionSummaryAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default)
    {
        var requested = ids.Where(id => id > 0).Distinct().Take(_options.MaximumBulkDocuments + 1).ToArray();
        if (requested.Length == 0)
            return new DocumentRecoverySelectionSummary(0, 0, 0, Array.Empty<string>());

        var rows = await _db.ProjectDocuments.AsNoTracking()
            .Where(document => requested.Contains(document.Id)
                && document.Status == ProjectDocumentStatus.SoftDeleted)
            .OrderBy(document => document.Title)
            .Select(document => new { document.Title, document.FileSize })
            .ToListAsync(cancellationToken);

        return new DocumentRecoverySelectionSummary(
            requested.Length,
            rows.Count,
            rows.Sum(row => row.FileSize),
            rows.Take(10).Select(row => row.Title).ToArray());
    }

    private static string? Normalize(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
