using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.Admin.Ingestion;

namespace ProjectManagement.Services.Admin.Maintenance;

public sealed record AdminMaintenanceSummary(
    bool PdfIngestionRunning,
    PdfIngestionRunRecord? LastPdfRun,
    int LegacyImportRuns,
    int LegacyProjectsImported,
    DateTimeOffset? LastLegacyImportUtc,
    string? LastLegacyImportBy);

public interface IAdminMaintenanceSummaryService
{
    Task<AdminMaintenanceSummary> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class AdminMaintenanceSummaryService : IAdminMaintenanceSummaryService
{
    private readonly ApplicationDbContext _db;
    private readonly IPdfIngestionRunGate _runGate;
    private readonly IPdfIngestionRunHistory _history;

    public AdminMaintenanceSummaryService(
        ApplicationDbContext db,
        IPdfIngestionRunGate runGate,
        IPdfIngestionRunHistory history)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _runGate = runGate ?? throw new ArgumentNullException(nameof(runGate));
        _history = history ?? throw new ArgumentNullException(nameof(history));
    }

    public async Task<AdminMaintenanceSummary> GetAsync(CancellationToken cancellationToken = default)
    {
        var aggregate = await _db.ProjectLegacyImports.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Runs = group.Count(),
                Projects = group.Sum(row => row.RowsImported),
                LastAt = group.Max(row => (DateTime?)row.ImportedAtUtc)
            })
            .SingleOrDefaultAsync(cancellationToken);

        string? lastActor = null;
        if (aggregate?.LastAt is DateTime lastAt)
        {
            var lastActorId = await _db.ProjectLegacyImports.AsNoTracking()
                .Where(row => row.ImportedAtUtc == lastAt)
                .OrderByDescending(row => row.Id)
                .Select(row => row.ImportedByUserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(lastActorId))
            {
                lastActor = await _db.Users.AsNoTracking()
                    .Where(user => user.Id == lastActorId)
                    .Select(user => string.IsNullOrWhiteSpace(user.FullName)
                        ? user.UserName ?? user.Id
                        : user.FullName)
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? lastActorId;
            }
        }

        return new AdminMaintenanceSummary(
            _runGate.IsRunning,
            _history.GetLatest(),
            aggregate?.Runs ?? 0,
            aggregate?.Projects ?? 0,
            aggregate?.LastAt is DateTime utc
                ? new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc))
                : null,
            lastActor);
    }
}
