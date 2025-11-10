using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.Navigation;

namespace ProjectManagement.Services.Search
{
    // SECTION: FFC global search contract
    public interface IGlobalFfcSearchService
    {
        Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);
    }

    // SECTION: FFC global search implementation
    public sealed class GlobalFfcSearchService : IGlobalFfcSearchService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IUrlBuilder _urlBuilder;

        public GlobalFfcSearchService(ApplicationDbContext dbContext, IUrlBuilder urlBuilder)
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

            var pattern = $"%{query.Trim()}%";
            var recordLimit = Math.Max(1, maxResults);
            var attachmentLimit = Math.Max(1, maxResults / 2);
            var hits = new List<GlobalSearchHit>();

            // ------------------------------------------------------------
            // 1. FFC records
            // ------------------------------------------------------------
            var records = await _dbContext.FfcRecords
                .AsNoTracking()
                .Include(record => record.Country)
                .Where(record => !record.IsDeleted && (
                    EF.Functions.ILike(record.Country.Name, pattern) ||
                    EF.Functions.ILike(record.OverallRemarks ?? string.Empty, pattern) ||
                    EF.Functions.ILike(record.IpaRemarks ?? string.Empty, pattern) ||
                    EF.Functions.ILike(record.GslRemarks ?? string.Empty, pattern) ||
                    EF.Functions.ILike(record.DeliveryRemarks ?? string.Empty, pattern) ||
                    EF.Functions.ILike(record.InstallationRemarks ?? string.Empty, pattern)))
                .OrderByDescending(record => record.CreatedAt)
                .Take(recordLimit)
                .ToListAsync(cancellationToken);

            foreach (var record in records)
            {
                var title = string.IsNullOrWhiteSpace(record.Country?.Name)
                    ? $"FFC record {record.Id}"
                    : $"{record.Country.Name} {record.Year}";

                var snippet = record.OverallRemarks;
                if (string.IsNullOrWhiteSpace(snippet))
                {
                    snippet = record.DeliveryRemarks
                              ?? record.InstallationRemarks
                              ?? record.IpaRemarks
                              ?? record.GslRemarks;
                }

                hits.Add(new GlobalSearchHit(
                    Source: "FFC",
                    Title: title,
                    Snippet: snippet,
                    Url: _urlBuilder.FfcRecordManage(record.Id),
                    Date: record.CreatedAt,
                    Score: 0.65m,
                    FileType: null,
                    Extra: record.Country?.Name));
            }

            // ------------------------------------------------------------
            // 2. FFC attachments (PDF only)
            // ------------------------------------------------------------
            var attachments = await _dbContext.FfcAttachments
                .AsNoTracking()
                .Include(attachment => attachment.Record)
                    .ThenInclude(record => record.Country)
                .Where(attachment =>
                    // EF-translatable check, no StringComparison
                    attachment.ContentType == "application/pdf" &&
                    (
                        EF.Functions.ILike(attachment.Caption ?? string.Empty, pattern) ||
                        EF.Functions.ILike(attachment.Record.Country.Name, pattern)
                    ))
                .OrderByDescending(attachment => attachment.UploadedAt)
                .Take(attachmentLimit)
                .ToListAsync(cancellationToken);

            foreach (var attachment in attachments)
            {
                var caption = string.IsNullOrWhiteSpace(attachment.Caption)
                    ? "FFC document"
                    : attachment.Caption;

                hits.Add(new GlobalSearchHit(
                    Source: "FFC",
                    Title: caption,
                    Snippet: attachment.Record?.OverallRemarks,
                    Url: _urlBuilder.FfcAttachmentView(attachment.Id),
                    Date: attachment.UploadedAt,
                    Score: 0.55m,
                    FileType: attachment.ContentType,
                    Extra: attachment.Record?.Country?.Name));
            }

            return hits;
        }
    }
}
