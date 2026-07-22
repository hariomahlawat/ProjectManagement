using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Admin.Calendar;

public sealed record HolidayListItem(
    int Id,
    DateOnly Date,
    string Name,
    HolidayType Type,
    bool IsObservedAsOfficeHoliday,
    string? AuthorityReference,
    string? ObservanceRemarks,
    DateTime? ObservanceChangedUtc,
    string? ObservanceChangedByUserId,
    bool IsDateClosedByAnotherEntry,
    string RowVersion)
{
    public bool AffectsSchedule =>
        Type == HolidayType.Gazetted || IsObservedAsOfficeHoliday;
}

public sealed record HolidayEditItem(
    int Id,
    DateOnly Date,
    string Name,
    HolidayType Type,
    bool IsObservedAsOfficeHoliday,
    string? AuthorityReference,
    string? ObservanceRemarks,
    DateTime? ObservanceChangedUtc,
    string? ObservanceChangedByUserId,
    bool IsDateClosedByAnotherEntry,
    IReadOnlyList<string> OtherEntriesOnDate,
    string RowVersion)
{
    public bool AffectsSchedule =>
        Type == HolidayType.Gazetted || IsObservedAsOfficeHoliday;
}

public interface IHolidayAdminService
{
    Task<IReadOnlyList<int>> GetAvailableYearsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HolidayListItem>> ListAsync(int year, CancellationToken cancellationToken = default);
    Task<HolidayEditItem?> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<AdminOperationResult<int>> CreateAsync(
        DateOnly date,
        string? name,
        HolidayType type,
        string? authorityReference,
        string? remarks,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> UpdateAsync(
        int id,
        DateOnly date,
        string? name,
        HolidayType type,
        string? authorityReference,
        string? remarks,
        string? rowVersion,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> DeclareOfficeObservanceAsync(
        int id,
        string? authorityReference,
        string? remarks,
        string? rowVersion,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> WithdrawOfficeObservanceAsync(
        int id,
        string? remarks,
        string? rowVersion,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> DeleteAsync(
        int id,
        string? rowVersion,
        CancellationToken cancellationToken = default);
}

public sealed class HolidayAdminService : IHolidayAdminService
{
    private const int MaximumNameLength = 160;
    private const int MaximumReferenceLength = 240;
    private const int MaximumRemarksLength = 1200;

    private readonly ApplicationDbContext _db;
    private readonly IAdminAuditService _audit;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IClock _clock;
    private readonly ILogger<HolidayAdminService> _logger;

    public HolidayAdminService(
        ApplicationDbContext db,
        IAdminAuditService audit,
        IHttpContextAccessor httpContextAccessor,
        IClock clock,
        ILogger<HolidayAdminService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<int>> GetAvailableYearsAsync(
        CancellationToken cancellationToken = default)
    {
        var dates = await _db.Holidays
            .AsNoTracking()
            .Select(holiday => holiday.Date)
            .ToListAsync(cancellationToken);

        return dates
            .Select(date => date.Year)
            .Distinct()
            .OrderByDescending(year => year)
            .ToArray();
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
            .ThenBy(holiday => holiday.Type)
            .ThenBy(holiday => holiday.Name)
            .Select(holiday => new
            {
                holiday.Id,
                holiday.Date,
                holiday.Name,
                holiday.Type,
                holiday.IsObservedAsOfficeHoliday,
                holiday.AuthorityReference,
                holiday.ObservanceRemarks,
                holiday.ObservanceChangedUtc,
                holiday.ObservanceChangedByUserId,
                holiday.RowVersion
            })
            .ToListAsync(cancellationToken);

        var closedDates = rows
            .Where(row => row.Type == HolidayType.Gazetted || row.IsObservedAsOfficeHoliday)
            .GroupBy(row => row.Date)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Id).ToHashSet());

        return rows.Select(row => new HolidayListItem(
            row.Id,
            row.Date,
            row.Name,
            row.Type,
            row.IsObservedAsOfficeHoliday,
            row.AuthorityReference,
            row.ObservanceRemarks,
            row.ObservanceChangedUtc,
            row.ObservanceChangedByUserId,
            closedDates.TryGetValue(row.Date, out var ids) && ids.Any(id => id != row.Id),
            Convert.ToBase64String(row.RowVersion)))
            .ToArray();
    }

    public async Task<HolidayEditItem?> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var holiday = await _db.Holidays
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new
            {
                item.Id,
                item.Date,
                item.Name,
                item.Type,
                item.IsObservedAsOfficeHoliday,
                item.AuthorityReference,
                item.ObservanceRemarks,
                item.ObservanceChangedUtc,
                item.ObservanceChangedByUserId,
                item.RowVersion
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (holiday is null)
        {
            return null;
        }

        var otherEntries = await _db.Holidays
            .AsNoTracking()
            .Where(item => item.Date == holiday.Date && item.Id != holiday.Id)
            .OrderBy(item => item.Type)
            .ThenBy(item => item.Name)
            .Select(item => new
            {
                item.Name,
                item.Type,
                item.IsObservedAsOfficeHoliday
            })
            .ToListAsync(cancellationToken);

        return new HolidayEditItem(
            holiday.Id,
            holiday.Date,
            holiday.Name,
            holiday.Type,
            holiday.IsObservedAsOfficeHoliday,
            holiday.AuthorityReference,
            holiday.ObservanceRemarks,
            holiday.ObservanceChangedUtc,
            holiday.ObservanceChangedByUserId,
            otherEntries.Any(item =>
                item.Type == HolidayType.Gazetted || item.IsObservedAsOfficeHoliday),
            otherEntries.Select(item =>
                $"{item.Name} · {DisplayClassification(item.Type, item.IsObservedAsOfficeHoliday)}")
                .ToArray(),
            Convert.ToBase64String(holiday.RowVersion));
    }

    public async Task<AdminOperationResult<int>> CreateAsync(
        DateOnly date,
        string? name,
        HolidayType type,
        string? authorityReference,
        string? remarks,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateInput(name, type, authorityReference, remarks);
        if (validation is not null)
        {
            return AdminOperationResult<int>.Failure(validation.Value.Message, validation.Value.Code);
        }

        var normalizedName = NormalizeText(name)!;
        var normalizedReference = NormalizeOptional(authorityReference);
        var normalizedRemarks = NormalizeOptional(remarks);
        var duplicate = await FindDuplicateAsync(0, date, normalizedName, type, cancellationToken);
        if (duplicate is not null)
        {
            return AdminOperationResult<int>.Failure(duplicate.Value.Message, duplicate.Value.Code);
        }

        var holiday = new Holiday
        {
            Date = date,
            Name = normalizedName,
            Type = type,
            IsObservedAsOfficeHoliday = type == HolidayType.Gazetted,
            AuthorityReference = normalizedReference,
            ObservanceRemarks = normalizedRemarks,
            ObservanceChangedUtc = type == HolidayType.Gazetted ? _clock.UtcNow.UtcDateTime : null,
            ObservanceChangedByUserId = type == HolidayType.Gazetted ? CurrentUserId() : null
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
                    Action: type == HolidayType.Gazetted
                        ? "GazettedHolidayCreated"
                        : "RestrictedHolidayCreated",
                    EntityType: nameof(Holiday),
                    EntityId: holiday.Id.ToString(),
                    After: AuditSnapshot(holiday),
                    Origin: "Settings.Holidays",
                    Message: $"Created {DisplayClassification(type, holiday.IsObservedAsOfficeHoliday)} '{holiday.Name}' on {holiday.Date:yyyy-MM-dd}."),
                cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return AdminOperationResult<int>.Success(
                holiday.Id,
                type == HolidayType.Gazetted
                    ? "Gazetted Holiday added as an office non-working day."
                    : "Restricted Holiday added for calendar information. The office remains open unless observance is declared.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return AdminOperationResult<int>.Failure(
                "An equivalent holiday entry already exists for the selected date.",
                "DuplicateHoliday");
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
        HolidayType type,
        string? authorityReference,
        string? remarks,
        string? rowVersion,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateInput(name, type, authorityReference, remarks);
        if (validation is not null)
        {
            return AdminOperationResult.Failure(validation.Value.Message, validation.Value.Code);
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

        var normalizedName = NormalizeText(name)!;
        var duplicate = await FindDuplicateAsync(id, date, normalizedName, type, cancellationToken);
        if (duplicate is not null)
        {
            return AdminOperationResult.Failure(duplicate.Value.Message, duplicate.Value.Code);
        }

        var before = AuditSnapshot(holiday);
        holiday.Date = date;
        holiday.Name = normalizedName;
        holiday.Type = type;
        holiday.AuthorityReference = NormalizeOptional(authorityReference);
        holiday.ObservanceRemarks = NormalizeOptional(remarks);

        // Changing classification never silently opens a previously closed day. A Gazetted
        // entry changed to Restricted remains office-observed until explicitly withdrawn.
        if (type == HolidayType.Gazetted)
        {
            holiday.IsObservedAsOfficeHoliday = true;
            holiday.ObservanceChangedUtc ??= _clock.UtcNow.UtcDateTime;
            holiday.ObservanceChangedByUserId ??= CurrentUserId();
        }

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
                    After: AuditSnapshot(holiday),
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
                "An equivalent holiday entry already exists for the selected date.",
                "DuplicateHoliday");
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

    public async Task<AdminOperationResult> DeclareOfficeObservanceAsync(
        int id,
        string? authorityReference,
        string? remarks,
        string? rowVersion,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateObservanceText(authorityReference, remarks);
        if (validation is not null)
        {
            return AdminOperationResult.Failure(validation.Value.Message, validation.Value.Code);
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

        if (holiday.Type != HolidayType.Restricted)
        {
            return AdminOperationResult.Failure(
                "Gazetted Holidays are already office holidays and cannot use this operation.",
                "HolidayNotRestricted");
        }

        if (holiday.IsObservedAsOfficeHoliday)
        {
            return AdminOperationResult.Success("This Restricted Holiday is already observed as an office holiday.");
        }

        var before = AuditSnapshot(holiday);
        holiday.IsObservedAsOfficeHoliday = true;
        holiday.AuthorityReference = NormalizeOptional(authorityReference) ?? holiday.AuthorityReference;
        holiday.ObservanceRemarks = NormalizeOptional(remarks);
        holiday.ObservanceChangedUtc = _clock.UtcNow.UtcDateTime;
        holiday.ObservanceChangedByUserId = CurrentUserId();
        _db.Entry(holiday).Property(item => item.RowVersion).OriginalValue = originalRowVersion;

        return await SaveObservanceChangeAsync(
            holiday,
            before,
            "RestrictedHolidayDeclaredOfficeHoliday",
            $"Declared Restricted Holiday '{holiday.Name}' as an office holiday.",
            "Restricted Holiday declared as an office holiday. It now affects working-day calculations.",
            cancellationToken);
    }

    public async Task<AdminOperationResult> WithdrawOfficeObservanceAsync(
        int id,
        string? remarks,
        string? rowVersion,
        CancellationToken cancellationToken = default)
    {
        var normalizedRemarks = NormalizeOptional(remarks);
        if (normalizedRemarks?.Length > MaximumRemarksLength)
        {
            return AdminOperationResult.Failure(
                $"Remarks cannot exceed {MaximumRemarksLength} characters.",
                "HolidayRemarksTooLong");
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

        if (holiday.Type != HolidayType.Restricted)
        {
            return AdminOperationResult.Failure(
                "Office observance cannot be withdrawn from a Gazetted Holiday.",
                "HolidayNotRestricted");
        }

        if (!holiday.IsObservedAsOfficeHoliday)
        {
            return AdminOperationResult.Success("This Restricted Holiday is already informational only.");
        }

        var before = AuditSnapshot(holiday);
        holiday.IsObservedAsOfficeHoliday = false;
        holiday.ObservanceRemarks = normalizedRemarks;
        holiday.ObservanceChangedUtc = _clock.UtcNow.UtcDateTime;
        holiday.ObservanceChangedByUserId = CurrentUserId();
        _db.Entry(holiday).Property(item => item.RowVersion).OriginalValue = originalRowVersion;

        return await SaveObservanceChangeAsync(
            holiday,
            before,
            "RestrictedHolidayOfficeObservanceWithdrawn",
            $"Withdrew office observance of Restricted Holiday '{holiday.Name}'.",
            "Office observance withdrawn. The Restricted Holiday remains visible for information and no longer affects future working-day calculations.",
            cancellationToken);
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

        var before = AuditSnapshot(holiday);
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

    private async Task<AdminOperationResult> SaveObservanceChangeAsync(
        Holiday holiday,
        object before,
        string action,
        string auditMessage,
        string userMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            await _db.SaveChangesAsync(cancellationToken);
            await _audit.RecordAsync(
                new AdminAuditEntry(
                    Action: action,
                    EntityType: nameof(Holiday),
                    EntityId: holiday.Id.ToString(),
                    Before: before,
                    After: AuditSnapshot(holiday),
                    Origin: "Settings.Holidays",
                    Message: auditMessage),
                cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return AdminOperationResult.Success(userMessage);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AdminOperationResult.Failure(
                "The holiday was changed by another administrator. Reload the page and review the current office status.",
                "ConcurrencyConflict");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return LogFailure(exception, action, holiday.Id);
        }
    }

    private async Task<(string Message, string Code)?> FindDuplicateAsync(
        int excludedId,
        DateOnly date,
        string normalizedName,
        HolidayType type,
        CancellationToken cancellationToken)
    {
        var lowerName = normalizedName.ToLowerInvariant();
        var duplicateName = await _db.Holidays.AsNoTracking().AnyAsync(
            holiday =>
                holiday.Id != excludedId
                && holiday.Date == date
                && holiday.Type == type
                && holiday.Name.ToLower() == lowerName,
            cancellationToken);
        if (duplicateName)
        {
            return ("An equivalent holiday entry already exists for the selected date.", "DuplicateHoliday");
        }

        if (type == HolidayType.Gazetted)
        {
            var anotherGazetted = await _db.Holidays.AsNoTracking().AnyAsync(
                holiday =>
                    holiday.Id != excludedId
                    && holiday.Date == date
                    && holiday.Type == HolidayType.Gazetted,
                cancellationToken);
            if (anotherGazetted)
            {
                return ("A Gazetted Holiday is already recorded for the selected date. Add any additional notification as a Restricted Holiday or update the existing entry.", "GazettedDateAlreadyExists");
            }
        }

        return null;
    }

    private static (string Message, string Code)? ValidateInput(
        string? name,
        HolidayType type,
        string? authorityReference,
        string? remarks)
    {
        var normalizedName = NormalizeText(name);
        if (normalizedName is null)
            return ("Holiday name is required.", "HolidayNameRequired");
        if (normalizedName.Length > MaximumNameLength)
            return ($"Holiday name cannot exceed {MaximumNameLength} characters.", "HolidayNameTooLong");
        if (!Enum.IsDefined(typeof(HolidayType), type))
            return ("Select a valid holiday classification.", "HolidayTypeInvalid");
        return ValidateObservanceText(authorityReference, remarks);
    }

    private static (string Message, string Code)? ValidateObservanceText(
        string? authorityReference,
        string? remarks)
    {
        if (NormalizeOptional(authorityReference)?.Length > MaximumReferenceLength)
            return ($"Authority or order reference cannot exceed {MaximumReferenceLength} characters.", "HolidayReferenceTooLong");
        if (NormalizeOptional(remarks)?.Length > MaximumRemarksLength)
            return ($"Remarks cannot exceed {MaximumRemarksLength} characters.", "HolidayRemarksTooLong");
        return null;
    }

    private static object AuditSnapshot(Holiday holiday) => new
    {
        holiday.Id,
        holiday.Date,
        holiday.Name,
        Type = holiday.Type.ToString(),
        holiday.IsObservedAsOfficeHoliday,
        holiday.AuthorityReference,
        holiday.ObservanceRemarks,
        holiday.ObservanceChangedUtc,
        holiday.ObservanceChangedByUserId,
        AffectsWorkingCalendar = holiday.AffectsWorkingCalendar
    };

    private string? CurrentUserId() =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

    private static string DisplayClassification(HolidayType type, bool observed) =>
        type == HolidayType.Gazetted
            ? "Gazetted Holiday"
            : observed
                ? "Restricted Holiday observed as office holiday"
                : "Restricted Holiday (informational)";

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? NormalizeOptional(string? value) => NormalizeText(value);

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
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState == PostgresErrorCodes.UniqueViolation;

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
