using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using ProjectManagement.Data;
using ProjectManagement.Services.Navigation;

namespace ProjectManagement.Services.Search
{
    // SECTION: Project global search contract
    public interface IGlobalProjectSearchService
    {
        Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);
    }

    // SECTION: Project global search implementation
    public sealed class GlobalProjectSearchService : IGlobalProjectSearchService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IUrlBuilder _urlBuilder;

        public GlobalProjectSearchService(ApplicationDbContext dbContext, IUrlBuilder urlBuilder)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _urlBuilder = urlBuilder ?? throw new ArgumentNullException(nameof(urlBuilder));
        }

        public async Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<GlobalSearchHit>();
            }

            var trimmed = query.Trim();
            var literalPattern = SearchLikePattern.Contains(trimmed);
            var headlineOptions = "StartSel=<mark>, StopSel=</mark>, MaxWords=35, MinWords=10, ShortWord=3";
            var limit = Math.Max(1, maxResults);

            if (!_dbContext.Database.IsNpgsql())
            {
                var fallbackProjects = await _dbContext.Projects
                    .AsNoTracking()
                    .Include(p => p.SponsoringUnit)
                    .Include(p => p.SponsoringLineDirectorate)
                    .Where(p =>
                        !p.IsDeleted &&
                        !p.IsArchived &&
                        (
                            (!string.IsNullOrEmpty(p.Name) && EF.Functions.Like(p.Name!, literalPattern, SearchLikePattern.EscapeCharacter)) ||
                            (!string.IsNullOrEmpty(p.ProjectBrief) && EF.Functions.Like(p.ProjectBrief!, literalPattern, SearchLikePattern.EscapeCharacter)) ||
                            (!string.IsNullOrEmpty(p.CaseFileNumber) && EF.Functions.Like(p.CaseFileNumber!, literalPattern, SearchLikePattern.EscapeCharacter)) ||
                            (p.SponsoringUnit != null && EF.Functions.Like(p.SponsoringUnit.Name, literalPattern, SearchLikePattern.EscapeCharacter)) ||
                            (p.SponsoringLineDirectorate != null && EF.Functions.Like(p.SponsoringLineDirectorate.Name, literalPattern, SearchLikePattern.EscapeCharacter))
                        ))
                    .Take(limit * 3)
                    .Select(p => new ProjectSearchRow(
                        p.Id,
                        p.Name,
                        p.ProjectBrief,
                        p.CreatedAt,
                        p.ArchivedAt,
                        p.DeletedAt,
                        p.SponsoringUnit != null ? p.SponsoringUnit.Name : null,
                        p.SponsoringLineDirectorate != null ? p.SponsoringLineDirectorate.Name : null,
                        null,
                        null))
                    .ToListAsync(cancellationToken);

                return BuildHits(fallbackProjects, limit);
            }

            // Preserve PostgreSQL FTS relevance all the way through candidate selection.
            // Search V1 remains the visible fallback during V2 shadow validation, so it must
            // not find with FTS and then silently reorder those matches by recency.
            var searchQuery = EF.Functions.WebSearchToTsQuery("english", trimmed);
            var projects = await _dbContext.Projects
                .AsNoTracking()
                .Where(p =>
                    !p.IsDeleted &&
                    !p.IsArchived &&
                    EF.Functions.ToTsVector(
                        "english",
                        (p.Name ?? string.Empty) + " " +
                        (p.ProjectBrief ?? string.Empty) + " " +
                        (p.CaseFileNumber ?? string.Empty) + " " +
                        (p.SponsoringUnit != null ? p.SponsoringUnit.Name : string.Empty) + " " +
                        (p.SponsoringLineDirectorate != null ? p.SponsoringLineDirectorate.Name : string.Empty))
                        .Matches(searchQuery))
                .OrderByDescending(p =>
                    EF.Functions.ToTsVector(
                        "english",
                        (p.Name ?? string.Empty) + " " +
                        (p.ProjectBrief ?? string.Empty) + " " +
                        (p.CaseFileNumber ?? string.Empty) + " " +
                        (p.SponsoringUnit != null ? p.SponsoringUnit.Name : string.Empty) + " " +
                        (p.SponsoringLineDirectorate != null ? p.SponsoringLineDirectorate.Name : string.Empty))
                        .RankCoverDensity(searchQuery))
                .ThenByDescending(p => p.CreatedAt)
                .Take(limit * 3)
                .Select(p => new ProjectSearchRow(
                    p.Id,
                    p.Name,
                    p.ProjectBrief,
                    p.CreatedAt,
                    p.ArchivedAt,
                    p.DeletedAt,
                    p.SponsoringUnit != null ? p.SponsoringUnit.Name : null,
                    p.SponsoringLineDirectorate != null ? p.SponsoringLineDirectorate.Name : null,
                    ApplicationDbContext.TsHeadline(
                        "english",
                        (p.Name ?? string.Empty) + " " +
                        (p.ProjectBrief ?? string.Empty) + " " +
                        (p.CaseFileNumber ?? string.Empty) + " " +
                        (p.SponsoringUnit != null ? p.SponsoringUnit.Name : string.Empty) + " " +
                        (p.SponsoringLineDirectorate != null ? p.SponsoringLineDirectorate.Name : string.Empty),
                        searchQuery,
                        headlineOptions),
                    (double?)EF.Functions.ToTsVector(
                        "english",
                        (p.Name ?? string.Empty) + " " +
                        (p.ProjectBrief ?? string.Empty) + " " +
                        (p.CaseFileNumber ?? string.Empty) + " " +
                        (p.SponsoringUnit != null ? p.SponsoringUnit.Name : string.Empty) + " " +
                        (p.SponsoringLineDirectorate != null ? p.SponsoringLineDirectorate.Name : string.Empty))
                        .RankCoverDensity(searchQuery)))
                .ToListAsync(cancellationToken);

            return BuildHits(projects, limit);
        }

        // SECTION: Project global search helpers
        private IReadOnlyList<GlobalSearchHit> BuildHits(IEnumerable<ProjectSearchRow> projects, int limit)
        {
            if (!projects.Any())
            {
                return Array.Empty<GlobalSearchHit>();
            }

            var ordered = projects
                .Select(p =>
                {
                    var date = p.ArchivedAt
                        ?? p.DeletedAt
                        ?? new DateTimeOffset(DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc));

                    var snippetParts = new List<string>(3);
                    if (!string.IsNullOrWhiteSpace(p.Snippet))
                    {
                        snippetParts.Add(p.Snippet);
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(p.ProjectBrief))
                            snippetParts.Add(CompactSnippet(p.ProjectBrief));
                        if (!string.IsNullOrWhiteSpace(p.SponsoringUnit))
                            snippetParts.Add($"Sponsoring unit: {p.SponsoringUnit}");
                        if (!string.IsNullOrWhiteSpace(p.LineDirectorate))
                            snippetParts.Add($"Line directorate: {p.LineDirectorate}");
                    }

                    var snippet = snippetParts.Count == 0 ? null : string.Join(" · ", snippetParts);

                    var title = string.IsNullOrWhiteSpace(p.Name) ? "Untitled project" : p.Name;

                    return new
                    {
                        Rank = p.Rank ?? 0d,
                        Date = date,
                        Hit = new GlobalSearchHit(
                            Source: "Projects",
                            Title: title,
                            Snippet: snippet,
                            Url: _urlBuilder.ProjectOverview(p.Id),
                            Date: date,
                            Score: 0.6m,
                            FileType: null,
                            Extra: null)
                    };
                })
                .OrderByDescending(x => x.Rank)
                .ThenByDescending(x => x.Date)
                .ThenBy(x => x.Hit.Title)
                .Take(limit)
                .Select(x => x.Hit)
                .ToList();

            return ordered;
        }

        private static string CompactSnippet(string value, int maximumLength = 280)
        {
            var normalized = string.Join(
                " ",
                value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (normalized.Length <= maximumLength)
            {
                return normalized;
            }

            var boundary = normalized.LastIndexOf(' ', maximumLength - 1);
            var length = boundary >= maximumLength / 2 ? boundary : maximumLength - 1;
            return normalized[..length].TrimEnd() + "…";
        }

        // SECTION: Project global search row mapping
        private sealed record ProjectSearchRow(
            int Id,
            string? Name,
            string? ProjectBrief,
            DateTime CreatedAt,
            DateTimeOffset? ArchivedAt,
            DateTimeOffset? DeletedAt,
            string? SponsoringUnit,
            string? LineDirectorate,
            string? Snippet,
            double? Rank);
    }
}
