using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Navigation;

namespace ProjectManagement.Services.Search;

// SECTION: Global project document search contract
public interface IGlobalProjectDocumentSearchService
{
    Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);
}

// SECTION: Global project document search implementation
public sealed class GlobalProjectDocumentSearchService : IGlobalProjectDocumentSearchService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IUrlBuilder _urlBuilder;

    public GlobalProjectDocumentSearchService(ApplicationDbContext dbContext, IUrlBuilder urlBuilder)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _urlBuilder = urlBuilder ?? throw new ArgumentNullException(nameof(urlBuilder));
    }

    public async Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<GlobalSearchHit>();
        }

        if (!_dbContext.Database.IsNpgsql())
        {
            return Array.Empty<GlobalSearchHit>();
        }

        var trimmed = query.Trim();
        var limit = Math.Clamp(maxResults, 1, 50);
        var tsQuery = EF.Functions.WebSearchToTsQuery("english", trimmed);

        var rows = await _dbContext.ProjectDocuments
            .AsNoTracking()
            .Where(document => document.Status == ProjectDocumentStatus.Published && !document.IsArchived)
            .Where(document => document.SearchVector != null && document.SearchVector.Matches(tsQuery))
            .OrderByDescending(document => ApplicationDbContext.TsRankCd(document.SearchVector!, tsQuery))
            .ThenByDescending(document => document.UploadedAtUtc)
            .Select(document => new
            {
                document.Id,
                document.Title,
                document.OriginalFileName,
                document.UploadedAtUtc,
                StageName = document.Stage != null ? document.Stage.StageName : null,
                Snippet = document.DocumentText != null
                    ? ApplicationDbContext.TsHeadline(
                        "english",
                        document.DocumentText.OcrText ?? string.Empty,
                        tsQuery,
                        "StartSel=<mark>,StopSel=</mark>,MaxWords=25,MinWords=10,ShortWord=3,HighlightAll=FALSE,FragmentDelimiter=…")
                    : null,
                Rank = ApplicationDbContext.TsRankCd(document.SearchVector!, tsQuery)
            })
            .Take(limit)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return Array.Empty<GlobalSearchHit>();
        }

        var hits = new List<GlobalSearchHit>(rows.Count);

        foreach (var row in rows)
        {
            var title = string.IsNullOrWhiteSpace(row.Title) ? row.OriginalFileName ?? "Project document" : row.Title;
            var snippet = string.IsNullOrWhiteSpace(row.Snippet) ? null : row.Snippet;
            var date = row.UploadedAtUtc;
            var score = row.Rank.HasValue ? Convert.ToDecimal(row.Rank.Value) : 0m;
            var extra = string.IsNullOrWhiteSpace(row.StageName) ? null : row.StageName;

            hits.Add(new GlobalSearchHit(
                Source: "Project documents",
                Title: title,
                Snippet: snippet,
                Url: _urlBuilder.ProjectDocumentPreview(row.Id),
                Date: date,
                Score: score,
                FileType: "pdf",
                Extra: extra));
        }

        return hits;
    }
}
