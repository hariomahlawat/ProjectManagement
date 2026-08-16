namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Shared publication-only policy for the optional per-project Additional Note. Notes are never
/// written back to the authoritative Project record and are not hard-truncated; length guidance is
/// editorial only.
/// </summary>
public static class CompendiumPublicationNotePolicy
{
    public const int AdvisoryCharacterCount = 600;
    public const int StrongAdvisoryCharacterCount = 1000;
    public const float ContinuationBodyHeightPoints = 610f;

    public static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Trim();

    public static string? EditorialAdvisory(string? value)
    {
        var clean = Normalize(value);
        if (clean.Length > StrongAdvisoryCharacterCount)
            return "Additional note is lengthy and may require a continuation page. Consider moving detailed explanatory content into the Project Brief where appropriate.";
        if (clean.Length > AdvisoryCharacterCount)
            return "Additional note is becoming lengthy. Keep it focused on information that should close the published dossier.";
        return null;
    }
}
