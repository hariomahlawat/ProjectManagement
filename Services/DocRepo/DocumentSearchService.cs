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
            // OCR text (ocrmypdf --sidecar) separates pages with a form feed, so the page of a
            // match is one more than the form feeds before it.
            var pageTerm = FirstSearchWord(preparedQuery);
            const string PageBreak = "\f";

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
                        .Matches(EF.Functions.WebSearchToTsQuery(SearchConfiguration, preparedQuery)),

                    MatchPage = pageTerm != null &&
                        d.DocumentText != null &&
                        d.DocumentText.OcrText != null &&
                        d.DocumentText.OcrText.Contains(PageBreak) &&
                        d.DocumentText.OcrText.ToLower().IndexOf(pageTerm) >= 0
                            ? (int?)(1
                                + d.DocumentText.OcrText.ToLower().Substring(0, d.DocumentText.OcrText.ToLower().IndexOf(pageTerm)).Length
                                - d.DocumentText.OcrText.ToLower().Substring(0, d.DocumentText.OcrText.ToLower().IndexOf(pageTerm)).Replace(PageBreak, "").Length)
                            : null
                })
                // 3) order by rank/date
                .OrderByDescending(r => r.Rank)
                .ThenByDescending(r => r.DocumentDate);
        }

        // A to_tsquery for as-you-type search: every word, the last one as a prefix
        // ("radar tri" finds "radar trials"). Only letters and digits reach the query.
        public static string? PrefixQuery(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var words = System.Text.RegularExpressions.Regex.Matches(text, @"[\p{L}\p{N}]+")
                .Select(match => match.Value.ToLowerInvariant())
                .Take(8)
                .ToList();
            if (words.Count == 0) return null;
            return string.Join(" & ", words.Select((word, index) => index == words.Count - 1 ? word + ":*" : word));
        }

        // The first plain word of a search (3+ letters, not an operator), lower-cased.
        public static string? FirstSearchWord(string preparedQuery)
        {
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(preparedQuery, @"(?<![-\p{L}\p{N}])[\p{L}\p{N}]{3,}"))
            {
                var word = match.Value.ToLowerInvariant();
                if (word is not ("and" or "not"))
                {
                    return word;
                }
            }

            return null;
        }

    }
}
