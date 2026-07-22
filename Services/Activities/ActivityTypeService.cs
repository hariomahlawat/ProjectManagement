using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Models.Activities;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Services.Activities;

public sealed class ActivityTypeService : IActivityTypeService
{
    private readonly IActivityTypeRepository _activityTypeRepository;
    private readonly IActivityTypeValidator _validator;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly IAdminAuditService _audit;

    public ActivityTypeService(
        IActivityTypeRepository activityTypeRepository,
        IActivityTypeValidator validator,
        IUserContext userContext,
        IClock clock,
        IAdminAuditService audit)
    {
        _activityTypeRepository = activityTypeRepository;
        _validator = validator;
        _userContext = userContext;
        _clock = clock;
        _audit = audit;
    }

    public async Task<ActivityType> CreateAsync(ActivityTypeInput input, CancellationToken cancellationToken = default)
    {
        EnsureAdminOrHod();
        await _validator.ValidateAsync(input, existing: null, cancellationToken);

        var userId = RequireUserId();
        var now = _clock.UtcNow;
        var type = new ActivityType
        {
            Name = MasterDataName.Normalize(input.Name),
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            IsActive = input.IsActive,
            CreatedByUserId = userId,
            CreatedAtUtc = now,
            LastModifiedByUserId = userId,
            LastModifiedAtUtc = now
        };

        try
        {
            await _activityTypeRepository.AddAsync(type, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw DuplicateNameValidationException();
        }

        await _audit.RecordAsync(new AdminAuditEntry(
            "MasterData.ActivityTypeCreated",
            "ActivityType",
            type.Id.ToString(),
            After: Snapshot(type),
            Origin: "Admin.ActivityTypes"), cancellationToken);

        return type;
    }

    public async Task<ActivityType> UpdateAsync(int activityTypeId, ActivityTypeInput input, CancellationToken cancellationToken = default)
    {
        var existing = await _activityTypeRepository.GetByIdAsync(activityTypeId, cancellationToken);
        if (existing is null)
        {
            throw new KeyNotFoundException("Activity type not found.");
        }

        EnsureAdminOrHod();
        await _validator.ValidateAsync(input, existing, cancellationToken);

        var before = Snapshot(existing);
        existing.Name = MasterDataName.Normalize(input.Name);
        existing.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        existing.IsActive = input.IsActive;
        existing.LastModifiedByUserId = RequireUserId();
        existing.LastModifiedAtUtc = _clock.UtcNow;

        if (input.RowVersion is not { Length: > 0 })
        {
            throw new ActivityValidationException(new Dictionary<string, List<string>>
            {
                [string.Empty] = new()
                {
                    "The record version is missing. Reload the page and try again."
                }
            });
        }

        try
        {
            await _activityTypeRepository.UpdateAsync(existing, input.RowVersion, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ActivityValidationException(new Dictionary<string, List<string>>
            {
                [string.Empty] = new()
                {
                    "This activity type was changed by another administrator. Reload the page and try again."
                }
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw DuplicateNameValidationException();
        }

        await _audit.RecordAsync(new AdminAuditEntry(
            "MasterData.ActivityTypeUpdated",
            "ActivityType",
            existing.Id.ToString(),
            Before: before,
            After: Snapshot(existing),
            Origin: "Admin.ActivityTypes"), cancellationToken);

        return existing;
    }

    public Task<IReadOnlyList<ActivityType>> ListAsync(CancellationToken cancellationToken = default) =>
        _activityTypeRepository.ListAsync(cancellationToken);

    private string RequireUserId()
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ActivityAuthorizationException("A signed-in user is required.");
        }

        return userId;
    }

    private void EnsureAdminOrHod()
    {
        var principal = _userContext.User;
        if (!IsAdminOrHod(principal))
        {
            throw new ActivityAuthorizationException("Only Admin or HoD roles can manage activity types.");
        }
    }

    private static bool IsAdminOrHod(ClaimsPrincipal principal) =>
        principal.IsInRole("Admin") || principal.IsInRole("HoD");

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation;

    private static ActivityValidationException DuplicateNameValidationException() =>
        new(new Dictionary<string, List<string>>
        {
            [nameof(ActivityTypeInput.Name)] = new()
            {
                "An activity type with this name already exists."
            }
        });

    private static object Snapshot(ActivityType item) => new
    {
        item.Id,
        item.Name,
        item.Description,
        item.IsActive,
        item.LastModifiedByUserId,
        item.LastModifiedAtUtc
    };
}
