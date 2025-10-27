using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Models.Activities;

namespace ProjectManagement.Services.Activities;

public interface IActivityTypeValidator
{
    Task ValidateAsync(ActivityTypeInput input, ActivityType? existing, CancellationToken cancellationToken);
}

internal sealed class ActivityTypeValidator : IActivityTypeValidator
{
    private readonly IActivityTypeRepository _activityTypeRepository;

    public ActivityTypeValidator(IActivityTypeRepository activityTypeRepository)
    {
        _activityTypeRepository = activityTypeRepository;
    }

    public async Task ValidateAsync(ActivityTypeInput input, ActivityType? existing, CancellationToken cancellationToken)
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
            AddError(string.Empty, "An activity type input payload is required.");
            throw new ActivityValidationException(errors);
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            AddError(nameof(input.Name), "Name is required.");
        }
        else if (input.Name.Length > 120)
        {
            AddError(nameof(input.Name), "Name must be 120 characters or fewer.");
        }

        if (input.Description is not null && input.Description.Length > 512)
        {
            AddError(nameof(input.Description), "Description must be 512 characters or fewer.");
        }

        if (errors.Count > 0)
        {
            throw new ActivityValidationException(errors);
        }

        var existingTypes = await _activityTypeRepository.ListAsync(cancellationToken);
        var normalizedName = input.Name.Trim();
        var duplicate = existingTypes
            .Where(x => existing is null || x.Id != existing.Id)
            .Any(x => string.Equals(x.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (duplicate)
        {
            AddError(nameof(input.Name), "An activity type with this name already exists.");
        }

        if (errors.Count > 0)
        {
            throw new ActivityValidationException(errors);
        }
    }
}
