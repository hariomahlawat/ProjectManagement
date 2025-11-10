using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.Navigation;

namespace ProjectManagement.Services.Search;

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

        var pattern = $"%{query.Trim()}%";
        var limit = Math.Max(1, maxResults);

        var projects = await _dbContext.Projects
            .AsNoTracking()
            .Include(project => project.SponsoringUnit)
            .Include(project => project.SponsoringLineDirectorate)
            .Where(project => !project.IsDeleted && !project.IsArchived && (
                EF.Functions.ILike(project.Name, pattern) ||
                EF.Functions.ILike(project.Description ?? string.Empty, pattern) ||
                EF.Functions.ILike(project.CaseFileNumber ?? string.Empty, pattern) ||
                (project.SponsoringUnit != null && EF.Functions.ILike(project.SponsoringUnit.Name, pattern)) ||
                (project.SponsoringLineDirectorate != null && EF.Functions.ILike(project.SponsoringLineDirectorate.Name, pattern))))
            .OrderByDescending(project => project.ArchivedAt ?? project.DeletedAt ?? (DateTimeOffset?)null)
            .ThenByDescending(project => project.CreatedAt)
            .Take(limit)
            .Select(project => new
            {
                project.Id,
                project.Name,
                project.Description,
                project.CreatedAt,
                project.ArchivedAt,
                project.DeletedAt,
                SponsoringUnit = project.SponsoringUnit != null ? project.SponsoringUnit.Name : null,
                LineDirectorate = project.SponsoringLineDirectorate != null ? project.SponsoringLineDirectorate.Name : null
            })
            .ToListAsync(cancellationToken);

        if (projects.Count == 0)
        {
            return Array.Empty<GlobalSearchHit>();
        }

        var hits = new List<GlobalSearchHit>(projects.Count);
        foreach (var project in projects)
        {
            var createdUtc = DateTime.SpecifyKind(project.CreatedAt, DateTimeKind.Utc);
            var date = project.ArchivedAt
                ?? project.DeletedAt
                ?? new DateTimeOffset(createdUtc);

            var snippetParts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(project.Description))
            {
                snippetParts.Add(project.Description);
            }

            if (!string.IsNullOrWhiteSpace(project.SponsoringUnit))
            {
                snippetParts.Add($"Sponsoring unit: {project.SponsoringUnit}");
            }

            if (!string.IsNullOrWhiteSpace(project.LineDirectorate))
            {
                snippetParts.Add($"Line directorate: {project.LineDirectorate}");
            }

            var snippet = snippetParts.Count == 0
                ? null
                : string.Join(" · ", snippetParts);

            hits.Add(new GlobalSearchHit(
                Source: "Projects",
                Title: project.Name,
                Snippet: snippet,
                Url: _urlBuilder.ProjectOverview(project.Id),
                Date: date,
                Score: 0.6m,
                FileType: null,
                Extra: null));
        }

        return hits;
    }
}
