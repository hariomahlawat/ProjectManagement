using System.Linq;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Services.DocRepo;

// SECTION: Document search service contract
public interface IDocumentSearchService
{
    bool TryPrepareQuery(string? rawQuery, out string preparedQuery);

    IQueryable<Document> ApplySearch(IQueryable<Document> source, string preparedQuery);
}

// SECTION: Document search service implementation
public sealed class DocumentSearchService : IDocumentSearchService
{
    private const string SearchConfiguration = "english";

    public bool TryPrepareQuery(string? rawQuery, out string preparedQuery)
    {
        preparedQuery = string.Empty;

        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return false;
        }

        preparedQuery = rawQuery.Trim();
        return preparedQuery.Length > 0;
    }

    public IQueryable<Document> ApplySearch(IQueryable<Document> source, string preparedQuery)
    {
        var tsQuery = EF.Functions.WebSearchToTsQuery(SearchConfiguration, preparedQuery);

        return source
            .Where(document => document.SearchVector != null && document.SearchVector.Matches(tsQuery))
            .OrderByDescending(document => EF.Functions.TsRankCd(document.SearchVector!, tsQuery))
            .ThenByDescending(document => document.DocumentDate.HasValue)
            .ThenByDescending(document => document.DocumentDate)
            .ThenByDescending(document => document.CreatedAtUtc);
    }
}
