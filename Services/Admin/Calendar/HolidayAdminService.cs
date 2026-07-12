using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models.Scheduling;

namespace ProjectManagement.Services.Admin.Calendar;

public sealed record HolidayListItem(
    int Id,
    DateOnly Date,
    string Name,
    string RowVersion);

public sealed record HolidayEditItem(
    int Id,
    DateOnly Date,
    string Name,
    string RowVersion);

public interface IHolidayAdminService
{
    Task<IReadOnlyList<int>> GetAvailableYearsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HolidayListItem>> ListAsync(int year, CancellationToken cancellationToken = default);
    Task<HolidayEditItem?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<AdminOperationResult<int>> CreateAsync(DateOnly date, string? name, CancellationToken cancellationToken = default);
    Task<AdminOperationResult> UpdateAsync(int id, DateOnly date, string? name, string? rowVersion, CancellationToken cancellationToken = default);
    Task<AdminOperationResult> DeleteAsync(int id, string? rowVersion, CancellationToken cancellationToken = default);
}

public sealed class HolidayAdminService : IHolidayAdminService
{
    private const int MaximumNameLength = 160;

    private readonly ApplicationDbContext _db;
    private readonly IAdminAuditService _audit;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<HolidayAdminService> _logger;

    public HolidayAdminService(
        ApplicationDbContext db,
        IAdminAuditService audit,
        IHttpContextAccessor httpContextAccessor,
        ILogger<HolidayAdminService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<int>> GetAvailableYearsAsync(CancellationToken cancellationToken = default)
    {
        var dates = await _db.Holidays
            .AsNoTracking()
            .Select(holiday => holiday.Date)
            .ToListAsync(cancellationToken);

        return dates
            .Select(date => date.Year)
            .Distinct()
            .OrderByDescending(year => year)
            .ToList();
    }

    public async Task<IReadOnlyList<HolidayListItem>> ListAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        var start = new DateOnly(year, 1, 1);
        var end = start.AddYears(1);

        var rows = await _db.Holidays
            .AsNoTracking()
            .Where(holiday => holiday.Date >= start && holiday.Date < end)
            .OrderBy(holiday => holiday.Date)
            .ThenBy(holiday => holiday.Name)
            .Select(holiday => new
            {
                holiday.Id,
                holiday.Date,
                holiday.Name,
                holiday.RowVersion
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(holiday => new HolidayListItem(
                holiday.Id,
                holiday.Date,
                holiday.Name,
                Convert.ToBase64String(holiday.RowVersion)))
            .ToList();
    }

    public async Task<HolidayEditItem?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var holiday = await _db.Holidays
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new
            {
                item.Id,
                item.Date,
                item.Name,
                item.RowVersion
            })
            .SingleOrDefaultAsync(cancellationToken);

        return holiday is null
            ? null
            : new HolidayEditItem(
                holiday.Id,
                holiday.Date,
                holiday.Name,
                Convert.ToBase64String(holiday.RowVersion));
    }

    public async Task<AdminOperationResult<int>> CreateAsync(
        DateOnly date,
        string? name,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);
        if (normalizedName is null)
        {
            return AdminOperationResult<int>.Failure("Holiday name is required.", "HolidayNameRequired");
        }

        if (normalizedName.Length > MaximumNameLength)
        {
            return AdminOperationResult<int>.Failure(
                $"Holiday name cannot exceed {MaximumNameLength} characters.",
                "HolidayNameTooLong");
        }

        if (await _db.Holidays.AsNoTracking().AnyAsync(holiday => holiday.Date == date, cancellationToken))
        {
            return AdminOperationResult<int>.Failure(
                "A holiday already exists for the selected date.",
                "DuplicateHolidayDate");
        }

        var holiday = new Holiday
        {
            Date = date,
            Name = normalizedName
        };

        _db.Holidays.Add(holiday);

        try
        {
            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            await _db.SaveChangesAsync(cancellationToken);

            await _audit.RecordAsync(
                new AdminAuditEntry(
                    Action: "HolidayCreated",
                    EntityType: nameof(Holiday),
                    EntityId: holiday.Id.ToString(),
                    After: new { holiday.Id, holiday.Date, holiday.Name },
                    Origin: "Settings.Holidays",
                    Message: $"Created holiday '{holiday.Name}' on {holiday.Date:yyyy-MM-dd}."),
                cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return AdminOperationResult<int>.Success(holiday.Id, "Holiday added.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return AdminOperationResult<int>.Failure(
                "A holiday already exists for the selected date.",
                "DuplicateHolidayDate");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return LogFailure<int>(exception, "create", holiday.Id);
        }
    }

