using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.Services.Activities;

public sealed class ActivityValidationException : Exception
{
    public ActivityValidationException(IDictionary<string, List<string>> errors)
        : base("Validation failed for the activity request.")
    {
        Errors = errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.AsReadOnly());
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Errors { get; }
}
