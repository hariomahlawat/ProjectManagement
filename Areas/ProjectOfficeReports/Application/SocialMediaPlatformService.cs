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

public sealed class SocialMediaPlatformService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;

    public SocialMediaPlatformService(ApplicationDbContext db, IClock clock, IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<IReadOnlyList<SocialMediaPlatform>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var query = _db.SocialMediaPlatforms.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        return await query
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SocialMediaPlatformSummary>> GetSummariesAsync(CancellationToken cancellationToken)
    {
        return await _db.SocialMediaPlatforms.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new SocialMediaPlatformSummary(
                x.Id,
                x.Name,
                x.Description,
                x.IsActive,
                x.CreatedAtUtc,
                x.CreatedByUserId,
                x.LastModifiedAtUtc,
                x.LastModifiedByUserId,
                x.RowVersion))
            .ToListAsync(cancellationToken);
    }

    public Task<SocialMediaPlatform?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return _db.SocialMediaPlatforms.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<SocialMediaPlatformMutationResult> CreateAsync(string name, string? description, string createdByUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return SocialMediaPlatformMutationResult.Invalid("Name is required.");
        }

        name = name.Trim();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        var exists = await _db.SocialMediaPlatforms.AnyAsync(x => x.Name == name, cancellationToken);
        if (exists)
        {
            return SocialMediaPlatformMutationResult.DuplicateName();
        }

        var now = _clock.UtcNow;
        var entity = new SocialMediaPlatform
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

        _db.SocialMediaPlatforms.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        await Audit.Events.SocialMediaPlatformAdded(entity.Id, name, createdByUserId).WriteAsync(_audit);

        return SocialMediaPlatformMutationResult.Success(entity);
    }

    public async Task<SocialMediaPlatformMutationResult> UpdateAsync(Guid id, string name, string? description, bool isActive, byte[] rowVersion, string modifiedByUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return SocialMediaPlatformMutationResult.Invalid("Name is required.");
        }

        name = name.Trim();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        var entity = await _db.SocialMediaPlatforms.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity == null)
        {
            return SocialMediaPlatformMutationResult.NotFound();
        }

        var duplicate = await _db.SocialMediaPlatforms
            .Where(x => x.Id != id)
            .AnyAsync(x => x.Name == name, cancellationToken);
        if (duplicate)
        {
            return SocialMediaPlatformMutationResult.DuplicateName();
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
            return SocialMediaPlatformMutationResult.Concurrency();
        }

        await Audit.Events.SocialMediaPlatformUpdated(entity.Id, name, isActive, modifiedByUserId).WriteAsync(_audit);

        return SocialMediaPlatformMutationResult.Success(entity);
    }

    public async Task<SocialMediaPlatformMutationResult> ToggleAsync(Guid id, bool enable, byte[] rowVersion, string modifiedByUserId, CancellationToken cancellationToken)
    {
        var existing = await _db.SocialMediaPlatforms.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing == null)
        {
            return SocialMediaPlatformMutationResult.NotFound();
        }

        return await UpdateAsync(id, existing.Name, existing.Description, enable, rowVersion, modifiedByUserId, cancellationToken);
    }

    public async Task<SocialMediaPlatformDeletionResult> DeleteAsync(Guid id, byte[] rowVersion, string performedByUserId, CancellationToken cancellationToken)
    {
        var entity = await _db.SocialMediaPlatforms.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity == null)
        {
            return SocialMediaPlatformDeletionResult.NotFound();
        }

        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = rowVersion;
        _db.SocialMediaPlatforms.Remove(entity);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SocialMediaPlatformDeletionResult.Concurrency();
        }

        await Audit.Events.SocialMediaPlatformDeleted(id, performedByUserId).WriteAsync(_audit);
        return SocialMediaPlatformDeletionResult.Success();
    }
}

public sealed record SocialMediaPlatformMutationResult(SocialMediaPlatformMutationOutcome Outcome, SocialMediaPlatform? Entity, IReadOnlyList<string> Errors)
{
    public static SocialMediaPlatformMutationResult Success(SocialMediaPlatform entity)
        => new(SocialMediaPlatformMutationOutcome.Success, entity, Array.Empty<string>());

    public static SocialMediaPlatformMutationResult NotFound()
        => new(SocialMediaPlatformMutationOutcome.NotFound, null, Array.Empty<string>());

    public static SocialMediaPlatformMutationResult DuplicateName()
        => new(SocialMediaPlatformMutationOutcome.DuplicateName, null, new[] { "A social media platform with the same name already exists." });

    public static SocialMediaPlatformMutationResult Invalid(string message)
        => new(SocialMediaPlatformMutationOutcome.Invalid, null, new[] { message });

    public static SocialMediaPlatformMutationResult Concurrency()
        => new(SocialMediaPlatformMutationOutcome.ConcurrencyConflict, null, new[] { "The social media platform was modified by another user. Please reload and try again." });
}

public enum SocialMediaPlatformMutationOutcome
{
    Success,
    NotFound,
    DuplicateName,
    Invalid,
    ConcurrencyConflict
}

public sealed record SocialMediaPlatformDeletionResult(SocialMediaPlatformDeletionOutcome Outcome)
{
    public static SocialMediaPlatformDeletionResult Success() => new(SocialMediaPlatformDeletionOutcome.Success);

    public static SocialMediaPlatformDeletionResult NotFound() => new(SocialMediaPlatformDeletionOutcome.NotFound);

    public static SocialMediaPlatformDeletionResult Concurrency() => new(SocialMediaPlatformDeletionOutcome.ConcurrencyConflict);
}

public enum SocialMediaPlatformDeletionOutcome
{
    Success,
    NotFound,
    ConcurrencyConflict
}

public sealed record SocialMediaPlatformSummary(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    string CreatedByUserId,
    DateTimeOffset? LastModifiedAtUtc,
    string? LastModifiedByUserId,
    byte[] RowVersion);
