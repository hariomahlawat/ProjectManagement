namespace ProjectManagement.Services.IndustryPartners;

public sealed class IndustryPartnerValidationException : Exception
{
    public IndustryPartnerValidationException(
        IDictionary<string, List<string>> errors,
        Exception? innerException = null)
        : base("Validation failed for the industry directory request.", innerException)
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = errors.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.AsReadOnly());
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Errors { get; }
}
