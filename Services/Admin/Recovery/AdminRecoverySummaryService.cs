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
        var retentionDays = _projectRetention.EffectiveTrashRetentionDays;
        var purgeCutoff = now.AddDays(-retentionDays);
        var dueSoonCutoff = now.AddDays(-(Math.Max(0, retentionDays - _options.DueSoonDays)));

        // The bins are defined once (AdminBins), so Home, this page and its tabs agree.
        var projectQuery = AdminBins.DeletedProjects(_db);
        var documentQuery = AdminBins.DeletedProjectDocuments(_db);

        // All queries share the request-scoped DbContext. EF Core does not permit
        // concurrent operations on one context, so execute the bounded aggregates
        // sequentially instead of using Task.WhenAll.
        var trashedProjects = await projectQuery.CountAsync(cancellationToken);
        var recycledDocuments = await documentQuery.CountAsync(cancellationToken);
        var deletedEvents = await AdminBins.CountEventsAsync(_db, cancellationToken);
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
        var take = _options.RecentOperationCount;
        // A person restoring their own notebook page is not administration: those are left out.
        var rows = await (
                from log in _db.AuditLogs.AsNoTracking()
                where (log.Action.Contains("Restore")
                        || log.Action.Contains("Purge")
                        || log.Action.Contains("Trash")
                        || log.Action.Contains("HardDelete")
                        || log.Action.Contains("Archived")
                        || log.Action.StartsWith("Chat.Attachments"))
                    && !log.Action.StartsWith("Notebook.")
                join user in _db.Users on log.UserId equals user.Id into users
                from user in users.DefaultIfEmpty()
                orderby log.TimeUtc descending, log.Id descending
                select new
                {
                    log.Action,
                    log.Level,
                    // The person's name when the log kept only their id.
                    Actor = log.UserName ?? (user == null ? log.UserId : (user.FullName ?? user.UserName)),
                    log.Message,
                    log.TimeUtc
                })
            .Take(take)
            .ToListAsync(cancellationToken);

        var auditOperations = rows.Select(row =>
        {
            var presentation = _actions.Describe(row.Action, row.Level);
            return new AdminRecoveryOperation(
                row.Action,
                presentation.Label,
                row.Actor,
                new DateTimeOffset(EnsureUtc(row.TimeUtc)),
                presentation.Tone,
                presentation.Icon,
                row.Message);
        });

        // Moving a project to Trash, restoring it, archiving and unarchiving are recorded in
        // the project's own history (ProjectAudits), not the global audit log, so they never
        // appeared here.
        var projectRows = await (
                from audit in _db.ProjectAudits.AsNoTracking()
                where ProjectRecoveryActions.Contains(audit.Action)
                join project in _db.Projects.IgnoreQueryFilters() on audit.ProjectId equals project.Id
                join user in _db.Users on audit.PerformedByUserId equals user.Id into users
                from user in users.DefaultIfEmpty()
                orderby audit.PerformedAt descending, audit.Id descending
                select new
                {
                    audit.Action,
                    audit.PerformedAt,
                    audit.Reason,
                    ProjectName = project.Name,
                    Actor = user == null ? audit.PerformedByUserId : (user.FullName ?? user.UserName)
                })
            .Take(take)
            .ToListAsync(cancellationToken);

        var projectOperations = projectRows.Select(row =>
        {
            var (title, tone, icon) = row.Action switch
            {
                "Trash" => ("Project moved to Trash", "warning", "bi-trash3"),
                "RestoreTrash" => ("Project restored from Trash", "success", "bi-arrow-counterclockwise"),
                "Archive" => ("Project archived", "neutral", "bi-archive"),
                _ => ("Project restored from archive", "success", "bi-box-arrow-up")
            };
            var detail = string.IsNullOrWhiteSpace(row.Reason)
                ? row.ProjectName
                : $"{row.ProjectName}. Reason: {row.Reason}";
            return new AdminRecoveryOperation(
                "Project." + row.Action,
                title,
                row.Actor,
                row.PerformedAt,
                tone,
                icon,
                detail);
        });

        return auditOperations
            .Concat(projectOperations)
            .OrderByDescending(operation => operation.WhenUtc)
            .Take(take)
            .ToArray();
    }

    private static readonly string[] ProjectRecoveryActions = { "Trash", "RestoreTrash", "Archive", "RestoreArchive" };

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
