using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Models.Activities;

namespace ProjectManagement.Services.Activities;

public interface IActivityInputValidator
{
    Task ValidateAsync(ActivityInput input, Activity? existing, CancellationToken cancellationToken);
}

internal sealed class ActivityInputValidator : IActivityInputValidator
{
    private readonly IActivityTypeRepository _activityTypeRepository;

    public ActivityInputValidator(IActivityTypeRepository activityTypeRepository)
    {
        _activityTypeRepository = activityTypeRepository;
    }

    public async Task ValidateAsync(ActivityInput input, Activity? existing, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void AddError(string key, string message)
        {
            if (!errors.TryGetValue(key, out var list))
            {
                list = new List<string>();
                errors[key] = list;
            }

            list.Add(message);
        }

        if (input is null)
        {
            AddError(string.Empty, "An activity input payload is required.");
            throw new ActivityValidationException(errors);
        }

        if (string.IsNullOrWhiteSpace(input.Title))
        {
            AddError(nameof(input.Title), "Title is required.");
        }
        else if (input.Title.Length > 200)
        {
            AddError(nameof(input.Title), "Title must be 200 characters or fewer.");
        }

        if (input.Description is not null && input.Description.Length > 2000)
        {
            AddError(nameof(input.Description), "Description must be 2000 characters or fewer.");
        }

        if (input.Location is not null && input.Location.Length > 450)
        {
            AddError(nameof(input.Location), "Location must be 450 characters or fewer.");
        }

        if (input.ActivityTypeId <= 0)
        {
            AddError(nameof(input.ActivityTypeId), "Activity type is required.");
        }

        // New records must always carry the actual event date. Historical imported
        // records that were created without a date remain editable without forcing
        // synthetic data; once a date exists it cannot be cleared.
        if (!input.ScheduledStartUtc.HasValue
            && (existing is null || existing.ScheduledStartUtc.HasValue))
        {
            AddError(nameof(input.ScheduledStartUtc), "Event date is required.");
        }

        if (input.ScheduledStartUtc.HasValue && input.ScheduledEndUtc.HasValue &&
            input.ScheduledEndUtc.Value < input.ScheduledStartUtc.Value)
        {
            AddError(nameof(input.ScheduledEndUtc), "End date must be on or after the start date.");
        }

        if (errors.Count > 0)
        {
            throw new ActivityValidationException(errors);
        }

        var type = await _activityTypeRepository.GetByIdAsync(input.ActivityTypeId, cancellationToken);
        if (type is null)
        {
            AddError(nameof(input.ActivityTypeId), "Selected activity type does not exist.");
        }
        else if (!type.IsActive)
        {
            AddError(nameof(input.ActivityTypeId), "Selected activity type is inactive.");
        }

        if (errors.Count > 0)
        {
            throw new ActivityValidationException(errors);
        }

        // Activity titles are descriptive, not identifiers. Repeated meeting/review
        // titles are legitimate and are therefore intentionally not uniqueness-validated.
    }
}
