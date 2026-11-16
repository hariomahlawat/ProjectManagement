using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Search;

// SECTION: IT, MOLTI and SRE projects global search contract
public interface IGlobalItProjectSearchService
{
    Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);
}

// SECTION: IT, MOLTI and SRE projects global search implementation
/// <summary>
/// The basic engine's IT, MOLTI and SRE projects: the typed text found in the title, a
/// reference number (first year or any later year) or the description. Each module is its own
/// source label ("MOLTI projects"), so the fallback hides it from users without that module's
/// policy, as Search V2 does (<see cref="SearchV2.Security.SearchSourcePolicies"/>).
/// </summary>
public sealed class GlobalItProjectSearchService : IGlobalItProjectSearchService
{
    private readonly ApplicationDbContext _dbContext;

    public GlobalItProjectSearchService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<GlobalSearchHit>();
        }

        // "cabling -fibre": the phrase "cabling", and no record whose text has "fibre" anywhere.
        var parsed = SearchPhraseQuery.Parse(query);
        var trimmed = parsed.Phrase.Trim();
        var pattern = SearchLikePattern.Contains(trimmed);
        var candidates = _dbContext.ItProjects.AsNoTracking().Where(project => !project.IsDeleted);
        foreach (var excluded in parsed.ExcludedPatterns)
        {
            candidates = candidates.Where(project =>
                !Regex.IsMatch(project.Title, excluded, RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(project.PppReferenceNumber, excluded, RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(project.Description, excluded, RegexOptions.IgnoreCase));
        }

        var projects = await candidates
            .Where(project =>
                (EF.Functions.ILike(project.Title, pattern, SearchLikePattern.EscapeCharacter) ||
                 EF.Functions.ILike(project.PppReferenceNumber, pattern, SearchLikePattern.EscapeCharacter) ||
                 EF.Functions.ILike(project.Description, pattern, SearchLikePattern.EscapeCharacter) ||
                 project.FinancialYears.Any(year => year.PppReferenceNumber != null &&
                     EF.Functions.ILike(year.PppReferenceNumber, pattern, SearchLikePattern.EscapeCharacter))))
            .OrderByDescending(project => project.UpdatedAtUtc)
            .Take(Math.Max(1, maxResults))
            .Select(project => new
            {
                project.Id,
                project.Module,
                project.Title,
                project.PppReferenceNumber,
                project.Description,
                project.FinancialYearStart,
                project.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return projects
            .Where(project => ItProjectModules.IsKnown(project.Module))
            .Select(project =>
            {
                var module = ItProjectModules.Get(project.Module);
                return new GlobalSearchHit(
                    Source: module.Name,
                    Title: project.Title,
                    Snippet: Excerpt(project.Description, trimmed),
                    Url: $"{module.RoutePrefix}/{project.Id}",
                    Date: new DateTimeOffset(DateTime.SpecifyKind(project.UpdatedAtUtc, DateTimeKind.Utc)),
                    Score: 0.6m,
                    FileType: null,
                    Extra: $"{module.Singular} · {project.PppReferenceNumber} · FY {FinancialYearHelper.Format(project.FinancialYearStart)}");
            })
            .ToArray();
    }

    /// <summary>
    /// About 240 characters of the description, starting a little before the match so a
    /// three-line card shows it; the opening words when the match is elsewhere.
    /// </summary>
    internal static string? Excerpt(string? description, string query, int length = 240)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var text = description.Trim();
        if (text.Length <= length) return text;

        var at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        var start = at < 0 ? 0 : Math.Max(0, at - 60);
        if (start > 0)
        {
            // Start on a word.
            var space = text.IndexOf(' ', start);
            start = space < 0 || space >= at ? start : space + 1;
        }

        var end = Math.Min(text.Length, start + length);
        return (start > 0 ? "…" : string.Empty) + text[start..end].Trim() + (end < text.Length ? "…" : string.Empty);
    }
}
