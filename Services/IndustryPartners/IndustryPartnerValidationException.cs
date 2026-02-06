using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.Services.IndustryPartners;

public sealed class IndustryPartnerValidationException : Exception
{
    public IndustryPartnerValidationException(IDictionary<string, List<string>> errors)
        : base("Validation failed for the industry partner request.")
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = errors.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<string>)kvp.Value.AsReadOnly());
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Errors { get; }
}
