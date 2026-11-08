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

        // The module holds tens of cases, not thousands: load them all once, because the year
        // filter ("live in that year") and the year list both need every case's stages.
        var all = await _db.ItProjects.AsNoTracking()
            .Where(project => !project.IsDeleted)
            .Include(project => project.Stages)
            .Include(project => project.FinancialYears)
            .ToListAsync(cancellationToken);

        var currentYear = ItProjectYearTypes.CurrentFinancialYear(_clock.UtcNow);

        // The year picker lists every year some case counts in, newest first.
        var years = all
            .SelectMany(project => ItProjectYearTypes.LiveYears(project, currentYear))
            .Distinct()
            .OrderByDescending(year => year)
            .ToList();

        IEnumerable<ItProject> loaded = all;
        if (query.FinancialYearStart is { } fy)
        {
            // A year shows every case that counts in it: its New cases and the CL / CF cases
            // carried into it.
            loaded = loaded.Where(project => ItProjectYearTypes.IsLive(project, fy, currentYear));
        }

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            loaded = loaded.Where(project => Matches(project, search));
        }

        var today = Today();
        var typeYear = query.FinancialYearStart ?? currentYear;
        var rows = loaded.Select(project => ToRow(project, today, ItProjectYearTypes.Resolve(project, typeYear, currentYear))).ToList();

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

        var byBand = scoped.OrderBy(row => BandOf(row.Status.Kind));
        if (query.Sort == ItProjectBoardSort.LongestWait)
        {
            // Only live cases wait; delivered and dropped ones keep stage order in their bands.
            byBand = byBand
                .ThenBy(row => IsActive(row.Status.Kind) && row.Timing.Basis == ItProjectTimeBasis.StageDate ? 0 : 1)
                .ThenByDescending(row => IsActive(row.Status.Kind) && row.Timing.Basis == ItProjectTimeBasis.StageDate ? row.Timing.DaysAtStage ?? 0 : 0);
        }

        var ordered = byBand
            .ThenBy(row => row.Status.CurrentStageCode is null ? int.MaxValue : ItProjectStages.OrderOf(row.Status.CurrentStageCode))
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ItProjectBoard(ordered, stageCounts, activeCount, completedCount, droppedCount, years, Figures(rows, today, typeYear, currentYear));
    }

    public Task<ItProject?> GetAsync(int id, CancellationToken cancellationToken = default)
        => _db.ItProjects
            .Include(project => project.Stages.OrderBy(stage => stage.SortOrder))
            .Include(project => project.FinancialYears)
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

    /// <summary>Title, the first IT PPP number or any later year's number, ignoring case.</summary>
    private static bool Matches(ItProject project, string search)
        => project.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
           project.PppReferenceNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
           project.FinancialYears.Any(year => year.PppReferenceNumber is { } number && number.Contains(search, StringComparison.OrdinalIgnoreCase));

    private static ItProjectBoardFigures Figures(IReadOnlyList<ItProjectBoardRow> rows, DateOnly today, int figuresYear, int currentYear)
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

        var delivered = rows.Where(row => row.Status.Kind == ItProjectStatusKind.Completed)
            .Select(row => (Row: row, On: row.Stages.FirstOrDefault(cell => cell.StageCode == ItProjectStages.Delivery)?.CompletedOn))
            .ToList();
        var deliveredDates = delivered.Where(item => item.On.HasValue).Select(item => item.On!.Value).ToList();

        // "Delivered in the year" counts by the delivery date when it is known, else by the project's FY.
        var deliveredInYear = delivered.Count(item => item.On is { } on
            ? FinancialYearHelper.Contains(figuresYear, on)
            : item.Row.FinancialYearStart == figuresYear);

        // New / CL / CF of the cases still running, in the board's year.
        var typed = active.Where(row => row.YearType is not null).ToList();

        return new ItProjectBoardFigures(
            longest,
            ActiveWithoutStageDate: inProgress.Count(row => row.Timing.Basis != ItProjectTimeBasis.StageDate),
            DatesMissingCount: active.Count(row => row.Timing.HasMissingDates),
            OnOrderLakhs: onOrder.Sum(cell => cell!.CostLakhs ?? 0m),
            OnOrderCount: onOrder.Count,
            DeliveredInFinancialYear: deliveredInYear,
            LastDeliveredOn: deliveredDates.Count == 0 ? null : deliveredDates.Max(),
            CurrentFinancialYearStart: currentYear,
            FinancialYearStart: figuresYear,
            ActiveNew: typed.Count(row => row.YearType!.Type == ItProjectYearType.New),
            ActiveCommittedLiability: typed.Count(row => row.YearType!.Type == ItProjectYearType.CommittedLiability),
            ActiveCarriedForward: typed.Count(row => row.YearType!.Type == ItProjectYearType.CarriedForward),
            NotStartedCount: active.Count(row => row.Status.Kind == ItProjectStatusKind.NotStarted));
    }

    private static ItProjectBoardRow ToRow(ItProject project, DateOnly today, ItProjectYearTypeInfo? yearType)
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
            ItProjectTiming.Resolve(project, today, ToLocalDate),
            yearType,
            project.FinancialYears
                .Where(year => !string.IsNullOrWhiteSpace(year.PppReferenceNumber))
                .OrderBy(year => year.FinancialYearStart)
                .Select(year => year.PppReferenceNumber!)
                .ToList());
    }
}
