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

public sealed class VisitTypeService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;

    public VisitTypeService(ApplicationDbContext db, IClock clock, IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<IReadOnlyList<VisitType>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var query = _db.VisitTypes.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        return await query
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VisitTypeSummary>> GetSummariesAsync(CancellationToken cancellationToken)
    {
        return await _db.VisitTypes.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new VisitTypeSummary(
                x.Id,
                x.Name,
                x.Description,
                x.IsActive,
                x.Visits.Count,
                x.CreatedAtUtc,
                x.CreatedByUserId,
                x.LastModifiedAtUtc,
                x.LastModifiedByUserId,
                x.RowVersion))
            .ToListAsync(cancellationToken);
    }

    public Task<VisitType?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return _db.VisitTypes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<VisitTypeMutationResult> CreateAsync(string name, string? description, string createdByUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return VisitTypeMutationResult.Invalid("Name is required.");
        }

        name = name.Trim();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        var exists = await _db.VisitTypes.AnyAsync(x => x.Name == name, cancellationToken);
        if (exists)
        {
            return VisitTypeMutationResult.DuplicateName();
        }

        var now = _clock.UtcNow;
        var entity = new VisitType
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

        _db.VisitTypes.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        await Audit.Events.VisitTypeAdded(entity.Id, name, createdByUserId).WriteAsync(_audit);

        return VisitTypeMutationResult.Success(entity);
    }

    public async Task<VisitTypeMutationResult> UpdateAsync(Guid id, string name, string? description, bool isActive, byte[] rowVersion, string modifiedByUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return VisitTypeMutationResult.Invalid("Name is required.");
        }

        name = name.Trim();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        var entity = await _db.VisitTypes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity == null)
        {
            return VisitTypeMutationResult.NotFound();
        }

        var duplicate = await _db.VisitTypes
            .Where(x => x.Id != id)
            .AnyAsync(x => x.Name == name, cancellationToken);
        if (duplicate)
        {
            return VisitTypeMutationResult.DuplicateName();
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
            return VisitTypeMutationResult.Concurrency();
        }

        await Audit.Events.VisitTypeUpdated(entity.Id, name, isActive, modifiedByUserId).WriteAsync(_audit);

        return VisitTypeMutationResult.Success(entity);
    }

    public async Task<VisitTypeDeletionResult> DeleteAsync(Guid id, byte[] rowVersion, string performedByUserId, CancellationToken cancellationToken)
    {
        var entity = await _db.VisitTypes
            .Include(x => x.Visits)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity == null)
        {
            return VisitTypeDeletionResult.NotFound();
        }

        if (entity.Visits.Count > 0)
        {
            return VisitTypeDeletionResult.InUse(entity.Visits.Count);
        }

        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = rowVersion;
        _db.VisitTypes.Remove(entity);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return VisitTypeDeletionResult.Concurrency();
        }

        await Audit.Events.VisitTypeDeleted(id, performedByUserId).WriteAsync(_audit);
        return VisitTypeDeletionResult.Success();
    }
}

public sealed record VisitTypeMutationResult(VisitTypeMutationOutcome Outcome, VisitType? Entity, IReadOnlyList<string> Errors)
{
    public static VisitTypeMutationResult Success(VisitType entity)
        => new(VisitTypeMutationOutcome.Success, entity, Array.Empty<string>());

    public static VisitTypeMutationResult NotFound()
        => new(VisitTypeMutationOutcome.NotFound, null, Array.Empty<string>());

    public static VisitTypeMutationResult DuplicateName()
        => new(VisitTypeMutationOutcome.DuplicateName, null, new[] { "A visit type with the same name already exists." });

    public static VisitTypeMutationResult Invalid(string message)
        => new(VisitTypeMutationOutcome.Invalid, null, new[] { message });

    public static VisitTypeMutationResult Concurrency()
        => new(VisitTypeMutationOutcome.ConcurrencyConflict, null, new[] { "The visit type was modified by another user. Please reload and try again." });
}

public enum VisitTypeMutationOutcome
{
    Success,
    NotFound,
    DuplicateName,
    Invalid,
    ConcurrencyConflict
}

public sealed record VisitTypeDeletionResult(VisitTypeDeletionOutcome Outcome, int ReferencedCount)
{
    public static VisitTypeDeletionResult Success() => new(VisitTypeDeletionOutcome.Success, 0);

    public static VisitTypeDeletionResult NotFound() => new(VisitTypeDeletionOutcome.NotFound, 0);

    public static VisitTypeDeletionResult InUse(int count) => new(VisitTypeDeletionOutcome.InUse, count);

    public static VisitTypeDeletionResult Concurrency() => new(VisitTypeDeletionOutcome.ConcurrencyConflict, 0);
}

public enum VisitTypeDeletionOutcome
{
    Success,
    NotFound,
    InUse,
    ConcurrencyConflict
}

public sealed record VisitTypeSummary(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int VisitCount,
    DateTimeOffset CreatedAtUtc,
    string CreatedByUserId,
    DateTimeOffset? LastModifiedAtUtc,
    string? LastModifiedByUserId,
    byte[] RowVersion);
