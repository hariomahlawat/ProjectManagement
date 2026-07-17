using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Admin.MasterData;

public sealed record CelebrationDirectoryRequest(
    string? Search,
    string? Type,
    string? Window,
    int Page,
    int PageSize);

public sealed record CelebrationAdminRow(
    Guid Id,
    CelebrationType EventType,
    string DisplayName,
    string Name,
    string? SpouseName,
    byte Day,
    byte Month,
    short? Year,
    DateOnly NextOccurrence,
    int DaysAway,
    DateTimeOffset UpdatedUtc);

public sealed record CelebrationDirectoryResult(
    IReadOnlyList<CelebrationAdminRow> Rows,
    int Total,
    int Birthdays,
    int Anniversaries,
    int NextThirtyDays,
    int FilteredCount,
    int Page,
    int PageSize,
    int TotalPages,
    string Search,
    string Type,
    string Window);

public sealed record CelebrationEditItem(
    Guid Id,
    CelebrationType EventType,
    string Name,
    string? SpouseName,
    byte Day,
    byte Month,
    short? Year);

public sealed record CelebrationSaveCommand(
    Guid? Id,
    CelebrationType EventType,
    string Name,
    string? SpouseName,
    byte Day,
    byte Month,
    short? Year,
    string ActorUserId);

public interface ICelebrationAdministrationService
{
    Task<CelebrationDirectoryResult> ListAsync(CelebrationDirectoryRequest request, CancellationToken cancellationToken = default);
    Task<CelebrationEditItem?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AdminOperationResult<Guid>> SaveAsync(CelebrationSaveCommand command, CancellationToken cancellationToken = default);
    Task<AdminOperationResult> DeleteAsync(Guid id, string actorUserId, CancellationToken cancellationToken = default);
}

public sealed class CelebrationAdministrationService : ICelebrationAdministrationService
{
    private const int MaximumPageSize = 100;

    private readonly ApplicationDbContext _db;
    private readonly IAdminTimeService _time;
    private readonly IAdminAuditService _audit;
    private readonly ILogger<CelebrationAdministrationService> _logger;

    public CelebrationAdministrationService(
        ApplicationDbContext db,
        IAdminTimeService time,
        IAdminAuditService audit,
        ILogger<CelebrationAdministrationService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CelebrationDirectoryResult> ListAsync(
        CelebrationDirectoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var search = request.Search?.Trim() ?? string.Empty;
        var type = NormalizeType(request.Type);
        var window = NormalizeWindow(request.Window);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 25 : request.PageSize, 10, MaximumPageSize);
        var page = Math.Max(1, request.Page);

        var source = _db.Celebrations.AsNoTracking().Where(item => item.DeletedUtc == null);
        var total = await source.CountAsync(cancellationToken);
        var birthdays = await source.CountAsync(item => item.EventType == CelebrationType.Birthday, cancellationToken);
        var anniversaries = total - birthdays;

        if (type == "birthday") source = source.Where(item => item.EventType == CelebrationType.Birthday);
        if (type == "anniversary") source = source.Where(item => item.EventType == CelebrationType.Anniversary);
        if (search.Length > 0)
        {
            source = source.Where(item =>
                EF.Functions.ILike(item.Name, $"%{search}%")
                || (item.SpouseName != null && EF.Functions.ILike(item.SpouseName, $"%{search}%")));
        }

        var entities = await source
            .OrderBy(item => item.Month)
            .ThenBy(item => item.Day)
            .ThenBy(item => item.Name)
            .Select(item => new
            {
                item.Id,
                item.EventType,
                item.Name,
                item.SpouseName,
                item.Day,
                item.Month,
                item.Year,
                item.UpdatedUtc
            })
            .ToListAsync(cancellationToken);

        var today = _time.TodayIst;
        var rows = entities.Select(item =>
        {
            var model = new Celebration
            {
                Id = item.Id,
                EventType = item.EventType,
                Name = item.Name,
                SpouseName = item.SpouseName,
                Day = item.Day,
                Month = item.Month,
                Year = item.Year
            };
            var next = CelebrationHelpers.NextOccurrenceLocal(model, today);
            return new CelebrationAdminRow(
                item.Id,
                item.EventType,
                CelebrationHelpers.DisplayName(model),
                item.Name,
                item.SpouseName,
                item.Day,
                item.Month,
                item.Year,
                next,
                CelebrationHelpers.DaysAway(today, next),
                item.UpdatedUtc);
        }).ToArray();

        var nextThirtyDays = rows.Count(item => item.DaysAway < 30);
        IEnumerable<CelebrationAdminRow> filtered = window switch
        {
            "today" => rows.Where(item => item.DaysAway == 0),
            "7" => rows.Where(item => item.DaysAway < 7),
            "15" => rows.Where(item => item.DaysAway < 15),
            "30" => rows.Where(item => item.DaysAway < 30),
            _ => rows
        };

        var ordered = filtered.OrderBy(item => item.NextOccurrence).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        var filteredCount = ordered.Length;
        var totalPages = Math.Max(1, (int)Math.Ceiling(filteredCount / (double)pageSize));
        page = Math.Min(page, totalPages);
        var pageRows = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();

        return new CelebrationDirectoryResult(
            pageRows,
            total,
            birthdays,
            anniversaries,
            nextThirtyDays,
            filteredCount,
            page,
            pageSize,
            totalPages,
            search,
            type,
            window);
    }

