using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Search;

// SECTION: Global search contract
public interface IGlobalSearchService
{
    Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, CancellationToken cancellationToken);
}

// SECTION: Global search implementation
public sealed class GlobalSearchService : IGlobalSearchService
{
    private readonly DocRepo.IGlobalDocRepoSearchService _docRepoSearchService;
    private readonly IGlobalFfcSearchService _ffcSearchService;
    private readonly IGlobalIprSearchService _iprSearchService;
    private readonly IGlobalActivitiesSearchService _activitiesSearchService;

    public GlobalSearchService(
        DocRepo.IGlobalDocRepoSearchService docRepoSearchService,
        IGlobalFfcSearchService ffcSearchService,
        IGlobalIprSearchService iprSearchService,
        IGlobalActivitiesSearchService activitiesSearchService)
    {
        _docRepoSearchService = docRepoSearchService ?? throw new ArgumentNullException(nameof(docRepoSearchService));
        _ffcSearchService = ffcSearchService ?? throw new ArgumentNullException(nameof(ffcSearchService));
        _iprSearchService = iprSearchService ?? throw new ArgumentNullException(nameof(iprSearchService));
        _activitiesSearchService = activitiesSearchService ?? throw new ArgumentNullException(nameof(activitiesSearchService));
    }

    public async Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<GlobalSearchHit>();
        }

        var docRepoTask = _docRepoSearchService.SearchAsync(query, 30, cancellationToken);
        var ffcTask = _ffcSearchService.SearchAsync(query, 20, cancellationToken);
        var iprTask = _iprSearchService.SearchAsync(query, 20, cancellationToken);
        var activitiesTask = _activitiesSearchService.SearchAsync(query, 20, cancellationToken);

        await Task.WhenAll(docRepoTask, ffcTask, iprTask, activitiesTask);

        var combined = docRepoTask.Result
            .Concat(ffcTask.Result)
            .Concat(iprTask.Result)
            .Concat(activitiesTask.Result)
            .ToList();

        if (combined.Count == 0)
        {
            return Array.Empty<GlobalSearchHit>();
        }

        return combined
            .OrderByDescending(hit => hit.Score)
            .ThenByDescending(hit => hit.Date)
            .ToList();
    }
}
