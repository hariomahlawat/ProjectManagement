using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
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

            // "cabling -fibre": the phrase "cabling", and no record or file whose text has "fibre".
            var parsed = SearchPhraseQuery.Parse(query);
            var pattern = SearchLikePattern.Contains(parsed.Phrase.Trim());
            var recordLimit = Math.Max(1, maxResults);
            var attachmentLimit = Math.Max(1, maxResults / 2);
            var hits = new List<GlobalSearchHit>();

            // ------------------------------------------------------------
            // 1. IPR records
            // ------------------------------------------------------------
            var recordCandidates = _dbContext.IprRecords.AsNoTracking();
            foreach (var excluded in parsed.ExcludedPatterns)
            {
                recordCandidates = recordCandidates.Where(record =>
                    !Regex.IsMatch(record.IprFilingNumber, excluded, RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(record.Title ?? string.Empty, excluded, RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(record.Notes ?? string.Empty, excluded, RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(record.FiledBy ?? string.Empty, excluded, RegexOptions.IgnoreCase));
            }

            var records = await recordCandidates
                .Where(record =>
                    (EF.Functions.ILike(record.IprFilingNumber, pattern, SearchLikePattern.EscapeCharacter) ||
                     EF.Functions.ILike(record.Title ?? string.Empty, pattern, SearchLikePattern.EscapeCharacter) ||
                     EF.Functions.ILike(record.Notes ?? string.Empty, pattern, SearchLikePattern.EscapeCharacter) ||
                     EF.Functions.ILike(record.FiledBy ?? string.Empty, pattern, SearchLikePattern.EscapeCharacter)))
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
                    Url: _urlBuilder.IprRecordView(record.Id),
                    Date: date,
                    Score: 0.6m,
                    FileType: null,
                    Extra: record.IprFilingNumber));
            }

            // ------------------------------------------------------------
            // 2. IPR attachments (PDF only) - EF friendly
            // ------------------------------------------------------------
            var attachmentCandidates = _dbContext.IprAttachments.AsNoTracking();
            foreach (var excluded in parsed.ExcludedPatterns)
            {
                attachmentCandidates = attachmentCandidates.Where(attachment =>
                    !Regex.IsMatch(attachment.OriginalFileName ?? string.Empty, excluded, RegexOptions.IgnoreCase));
            }

            var attachments = await attachmentCandidates
                .Where(attachment =>
                    !attachment.IsArchived &&
                    attachment.ContentType == "application/pdf" &&
                    (
                        EF.Functions.ILike(attachment.OriginalFileName ?? string.Empty, pattern, SearchLikePattern.EscapeCharacter) ||
                        EF.Functions.ILike(attachment.ContentType ?? string.Empty, pattern, SearchLikePattern.EscapeCharacter)
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
