using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Compendiums;

public sealed record CompendiumReviewFingerprintInput(
    int ProjectId,
    string ProjectName,
    ProjectLifecycleStatus LifecycleStatus,
    string? ProjectCategory,
    string? TechnicalCategory,
    string? ArmService,
    int? CompletionYear,
    bool? ProliferationAvailability,
    decimal? ProliferationCostLakhs,
    string? Description,
    int? ResolvedPhotoId,
    CompendiumImageSelectionMode ImageSelectionMode,
    double FocalX,
    double FocalY)
{
    public CompendiumNarrativeSource NarrativeSource { get; init; } = CompendiumNarrativeSource.ProjectDescription;
}

public static class CompendiumReviewFingerprint
{
    private const string ContractVersion = "compendium-review-v2";

    public static string Create(CompendiumReviewFingerprintInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var canonical = string.Join("\u001f", new[]
        {
            ContractVersion,
            input.ProjectId.ToString(CultureInfo.InvariantCulture),
            Clean(input.ProjectName),
            input.LifecycleStatus.ToString(),
            Clean(input.ProjectCategory),
            Clean(input.TechnicalCategory),
            Clean(input.ArmService),
            input.CompletionYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            input.ProliferationAvailability switch { true => "true", false => "false", _ => "null" },
            input.ProliferationCostLakhs?.ToString("0.############################", CultureInfo.InvariantCulture) ?? string.Empty,
            input.NarrativeSource.ToString(),
            NormalizeNarrative(input.Description),
            input.ResolvedPhotoId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            input.ImageSelectionMode.ToString(),
            Clamp(input.FocalX).ToString("0.0000", CultureInfo.InvariantCulture),
            Clamp(input.FocalY).ToString("0.0000", CultureInfo.InvariantCulture)
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string Clean(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string NormalizeNarrative(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Trim();

    private static double Clamp(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : .5d;
}
