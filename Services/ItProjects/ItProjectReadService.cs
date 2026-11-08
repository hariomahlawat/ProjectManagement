using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.ItProjects;

namespace ProjectManagement.Services.ItProjects;

public sealed class ItProjectReadService : IItProjectReadService
{
    private readonly ApplicationDbContext _db;

    public ItProjectReadService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<ItProjectBoard> GetBoardAsync(ItProjectBoardQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var projects = _db.ItProjects.AsNoTracking().Where(project => !project.IsDeleted);

        if (query.FinancialYearStart.HasValue)
        {
            var fy = query.FinancialYearStart.Value;
            projects = projects.Where(project => project.FinancialYearStart == fy);
        }

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // ToLower + Contains translates on PostgreSQL and on the SQLite test provider alike.
            var needle = search.ToLowerInvariant();
            projects = projects.Where(project =>
                project.Title.ToLower().Contains(needle) ||
                project.PppReferenceNumber.ToLower().Contains(needle));
        }

        var loaded = await projects
            .Include(project => project.Stages)
            .ToListAsync(cancellationToken);

        var years = await _db.ItProjects.AsNoTracking()
            .Where(project => !project.IsDeleted)
            .Select(project => project.FinancialYearStart)
            .Distinct()
            .OrderByDescending(year => year)
            .ToListAsync(cancellationToken);

        var rows = loaded.Select(ToRow).ToList();

        var activeCount = rows.Count(row => IsActive(row.Status.Kind));
        var completedCount = rows.Count(row => row.Status.Kind == ItProjectStatusKind.Completed);
        var droppedCount = rows.Count(row => row.Status.Kind == ItProjectStatusKind.Dropped);

        var stageCounts = ItProjectStages.All
            .Select(definition => new ItProjectBoardStageCount(
                definition.Code,
                rows.Count(row => IsActive(row.Status.Kind) && row.Status.CurrentStageCode == definition.Code)))
            .ToList();

        var scoped = query.Scope switch
        {
            ItProjectBoardScope.Active => rows.Where(row => IsActive(row.Status.Kind)),
            ItProjectBoardScope.Completed => rows.Where(row => row.Status.Kind == ItProjectStatusKind.Completed),
            ItProjectBoardScope.Dropped => rows.Where(row => row.Status.Kind == ItProjectStatusKind.Dropped),
            _ => rows
        };

        var ordered = scoped
            .OrderBy(row => BandOf(row.Status.Kind))
            .ThenBy(row => row.Status.CurrentStageCode is null ? int.MaxValue : ItProjectStages.OrderOf(row.Status.CurrentStageCode))
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ItProjectBoard(ordered, stageCounts, activeCount, completedCount, droppedCount, years);
    }

    public Task<ItProject?> GetAsync(int id, CancellationToken cancellationToken = default)
        => _db.ItProjects
            .Include(project => project.Stages.OrderBy(stage => stage.SortOrder))
            .FirstOrDefaultAsync(project => project.Id == id && !project.IsDeleted, cancellationToken);

    private static bool IsActive(ItProjectStatusKind kind)
        => kind is ItProjectStatusKind.NotStarted or ItProjectStatusKind.InProgress;

    private static int BandOf(ItProjectStatusKind kind) => kind switch
    {
        ItProjectStatusKind.NotStarted => 0,
        ItProjectStatusKind.InProgress => 0,
        ItProjectStatusKind.Completed => 1,
        ItProjectStatusKind.Dropped => 2,
        _ => 3
    };

    private static ItProjectBoardRow ToRow(ItProject project)
    {
        var status = ItProjectStatus.Resolve(project);
        var cells = project.Stages
            .OrderBy(stage => stage.SortOrder)
            .Select(stage => new ItProjectBoardStageCell(
                stage.StageCode,
                stage.SortOrder,
                stage.IsCompleted,
                stage.CompletedOn,
                stage.IsSkipped,
                stage.CostLakhs,
                IsCurrent: status.Kind == ItProjectStatusKind.InProgress && status.CurrentStageCode == stage.StageCode))
            .ToList();

        return new ItProjectBoardRow(
            project.Id,
            project.Title,
            project.PppReferenceNumber,
            project.FinancialYearStart,
            status,
            project.DroppedOn,
            project.DroppedReason,
            project.UpdatedAtUtc,
            cells);
    }
}
