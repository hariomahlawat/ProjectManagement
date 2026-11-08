using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.ItProjects;

public sealed class ItProjectReadService : IItProjectReadService
{
    private static readonly TimeZoneInfo Ist = TimeZoneHelper.GetIst();

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public ItProjectReadService(ApplicationDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
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
            .Include(project => project.YearTypeOverrides)
            .ToListAsync(cancellationToken);

        var years = await _db.ItProjects.AsNoTracking()
            .Where(project => !project.IsDeleted)
            .Select(project => project.FinancialYearStart)
            .Distinct()
            .OrderByDescending(year => year)
            .ToListAsync(cancellationToken);

        var today = Today();
        var rows = loaded.Select(project => ToRow(project, today)).ToList();

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

        return new ItProjectBoard(ordered, stageCounts, activeCount, completedCount, droppedCount, years, Figures(rows, today));
    }

    public Task<ItProject?> GetAsync(int id, CancellationToken cancellationToken = default)
        => _db.ItProjects
            .Include(project => project.Stages.OrderBy(stage => stage.SortOrder))
            .Include(project => project.YearTypeOverrides)
            .FirstOrDefaultAsync(project => project.Id == id && !project.IsDeleted, cancellationToken);

    public ItProjectTiming GetTiming(ItProject project) => ItProjectTiming.Resolve(project, Today(), ToLocalDate);

    public IReadOnlyList<ItProjectYearTypeInfo> GetYearTypes(ItProject project)
        => ItProjectYearTypes.ResolveAll(project, ItProjectYearTypes.CurrentFinancialYear(_clock.UtcNow));

    public async Task<string?> GetUserDisplayNameAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var user = await _db.Users.AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new { candidate.FullName, candidate.UserName })
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName;
    }

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

    private DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.UtcNow, Ist).DateTime);

    private static DateOnly ToLocalDate(DateTime utc)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Ist));

    private static ItProjectBoardFigures Figures(IReadOnlyList<ItProjectBoardRow> rows, DateOnly today)
    {
        var active = rows.Where(row => IsActive(row.Status.Kind)).ToList();
        var inProgress = active.Where(row => row.Status.Kind == ItProjectStatusKind.InProgress).ToList();

        var longest = inProgress
            .Where(row => row.Timing.Basis == ItProjectTimeBasis.StageDate)
            .OrderByDescending(row => row.Timing.DaysAtStage)
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .Select(row => new ItProjectLongestWait(row.Id, row.Title, row.Status.CurrentStageCode!, row.Timing.DaysAtStage!.Value))
            .FirstOrDefault();

        var onOrder = inProgress
            .Where(row => row.Status.CurrentStageCode == ItProjectStages.Delivery)
            .Select(row => row.Stages.FirstOrDefault(cell => cell.StageCode == ItProjectStages.So))
            .Where(cell => cell is { IsCompleted: true })
            .ToList();

        var thisFinancialYear = FinancialYearHelper.GetStartYear(today);
        var delivered = rows.Where(row => row.Status.Kind == ItProjectStatusKind.Completed)
            .Select(row => (Row: row, On: row.Stages.FirstOrDefault(cell => cell.StageCode == ItProjectStages.Delivery)?.CompletedOn))
            .ToList();
        var deliveredDates = delivered.Where(item => item.On.HasValue).Select(item => item.On!.Value).ToList();

        // "Delivered this FY" counts by the delivery date when it is known, else by the project's FY.
        var deliveredThisYear = delivered.Count(item => item.On is { } on
            ? FinancialYearHelper.Contains(thisFinancialYear, on)
            : item.Row.FinancialYearStart == thisFinancialYear);

        return new ItProjectBoardFigures(
            longest,
            ActiveWithoutStageDate: inProgress.Count(row => row.Timing.Basis != ItProjectTimeBasis.StageDate),
            DatesMissingCount: active.Count(row => row.Timing.HasMissingDates),
            OnOrderLakhs: onOrder.Sum(cell => cell!.CostLakhs ?? 0m),
            OnOrderCount: onOrder.Count,
            DeliveredThisFinancialYear: deliveredThisYear,
            LastDeliveredOn: deliveredDates.Count == 0 ? null : deliveredDates.Max(),
            CurrentFinancialYearStart: thisFinancialYear);
    }

    private static ItProjectBoardRow ToRow(ItProject project, DateOnly today)
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
                IsCurrent: IsActive(status.Kind) && status.CurrentStageCode == stage.StageCode))
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
            cells,
            ItProjectTiming.Resolve(project, today, ToLocalDate));
    }
}
