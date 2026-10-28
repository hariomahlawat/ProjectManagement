using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using ProjectManagement.Areas.DocumentRepository.Models;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.Search;
using System;
using System.Linq;

namespace ProjectManagement.Services.DocRepo
{
    public interface IDocumentSearchService
    {
        bool TryPrepareQuery(string? rawQuery, out string preparedQuery);

        IQueryable<Document> ApplySearch(IQueryable<Document> source, string preparedQuery);

        /// <summary>Only the full-text match, unordered; for counts such as filter facets.</summary>
        IQueryable<Document> ApplyMatch(IQueryable<Document> source, string preparedQuery);

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
        public IQueryable<Document> ApplyMatch(IQueryable<Document> source, string preparedQuery)
        {
            return source
                .Where(d =>
                    d.SearchVector != null &&
                    d.SearchVector.Matches(EF.Functions.WebSearchToTsQuery(SearchConfiguration, preparedQuery)));
        }

        public IQueryable<Document> ApplySearch(IQueryable<Document> source, string preparedQuery)
        {
            return ApplyMatch(source, preparedQuery)
                .OrderByDescending(d =>
                    d.SearchVector!.RankCoverDensity(EF.Functions.WebSearchToTsQuery(SearchConfiguration, preparedQuery)))
                .ThenByDescending(d => d.DocumentDate.HasValue)
                .ThenByDescending(d => d.DocumentDate)
                .ThenByDescending(d => d.CreatedAtUtc);
        }

        // SECTION: Projected search composition
        public IQueryable<DocumentSearchResultVm> ApplySearchProjected(IQueryable<Document> source, string preparedQuery)
        {
            var normalizedQuery = preparedQuery.ToLowerInvariant();
            var literalPattern = SearchLikePattern.Contains(preparedQuery);

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
                    ReceivedFrom = d.ReceivedFrom,
                    DocumentDate = d.DocumentDate,
                    OfficeCategoryId = d.OfficeCategoryId,
                    DocumentCategoryId = d.DocumentCategoryId,
                    FileSizeBytes = d.FileSizeBytes,
                    CreatedAtUtc = d.CreatedAtUtc,
                    OfficeCategoryName = d.OfficeCategory != null ? d.OfficeCategory.Name : null,
                    DocumentCategoryName = d.DocumentCategory != null ? d.DocumentCategory.Name : null,
                    IsActive = d.IsActive,
                    IsAots = d.IsAots,

                    // Tags are loaded for the page of results afterwards (see Documents/Index).
                    Tags = Array.Empty<string>(),

                    OcrStatus = d.OcrStatus,
                    OcrFailureReason = d.OcrFailureReason,

                    // rank
                    Rank = (double?)d.SearchVector!.RankCoverDensity(
                        EF.Functions.WebSearchToTsQuery(SearchConfiguration, preparedQuery)),

                    // query-aware snippet from PG
                    Snippet = d.DocumentText != null
                        ? ApplicationDbContext.TsHeadline(
                            "english",
                            // you only have OCR text here
                            (d.DocumentText.OcrText ?? ""),
                            EF.Functions.WebSearchToTsQuery(SearchConfiguration, preparedQuery),
                            "StartSel=<mark>, StopSel=</mark>, MaxFragments=2, MaxWords=20")
                        : null,

                    // match hints
                    MatchedInSubject = d.Subject != null &&
                        EF.Functions.ILike(d.Subject, literalPattern, SearchLikePattern.EscapeCharacter),

                    MatchedInTags = d.DocumentTags.Any(dt =>
                        dt.Tag.Name == preparedQuery ||
                        dt.Tag.NormalizedName == normalizedQuery),

                    MatchedInBody = d.DocumentText != null &&
                        EF.Functions.ToTsVector(
                            SearchConfiguration,
                            d.DocumentText.OcrText ?? string.Empty)
                        .Matches(EF.Functions.WebSearchToTsQuery(SearchConfiguration, preparedQuery))
                })
                // 3) order by rank/date
                .OrderByDescending(r => r.Rank)
                .ThenByDescending(r => r.DocumentDate);
        }

    }
}
