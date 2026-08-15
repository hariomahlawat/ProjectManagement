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
    public string? PublicationSectionKey { get; init; }
    public string? PublicationSectionName { get; init; }
    public CompendiumImageFitMode ImageFitMode { get; init; } = CompendiumImageFitMode.Fill;
    public CompendiumDossierLayout DossierLayout { get; init; } = CompendiumDossierLayout.Automatic;
    public IReadOnlyList<CompendiumDossierImageSelection> DossierImages { get; init; } = Array.Empty<CompendiumDossierImageSelection>();
    public IReadOnlyList<string> TechnicalSpecifications { get; init; } = Array.Empty<string>();
    public IReadOnlyList<CompendiumIprCredentialDto> IprCredentials { get; init; } = Array.Empty<CompendiumIprCredentialDto>();
    public CompendiumTechnologyTransferDto? TechnologyTransfer { get; init; }
}

public static class CompendiumReviewFingerprint
{
    private const string ContractVersion = "compendium-review-v5";

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
            Clean(input.PublicationSectionKey),
            Clean(input.PublicationSectionName),
            NormalizeNarrative(input.Description),
            input.ResolvedPhotoId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            input.ImageSelectionMode.ToString(),
            input.ImageFitMode.ToString(),
            Clamp(input.FocalX).ToString("0.0000", CultureInfo.InvariantCulture),
            Clamp(input.FocalY).ToString("0.0000", CultureInfo.InvariantCulture),
            input.DossierLayout.ToString(),
            CanonicalDossierImages(input.DossierImages),
            CanonicalList(input.TechnicalSpecifications),
            CanonicalIpr(input.IprCredentials),
            input.TechnologyTransfer is null
                ? string.Empty
                : $"{Clean(input.TechnologyTransfer.Status)}:{input.TechnologyTransfer.CompletionYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}"
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

    private static string CanonicalDossierImages(IReadOnlyList<CompendiumDossierImageSelection>? images)
        => string.Join("|", (images ?? Array.Empty<CompendiumDossierImageSelection>())
            .OrderBy(image => image.Role)
            .Select(image => string.Join(":", new[]
            {
                image.Role.ToString(),
                image.PhotoId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                image.FitMode.ToString(),
                Clamp(image.FocalX).ToString("0.0000", CultureInfo.InvariantCulture),
                Clamp(image.FocalY).ToString("0.0000", CultureInfo.InvariantCulture)
            })));

    private static string CanonicalList(IReadOnlyList<string>? values)
        => string.Join("|", (values ?? Array.Empty<string>()).Select(NormalizeNarrative));

    private static string CanonicalIpr(IReadOnlyList<CompendiumIprCredentialDto>? values)
        => string.Join("|", (values ?? Array.Empty<CompendiumIprCredentialDto>())
            .OrderBy(value => value.Type, StringComparer.Ordinal)
            .ThenBy(value => value.Status, StringComparer.Ordinal)
            .ThenBy(value => value.Year)
            .Select(value => $"{Clean(value.Type)}:{Clean(value.Status)}:{value.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}"));

    private static double Clamp(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : .5d;
}