    public async Task<CelebrationEditItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _db.Celebrations.AsNoTracking()
            .Where(item => item.Id == id && item.DeletedUtc == null)
            .Select(item => new CelebrationEditItem(
                item.Id,
                item.EventType,
                item.Name,
                item.SpouseName,
                item.Day,
                item.Month,
                item.Year))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<AdminOperationResult<Guid>> SaveAsync(
        CelebrationSaveCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(command.EventType))
        {
            return AdminOperationResult<Guid>.Failure("Select a valid celebration type.", "InvalidCelebrationType");
        }

        var name = command.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return AdminOperationResult<Guid>.Failure("Name is required.", "CelebrationNameRequired");
        }
        if (name.Length > 120)
        {
            return AdminOperationResult<Guid>.Failure("Name must be 120 characters or fewer.", "CelebrationNameTooLong");
        }
        if (command.Month is < 1 or > 12 || command.Day < 1)
        {
            return AdminOperationResult<Guid>.Failure("Select a valid annual date.", "InvalidCelebrationDate");
        }

        var calendarYear = command.Year ?? 2000;
        if (calendarYear is < 1 or > 9999 || command.Day > DateTime.DaysInMonth(calendarYear, command.Month))
        {
            return AdminOperationResult<Guid>.Failure("The selected date is invalid for this month.", "InvalidCelebrationDate");
        }

        var spouseName = string.IsNullOrWhiteSpace(command.SpouseName) ? null : command.SpouseName.Trim();
        if (spouseName?.Length > 120)
        {
            return AdminOperationResult<Guid>.Failure("Spouse name must be 120 characters or fewer.", "CelebrationSpouseNameTooLong");
        }

        Celebration? entity;
        object? before = null;
        var created = command.Id is null;
        if (command.Id is not Guid celebrationId)
        {
            entity = new Celebration
            {
                Id = Guid.NewGuid(),
                CreatedById = command.ActorUserId,
                CreatedUtc = _time.UtcNow
            };
            _db.Celebrations.Add(entity);
        }
        else
        {
            entity = await _db.Celebrations.SingleOrDefaultAsync(
                item => item.Id == celebrationId && item.DeletedUtc == null,
                cancellationToken);
            if (entity is null)
            {
                return AdminOperationResult<Guid>.Failure("The celebration could not be found.", "CelebrationNotFound");
            }
            before = Snapshot(entity);
        }

        entity.EventType = command.EventType;
        entity.Name = name;
        entity.SpouseName = spouseName;
        entity.Day = command.Day;
        entity.Month = command.Month;
        entity.Year = command.Year;
        entity.UpdatedUtc = _time.UtcNow;

        try
        {
            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.RecordAsync(new AdminAuditEntry(
                created ? "CelebrationCreated" : "CelebrationUpdated",
                nameof(Celebration),
                entity.Id.ToString(),
                Before: before,
                After: Snapshot(entity),
                ActorUserId: command.ActorUserId,
                Origin: "Admin.MasterData.Celebrations",
                Message: created
                    ? $"Created {Label(entity.EventType).ToLowerInvariant()} entry '{CelebrationHelpers.DisplayName(entity)}'."
                    : $"Updated {Label(entity.EventType).ToLowerInvariant()} entry '{CelebrationHelpers.DisplayName(entity)}'."), cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);

            return AdminOperationResult<Guid>.Success(
                entity.Id,
                created ? $"{Label(entity.EventType)} added." : $"{Label(entity.EventType)} updated.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Celebration save failed for {CelebrationId}.", entity.Id);
            return AdminOperationResult<Guid>.Failure(
                "The celebration could not be saved. Review the application log using the request trace.",
                "CelebrationSaveFailed");
        }
    }

    public async Task<AdminOperationResult> DeleteAsync(
        Guid id,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.Celebrations.SingleOrDefaultAsync(
            item => item.Id == id && item.DeletedUtc == null,
            cancellationToken);
        if (entity is null)
        {
            return AdminOperationResult.Failure("The celebration could not be found.", "CelebrationNotFound");
        }

        var before = Snapshot(entity);
        entity.DeletedUtc = _time.UtcNow;
        entity.UpdatedUtc = _time.UtcNow;

        try
        {
            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.RecordAsync(new AdminAuditEntry(
                "CelebrationDeleted",
                nameof(Celebration),
                entity.Id.ToString(),
                Before: before,
                After: new { entity.Id, entity.DeletedUtc, ActorUserId = actorUserId },
                ActorUserId: actorUserId,
                Origin: "Admin.MasterData.Celebrations",
                Message: $"Deleted {Label(entity.EventType).ToLowerInvariant()} entry '{CelebrationHelpers.DisplayName(entity)}'."), cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return AdminOperationResult.Success($"{Label(entity.EventType)} deleted.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Celebration deletion failed for {CelebrationId}.", id);
            return AdminOperationResult.Failure("The celebration could not be deleted.", "CelebrationDeleteFailed");
        }
    }

    private static object Snapshot(Celebration item) => new
    {
        item.Id,
        item.EventType,
        item.Name,
        item.SpouseName,
        item.Day,
        item.Month,
        item.Year,
        item.UpdatedUtc,
        item.DeletedUtc
    };

    private static string Label(CelebrationType type) => type == CelebrationType.Anniversary ? "Anniversary" : "Birthday";
    private static string NormalizeType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "birthday" => "birthday",
        "anniversary" => "anniversary",
        _ => "all"
    };
    private static string NormalizeWindow(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "today" => "today",
        "7" => "7",
        "15" => "15",
        "30" => "30",
        _ => "all"
    };
}
