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
    public string? AdditionalNote { get; init; }
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
            Information("zeroCost", "Proliferation cost is explicitly recorded as zero.");
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

        var noteAdvisory = CompendiumPublicationNotePolicy.EditorialAdvisory(context.AdditionalNote);
        if (!string.IsNullOrWhiteSpace(noteAdvisory))
        {
            if (CompendiumPublicationNotePolicy.Normalize(context.AdditionalNote).Length > CompendiumPublicationNotePolicy.StrongAdvisoryCharacterCount)
                Warning("additionalNoteLong", noteAdvisory);
            else
                Information("additionalNoteLength", noteAdvisory);
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
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (normalized.Length == 0) return false;

        if (normalized.Contains("lorem ipsum", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("dummy text", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("dummy description", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Generic sample/test phrases are only suspicious in relatively short drafts. This avoids
        // flagging legitimate long technical briefs that happen to discuss test text or samples.
        if (normalized.Length <= 360
            && (normalized.Contains("testing text", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("test text", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("sample text", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("sample description", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Keep short status placeholders conservative so ordinary technical prose containing words
        // such as "test" or "update" is never flagged merely because of vocabulary.
        if (normalized.Length <= 96)
        {
            return normalized.Equals("tbd", StringComparison.OrdinalIgnoreCase)
                   || normalized.Equals("to be updated", StringComparison.OrdinalIgnoreCase)
                   || normalized.Equals("to be added", StringComparison.OrdinalIgnoreCase)
                   || normalized.Equals("not yet updated", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool ContainsDuplicateNarrativeParagraph(string value)
    {
        var paragraphs = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeForSimilarity)
            .Where(paragraph => paragraph.Length >= 120)
            .ToArray();

        for (var left = 0; left < paragraphs.Length; left++)
        {
            for (var right = left + 1; right < paragraphs.Length; right++)
            {
                if (Similarity(paragraphs[left], paragraphs[right]) >= .92d)
                    return true;
            }
        }

        return paragraphs.Length < 2 && ContainsRepeatedLongWordBlock(value);
    }

    private static string NormalizeForSimilarity(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static double Similarity(string left, string right)
    {
        var leftWords = left.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var rightWords = right.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (leftWords.Length < 20 || rightWords.Length < 20) return 0d;
        var compared = Math.Min(leftWords.Length, rightWords.Length);
        if (Math.Abs(leftWords.Length - rightWords.Length) > Math.Max(6, compared / 8)) return 0d;
        var matches = 0;
        for (var index = 0; index < compared; index++)
        {
            if (string.Equals(leftWords[index], rightWords[index], StringComparison.Ordinal)) matches++;
        }
        return matches / (double)compared;
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
