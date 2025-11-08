using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using ProjectManagement.Areas.DocumentRepository.Models;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using System;
using System.Linq;

namespace ProjectManagement.Services.DocRepo
{
    public interface IDocumentSearchService
    {
        bool TryPrepareQuery(string? rawQuery, out string preparedQuery);

        IQueryable<Document> ApplySearch(IQueryable<Document> source, string preparedQuery);

        IQueryable<DocumentSearchResultVm> ApplySearchProjected(IQueryable<Document> source, string preparedQuery);
    }

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
                .Where(d =>
                    d.SearchVector != null &&
                    d.SearchVector.Matches(
                        EF.Functions.WebSearchToTsQuery(SearchConfiguration, preparedQuery)))
                .OrderByDescending(d =>
                    d.SearchVector!.RankCoverDensity(
                        EF.Functions.WebSearchToTsQuery(SearchConfiguration, preparedQuery)))
                .ThenByDescending(d => d.DocumentDate.HasValue)
                .ThenByDescending(d => d.DocumentDate)
                .ThenByDescending(d => d.CreatedAtUtc);
        }

        public IQueryable<DocumentSearchResultVm> ApplySearchProjected(IQueryable<Document> source, string preparedQuery)
        {
            return source
                // 1) full-text filter, fully inlined
                .Where(d =>
                    d.SearchVector != null &&
                    d.SearchVector.Matches(
                        EF.Functions.WebSearchToTsQuery(SearchConfiguration, preparedQuery)))
                // 2) project directly to VM
                .Select(d => new DocumentSearchResultVm
                {
                    Id = d.Id,
                    Subject = d.Subject,
                    DocumentDate = d.DocumentDate,
                    OfficeCategoryName = d.OfficeCategory != null ? d.OfficeCategory.Name : null,
                    DocumentCategoryName = d.DocumentCategory != null ? d.DocumentCategory.Name : null,

                    // we can’t project per-row IReadOnlyCollection<string> cleanly, so return empty
                    Tags = Array.Empty<string>(),

                    OcrStatus = d.OcrStatus,
                    OcrFailureReason = d.OcrFailureReason,

                    // rank
                    Rank = (double?)d.SearchVector!.RankCoverDensity(
                        EF.Functions.WebSearchToTsQuery("english", preparedQuery)),

                    // query-aware snippet from PG
                    Snippet = d.DocumentText != null
                        ? ApplicationDbContext.TsHeadline(
                            "english",
                            // you only have OCR text here
                            (d.DocumentText.OcrText ?? ""),
                            EF.Functions.WebSearchToTsQuery("english", preparedQuery),
                            "StartSel=<mark>, StopSel=</mark>, MaxFragments=2, MaxWords=20")
                        : null,

                    // match hints
                    MatchedInSubject = d.Subject != null &&
                        EF.Functions.ILike(d.Subject, "%" + preparedQuery + "%"),

                    MatchedInTags = d.DocumentTags.Any(dt =>
                        dt.Tag.Name == preparedQuery ||
                        dt.Tag.NormalizedName == preparedQuery.ToLower()),

                    MatchedInBody = d.DocumentText != null
                })
                // 3) order by rank/date
                .OrderByDescending(r => r.Rank)
                .ThenByDescending(r => r.DocumentDate);
        }

    }
}
