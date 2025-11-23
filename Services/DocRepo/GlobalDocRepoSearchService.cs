using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.DocumentRepository.Models;
using ProjectManagement.Data;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Search;

namespace ProjectManagement.Services.DocRepo;

// SECTION: Global document repository search contract
public interface IGlobalDocRepoSearchService
{
    Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);
}

// SECTION: Global document repository search implementation
public sealed class GlobalDocRepoSearchService : IGlobalDocRepoSearchService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDocumentSearchService _documentSearchService;
    private readonly IUrlBuilder _urlBuilder;

    public GlobalDocRepoSearchService(
        ApplicationDbContext dbContext,
        IDocumentSearchService documentSearchService,
        IUrlBuilder urlBuilder)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _documentSearchService = documentSearchService ?? throw new ArgumentNullException(nameof(documentSearchService));
        _urlBuilder = urlBuilder ?? throw new ArgumentNullException(nameof(urlBuilder));
    }

    public async Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        if (!_documentSearchService.TryPrepareQuery(query, out var preparedQuery))
        {
            return Array.Empty<GlobalSearchHit>();
        }

        // SECTION: Provider guard - WebSearchToTsQuery only works on PostgreSQL
        if (!_dbContext.Database.IsNpgsql())
        {
            return Array.Empty<GlobalSearchHit>();
        }

        var baseQuery = _dbContext.Documents
            .AsNoTracking()
            .Where(document => !document.IsDeleted);

        var projected = await _documentSearchService
            .ApplySearchProjected(baseQuery, preparedQuery)
            .Take(maxResults)
            .ToListAsync(cancellationToken);

        if (projected.Count == 0)
        {
            return Array.Empty<GlobalSearchHit>();
        }

        var ids = projected.Select(p => p.Id).ToArray();
        var metadata = await _dbContext.Documents
            .AsNoTracking()
            .Where(document => ids.Contains(document.Id))
            .Select(document => new
            {
                document.Id,
                document.DocumentDate,
                document.CreatedAtUtc,
                document.OriginalFileName
            })
            .ToListAsync(cancellationToken);

        var metadataMap = metadata.ToDictionary(item => item.Id);
        var hits = new List<GlobalSearchHit>(projected.Count);

        foreach (var result in projected)
        {
            metadataMap.TryGetValue(result.Id, out var meta);
            DateTimeOffset? date = null;
            if (meta?.DocumentDate is { } docDate)
            {
                date = new DateTimeOffset(docDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            }
            else if (meta is not null)
            {
                var created = DateTime.SpecifyKind(meta.CreatedAtUtc, DateTimeKind.Utc);
                date = new DateTimeOffset(created);
            }

            var title = string.IsNullOrWhiteSpace(result.Subject)
                ? meta?.OriginalFileName ?? "Document"
                : result.Subject;

            var snippet = string.IsNullOrWhiteSpace(result.Snippet) ? null : result.Snippet;
            var score = result.Rank.HasValue ? Convert.ToDecimal(result.Rank.Value) : 0m;

            hits.Add(new GlobalSearchHit(
                Source: "Document Repository",
                Title: title,
                Snippet: snippet,
                Url: _urlBuilder.DocumentRepositoryView(result.Id),
                Date: date,
                Score: score,
                FileType: "pdf",
                Extra: null));
        }

        return hits;
    }
}
