using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using ProjectManagement.Areas.DocumentRepository.Models;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;

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
        var loweredQuery = preparedQuery.ToLowerInvariant();
        var likePattern = $"%{preparedQuery}%";
        var tsQuery = EF.Functions.WebSearchToTsQuery(SearchConfiguration, preparedQuery);

        return source
            .Where(document =>
                document.SearchVector != null &&
                document.SearchVector.Matches(tsQuery))
            .Select(document => new
            {
                Document = document,
                Rank = document.SearchVector!.RankCoverDensity(tsQuery)
            })
            .OrderByDescending(result => result.Rank)
            .ThenByDescending(result => result.Document.DocumentDate.HasValue)
            .ThenByDescending(result => result.Document.DocumentDate)
            .ThenByDescending(result => result.Document.CreatedAtUtc)
            .Select(result => new DocumentSearchResultVm
            {
                Id = result.Document.Id,
                Subject = result.Document.Subject,
                DocumentDate = result.Document.DocumentDate,
                OfficeCategoryName = result.Document.OfficeCategory != null ? result.Document.OfficeCategory.Name : null,
                DocumentCategoryName = result.Document.DocumentCategory != null ? result.Document.DocumentCategory.Name : null,
                Tags = result.Document.DocumentTags
                    .OrderBy(documentTag => documentTag.Tag.Name)
                    .Select(documentTag => documentTag.Tag.Name)
                    .ToList(),
                OcrStatus = result.Document.OcrStatus,
                OcrFailureReason = result.Document.OcrFailureReason,
                Rank = result.Rank,
                Snippet = result.Document.DocumentText != null && result.Document.DocumentText.OcrText != null
                    ? result.Document.DocumentText.OcrText.Substring(0, 200)
                    : null,
                MatchedInSubject = result.Document.Subject != null && EF.Functions.ILike(result.Document.Subject, likePattern),
                MatchedInTags = result.Document.DocumentTags.Any(documentTag =>
                    documentTag.Tag.Name == preparedQuery ||
                    documentTag.Tag.NormalizedName == loweredQuery),
                MatchedInBody = result.Document.DocumentText != null
            });
    }
}
