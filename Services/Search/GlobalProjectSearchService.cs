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
                            (!string.IsNullOrEmpty(p.Name) && EF.Functions.Like(p.Name!, $"%{trimmed}%")) ||
                            (!string.IsNullOrEmpty(p.Description) && EF.Functions.Like(p.Description!, $"%{trimmed}%")) ||
                            (!string.IsNullOrEmpty(p.CaseFileNumber) && EF.Functions.Like(p.CaseFileNumber!, $"%{trimmed}%")) ||
                            (p.SponsoringUnit != null && EF.Functions.Like(p.SponsoringUnit.Name, $"%{trimmed}%")) ||
                            (p.SponsoringLineDirectorate != null && EF.Functions.Like(p.SponsoringLineDirectorate.Name, $"%{trimmed}%"))
                        ))
                    .Take(limit * 3)
                    .Select(p => new ProjectSearchRow(
                        p.Id,
                        p.Name,
                        p.Description,
                        p.CreatedAt,
                        p.ArchivedAt,
                        p.DeletedAt,
                        p.SponsoringUnit != null ? p.SponsoringUnit.Name : null,
                        p.SponsoringLineDirectorate != null ? p.SponsoringLineDirectorate.Name : null,
                        null))
                    .ToListAsync(cancellationToken);

                return BuildHits(fallbackProjects, limit);
            }

            // 1) EF-friendly query only
            var projects = await _dbContext.Projects
                .AsNoTracking()
                .Include(p => p.SponsoringUnit)
                .Include(p => p.SponsoringLineDirectorate)
                .Where(p =>
                    !p.IsDeleted &&
                    !p.IsArchived &&
                    EF.Functions.ToTsVector("english",
                        (p.Name ?? string.Empty) + " " +
                        (p.Description ?? string.Empty) + " " +
                        (p.CaseFileNumber ?? string.Empty) + " " +
                        (p.SponsoringUnit != null ? p.SponsoringUnit.Name : string.Empty) + " " +
                        (p.SponsoringLineDirectorate != null ? p.SponsoringLineDirectorate.Name : string.Empty))
                        .Matches(EF.Functions.WebSearchToTsQuery("english", trimmed)))
                // pull a few extra so sorting in-memory still has room
                .Take(limit * 3)
                .Select(p => new ProjectSearchRow(
                    p.Id,
                    p.Name,
                    p.Description,
                    p.CreatedAt,
                    p.ArchivedAt,
                    p.DeletedAt,
                    p.SponsoringUnit != null ? p.SponsoringUnit.Name : null,
                    p.SponsoringLineDirectorate != null ? p.SponsoringLineDirectorate.Name : null,
                    ApplicationDbContext.TsHeadline(
                        "english",
                        (p.Name ?? string.Empty) + " " +
                        (p.Description ?? string.Empty) + " " +
                        (p.CaseFileNumber ?? string.Empty) + " " +
                        (p.SponsoringUnit != null ? p.SponsoringUnit.Name : string.Empty) + " " +
                        (p.SponsoringLineDirectorate != null ? p.SponsoringLineDirectorate.Name : string.Empty),
                        EF.Functions.WebSearchToTsQuery("english", trimmed),
                        headlineOptions)))
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
                        if (!string.IsNullOrWhiteSpace(p.Description))
                            snippetParts.Add(p.Description);
                        if (!string.IsNullOrWhiteSpace(p.SponsoringUnit))
                            snippetParts.Add($"Sponsoring unit: {p.SponsoringUnit}");
                        if (!string.IsNullOrWhiteSpace(p.LineDirectorate))
                            snippetParts.Add($"Line directorate: {p.LineDirectorate}");
                    }

                    var snippet = snippetParts.Count == 0 ? null : string.Join(" · ", snippetParts);

                    return new
                    {
                        Date = date,
                        Hit = new GlobalSearchHit(
                            Source: "Projects",
                            Title: p.Name,
                            Snippet: snippet,
                            Url: _urlBuilder.ProjectOverview(p.Id),
                            Date: date,
                            Score: 0.6m,
                            FileType: null,
                            Extra: null)
                    };
                })
                .OrderByDescending(x => x.Date)
                .ThenBy(x => x.Hit.Title)
                .Take(limit)
                .Select(x => x.Hit)
                .ToList();

            return ordered;
        }

        // SECTION: Project global search row mapping
        private sealed record ProjectSearchRow(
            int Id,
            string? Name,
            string? Description,
            DateTime CreatedAt,
            DateTimeOffset? ArchivedAt,
            DateTimeOffset? DeletedAt,
            string? SponsoringUnit,
            string? LineDirectorate,
            string? Snippet);
    }
}
