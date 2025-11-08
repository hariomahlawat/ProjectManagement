using System.Linq;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data.DocRepo;
using Npgsql.EntityFrameworkCore.PostgreSQL;

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
        return source
            .Where(document =>
                document.SearchVector != null &&
                document.SearchVector.Matches(
                    EF.Functions.WebSearchToTsQuery(SearchConfiguration, preparedQuery)))
            .OrderByDescending(document =>
                document.SearchVector!.RankCoverDensity(
                    EF.Functions.WebSearchToTsQuery(SearchConfiguration, preparedQuery)))
            .ThenByDescending(document => document.DocumentDate.HasValue)
            .ThenByDescending(document => document.DocumentDate)
            .ThenByDescending(document => document.CreatedAtUtc);
    }

}
