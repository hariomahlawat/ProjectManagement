using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Ffc;

public interface IFfcRecordCommandService
{
    Task<FfcCommandResult> CreateAsync(
        FfcRecordCreateCommand command,
        CancellationToken cancellationToken = default);

    Task<FfcCommandResult> UpdateAsync(
        FfcRecordUpdateCommand command,
        CancellationToken cancellationToken = default);

    Task<FfcCommandResult> ArchiveAsync(
        long recordId,
        string? rowVersion,
        CancellationToken cancellationToken = default);

    Task<FfcCommandResult> RestoreAsync(
        long recordId,
        string? rowVersion,
        CancellationToken cancellationToken = default);
}

public sealed class FfcRecordCommandService : IFfcRecordCommandService
{
    private const string ActiveCountryYearConstraint = "UX_FfcRecords_CountryId_Year_Active";
    private const int RemarksMaxLength = 4000;

    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<FfcRecordCommandService> _logger;

    public FfcRecordCommandService(
        ApplicationDbContext db,
        IAuditService audit,
        IHttpContextAccessor httpContextAccessor,
        ILogger<FfcRecordCommandService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FfcCommandResult> CreateAsync(
        FfcRecordCreateCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = await ValidateAsync(
            command.CountryId,
            command.Year,
            command.IpaCompleted,
            command.IpaDate,
            command.IpaRemarks,
            command.GslCompleted,
            command.GslDate,
            command.GslRemarks,
            command.OverallRemarks,
            currentRecordId: null,
            cancellationToken);

        if (validation is not null)
        {
            return validation;
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new FfcRecord
        {
            CountryId = command.CountryId,
            Year = command.Year,
            IpaYes = command.IpaCompleted,
            IpaDate = command.IpaCompleted ? command.IpaDate : null,
            IpaRemarks = Normalize(command.IpaRemarks),
            GslYes = command.GslCompleted,
            GslDate = command.GslCompleted ? command.GslDate : null,
            GslRemarks = Normalize(command.GslRemarks),
            OverallRemarks = Normalize(command.OverallRemarks),
            IsDeleted = false,
            CreatedByUserId = command.CreatedByUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.FfcRecords.Add(entity);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsConstraintViolation(exception, ActiveCountryYearConstraint))
        {
            return FfcCommandResult.Invalid(
                fieldErrors: Error("Year", "An active FFC record already exists for the selected country and year."));
        }

        await TryAuditAsync(
            "ProjectOfficeReports.FFC.RecordCreated",
            entity.Id,
            before: null,
            after: Snapshot(entity));

        return FfcCommandResult.Ok(entity.Id, "FFC record created.");
    }

    public async Task<FfcCommandResult> UpdateAsync(
        FfcRecordUpdateCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = await ValidateAsync(
            command.CountryId,
            command.Year,
            command.IpaCompleted,
            command.IpaDate,
            command.IpaRemarks,
            command.GslCompleted,
            command.GslDate,
            command.GslRemarks,
            command.OverallRemarks,
            command.RecordId,
            cancellationToken);

        if (validation is not null)
        {
            return validation;
        }

        if (!TryDecodeRowVersion(command.RowVersion, out var rowVersion))
        {
            return FfcCommandResult.Conflict(
                "The record version is missing or invalid. Reload the workspace and try again.");
        }

        var entity = await _db.FfcRecords
            .FirstOrDefaultAsync(record => record.Id == command.RecordId && !record.IsDeleted, cancellationToken);

        if (entity is null)
        {
            return FfcCommandResult.Invalid("The FFC record was not found or has been archived.");
        }

        var before = Snapshot(entity);
        _db.Entry(entity).Property(record => record.RowVersion).OriginalValue = rowVersion;

        entity.CountryId = command.CountryId;
        entity.Year = command.Year;
        entity.IpaYes = command.IpaCompleted;
        entity.IpaDate = command.IpaCompleted ? command.IpaDate : null;
        entity.IpaRemarks = Normalize(command.IpaRemarks);
        entity.GslYes = command.GslCompleted;
        entity.GslDate = command.GslCompleted ? command.GslDate : null;
        entity.GslRemarks = Normalize(command.GslRemarks);
        entity.OverallRemarks = Normalize(command.OverallRemarks);
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return FfcCommandResult.Conflict(
                "This record was modified by another user. Reload the workspace, review the latest values and save again.");
        }
        catch (DbUpdateException exception) when (IsConstraintViolation(exception, ActiveCountryYearConstraint))
        {
            return FfcCommandResult.Invalid(
                fieldErrors: Error("Year", "An active FFC record already exists for the selected country and year."));
        }

        await TryAuditAsync(
            "ProjectOfficeReports.FFC.RecordUpdated",
            entity.Id,
            before,
            Snapshot(entity));

        return FfcCommandResult.Ok(entity.Id, "Record details updated.");
    }

    public async Task<FfcCommandResult> ArchiveAsync(
        long recordId,
        string? rowVersion,
        CancellationToken cancellationToken = default)
    {
        if (!TryDecodeRowVersion(rowVersion, out var decodedRowVersion))
        {
            return FfcCommandResult.Conflict(
                "The record version is missing or invalid. Reload the workspace and try again.");
        }

        var entity = await _db.FfcRecords
            .FirstOrDefaultAsync(record => record.Id == recordId && !record.IsDeleted, cancellationToken);

        if (entity is null)
        {
            return FfcCommandResult.Invalid("The FFC record was not found or is already archived.");
        }

        var before = Snapshot(entity);
        _db.Entry(entity).Property(record => record.RowVersion).OriginalValue = decodedRowVersion;
        entity.IsDeleted = true;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return FfcCommandResult.Conflict(
                "This record was modified by another user. Reload the workspace before archiving it.");
        }

        await TryAuditAsync(
            "ProjectOfficeReports.FFC.RecordArchived",
            entity.Id,
            before,
            Snapshot(entity));

        return FfcCommandResult.Ok(entity.Id, "FFC record archived.");
    }