    public async Task<AdminOperationResult> UpdateAsync(
        int id,
        DateOnly date,
        string? name,
        string? rowVersion,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);
        if (normalizedName is null)
        {
            return AdminOperationResult.Failure("Holiday name is required.", "HolidayNameRequired");
        }

        if (normalizedName.Length > MaximumNameLength)
        {
            return AdminOperationResult.Failure(
                $"Holiday name cannot exceed {MaximumNameLength} characters.",
                "HolidayNameTooLong");
        }

        if (!TryDecodeRowVersion(rowVersion, out var originalRowVersion))
        {
            return AdminOperationResult.Failure(
                "The holiday was opened from an outdated page. Reload it and try again.",
                "ConcurrencyTokenMissing");
        }

        var holiday = await _db.Holidays.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (holiday is null)
        {
            return AdminOperationResult.Failure("The holiday could not be found.", "HolidayNotFound");
        }

        var before = new { holiday.Id, holiday.Date, holiday.Name };
        holiday.Date = date;
        holiday.Name = normalizedName;
        _db.Entry(holiday).Property(item => item.RowVersion).OriginalValue = originalRowVersion;

        try
        {
            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            await _db.SaveChangesAsync(cancellationToken);

            await _audit.RecordAsync(
                new AdminAuditEntry(
                    Action: "HolidayUpdated",
                    EntityType: nameof(Holiday),
                    EntityId: holiday.Id.ToString(),
                    Before: before,
                    After: new { holiday.Id, holiday.Date, holiday.Name },
                    Origin: "Settings.Holidays",
                    Message: $"Updated holiday '{holiday.Name}'."),
                cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return AdminOperationResult.Success("Holiday updated.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return AdminOperationResult.Failure(
                "The holiday was changed by another administrator. Reload the page before saving again.",
                "ConcurrencyConflict");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return AdminOperationResult.Failure(
                "A holiday already exists for the selected date.",
                "DuplicateHolidayDate");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return LogFailure(exception, "update", id);
        }
    }

    public async Task<AdminOperationResult> DeleteAsync(
        int id,
        string? rowVersion,
        CancellationToken cancellationToken = default)
    {
        if (!TryDecodeRowVersion(rowVersion, out var originalRowVersion))
        {
            return AdminOperationResult.Failure(
                "The holiday was opened from an outdated page. Reload it and try again.",
                "ConcurrencyTokenMissing");
        }

        var holiday = await _db.Holidays.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (holiday is null)
        {
            return AdminOperationResult.Failure("The holiday could not be found.", "HolidayNotFound");
        }

        var before = new { holiday.Id, holiday.Date, holiday.Name };
        _db.Entry(holiday).Property(item => item.RowVersion).OriginalValue = originalRowVersion;
        _db.Holidays.Remove(holiday);

        try
        {
            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            await _db.SaveChangesAsync(cancellationToken);

            await _audit.RecordAsync(
                new AdminAuditEntry(
                    Action: "HolidayDeleted",
                    EntityType: nameof(Holiday),
                    EntityId: id.ToString(),
                    Before: before,
                    Origin: "Settings.Holidays",
                    Message: $"Deleted holiday '{holiday.Name}'."),
                cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return AdminOperationResult.Success("Holiday deleted.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return AdminOperationResult.Failure(
                "The holiday was changed by another administrator. Reload the page before deleting it.",
                "ConcurrencyConflict");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return LogFailure(exception, "delete", id);
        }
    }

    private static string? NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool TryDecodeRowVersion(string? value, out byte[] rowVersion)
    {
        rowVersion = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            rowVersion = Convert.FromBase64String(value);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException postgresException &&
        postgresException.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation;

    private AdminOperationResult LogFailure(Exception exception, string operation, int id)
    {
        var traceId = _httpContextAccessor.HttpContext?.TraceIdentifier;
        _logger.LogError(
            exception,
            "Holiday {Operation} failed. HolidayId={HolidayId}, TraceId={TraceId}",
            operation,
            id,
            traceId);

        return AdminOperationResult.Failure(
            "The holiday operation could not be completed. Quote the trace reference to the administrator.",
            "HolidayOperationFailed",
            traceId);
    }

    private AdminOperationResult<T> LogFailure<T>(Exception exception, string operation, int id)
    {
        var traceId = _httpContextAccessor.HttpContext?.TraceIdentifier;
        _logger.LogError(
            exception,
            "Holiday {Operation} failed. HolidayId={HolidayId}, TraceId={TraceId}",
            operation,
            id,
            traceId);

        return AdminOperationResult<T>.Failure(
            "The holiday operation could not be completed. Quote the trace reference to the administrator.",
            "HolidayOperationFailed",
            traceId);
    }
}
