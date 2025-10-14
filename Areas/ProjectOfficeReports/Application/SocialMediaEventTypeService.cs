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

public sealed class SocialMediaEventTypeService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;

    public SocialMediaEventTypeService(ApplicationDbContext db, IClock clock, IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<IReadOnlyList<SocialMediaEventType>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var query = _db.SocialMediaEventTypes.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        return await query
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SocialMediaEventTypeSummary>> GetSummariesAsync(CancellationToken cancellationToken)
    {
        return await _db.SocialMediaEventTypes.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new SocialMediaEventTypeSummary(
                x.Id,
                x.Name,
                x.Description,
                x.IsActive,
                x.Events.Count,
                x.CreatedAtUtc,
                x.CreatedByUserId,
                x.LastModifiedAtUtc,
                x.LastModifiedByUserId,
                x.RowVersion))
            .ToListAsync(cancellationToken);
    }

    public Task<SocialMediaEventType?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return _db.SocialMediaEventTypes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<SocialMediaEventTypeMutationResult> CreateAsync(string name, string? description, string createdByUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return SocialMediaEventTypeMutationResult.Invalid("Name is required.");
        }

        name = name.Trim();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        var exists = await _db.SocialMediaEventTypes.AnyAsync(x => x.Name == name, cancellationToken);
        if (exists)
        {
            return SocialMediaEventTypeMutationResult.DuplicateName();
        }

        var now = _clock.UtcNow;
        var entity = new SocialMediaEventType
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            CreatedAtUtc = now,
            CreatedByUserId = createdByUserId,
            LastModifiedAtUtc = now,
            LastModifiedByUserId = createdByUserId,
            IsActive = true
        };

        _db.SocialMediaEventTypes.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        await Audit.Events.SocialMediaEventTypeAdded(entity.Id, name, createdByUserId).WriteAsync(_audit);

        return SocialMediaEventTypeMutationResult.Success(entity);
    }

    public async Task<SocialMediaEventTypeMutationResult> UpdateAsync(Guid id, string name, string? description, bool isActive, byte[] rowVersion, string modifiedByUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return SocialMediaEventTypeMutationResult.Invalid("Name is required.");
        }

        name = name.Trim();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        var entity = await _db.SocialMediaEventTypes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity == null)
        {
            return SocialMediaEventTypeMutationResult.NotFound();
        }

        var duplicate = await _db.SocialMediaEventTypes
            .Where(x => x.Id != id)
            .AnyAsync(x => x.Name == name, cancellationToken);
        if (duplicate)
        {
            return SocialMediaEventTypeMutationResult.DuplicateName();
        }

        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = rowVersion;

        var now = _clock.UtcNow;
        entity.Name = name;
        entity.Description = description;
        entity.IsActive = isActive;
        entity.LastModifiedAtUtc = now;
        entity.LastModifiedByUserId = modifiedByUserId;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SocialMediaEventTypeMutationResult.Concurrency();
        }

        await Audit.Events.SocialMediaEventTypeUpdated(entity.Id, name, isActive, modifiedByUserId).WriteAsync(_audit);

        return SocialMediaEventTypeMutationResult.Success(entity);
    }

    public async Task<SocialMediaEventTypeDeletionResult> DeleteAsync(Guid id, byte[] rowVersion, string performedByUserId, CancellationToken cancellationToken)
    {
        var entity = await _db.SocialMediaEventTypes
            .Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity == null)
        {
            return SocialMediaEventTypeDeletionResult.NotFound();
        }

        if (entity.Events.Count > 0)
        {
            return SocialMediaEventTypeDeletionResult.InUse(entity.Events.Count);
        }

        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = rowVersion;
        _db.SocialMediaEventTypes.Remove(entity);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SocialMediaEventTypeDeletionResult.Concurrency();
        }

        await Audit.Events.SocialMediaEventTypeDeleted(id, performedByUserId).WriteAsync(_audit);
        return SocialMediaEventTypeDeletionResult.Success();
    }
}

public sealed record SocialMediaEventTypeMutationResult(SocialMediaEventTypeMutationOutcome Outcome, SocialMediaEventType? Entity, IReadOnlyList<string> Errors)
{
    public static SocialMediaEventTypeMutationResult Success(SocialMediaEventType entity)
        => new(SocialMediaEventTypeMutationOutcome.Success, entity, Array.Empty<string>());

    public static SocialMediaEventTypeMutationResult NotFound()
        => new(SocialMediaEventTypeMutationOutcome.NotFound, null, Array.Empty<string>());

    public static SocialMediaEventTypeMutationResult DuplicateName()
        => new(SocialMediaEventTypeMutationOutcome.DuplicateName, null, new[] { "A social media event type with the same name already exists." });

    public static SocialMediaEventTypeMutationResult Invalid(string message)
        => new(SocialMediaEventTypeMutationOutcome.Invalid, null, new[] { message });

    public static SocialMediaEventTypeMutationResult Concurrency()
        => new(SocialMediaEventTypeMutationOutcome.ConcurrencyConflict, null, new[] { "The social media event type was modified by another user. Please reload and try again." });
}

public enum SocialMediaEventTypeMutationOutcome
{
    Success,
    NotFound,
    DuplicateName,
    Invalid,
    ConcurrencyConflict
}

public sealed record SocialMediaEventTypeDeletionResult(SocialMediaEventTypeDeletionOutcome Outcome, int ReferencedCount)
{
    public static SocialMediaEventTypeDeletionResult Success() => new(SocialMediaEventTypeDeletionOutcome.Success, 0);

    public static SocialMediaEventTypeDeletionResult NotFound() => new(SocialMediaEventTypeDeletionOutcome.NotFound, 0);

    public static SocialMediaEventTypeDeletionResult InUse(int count) => new(SocialMediaEventTypeDeletionOutcome.InUse, count);

    public static SocialMediaEventTypeDeletionResult Concurrency() => new(SocialMediaEventTypeDeletionOutcome.ConcurrencyConflict, 0);
}

public enum SocialMediaEventTypeDeletionOutcome
{
    Success,
    NotFound,
    InUse,
    ConcurrencyConflict
}

public sealed record SocialMediaEventTypeSummary(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int EventCount,
    DateTimeOffset CreatedAtUtc,
    string CreatedByUserId,
    DateTimeOffset? LastModifiedAtUtc,
    string? LastModifiedByUserId,
    byte[] RowVersion);
