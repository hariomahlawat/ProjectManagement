using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
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
            var searchQuery = EF.Functions.WebSearchToTsQuery("english", trimmed);
            var headlineOptions = "StartSel=<mark>, StopSel=</mark>, MaxWords=35, MinWords=10, ShortWord=3";
            var limit = Math.Max(1, maxResults);

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
                        .Matches(searchQuery))
                // pull a few extra so sorting in-memory still has room
                .Take(limit * 3)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Description,
                    p.CreatedAt,
                    p.ArchivedAt,
                    p.DeletedAt,
                    SponsoringUnit = p.SponsoringUnit != null ? p.SponsoringUnit.Name : null,
                    LineDirectorate = p.SponsoringLineDirectorate != null ? p.SponsoringLineDirectorate.Name : null,
                    Snippet = EF.Functions.TsHeadline(
                        "english",
                        (p.Name ?? string.Empty) + " " +
                        (p.Description ?? string.Empty) + " " +
                        (p.CaseFileNumber ?? string.Empty) + " " +
                        (p.SponsoringUnit != null ? p.SponsoringUnit.Name : string.Empty) + " " +
                        (p.SponsoringLineDirectorate != null ? p.SponsoringLineDirectorate.Name : string.Empty),
                        searchQuery,
                        headlineOptions)
                })
                .ToListAsync(cancellationToken);

            if (projects.Count == 0)
            {
                return Array.Empty<GlobalSearchHit>();
            }

            // 2) in-memory: pick the “best” date & sort
            var ordered = projects
                .Select(p =>
                {
                    // prefer archived, then deleted, then created
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
    }
}
