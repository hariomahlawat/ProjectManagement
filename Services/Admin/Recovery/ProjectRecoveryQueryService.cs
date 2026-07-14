using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Admin.Recovery;

public sealed record ProjectTrashQuery(
    string? Search = null,
    string? Retention = null,
    int Page = 1,
    int PageSize = 25);

public sealed record ProjectArchiveQuery(
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

public sealed record ProjectRecoveryRow(
    int ProjectId,
    string Name,
    string? CaseFileNumber,
    string? HodDisplay,
    string? ProjectOfficerDisplay,
    DateTimeOffset? DeletedAtUtc,
    string? DeletedByDisplay,
    string? DeleteReason,
    string? DeleteMethod,
    DateTimeOffset? PurgeScheduledUtc,
    int? DaysUntilPurge,
    bool IsPurgeDue,
    bool IsArchived,
    int DocumentCount,
    int PhotoCount,
    int VideoCount,
    long StoredBytes);

public sealed record ArchivedProjectRow(
    int ProjectId,
    string Name,
    string? CaseFileNumber,
    string? StageCode,
    string? HodDisplay,
    string? ProjectOfficerDisplay,
    DateTimeOffset? ArchivedAtUtc,
    string? ArchivedByDisplay,
    int DocumentCount,
    int PhotoCount,
    int VideoCount);

public sealed record ProjectRecoveryPreview(
    int ProjectId,
    string Name,
    string? CaseFileNumber,
    bool IsArchived,
    string? DeleteReason,
    DateTimeOffset? DeletedAtUtc,
    DateTimeOffset? PurgeScheduledUtc,
    int DocumentCount,
    int PhotoCount,
    int VideoCount,
    long StoredBytes)
{
    public int RelatedAssetCount => DocumentCount + PhotoCount + VideoCount;
}

public sealed record ProjectRecoveryPage<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public interface IProjectRecoveryQueryService
{
    Task<ProjectRecoveryPage<ProjectRecoveryRow>> QueryTrashAsync(
        ProjectTrashQuery query,
        CancellationToken cancellationToken = default);

    Task<ProjectRecoveryPage<ArchivedProjectRow>> QueryArchivedAsync(
        ProjectArchiveQuery query,
        CancellationToken cancellationToken = default);

    Task<ProjectRecoveryPreview?> GetPreviewAsync(
        int projectId,
        CancellationToken cancellationToken = default);
}

public sealed class ProjectRecoveryQueryService : IProjectRecoveryQueryService
{
    private readonly ApplicationDbContext _db;
    private readonly IAdminTimeService _time;
    private readonly ProjectRetentionOptions _retention;
    private readonly AdminRecoveryOptions _options;

    public ProjectRecoveryQueryService(
        ApplicationDbContext db,
        IAdminTimeService time,
        IOptions<ProjectRetentionOptions> retention,
        IOptions<AdminRecoveryOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _retention = retention?.Value ?? throw new ArgumentNullException(nameof(retention));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<ProjectRecoveryPage<ProjectRecoveryRow>> QueryTrashAsync(
        ProjectTrashQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(
            query.PageSize <= 0 ? _options.DefaultPageSize : query.PageSize,
            10,
            _options.MaximumPageSize);
        var search = NormalizeSearch(query.Search);
        var now = _time.UtcNow;
        var retentionDays = Math.Max(0, _retention.TrashRetentionDays);
        var dueCutoff = now.AddDays(-retentionDays);
        var dueSoonCutoff = now.AddDays(-(Math.Max(0, retentionDays - _options.DueSoonDays)));

        var projects = _db.Projects.IgnoreQueryFilters().AsNoTracking()
            .Where(project => project.IsDeleted);

        if (search is not null)
        {
            var pattern = $"%{search}%";
            projects = projects.Where(project =>
                EF.Functions.ILike(project.Name, pattern)
                || (project.CaseFileNumber != null && EF.Functions.ILike(project.CaseFileNumber, pattern))
                || (project.DeleteReason != null && EF.Functions.ILike(project.DeleteReason, pattern)));
        }

        projects = query.Retention?.Trim().ToLowerInvariant() switch
        {
            "due" => projects.Where(project => project.DeletedAt.HasValue && project.DeletedAt.Value <= dueCutoff),
            "due-soon" => projects.Where(project => project.DeletedAt.HasValue
                && project.DeletedAt.Value > dueCutoff
                && project.DeletedAt.Value <= dueSoonCutoff),
            "safe" => projects.Where(project => !project.DeletedAt.HasValue || project.DeletedAt.Value > dueSoonCutoff),
            _ => projects
        };

        var totalCount = await projects.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        var rows = await projects
            .OrderBy(project => project.DeletedAt)
            .ThenBy(project => project.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(project => new
            {
                project.Id,
                project.Name,
                project.CaseFileNumber,
                HodDisplay = project.HodUser == null ? null : project.HodUser.FullName ?? project.HodUser.UserName,
                ProjectOfficerDisplay = project.LeadPoUser == null ? null : project.LeadPoUser.FullName ?? project.LeadPoUser.UserName,
                project.DeletedAt,
                project.DeletedByUserId,
                project.DeleteReason,
                project.DeleteMethod,
                project.IsArchived,
                DocumentCount = _db.ProjectDocuments.Count(document => document.ProjectId == project.Id),
                PhotoCount = _db.ProjectPhotos.Count(photo => photo.ProjectId == project.Id),
                VideoCount = _db.ProjectVideos.Count(video => video.ProjectId == project.Id),
                StoredBytes = (_db.ProjectDocuments
                    .Where(document => document.ProjectId == project.Id)
                    .Sum(document => (long?)document.FileSize) ?? 0)
                    + (_db.ProjectVideos
                        .Where(video => video.ProjectId == project.Id)
                        .Sum(video => (long?)video.FileSize) ?? 0)
            })
            .ToListAsync(cancellationToken);

        var deletedByIds = rows
            .Select(row => row.DeletedByUserId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var deletedBy = await ResolveUsersAsync(deletedByIds, cancellationToken);

        var items = rows.Select(row =>
        {
            DateTimeOffset? purgeAt = row.DeletedAt?.AddDays(retentionDays);
            int? daysUntil = purgeAt.HasValue
                ? Math.Max(0, (int)Math.Ceiling((purgeAt.Value - now).TotalDays))
                : null;
            return new ProjectRecoveryRow(
                row.Id,
                row.Name,
                row.CaseFileNumber,
                row.HodDisplay,
                row.ProjectOfficerDisplay,
                row.DeletedAt,
                ResolveUser(row.DeletedByUserId, deletedBy),
                row.DeleteReason,
                row.DeleteMethod,
                purgeAt,
                daysUntil,
                purgeAt.HasValue && purgeAt.Value <= now,
                row.IsArchived,
                row.DocumentCount,
                row.PhotoCount,
                row.VideoCount,
                row.StoredBytes);
        }).ToArray();

        return new ProjectRecoveryPage<ProjectRecoveryRow>(items, totalCount, page, pageSize);
    }

    public async Task<ProjectRecoveryPage<ArchivedProjectRow>> QueryArchivedAsync(
        ProjectArchiveQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(
            query.PageSize <= 0 ? _options.DefaultPageSize : query.PageSize,
            10,
            _options.MaximumPageSize);
        var search = NormalizeSearch(query.Search);

        var projects = _db.Projects.IgnoreQueryFilters().AsNoTracking()
            .Where(project => project.IsArchived && !project.IsDeleted);
        if (search is not null)
        {
            var pattern = $"%{search}%";
            projects = projects.Where(project =>
                EF.Functions.ILike(project.Name, pattern)
                || (project.CaseFileNumber != null && EF.Functions.ILike(project.CaseFileNumber, pattern)));
        }

        var totalCount = await projects.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        var rows = await projects
            .OrderByDescending(project => project.ArchivedAt)
            .ThenBy(project => project.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(project => new
            {
                project.Id,
                project.Name,
                project.CaseFileNumber,
                project.ArchivedAt,
                project.ArchivedByUserId,
                HodDisplay = project.HodUser == null ? null : project.HodUser.FullName ?? project.HodUser.UserName,
                ProjectOfficerDisplay = project.LeadPoUser == null ? null : project.LeadPoUser.FullName ?? project.LeadPoUser.UserName,
                StageCode = project.ProjectStages
                    .OrderByDescending(stage => stage.SortOrder)
                    .Select(stage => stage.StageCode)
                    .FirstOrDefault(),
                DocumentCount = _db.ProjectDocuments.Count(document => document.ProjectId == project.Id),
                PhotoCount = _db.ProjectPhotos.Count(photo => photo.ProjectId == project.Id),
                VideoCount = _db.ProjectVideos.Count(video => video.ProjectId == project.Id)
            })
            .ToListAsync(cancellationToken);

        var actorIds = rows.Select(row => row.ArchivedByUserId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var actors = await ResolveUsersAsync(actorIds, cancellationToken);

        var items = rows.Select(row => new ArchivedProjectRow(
            row.Id,
            row.Name,
            row.CaseFileNumber,
            row.StageCode,
            row.HodDisplay,
            row.ProjectOfficerDisplay,
            row.ArchivedAt,
            ResolveUser(row.ArchivedByUserId, actors),
            row.DocumentCount,
            row.PhotoCount,
            row.VideoCount)).ToArray();

        return new ProjectRecoveryPage<ArchivedProjectRow>(items, totalCount, page, pageSize);
    }

    public async Task<ProjectRecoveryPreview?> GetPreviewAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        var retentionDays = Math.Max(0, _retention.TrashRetentionDays);
        return await _db.Projects.IgnoreQueryFilters().AsNoTracking()
            .Where(project => project.Id == projectId && project.IsDeleted)
            .Select(project => new ProjectRecoveryPreview(
                project.Id,
                project.Name,
                project.CaseFileNumber,
                project.IsArchived,
                project.DeleteReason,
                project.DeletedAt,
                project.DeletedAt.HasValue ? project.DeletedAt.Value.AddDays(retentionDays) : null,
                _db.ProjectDocuments.Count(document => document.ProjectId == project.Id),
                _db.ProjectPhotos.Count(photo => photo.ProjectId == project.Id),
                _db.ProjectVideos.Count(video => video.ProjectId == project.Id),
                (_db.ProjectDocuments.Where(document => document.ProjectId == project.Id)
                    .Sum(document => (long?)document.FileSize) ?? 0)
                + (_db.ProjectVideos.Where(video => video.ProjectId == project.Id)
                    .Sum(video => (long?)video.FileSize) ?? 0)))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, string>> ResolveUsersAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var rows = await _db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new
            {
                user.Id,
                Display = string.IsNullOrWhiteSpace(user.FullName)
                    ? user.UserName ?? user.Id
                    : user.FullName
            })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Id, row => row.Display, StringComparer.Ordinal);
    }

    private static string? ResolveUser(string? id, IReadOnlyDictionary<string, string> users) =>
        !string.IsNullOrWhiteSpace(id) && users.TryGetValue(id, out var display)
            ? display
            : id;

    private static string? NormalizeSearch(string? value)
    {
        var search = value?.Trim();
        if (string.IsNullOrWhiteSpace(search)) return null;
        return search.Length <= 160 ? search : search[..160];
    }
}
