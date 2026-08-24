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
    string? SponsoringLineDirectorate,
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
    public CompendiumBalancedTextFlowMode BalancedTextFlowMode { get; init; } = CompendiumBalancedTextFlowMode.FlowBelowImage;
    public CompendiumNarrativeAlignment NarrativeAlignment { get; init; } = CompendiumNarrativeAlignment.Left;
    public CompendiumProjectParticularsStyle ProjectParticularsStyle { get; init; } = CompendiumProjectParticularsStyle.Panel;
    public IReadOnlyList<CompendiumDossierImageSelection> DossierImages { get; init; } = Array.Empty<CompendiumDossierImageSelection>();
    public IReadOnlyList<string> TechnicalSpecifications { get; init; } = Array.Empty<string>();
    public IReadOnlyList<CompendiumIprCredentialDto> IprCredentials { get; init; } = Array.Empty<CompendiumIprCredentialDto>();
    public CompendiumTechnologyTransferDto? TechnologyTransfer { get; init; }
    public string? AdditionalNote { get; init; }
}

public static class CompendiumReviewFingerprint
{
    private const string LeftAlignedContractVersion = "compendium-review-v19-cover-identity";
    private const string JustifiedContractVersion = "compendium-review-v20-semantic-justification";

    public static string Create(CompendiumReviewFingerprintInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Phase 44 changes only the physical treatment of Justified prose. Keep the exact v19
        // contract for existing Left-aligned dossiers so they are not needlessly sent back for
        // review; existing Justified reviews are intentionally invalidated once.
        var contractVersion = input.NarrativeAlignment == CompendiumNarrativeAlignment.Justified
            ? JustifiedContractVersion
            : LeftAlignedContractVersion;

        var canonical = string.Join("\u001f", new[]
        {
            contractVersion,
            input.ProjectId.ToString(CultureInfo.InvariantCulture),
            Clean(input.ProjectName),
            input.LifecycleStatus.ToString(),
            Clean(input.ProjectCategory),
            Clean(input.TechnicalCategory),
            Clean(input.SponsoringLineDirectorate),
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
            input.BalancedTextFlowMode.ToString(),
            input.NarrativeAlignment.ToString(),
            CompendiumProjectParticularsLayoutPolicy.Normalize(input.ProjectParticularsStyle).ToString(),
            NormalizeNarrative(input.AdditionalNote),
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
                image.PhotoVersion?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                image.SourceWidth?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                image.SourceHeight?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
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
