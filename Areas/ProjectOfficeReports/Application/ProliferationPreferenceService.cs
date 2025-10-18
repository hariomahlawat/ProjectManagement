using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class ProliferationPreferenceService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;

    public ProliferationPreferenceService(ApplicationDbContext db, IClock clock, IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public Task<ProliferationYearPreference?> FindAsync(
        int projectId,
        ProliferationSource source,
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User identifier is required.", nameof(userId));
        }

        return _db.ProliferationYearPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ProjectId == projectId && x.Source == source && x.UserId == userId,
                cancellationToken);
    }

    public async Task<ProliferationPreferenceCommandResult> SetPreferenceAsync(
        int projectId,
        ProliferationSource source,
        int year,
        string userId,
        byte[]? expectedRowVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ProliferationPreferenceCommandResult.Invalid("User identifier is required.");
        }

        if (year < 1900 || year > 9999)
        {
            return ProliferationPreferenceCommandResult.Invalid("Year must be between 1900 and 9999.");
        }

        var preference = await _db.ProliferationYearPreferences
            .FirstOrDefaultAsync(
                x => x.ProjectId == projectId && x.Source == source && x.UserId == userId,
                cancellationToken);

        var now = _clock.UtcNow;

        if (preference is null)
        {
            if (expectedRowVersion is { Length: > 0 })
            {
                return ProliferationPreferenceCommandResult.ConcurrencyConflict();
            }

            preference = new ProliferationYearPreference
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Source = source,
                Year = year,
                UserId = userId,
                CreatedAtUtc = now,
                CreatedByUserId = userId,
                LastModifiedAtUtc = now,
                LastModifiedByUserId = userId,
                RowVersion = Guid.NewGuid().ToByteArray()
            };

            _db.ProliferationYearPreferences.Add(preference);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ProliferationPreferenceCommandResult.ConcurrencyConflict();
            }
            catch (DbUpdateException)
            {
                var exists = await _db.ProliferationYearPreferences
                    .AsNoTracking()
                    .AnyAsync(
                        x => x.ProjectId == projectId && x.Source == source && x.UserId == userId,
                        cancellationToken);

                if (exists)
                {
                    return ProliferationPreferenceCommandResult.ConcurrencyConflict();
                }

                throw;
            }

            await Audit.Events.ProliferationPreferenceSaved(
                    projectId,
                    source,
                    year,
                    userId,
                    ProliferationPreferenceChangeOutcome.Created.ToString())
                .WriteAsync(_audit);

            return ProliferationPreferenceCommandResult.Success(preference, ProliferationPreferenceChangeOutcome.Created);
        }

        if (!TryApplyConcurrencyToken(preference, expectedRowVersion))
        {
            return ProliferationPreferenceCommandResult.ConcurrencyConflict();
        }

        preference.Year = year;
        preference.LastModifiedAtUtc = now;
        preference.LastModifiedByUserId = userId;
        preference.RowVersion = Guid.NewGuid().ToByteArray();

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ProliferationPreferenceCommandResult.ConcurrencyConflict();
        }

        await Audit.Events.ProliferationPreferenceSaved(
                projectId,
                source,
                year,
                userId,
                ProliferationPreferenceChangeOutcome.Updated.ToString())
            .WriteAsync(_audit);

        return ProliferationPreferenceCommandResult.Success(preference, ProliferationPreferenceChangeOutcome.Updated);
    }

    public async Task<ProliferationPreferenceCommandResult> ClearPreferenceAsync(
        int projectId,
        ProliferationSource source,
        string userId,
        byte[]? expectedRowVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ProliferationPreferenceCommandResult.Invalid("User identifier is required.");
        }

        var preference = await _db.ProliferationYearPreferences
            .FirstOrDefaultAsync(
                x => x.ProjectId == projectId && x.Source == source && x.UserId == userId,
                cancellationToken);

        if (preference is null)
        {
            if (expectedRowVersion is { Length: > 0 })
            {
                return ProliferationPreferenceCommandResult.ConcurrencyConflict();
            }

            return ProliferationPreferenceCommandResult.Success(
                null,
                ProliferationPreferenceChangeOutcome.NoChange);
        }

        if (!TryApplyConcurrencyToken(preference, expectedRowVersion))
        {
            return ProliferationPreferenceCommandResult.ConcurrencyConflict();
        }

        _db.ProliferationYearPreferences.Remove(preference);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ProliferationPreferenceCommandResult.ConcurrencyConflict();
        }

        await Audit.Events.ProliferationPreferenceCleared(projectId, source, userId)
            .WriteAsync(_audit);

        return ProliferationPreferenceCommandResult.Success(null, ProliferationPreferenceChangeOutcome.Cleared);
    }

    private bool TryApplyConcurrencyToken(ProliferationYearPreference preference, byte[]? expectedRowVersion)
    {
        if (preference.RowVersion is { Length: > 0 })
        {
            if (expectedRowVersion is not { Length: > 0 })
            {
                return false;
            }

            if (!preference.RowVersion.AsSpan().SequenceEqual(expectedRowVersion))
            {
                return false;
            }

            _db.Entry(preference).Property(x => x.RowVersion).OriginalValue = expectedRowVersion;
            return true;
        }

        if (expectedRowVersion is { Length: > 0 })
        {
            _db.Entry(preference).Property(x => x.RowVersion).OriginalValue = expectedRowVersion;
        }

        return true;
    }
}

public sealed record ProliferationPreferenceCommandResult(
    ProliferationPreferenceChangeOutcome Outcome,
    ProliferationYearPreference? Preference,
    IReadOnlyList<string> Errors)
{
    public static ProliferationPreferenceCommandResult Success(
        ProliferationYearPreference? preference,
        ProliferationPreferenceChangeOutcome outcome)
        => new(outcome, preference, Array.Empty<string>());

    public static ProliferationPreferenceCommandResult Invalid(string message)
        => new(ProliferationPreferenceChangeOutcome.Invalid, null, new[] { message });

    public static ProliferationPreferenceCommandResult ConcurrencyConflict()
        => new(ProliferationPreferenceChangeOutcome.ConcurrencyConflict, null, new[]
        {
            "The preference was updated by another user. Please refresh the page and try again."
        });
}

public enum ProliferationPreferenceChangeOutcome
{
    Created,
    Updated,
    Cleared,
    NoChange,
    Invalid,
    ConcurrencyConflict
}
