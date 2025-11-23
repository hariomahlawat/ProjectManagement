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

        // SECTION: Query preparation
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

        // SECTION: EF-friendly search composition
        public IQueryable<Document> ApplySearch(IQueryable<Document> source, string preparedQuery)
        {
            return
                from d in source
                let searchQuery = EF.Functions.WebSearchToTsQuery(SearchConfiguration, preparedQuery)
                where d.SearchVector != null &&
                    d.SearchVector.Matches(searchQuery)
                orderby d.SearchVector!.RankCoverDensity(searchQuery) descending,
                    d.DocumentDate.HasValue descending,
                    d.DocumentDate descending,
                    d.CreatedAtUtc descending
                select d;
        }

        // SECTION: Projected search composition
        public IQueryable<DocumentSearchResultVm> ApplySearchProjected(IQueryable<Document> source, string preparedQuery)
        {
            var normalizedQuery = preparedQuery.ToLowerInvariant();

            return
                from d in source
                let searchQuery = EF.Functions.WebSearchToTsQuery(SearchConfiguration, preparedQuery)
                // 1) full-text filter, fully inlined
                where d.SearchVector != null &&
                    d.SearchVector.Matches(searchQuery)
                // 2) project directly to VM
                select new DocumentSearchResultVm
                {
                    Id = d.Id,
                    Subject = d.Subject,
                    DocumentDate = d.DocumentDate,
                    OfficeCategoryName = d.OfficeCategory != null ? d.OfficeCategory.Name : null,
                    DocumentCategoryName = d.DocumentCategory != null ? d.DocumentCategory.Name : null,

                    // we cant project per-row IReadOnlyCollection<string> cleanly, so return empty
                    Tags = Array.Empty<string>(),

                    OcrStatus = d.OcrStatus,
                    OcrFailureReason = d.OcrFailureReason,

                    // rank
                    Rank = (double?)d.SearchVector!.RankCoverDensity(searchQuery),

                    // query-aware snippet from PG
                    Snippet = d.DocumentText != null
                        ? ApplicationDbContext.TsHeadline(
                            "english",
                            // you only have OCR text here
                            (d.DocumentText.OcrText ?? ""),
                            searchQuery,
                            "StartSel=<mark>, StopSel=</mark>, MaxFragments=2, MaxWords=20")
                        : null,

                    // match hints
                    MatchedInSubject = d.Subject != null &&
                        EF.Functions.ILike(d.Subject, "%" + preparedQuery + "%"),

                    MatchedInTags = d.DocumentTags.Any(dt =>
                        dt.Tag.Name == preparedQuery ||
                        dt.Tag.NormalizedName == normalizedQuery),

                    MatchedInBody = d.DocumentText != null
                }
                // 3) order by rank/date
                into result
                orderby result.Rank descending, result.DocumentDate descending
                select result;
        }

    }
}
