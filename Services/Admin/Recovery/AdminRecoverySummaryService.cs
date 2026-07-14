using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Admin.Recovery;

public sealed record AdminRecoveryOperation(
    string Action,
    string Title,
    string? Actor,
    DateTimeOffset WhenUtc,
    string Tone,
    string Icon,
    string? Detail);

public sealed record AdminRecoverySummary(
    int TrashedProjects,
    int RecycledDocuments,
    int DeletedEvents,
    int ArchivedProjects,
    int DueForPurge,
    int DueSoon,
    int DueSoonWindowDays,
    long RecycledDocumentBytes,
    int ProjectRetentionDays,
    IReadOnlyList<AdminRecoveryOperation> RecentOperations);

public interface IAdminRecoverySummaryService
{
    Task<AdminRecoverySummary> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class AdminRecoverySummaryService : IAdminRecoverySummaryService
{
    private readonly ApplicationDbContext _db;
    private readonly IAdminTimeService _time;
    private readonly IAuditActionPresentationCatalog _actions;
    private readonly ProjectRetentionOptions _projectRetention;
    private readonly AdminRecoveryOptions _options;

    public AdminRecoverySummaryService(
        ApplicationDbContext db,
        IAdminTimeService time,
        IAuditActionPresentationCatalog actions,
        IOptions<ProjectRetentionOptions> projectRetention,
        IOptions<AdminRecoveryOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _projectRetention = projectRetention?.Value ?? throw new ArgumentNullException(nameof(projectRetention));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<AdminRecoverySummary> GetAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.UtcNow;
        var retentionDays = Math.Max(0, _projectRetention.TrashRetentionDays);
        var purgeCutoff = now.AddDays(-retentionDays);
        var dueSoonCutoff = now.AddDays(-(Math.Max(0, retentionDays - _options.DueSoonDays)));

        var projectQuery = _db.Projects.IgnoreQueryFilters().AsNoTracking().Where(project => project.IsDeleted);
        var documentQuery = _db.ProjectDocuments.AsNoTracking()
            .Where(document => document.Status == ProjectDocumentStatus.SoftDeleted);

        // All queries share the request-scoped DbContext. EF Core does not permit
        // concurrent operations on one context, so execute the bounded aggregates
        // sequentially instead of using Task.WhenAll.
        var trashedProjects = await projectQuery.CountAsync(cancellationToken);
        var recycledDocuments = await documentQuery.CountAsync(cancellationToken);
        var deletedEvents = await _db.Events.AsNoTracking()
            .CountAsync(calendarEvent => calendarEvent.IsDeleted, cancellationToken);
        var archivedProjects = await _db.Projects.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(project => project.IsArchived && !project.IsDeleted, cancellationToken);
        var dueForPurge = await projectQuery.CountAsync(
            project => project.DeletedAt.HasValue && project.DeletedAt.Value <= purgeCutoff,
            cancellationToken);
        var dueSoon = await projectQuery.CountAsync(
            project => project.DeletedAt.HasValue
                && project.DeletedAt.Value > purgeCutoff
                && project.DeletedAt.Value <= dueSoonCutoff,
            cancellationToken);
        var documentBytes = await documentQuery
            .SumAsync(document => (long?)document.FileSize, cancellationToken) ?? 0;
        var operations = await LoadRecentOperationsAsync(cancellationToken);

        return new AdminRecoverySummary(
            trashedProjects,
            recycledDocuments,
            deletedEvents,
            archivedProjects,
            dueForPurge,
            dueSoon,
            _options.DueSoonDays,
            documentBytes,
            retentionDays,
            operations);
    }

    private async Task<IReadOnlyList<AdminRecoveryOperation>> LoadRecentOperationsAsync(
        CancellationToken cancellationToken)
    {
        var rows = await _db.AuditLogs.AsNoTracking()
            .Where(log =>
                log.Action.Contains("Restore")
                || log.Action.Contains("Purge")
                || log.Action.Contains("Trash")
                || log.Action.Contains("HardDelete")
                || log.Action.Contains("Archived"))
            .OrderByDescending(log => log.TimeUtc)
            .ThenByDescending(log => log.Id)
            .Take(_options.RecentOperationCount)
            .Select(log => new
            {
                log.Action,
                log.Level,
                log.UserName,
                log.UserId,
                log.Message,
                log.TimeUtc
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row =>
        {
            var presentation = _actions.Describe(row.Action, row.Level);
            return new AdminRecoveryOperation(
                row.Action,
                presentation.Label,
                row.UserName ?? row.UserId,
                new DateTimeOffset(EnsureUtc(row.TimeUtc)),
                presentation.Tone,
                presentation.Icon,
                row.Message);
        }).ToArray();
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
