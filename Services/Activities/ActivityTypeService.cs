using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Models.Activities;

namespace ProjectManagement.Services.Activities;

public sealed class ActivityTypeService : IActivityTypeService
{
    private readonly IActivityTypeRepository _activityTypeRepository;
    private readonly IActivityTypeValidator _validator;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public ActivityTypeService(IActivityTypeRepository activityTypeRepository,
                               IActivityTypeValidator validator,
                               IUserContext userContext,
                               IClock clock)
    {
        _activityTypeRepository = activityTypeRepository;
        _validator = validator;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<ActivityType> CreateAsync(ActivityTypeInput input, CancellationToken cancellationToken = default)
    {
        EnsureAdminOrHod();
        await _validator.ValidateAsync(input, existing: null, cancellationToken);

        var userId = RequireUserId();
        var now = _clock.UtcNow;
        var type = new ActivityType
        {
            Name = input.Name.Trim(),
            Description = input.Description?.Trim(),
            IsActive = input.IsActive,
            CreatedByUserId = userId,
            CreatedAtUtc = now,
            LastModifiedByUserId = userId,
            LastModifiedAtUtc = now
        };

        await _activityTypeRepository.AddAsync(type, cancellationToken);
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

        existing.Name = input.Name.Trim();
        existing.Description = input.Description?.Trim();
        existing.IsActive = input.IsActive;
        existing.LastModifiedByUserId = RequireUserId();
        existing.LastModifiedAtUtc = _clock.UtcNow;

        await _activityTypeRepository.UpdateAsync(existing, cancellationToken);
        return existing;
    }

    public Task<IReadOnlyList<ActivityType>> ListAsync(CancellationToken cancellationToken = default)
    {
        return _activityTypeRepository.ListAsync(cancellationToken);
    }

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

    private static bool IsAdminOrHod(ClaimsPrincipal principal)
    {
        return principal.IsInRole("Admin") || principal.IsInRole("HoD");
    }
}