    public async Task<FfcCommandResult> RestoreAsync(
        long recordId,
        string? rowVersion,
        CancellationToken cancellationToken = default)
    {
        if (!TryDecodeRowVersion(rowVersion, out var decodedRowVersion))
        {
            return FfcCommandResult.Conflict(
                "The record version is missing or invalid. Reload the archived-records page and try again.");
        }

        var entity = await _db.FfcRecords
            .FirstOrDefaultAsync(record => record.Id == recordId && record.IsDeleted, cancellationToken);

        if (entity is null)
        {
            return FfcCommandResult.Invalid("The archived FFC record was not found or has already been restored.");
        }

        var activeDuplicateExists = await _db.FfcRecords
            .AsNoTracking()
            .AnyAsync(record =>
                !record.IsDeleted &&
                record.CountryId == entity.CountryId &&
                record.Year == entity.Year,
                cancellationToken);

        if (activeDuplicateExists)
        {
            return FfcCommandResult.Invalid(
                "This record cannot be restored because an active FFC record already exists for the same country and year.");
        }

        var before = Snapshot(entity);
        _db.Entry(entity).Property(record => record.RowVersion).OriginalValue = decodedRowVersion;
        entity.IsDeleted = false;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return FfcCommandResult.Conflict(
                "This archived record was modified by another user. Reload the page before restoring it.");
        }
        catch (DbUpdateException exception) when (IsConstraintViolation(exception, ActiveCountryYearConstraint))
        {
            return FfcCommandResult.Invalid(
                "This record cannot be restored because an active FFC record already exists for the same country and year.");
        }

        await TryAuditAsync(
            "ProjectOfficeReports.FFC.RecordRestored",
            entity.Id,
            before,
            Snapshot(entity));

