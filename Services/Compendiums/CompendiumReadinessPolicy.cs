using System.Text;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Compendiums;

public sealed record CompendiumProjectReadinessContext(
    int ProjectId,
    string ProjectName,
    ProjectLifecycleStatus LifecycleStatus,
    int? CompletionYear,
    string? SponsoringLineDirectorate,
    string? Description,
    decimal? ProliferationCostLakhs,
    bool? ProliferationAvailability,
    int? ResolvedPhotoId,
    bool ResolvedPhotoUsable,
    CompendiumImageSelectionMode ImageSelectionMode,
    int? EffectiveDpi,
    bool ExplicitPhotoUnavailable,
    string CurrentReviewFingerprint,
    string? SubmittedReviewFingerprint)
{
    public string NarrativeLabel { get; init; } = "Project description";
    public string? DossierEditorialWarning { get; init; }
}

public sealed record CompendiumProjectReadinessAssessment(
    IReadOnlyList<CompendiumPublicationIssue> PublicationIssues,
    IReadOnlyList<CompendiumFindingDto> Findings,
    bool IsReviewed,
    bool IsReviewStale);

public interface ICompendiumReadinessPolicy
{
    CompendiumProjectReadinessAssessment Evaluate(CompendiumProjectReadinessContext context);
}

/// <summary>
/// Publication-quality policy for selected Compendium projects. The policy intentionally distinguishes
/// genuine blockers from editorial warnings and contextual information; normal project-data gaps do not
/// prevent preview/final generation unless the publication cannot be composed safely.
/// </summary>
public sealed class CompendiumReadinessPolicy : ICompendiumReadinessPolicy
{
    public CompendiumProjectReadinessAssessment Evaluate(CompendiumProjectReadinessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<CompendiumPublicationIssue>();
        var findings = new List<CompendiumFindingDto>();
        var submitted = NormalizeFingerprint(context.SubmittedReviewFingerprint);
        var current = NormalizeFingerprint(context.CurrentReviewFingerprint);
        var isReviewed = submitted is not null && string.Equals(submitted, current, StringComparison.Ordinal);
        var isReviewStale = submitted is not null && !isReviewed;

        void Blocker(string code, string message)
            => findings.Add(new CompendiumFindingDto(
                CompendiumFindingSeverity.Blocker,
                code,
                message,
                context.ProjectId,
                context.ProjectName));

        void Warning(string code, string message)
            => findings.Add(new CompendiumFindingDto(
                CompendiumFindingSeverity.Warning,
                code,
                message,
                context.ProjectId,
                context.ProjectName));

        void Information(string code, string message)
            => findings.Add(new CompendiumFindingDto(
                CompendiumFindingSeverity.Information,
                code,
                message,
                context.ProjectId,
                context.ProjectName));

        if (context.ExplicitPhotoUnavailable)
        {
            Warning(
                "publicationImageUnavailable",
                "The explicitly selected publication image is no longer available. PRISM has temporarily resolved the current best project image.");
        }

        if (!context.ResolvedPhotoId.HasValue || !context.ResolvedPhotoUsable)
        {
            issues.Add(CompendiumPublicationIssue.MissingPhoto);
            Warning(
                context.ResolvedPhotoId.HasValue ? "selectedPhotoUnavailable" : "missingPhoto",
                context.ResolvedPhotoId.HasValue
                    ? "The selected publication photograph cannot currently be opened from storage."
                    : "No usable publication photograph is available.");
        }
        else
        {
            if (context.EffectiveDpi is int dpi && dpi < CompendiumPublicationImagePolicy.AcceptableDpi)
            {
                Warning(
                    "lowResolutionPhoto",
                    $"The selected photograph is approximately {dpi} DPI at the current Compendium image size; use a higher-resolution source where possible.");
            }
            else if (context.EffectiveDpi is int acceptableDpi
                     && acceptableDpi < CompendiumPublicationImagePolicy.GoodDpi)
            {
                Information(
                    "acceptableResolutionPhoto",
                    $"The selected photograph is approximately {acceptableDpi} DPI at publication size. It is usable, but a higher-resolution source would provide more reserve.");
            }

        }

        if (string.IsNullOrWhiteSpace(context.SponsoringLineDirectorate))
        {
            issues.Add(CompendiumPublicationIssue.MissingSponsoringLineDirectorate);
            Information("missingSponsoringLineDirectorate", "Arms / Services is not recorded.");
        }

        if (context.ProliferationAvailability == true && !context.ProliferationCostLakhs.HasValue)
        {
            issues.Add(CompendiumPublicationIssue.MissingProliferationCost);
            Warning(
                "missingCost",
                "This project is marked available for proliferation but no proliferation cost is recorded.");
        }
        else if (context.ProliferationAvailability == true && context.ProliferationCostLakhs == 0m)
        {
            issues.Add(CompendiumPublicationIssue.ZeroProliferationCost);
            Warning("zeroCost", "Proliferation cost is zero; verify that this is intentional.");
        }

        if (string.IsNullOrWhiteSpace(context.Description))
        {
            issues.Add(CompendiumPublicationIssue.MissingDescription);
            Blocker("missingDescription", $"{NormalizeNarrativeLabel(context.NarrativeLabel)} is not recorded. Choose another publication narrative for this project or record the missing content before final issue.");
        }
        else
        {
            if (LooksLikePlaceholderNarrative(context.Description))
            {
                Warning(
                    "placeholderNarrative",
                    $"{NormalizeNarrativeLabel(context.NarrativeLabel)} appears to contain placeholder or test text; replace it before formal issue.");
            }

            if (ContainsDuplicateNarrativeParagraph(context.Description))
            {
                Warning(
                    "duplicateNarrativeParagraph",
                    $"{NormalizeNarrativeLabel(context.NarrativeLabel)} appears to repeat a paragraph. Review the source content before formal issue.");
            }
        }

        if (!string.IsNullOrWhiteSpace(context.DossierEditorialWarning))
        {
            Warning("dossierCompositionImbalance", context.DossierEditorialWarning.Trim());
        }

        if (context.LifecycleStatus == ProjectLifecycleStatus.Completed && !context.CompletionYear.HasValue)
        {
            issues.Add(CompendiumPublicationIssue.MissingCompletionYear);
            Warning("missingCompletionYear", "Completed project has no completion year.");
        }

        if (LooksLikeAiWasEnteredAsAl(context.ProjectName))
        {
            issues.Add(CompendiumPublicationIssue.PossibleTitleTypo);
            Warning("possibleTitleTypo", "Project title may contain “Al” where “AI” was intended.");
        }


        return new CompendiumProjectReadinessAssessment(
            issues,
            findings,
            isReviewed,
            isReviewStale);
    }

