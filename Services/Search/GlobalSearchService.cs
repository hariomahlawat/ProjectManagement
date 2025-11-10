using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace ProjectManagement.Services.Search
{
    // SECTION: Global search contract
    public interface IGlobalSearchService
    {
        Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, CancellationToken cancellationToken);
    }

    // SECTION: Global search implementation
    public sealed class GlobalSearchService : IGlobalSearchService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public GlobalSearchService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        }

        public async Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<GlobalSearchHit>();
            }

            // create separate scopes so each search gets its own DbContext
            var docScope = _scopeFactory.CreateScope();
            var ffcScope = _scopeFactory.CreateScope();
            var iprScope = _scopeFactory.CreateScope();
            var actScope = _scopeFactory.CreateScope();
            var projectScope = _scopeFactory.CreateScope();
            var reportsScope = _scopeFactory.CreateScope();

            try
            {
                var docService = docScope.ServiceProvider
                    .GetRequiredService<DocRepo.IGlobalDocRepoSearchService>();
                var ffcService = ffcScope.ServiceProvider
                    .GetRequiredService<IGlobalFfcSearchService>();
                var iprService = iprScope.ServiceProvider
                    .GetRequiredService<IGlobalIprSearchService>();
                var actService = actScope.ServiceProvider
                    .GetRequiredService<IGlobalActivitiesSearchService>();
                var projectService = projectScope.ServiceProvider
                    .GetRequiredService<IGlobalProjectSearchService>();
                var reportsService = reportsScope.ServiceProvider
                    .GetRequiredService<IGlobalProjectReportsSearchService>();

                // run all module searches in parallel, each has its own scope/DbContext now
                var docTask = docService.SearchAsync(query, 30, cancellationToken);
                var ffcTask = ffcService.SearchAsync(query, 20, cancellationToken);
                var iprTask = iprService.SearchAsync(query, 20, cancellationToken);
                var actTask = actService.SearchAsync(query, 20, cancellationToken);
                var projectTask = projectService.SearchAsync(query, 20, cancellationToken);
                var reportsTask = reportsService.SearchAsync(query, 20, cancellationToken);

                await Task.WhenAll(docTask, ffcTask, iprTask, actTask, projectTask, reportsTask);

                var combined = docTask.Result
                    .Concat(ffcTask.Result)
                    .Concat(iprTask.Result)
                    .Concat(actTask.Result)
                    .Concat(projectTask.Result)
                    .Concat(reportsTask.Result)
                    .ToList();

                if (combined.Count == 0)
                {
                    return Array.Empty<GlobalSearchHit>();
                }

                // dedupe by Url and keep best scored
                var distinct = combined
                    .GroupBy(h => h.Url, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g
                        .OrderByDescending(x => x.Score)
                        .ThenByDescending(x => x.Date)
                        .First())
                    .OrderByDescending(h => h.Score)
                    .ThenByDescending(h => h.Date)
                    .ToList();

                return distinct;
            }
            finally
            {
                // make sure we dispose all scopes
                docScope.Dispose();
                ffcScope.Dispose();
                iprScope.Dispose();
                actScope.Dispose();
                projectScope.Dispose();
                reportsScope.Dispose();
            }
        }
    }
}
