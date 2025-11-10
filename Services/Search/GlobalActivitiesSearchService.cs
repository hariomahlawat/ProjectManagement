using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Activities;
using ProjectManagement.Services.Navigation;

namespace ProjectManagement.Services.Search;

// SECTION: Activities global search contract
public interface IGlobalActivitiesSearchService
{
    Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);
}

// SECTION: Activities global search implementation
public sealed class GlobalActivitiesSearchService : IGlobalActivitiesSearchService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IUrlBuilder _urlBuilder;

    public GlobalActivitiesSearchService(ApplicationDbContext dbContext, IUrlBuilder urlBuilder)
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

        var activities = await _dbContext.Activities
            .AsNoTracking()
            .Where(activity => !activity.IsDeleted && (
                EF.Functions.ILike(activity.Title, pattern) ||
                EF.Functions.ILike(activity.Description ?? string.Empty, pattern) ||
                EF.Functions.ILike(activity.Location ?? string.Empty, pattern)))
            .OrderByDescending(activity => activity.ScheduledStartUtc ?? activity.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        if (activities.Count == 0)
        {
            return Array.Empty<GlobalSearchHit>();
        }

        var hits = new List<GlobalSearchHit>(activities.Count);
        foreach (var activity in activities)
        {
            var date = activity.ScheduledStartUtc ?? activity.CreatedAtUtc;
            hits.Add(new GlobalSearchHit(
                Source: "Activities",
                Title: activity.Title,
                Snippet: activity.Description,
                Url: _urlBuilder.ActivityDetails(activity.Id),
                Date: date,
                Score: 0.45m,
                FileType: null,
                Extra: activity.Location));
        }

        return hits;
    }
}