        return FfcCommandResult.Ok(entity.Id, "FFC record restored.");
    }

    private async Task<FfcCommandResult?> ValidateAsync(
        long countryId,
        short year,
        bool ipaCompleted,
        DateOnly? ipaDate,
        string? ipaRemarks,
        bool gslCompleted,
        DateOnly? gslDate,
        string? gslRemarks,
        string? overallRemarks,
        long? currentRecordId,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (countryId <= 0)
        {
            errors["CountryId"] = ["Select a country."];
        }
        else
        {
            var countryIsActive = await _db.FfcCountries
                .AsNoTracking()
                .AnyAsync(country => country.Id == countryId && country.IsActive, cancellationToken);

            var isCurrentRecordCountry = false;
            if (!countryIsActive && currentRecordId.HasValue)
            {
                isCurrentRecordCountry = await _db.FfcRecords
                    .AsNoTracking()
                    .AnyAsync(record =>
                        record.Id == currentRecordId.Value &&
                        !record.IsDeleted &&
                        record.CountryId == countryId,
                        cancellationToken);
            }

            if (!countryIsActive && !isCurrentRecordCountry)
            {
                errors["CountryId"] = ["The selected country is not available for FFC reporting."];
            }
        }

        if (year is < 2000 or > 2100)
        {
            errors["Year"] = ["Enter a year between 2000 and 2100."];
        }

        if (!ipaCompleted && ipaDate.HasValue)
        {
            errors["IpaDate"] = ["An IPA date can only be recorded when IPA is completed."];
        }

        if (!gslCompleted && gslDate.HasValue)
        {
            errors["GslDate"] = ["A GSL date can only be recorded when GSL is completed."];
        }

        AddLengthError(errors, "IpaRemarks", ipaRemarks, RemarksMaxLength, "IPA remarks");
        AddLengthError(errors, "GslRemarks", gslRemarks, RemarksMaxLength, "GSL remarks");
        AddLengthError(errors, "OverallRemarks", overallRemarks, RemarksMaxLength, "Overall remarks");

        if (countryId > 0 && year is >= 2000 and <= 2100)
        {
            var duplicateExists = await _db.FfcRecords
                .AsNoTracking()
                .AnyAsync(record =>
                    !record.IsDeleted &&
                    record.CountryId == countryId &&
                    record.Year == year &&
                    (!currentRecordId.HasValue || record.Id != currentRecordId.Value),
                    cancellationToken);

            if (duplicateExists)
            {
                errors["Year"] = ["An active FFC record already exists for the selected country and year."];
            }
        }

        return errors.Count == 0
            ? null
            : FfcCommandResult.Invalid(fieldErrors: errors);
    }

    private async Task TryAuditAsync(
        string action,
        long recordId,
        IReadOnlyDictionary<string, string?>? before,
        IReadOnlyDictionary<string, string?>? after)
    {
        try
        {
            var http = _httpContextAccessor.HttpContext;
            var user = http?.User;
            var data = new Dictionary<string, string?>
            {
                ["RecordId"] = recordId.ToString(CultureInfo.InvariantCulture)
            };

            if (before is not null)
            {
                foreach (var pair in before)
                {
                    data[$"Before.{pair.Key}"] = pair.Value;
                }
            }

            if (after is not null)
            {
                foreach (var pair in after)
                {
                    data[$"After.{pair.Key}"] = pair.Value;
                }
            }

            await _audit.LogAsync(
                action,
                userId: user?.FindFirstValue(ClaimTypes.NameIdentifier),
                userName: user?.Identity?.Name,
                data: data,
                http: http);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to write FFC audit entry {Action} for record {RecordId}.", action, recordId);
        }
    }

    private static Dictionary<string, string?> Snapshot(FfcRecord record)
        => new(StringComparer.Ordinal)
        {
            ["CountryId"] = record.CountryId.ToString(CultureInfo.InvariantCulture),
            ["Year"] = record.Year.ToString(CultureInfo.InvariantCulture),
            ["IpaCompleted"] = record.IpaYes.ToString(),
            ["IpaDate"] = record.IpaDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["IpaRemarks"] = record.IpaRemarks,
            ["GslCompleted"] = record.GslYes.ToString(),
            ["GslDate"] = record.GslDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["GslRemarks"] = record.GslRemarks,
            ["OverallRemarks"] = record.OverallRemarks,
            ["IsDeleted"] = record.IsDeleted.ToString()
        };

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    private static void AddLengthError(
        IDictionary<string, string[]> errors,
        string key,
        string? value,
        int maximumLength,
        string label)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maximumLength)
        {
            errors[key] = [$"{label} must be {maximumLength} characters or fewer."];
        }
    }

    private static IReadOnlyDictionary<string, string[]> Error(string key, string message)
        => new Dictionary<string, string[]>(StringComparer.Ordinal) { [key] = [message] };

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

    private static bool IsConstraintViolation(DbUpdateException exception, string constraintName)
        => exception.InnerException is PostgresException postgresException
           && string.Equals(postgresException.ConstraintName, constraintName, StringComparison.Ordinal);
}
