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

public interface IActivityTypeService
{
    Task<IReadOnlyList<ActivityType>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActivityTypeSummary>> GetSummariesAsync(CancellationToken cancellationToken);

    Task<ActivityType?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<ActivityTypeMutationResult> CreateAsync(
        string name,
        string? description,
        int ordinal,
        CancellationToken cancellationToken);

    Task<ActivityTypeMutationResult> UpdateAsync(
        Guid id,
        string name,
        string? description,
        bool isActive,
        int ordinal,
        byte[] rowVersion,
        CancellationToken cancellationToken);

    Task<ActivityTypeDeletionResult> DeleteAsync(
        Guid id,
        byte[] rowVersion,
        CancellationToken cancellationToken);
}

public sealed class ActivityTypeService : IActivityTypeService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IUserContext _userContext;

    public ActivityTypeService(
        ApplicationDbContext db,
        IClock clock,
        IAuditService audit,
        IUserContext userContext)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    }

    public async Task<IReadOnlyList<ActivityType>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var query = _db.ActivityTypes.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        return await query
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActivityTypeSummary>> GetSummariesAsync(CancellationToken cancellationToken)
    {
        return await _db.ActivityTypes.AsNoTracking()
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.Name)
            .Select(x => new ActivityTypeSummary(
                x.Id,
                x.Name,
                x.Description,
                x.IsActive,
                x.Ordinal,
                x.Activities.Count,
                x.CreatedAtUtc,
                x.CreatedByUserId,
                x.LastModifiedAtUtc,
                x.LastModifiedByUserId,
                x.RowVersion))
            .ToListAsync(cancellationToken);
    }

    public Task<ActivityType?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return _db.ActivityTypes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<ActivityTypeMutationResult> CreateAsync(
        string name,
        string? description,
        int ordinal,
        CancellationToken cancellationToken)
    {
        if (!TryGetManagerUserId(out var userId))
        {
            return ActivityTypeMutationResult.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return ActivityTypeMutationResult.Invalid("Name is required.");
        }

        if (ordinal < 0)
        {
            return ActivityTypeMutationResult.Invalid("Ordinal must be zero or greater.");
        }

        name = name.Trim();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        var duplicate = await _db.ActivityTypes.AnyAsync(x => x.Name == name, cancellationToken);
        if (duplicate)
        {
            return ActivityTypeMutationResult.DuplicateName();
        }

        var now = _clock.UtcNow;
        var entity = new ActivityType
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            IsActive = true,
            Ordinal = ordinal,
            CreatedAtUtc = now,
            CreatedByUserId = userId,
            LastModifiedAtUtc = now,
            LastModifiedByUserId = userId
        };

        _db.ActivityTypes.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        await Audit.Events.ActivityTypeAdded(entity.Id, name, ordinal, userId).WriteAsync(_audit);
        return ActivityTypeMutationResult.Success(entity);
    }

    public async Task<ActivityTypeMutationResult> UpdateAsync(
        Guid id,
        string name,
        string? description,
        bool isActive,
        int ordinal,
        byte[] rowVersion,
        CancellationToken cancellationToken)
    {
        if (!TryGetManagerUserId(out var userId))
        {
            return ActivityTypeMutationResult.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return ActivityTypeMutationResult.Invalid("Name is required.");
        }

        if (ordinal < 0)
        {
            return ActivityTypeMutationResult.Invalid("Ordinal must be zero or greater.");
        }

        name = name.Trim();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        var entity = await _db.ActivityTypes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity == null)
        {
            return ActivityTypeMutationResult.NotFound();
        }

        var duplicate = await _db.ActivityTypes
            .Where(x => x.Id != id)
            .AnyAsync(x => x.Name == name, cancellationToken);
        if (duplicate)
        {
            return ActivityTypeMutationResult.DuplicateName();
        }

        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = rowVersion;

        entity.Name = name;
        entity.Description = description;
        entity.IsActive = isActive;
        entity.Ordinal = ordinal;
        entity.LastModifiedAtUtc = _clock.UtcNow;
        entity.LastModifiedByUserId = userId;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ActivityTypeMutationResult.Concurrency();
        }

        await Audit.Events.ActivityTypeUpdated(entity.Id, name, isActive, ordinal, userId).WriteAsync(_audit);
        return ActivityTypeMutationResult.Success(entity);
    }

    public async Task<ActivityTypeDeletionResult> DeleteAsync(
        Guid id,
        byte[] rowVersion,
        CancellationToken cancellationToken)
    {
        if (!TryGetManagerUserId(out var userId))
        {
            return ActivityTypeDeletionResult.Unauthorized();
        }

        var entity = await _db.ActivityTypes
            .Include(x => x.Activities)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity == null)
        {
            return ActivityTypeDeletionResult.NotFound();
        }

        if (entity.Activities.Count > 0)
        {
            return ActivityTypeDeletionResult.InUse(entity.Activities.Count);
        }

        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = rowVersion;
        _db.ActivityTypes.Remove(entity);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ActivityTypeDeletionResult.Concurrency();
        }

        await Audit.Events.ActivityTypeDeleted(id, userId).WriteAsync(_audit);
        return ActivityTypeDeletionResult.Success();
    }

    private bool TryGetManagerUserId(out string userId)
    {
        userId = string.Empty;
        var principal = _userContext.User;
        if (principal == null)
        {
            return false;
        }

        if (!ProjectOfficeReportsPolicies.IsActivityTypeManager(principal))
        {
            return false;
        }

        var value = _userContext.UserId;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        userId = value;
        return true;
    }
}