    private static string NormalizeNarrativeLabel(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Selected publication narrative" : value.Trim();

    private static string? NormalizeFingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = value.Trim();
        return clean.Length <= 128 ? clean : clean[..128];
    }

    private static bool LooksLikeAiWasEnteredAsAl(string value)
    {
        var normalized = value.TrimStart();
        return normalized.StartsWith("Al Based", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("Al-based", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikePlaceholderNarrative(string value)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Contains("lorem ipsum", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("dummy text", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("testing text", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("sample text", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsDuplicateNarrativeParagraph(string value)
    {
        var paragraphs = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(paragraph => string.Join(' ', paragraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim())
            .Where(paragraph => paragraph.Length >= 80)
            .ToArray();

        if (paragraphs.Length < 2)
        {
            // Some stored project briefs are plain text without paragraph breaks. Compare word
            // sequences around the midpoint so punctuation/spacing differences do not hide an
            // accidentally duplicated long block. This is deliberately conservative: the repeated
            // halves must be almost identical before publication preflight raises a warning.
            return ContainsRepeatedLongWordBlock(value);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var paragraph in paragraphs)
        {
            if (!seen.Add(paragraph))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsRepeatedLongWordBlock(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        var words = builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length < 50) return false;

        var midpoint = words.Length / 2;
        for (var split = Math.Max(20, midpoint - 4); split <= Math.Min(words.Length - 20, midpoint + 4); split++)
        {
            var leftLength = split;
            var rightLength = words.Length - split;
            if (Math.Abs(leftLength - rightLength) > 8) continue;

            var compared = Math.Min(leftLength, rightLength);
            var matches = 0;
            for (var index = 0; index < compared; index++)
            {
                if (string.Equals(words[index], words[split + index], StringComparison.Ordinal))
                    matches++;
            }

            if (compared >= 25 && matches / (double)compared >= .94d)
                return true;
        }

        return false;
    }
}
