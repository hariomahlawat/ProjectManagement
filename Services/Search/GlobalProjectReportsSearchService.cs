using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Navigation;

namespace ProjectManagement.Services.Search
{
    // SECTION: Project office reports global search contract
    public interface IGlobalProjectReportsSearchService
    {
        Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);
    }

    // SECTION: Project office reports global search implementation
    public sealed class GlobalProjectReportsSearchService : IGlobalProjectReportsSearchService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IUrlBuilder _urlBuilder;

        public GlobalProjectReportsSearchService(ApplicationDbContext dbContext, IUrlBuilder urlBuilder)
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

            var trimmed = query.Trim();
            var searchQuery = EF.Functions.WebSearchToTsQuery("english", trimmed);
            var limit = Math.Max(1, maxResults);
            var perSourceLimit = Math.Max(1, (int)Math.Ceiling(limit / 5d));
            var headlineOptions = "StartSel=<mark>, StopSel=</mark>, MaxWords=35, MinWords=10, ShortWord=3";
            var hits = new List<GlobalSearchHit>();

            await AppendVisitsAsync(searchQuery, headlineOptions, perSourceLimit, hits, cancellationToken).ConfigureAwait(false);
            await AppendSocialMediaAsync(searchQuery, headlineOptions, perSourceLimit, hits, cancellationToken).ConfigureAwait(false);
            await AppendTrainingAsync(trimmed, searchQuery, headlineOptions, perSourceLimit, hits, cancellationToken).ConfigureAwait(false);
            await AppendTotAsync(searchQuery, headlineOptions, perSourceLimit, hits, cancellationToken).ConfigureAwait(false);
            await AppendProliferationAsync(searchQuery, headlineOptions, perSourceLimit, hits, cancellationToken).ConfigureAwait(false);

            return hits;
        }

        // SECTION: Visits tracker search
        private async Task AppendVisitsAsync(NpgsqlTsQuery searchQuery, string headlineOptions, int limit, ICollection<GlobalSearchHit> hits, CancellationToken cancellationToken)
        {
            var visits = await _dbContext.Visits
                .AsNoTracking()
                .Where(visit =>
                    EF.Functions.ToTsVector("english",
                        (visit.VisitorName ?? string.Empty) + " " +
                        (visit.Remarks ?? string.Empty) + " " +
                        (visit.VisitType != null ? visit.VisitType.Name : string.Empty))
                    .Matches(searchQuery))
                .OrderByDescending(visit => visit.LastModifiedAtUtc ?? visit.CreatedAtUtc)
                .Take(limit)
                .Select(visit => new
                {
                    visit.Id,
                    visit.VisitorName,
                    visit.Remarks,
                    visit.DateOfVisit,
                    visit.CreatedAtUtc,
                    visit.LastModifiedAtUtc,
                    TypeName = visit.VisitType != null ? visit.VisitType.Name : null,
                    Snippet = EF.Functions.TsHeadline(
                        "english",
                        (visit.VisitorName ?? string.Empty) + " " +
                        (visit.Remarks ?? string.Empty) + " " +
                        (visit.VisitType != null ? visit.VisitType.Name : string.Empty),
                        searchQuery,
                        headlineOptions)
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var visit in visits)
            {
                var date = visit.LastModifiedAtUtc ?? visit.CreatedAtUtc;

                var title = string.IsNullOrWhiteSpace(visit.TypeName)
                    ? visit.VisitorName
                    : $"{visit.TypeName} · {visit.VisitorName}";

                var snippet = string.IsNullOrWhiteSpace(visit.Snippet)
                    ? $"Visit on {visit.DateOfVisit.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}"
                    : visit.Snippet;

                hits.Add(new GlobalSearchHit(
                    Source: "Visits tracker",
                    Title: title,
                    Snippet: snippet,
                    Url: _urlBuilder.ProjectOfficeVisitDetails(visit.Id),
                    Date: date,
                    Score: 0.55m,
                    FileType: null,
                    Extra: null));
            }
        }

        // SECTION: Social media tracker search
        private async Task AppendSocialMediaAsync(NpgsqlTsQuery searchQuery, string headlineOptions, int limit, ICollection<GlobalSearchHit> hits, CancellationToken cancellationToken)
        {
            var events = await _dbContext.SocialMediaEvents
                .AsNoTracking()
                .Where(e =>
                    EF.Functions.ToTsVector("english",
                        (e.Title ?? string.Empty) + " " +
                        (e.Description ?? string.Empty) + " " +
                        (e.SocialMediaPlatform != null ? e.SocialMediaPlatform.Name : string.Empty))
                    .Matches(searchQuery))
                .OrderByDescending(e => e.LastModifiedAtUtc ?? e.CreatedAtUtc)
                .Take(limit)
                .Select(e => new
                {
                    e.Id,
                    e.Title,
                    e.Description,
                    e.CreatedAtUtc,
                    e.LastModifiedAtUtc,
                    PlatformName = e.SocialMediaPlatform != null ? e.SocialMediaPlatform.Name : null,
                    Snippet = EF.Functions.TsHeadline(
                        "english",
                        (e.Title ?? string.Empty) + " " +
                        (e.Description ?? string.Empty) + " " +
                        (e.SocialMediaPlatform != null ? e.SocialMediaPlatform.Name : string.Empty),
                        searchQuery,
                        headlineOptions)
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var ev in events)
            {
                var date = ev.LastModifiedAtUtc ?? ev.CreatedAtUtc;
                var title = ev.PlatformName is null
                    ? ev.Title
                    : $"{ev.Title} ({ev.PlatformName})";

                hits.Add(new GlobalSearchHit(
                    Source: "Social media tracker",
                    Title: title,
                    Snippet: string.IsNullOrWhiteSpace(ev.Snippet) ? ev.Description : ev.Snippet,
                    // your app uses /ProjectOfficeReports/SocialMedia/Details/{id}
                    Url: $"/ProjectOfficeReports/SocialMedia/Details/{ev.Id}",
                    Date: date,
                    Score: 0.52m,
                    FileType: null,
                    Extra: null));
            }
        }

        // SECTION: Training tracker search
        private async Task AppendTrainingAsync(string trimmedQuery, NpgsqlTsQuery searchQuery, string headlineOptions, int limit, ICollection<GlobalSearchHit> hits, CancellationToken cancellationToken)
        {
            var numericMatch = int.TryParse(trimmedQuery, NumberStyles.Integer, CultureInfo.InvariantCulture, out var yearQuery)
                ? yearQuery
                : (int?)null;

            var trainings = await _dbContext.Trainings
                .AsNoTracking()
                .Where(training =>
                    EF.Functions.ToTsVector("english",
                        (training.Notes ?? string.Empty) + " " +
                        (training.TrainingType != null ? training.TrainingType.Name : string.Empty))
                        .Matches(searchQuery) ||
                    (numericMatch.HasValue && training.TrainingYear == numericMatch.Value))
                .OrderByDescending(training => training.LastModifiedAtUtc ?? training.CreatedAtUtc)
                .Take(limit)
                .Select(training => new
                {
                    training.Id,
                    training.Notes,
                    training.StartDate,
                    training.EndDate,
                    training.TrainingYear,
                    training.CreatedAtUtc,
                    training.LastModifiedAtUtc,
                    TrainingTypeName = training.TrainingType != null ? training.TrainingType.Name : null,
                    Snippet = EF.Functions.TsHeadline(
                        "english",
                        (training.Notes ?? string.Empty) + " " +
                        (training.TrainingType != null ? training.TrainingType.Name : string.Empty),
                        searchQuery,
                        headlineOptions)
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var training in trainings)
            {
                var date = training.LastModifiedAtUtc ?? training.CreatedAtUtc;
                var titleParts = new List<string>(3);

                if (!string.IsNullOrWhiteSpace(training.TrainingTypeName))
                {
                    titleParts.Add(training.TrainingTypeName);
                }

                if (training.TrainingYear.HasValue)
                {
                    titleParts.Add(training.TrainingYear.Value.ToString(CultureInfo.InvariantCulture));
                }

                if (training.StartDate.HasValue || training.EndDate.HasValue)
                {
                    var range = string.Join(" – ", new[]
                    {
                        training.StartDate?.ToString("dd MMM", CultureInfo.InvariantCulture),
                        training.EndDate?.ToString("dd MMM", CultureInfo.InvariantCulture)
                    }.Where(v => !string.IsNullOrWhiteSpace(v)));

                    if (!string.IsNullOrWhiteSpace(range))
                    {
                        titleParts.Add(range);
                    }
                }

                var title = titleParts.Count == 0
                    ? "Training record"
                    : string.Join(" · ", titleParts);

                hits.Add(new GlobalSearchHit(
                    Source: "Training tracker",
                    Title: title,
                    Snippet: string.IsNullOrWhiteSpace(training.Snippet) ? training.Notes : training.Snippet,
                    Url: _urlBuilder.ProjectOfficeTrainingManage(training.Id),
                    Date: date,
                    Score: 0.5m,
                    FileType: null,
                    Extra: null));
            }
        }

        // SECTION: TOT tracker search
        private async Task AppendTotAsync(NpgsqlTsQuery searchQuery, string headlineOptions, int limit, ICollection<GlobalSearchHit> hits, CancellationToken cancellationToken)
        {
            var tots = await _dbContext.ProjectTots
                .AsNoTracking()
                .Join(
                    _dbContext.Projects.AsNoTracking(),
                    tot => tot.ProjectId,
                    project => project.Id,
                    (tot, project) => new { tot, project })
                .Where(entry =>
                    EF.Functions.ToTsVector("english",
                        (entry.project.Name ?? string.Empty) + " " +
                        (entry.tot.MetDetails ?? string.Empty))
                        .Matches(searchQuery))
                .OrderByDescending(entry => entry.tot.LastApprovedOnUtc ??
                    (entry.tot.CompletedOn.HasValue
                        ? entry.tot.CompletedOn.Value.ToDateTime(TimeOnly.MinValue)
                        : entry.tot.StartedOn.HasValue
                            ? entry.tot.StartedOn.Value.ToDateTime(TimeOnly.MinValue)
                            : entry.project.CreatedAt))
                .Take(limit)
                .Select(entry => new
                {
                    entry.tot.Id,
                    entry.tot.ProjectId,
                    entry.tot.Status,
                    entry.tot.MetDetails,
                    entry.tot.StartedOn,
                    entry.tot.CompletedOn,
                    entry.tot.LastApprovedOnUtc,
                    ProjectName = entry.project.Name,
                    Date = entry.tot.LastApprovedOnUtc ??
                        (entry.tot.CompletedOn.HasValue
                            ? entry.tot.CompletedOn.Value.ToDateTime(TimeOnly.MinValue)
                            : entry.tot.StartedOn.HasValue
                                ? entry.tot.StartedOn.Value.ToDateTime(TimeOnly.MinValue)
                                : entry.project.CreatedAt),
                    Snippet = EF.Functions.TsHeadline(
                        "english",
                        (entry.project.Name ?? string.Empty) + " " +
                        (entry.tot.MetDetails ?? string.Empty),
                        searchQuery,
                        headlineOptions)
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var tot in tots)
            {
                var titleParts = new List<string>(3);
                if (!string.IsNullOrWhiteSpace(tot.ProjectName))
                {
                    titleParts.Add(tot.ProjectName);
                }

                titleParts.Add(tot.Status.ToString());

                var dateText = tot.CompletedOn.HasValue
                    ? tot.CompletedOn.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
                    : tot.StartedOn.HasValue
                        ? tot.StartedOn.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
                        : null;

                if (!string.IsNullOrWhiteSpace(dateText))
                {
                    titleParts.Add(dateText);
                }

                var title = titleParts.Count == 0
                    ? "TOT tracker entry"
                    : string.Join(" · ", titleParts);

                hits.Add(new GlobalSearchHit(
                    Source: "TOT tracker",
                    Title: title,
                    Snippet: string.IsNullOrWhiteSpace(tot.Snippet) ? tot.MetDetails : tot.Snippet,
                    Url: _urlBuilder.ProjectOfficeTotTracker(tot.ProjectId),
                    Date: tot.Date,
                    Score: 0.48m,
                    FileType: null,
                    Extra: null));
            }
        }

        // SECTION: Proliferation survey search
        private async Task AppendProliferationAsync(NpgsqlTsQuery searchQuery, string headlineOptions, int limit, ICollection<GlobalSearchHit> hits, CancellationToken cancellationToken)
        {
            var yearlyRecords = await _dbContext.ProliferationYearlies
                .AsNoTracking()
                .Join(
                    _dbContext.Projects.AsNoTracking(),
                    record => record.ProjectId,
                    project => project.Id,
                    (record, project) => new { record, project })
                .Where(entry =>
                    EF.Functions.ToTsVector("english",
                        (entry.project.Name ?? string.Empty) + " " +
                        (entry.record.Remarks ?? string.Empty))
                        .Matches(searchQuery))
                .OrderByDescending(entry => entry.record.LastUpdatedOnUtc)
                .Take(limit)
                .Select(entry => new
                {
                    entry.record.Id,
                    entry.record.ProjectId,
                    entry.record.Source,
                    entry.record.Year,
                    entry.record.Remarks,
                    entry.record.CreatedOnUtc,
                    entry.record.LastUpdatedOnUtc,
                    ProjectName = entry.project.Name,
                    Snippet = EF.Functions.TsHeadline(
                        "english",
                        (entry.project.Name ?? string.Empty) + " " +
                        (entry.record.Remarks ?? string.Empty),
                        searchQuery,
                        headlineOptions)
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var granularRecords = await _dbContext.ProliferationGranularEntries
                .AsNoTracking()
                .Join(
                    _dbContext.Projects.AsNoTracking(),
                    record => record.ProjectId,
                    project => project.Id,
                    (record, project) => new { record, project })
                .Where(entry =>
                    EF.Functions.ToTsVector("english",
                        (entry.project.Name ?? string.Empty) + " " +
                        (entry.record.UnitName ?? string.Empty) + " " +
                        (entry.record.Remarks ?? string.Empty))
                        .Matches(searchQuery))
                .OrderByDescending(entry => entry.record.LastUpdatedOnUtc)
                .Take(limit)
                .Select(entry => new
                {
                    entry.record.Id,
                    entry.record.ProjectId,
                    entry.record.Source,
                    entry.record.ProliferationDate,
                    entry.record.Remarks,
                    entry.record.CreatedOnUtc,
                    entry.record.LastUpdatedOnUtc,
                    ProjectName = entry.project.Name,
                    Snippet = EF.Functions.TsHeadline(
                        "english",
                        (entry.project.Name ?? string.Empty) + " " +
                        (entry.record.UnitName ?? string.Empty) + " " +
                        (entry.record.Remarks ?? string.Empty),
                        searchQuery,
                        headlineOptions)
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var records = yearlyRecords
                .Select(record => new
                {
                    record.Id,
                    record.ProjectId,
                    record.ProjectName,
                    record.Source,
                    Kind = ProliferationRecordKind.Yearly,
                    Year = (int?)record.Year,
                    ProliferationDate = (DateOnly?)null,
                    record.Remarks,
                    record.Snippet,
                    Date = record.LastUpdatedOnUtc
                })
                .Concat(granularRecords.Select(record => new
                {
                    record.Id,
                    record.ProjectId,
                    record.ProjectName,
                    record.Source,
                    Kind = ProliferationRecordKind.Granular,
                    Year = (int?)record.ProliferationDate.Year,
                    ProliferationDate = (DateOnly?)record.ProliferationDate,
                    record.Remarks,
                    record.Snippet,
                    Date = record.LastUpdatedOnUtc
                }))
                .OrderByDescending(record => record.Date)
                .Take(limit)
                .ToList();

            foreach (var record in records)
            {
                var titleParts = new List<string>(3);
                if (!string.IsNullOrWhiteSpace(record.ProjectName))
                {
                    titleParts.Add(record.ProjectName);
                }

                var sourceLabel = record.Source.ToString();

                if (record.Kind == ProliferationRecordKind.Yearly)
                {
                    titleParts.Add(record.Year.HasValue
                        ? $"{sourceLabel} ({record.Year.Value.ToString(CultureInfo.InvariantCulture)})"
                        : sourceLabel);
                }
                else
                {
                    var dateLabel = record.ProliferationDate?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
                    titleParts.Add(string.IsNullOrWhiteSpace(dateLabel) ? sourceLabel : $"{sourceLabel} ({dateLabel})");
                }

                var title = titleParts.Count == 0
                    ? "Proliferation record"
                    : string.Join(" · ", titleParts);

                var snippet = string.IsNullOrWhiteSpace(record.Snippet) ? record.Remarks : record.Snippet;

                hits.Add(new GlobalSearchHit(
                    Source: "Proliferation tracker",
                    Title: title,
                    Snippet: snippet,
                    Url: _urlBuilder.ProjectOfficeProliferationManage(record.ProjectId, record.Kind, record.Source, record.Year),
                    Date: record.Date,
                    Score: 0.47m,
                    FileType: null,
                    Extra: record.Year?.ToString(CultureInfo.InvariantCulture)));
            }
        }
    }
}