public sealed record ActivityTypeSummary(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int Ordinal,
    int ActivityCount,
    DateTimeOffset CreatedAtUtc,
    string CreatedByUserId,
    DateTimeOffset? LastModifiedAtUtc,
    string? LastModifiedByUserId,
    byte[] RowVersion);

public sealed record ActivityTypeMutationResult(
    ActivityTypeMutationOutcome Outcome,
    ActivityType? Entity,
    IReadOnlyList<string> Errors)
{
    public static ActivityTypeMutationResult Success(ActivityType entity)
        => new(ActivityTypeMutationOutcome.Success, entity, Array.Empty<string>());

    public static ActivityTypeMutationResult Unauthorized()
        => new(ActivityTypeMutationOutcome.Unauthorized, null, new[] { "You are not allowed to manage activity types." });

    public static ActivityTypeMutationResult Invalid(string message)
        => new(ActivityTypeMutationOutcome.Invalid, null, new[] { message });

    public static ActivityTypeMutationResult DuplicateName()
        => new(ActivityTypeMutationOutcome.DuplicateName, null, new[] { "An activity type with the same name already exists." });

    public static ActivityTypeMutationResult NotFound()
        => new(ActivityTypeMutationOutcome.NotFound, null, new[] { "The requested activity type could not be found." });

    public static ActivityTypeMutationResult Concurrency()
        => new(ActivityTypeMutationOutcome.ConcurrencyConflict, null, new[] { "The activity type was modified by another user. Please reload and try again." });
}

public enum ActivityTypeMutationOutcome
{
    Success,
    Unauthorized,
    Invalid,
    DuplicateName,
    NotFound,
    ConcurrencyConflict
}

public sealed record ActivityTypeDeletionResult(ActivityTypeDeletionOutcome Outcome, IReadOnlyList<string> Errors)
{
    public static ActivityTypeDeletionResult Success()
        => new(ActivityTypeDeletionOutcome.Success, Array.Empty<string>());

    public static ActivityTypeDeletionResult Unauthorized()
        => new(ActivityTypeDeletionOutcome.Unauthorized, new[] { "You are not allowed to manage activity types." });

    public static ActivityTypeDeletionResult NotFound()
        => new(ActivityTypeDeletionOutcome.NotFound, new[] { "The requested activity type could not be found." });

    public static ActivityTypeDeletionResult InUse(int activityCount)
        => new(ActivityTypeDeletionOutcome.InUse, new[] { $"Cannot delete the activity type because it is referenced by {activityCount} activity(ies)." });

    public static ActivityTypeDeletionResult Concurrency()
        => new(ActivityTypeDeletionOutcome.ConcurrencyConflict, new[] { "The activity type was modified by another user. Please reload and try again." });
}

public enum ActivityTypeDeletionOutcome
{
    Success,
    Unauthorized,
    NotFound,
    InUse,
    ConcurrencyConflict
}
