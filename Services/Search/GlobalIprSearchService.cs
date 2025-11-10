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
    // SECTION: IPR global search contract
    public interface IGlobalIprSearchService
    {
        Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);
    }

    // SECTION: IPR global search implementation
    public sealed class GlobalIprSearchService : IGlobalIprSearchService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IUrlBuilder _urlBuilder;

        public GlobalIprSearchService(ApplicationDbContext dbContext, IUrlBuilder urlBuilder)
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
            // 1. IPR records
            // ------------------------------------------------------------
            var records = await _dbContext.IprRecords
                .AsNoTracking()
                .Where(record =>
                    EF.Functions.ILike(record.IprFilingNumber, pattern) ||
                    EF.Functions.ILike(record.Title ?? string.Empty, pattern) ||
                    EF.Functions.ILike(record.Notes ?? string.Empty, pattern) ||
                    EF.Functions.ILike(record.FiledBy ?? string.Empty, pattern))
                .OrderByDescending(record => record.GrantedAtUtc ?? record.FiledAtUtc ?? DateTimeOffset.MinValue)
                .Take(recordLimit)
                .ToListAsync(cancellationToken);

            foreach (var record in records)
            {
                var title = string.IsNullOrWhiteSpace(record.Title)
                    ? record.IprFilingNumber
                    : record.Title;

                var date = record.GrantedAtUtc ?? record.FiledAtUtc;

                hits.Add(new GlobalSearchHit(
                    Source: "IPR",
                    Title: title ?? $"IPR {record.Id}",
                    Snippet: record.Notes,
                    Url: _urlBuilder.IprRecordManage(record.Id),
                    Date: date,
                    Score: 0.6m,
                    FileType: null,
                    Extra: record.IprFilingNumber));
            }

            // ------------------------------------------------------------
            // 2. IPR attachments (PDF only) - EF friendly
            // ------------------------------------------------------------
            var attachments = await _dbContext.IprAttachments
                .AsNoTracking()
                .Where(attachment =>
                    !attachment.IsArchived &&
                    attachment.ContentType == "application/pdf" && // <-- fixed
                    (
                        EF.Functions.ILike(attachment.OriginalFileName ?? string.Empty, pattern) ||
                        EF.Functions.ILike(attachment.ContentType ?? string.Empty, pattern)
                    ))
                .OrderByDescending(attachment => attachment.UploadedAtUtc)
                .Take(attachmentLimit)
                .ToListAsync(cancellationToken);

            if (attachments.Count > 0)
            {
                var recordIds = attachments.Select(a => a.IprRecordId).Distinct().ToArray();

                var recordMap = await _dbContext.IprRecords
                    .AsNoTracking()
                    .Where(record => recordIds.Contains(record.Id))
                    .Select(record => new { record.Id, record.IprFilingNumber, record.Title })
                    .ToDictionaryAsync(x => x.Id, cancellationToken);

                foreach (var attachment in attachments)
                {
                    recordMap.TryGetValue(attachment.IprRecordId, out var owningRecord);
                    var displayName = attachment.OriginalFileName;
                    var recordTitle = owningRecord?.Title ?? owningRecord?.IprFilingNumber;

                    hits.Add(new GlobalSearchHit(
                        Source: "IPR",
                        Title: displayName,
                        Snippet: recordTitle,
                        Url: _urlBuilder.IprAttachmentDownload(attachment.IprRecordId, attachment.Id),
                        Date: attachment.UploadedAtUtc,
                        Score: 0.5m,
                        FileType: attachment.ContentType,
                        Extra: owningRecord?.IprFilingNumber));
                }
            }

            return hits;
        }
    }
}
