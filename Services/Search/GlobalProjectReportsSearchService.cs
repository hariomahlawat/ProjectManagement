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
                    Url: _urlBuilder.ProjectOfficeTrainingDetails(training.Id),
                    Date: date,
                    Score: 0.5m,
                    FileType: null,
                    Extra: null));
            }
        }

        // SECTION: TOT tracker search
        private async Task AppendTotAsync(NpgsqlTsQuery searchQuery, string headlineOptions, int limit, ICollection<GlobalSearchHit> hits, CancellationToken cancellationToken)
        {
            var trainings = await _dbContext.TotTrainings
                .AsNoTracking()
                .Where(training =>
                    EF.Functions.ToTsVector("english", training.Notes ?? string.Empty)
                        .Matches(searchQuery))
                .OrderByDescending(training => training.LastModifiedAtUtc ?? training.CreatedAtUtc)
                .Take(limit)
                .Select(training => new
                {
                    training.Id,
                    training.Notes,
                    training.StartDate,
                    training.EndDate,
                    training.CreatedAtUtc,
                    training.LastModifiedAtUtc,
                    Snippet = EF.Functions.TsHeadline(
                        "english",
                        training.Notes ?? string.Empty,
                        searchQuery,
                        headlineOptions)
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var training in trainings)
            {
                var titleParts = new List<string>(2);
                if (training.StartDate.HasValue || training.EndDate.HasValue)
                {
                    titleParts.Add(string.Join(" – ", new[]
                    {
                        training.StartDate?.ToString("dd MMM", CultureInfo.InvariantCulture),
                        training.EndDate?.ToString("dd MMM", CultureInfo.InvariantCulture)
                    }.Where(v => !string.IsNullOrWhiteSpace(v))));
                }

                if (!string.IsNullOrWhiteSpace(training.Notes))
                {
                    titleParts.Add(training.Notes);
                }

                var title = titleParts.Count == 0
                    ? "TOT training"
                    : string.Join(" · ", titleParts);

                hits.Add(new GlobalSearchHit(
                    Source: "TOT tracker",
                    Title: title,
                    Snippet: string.IsNullOrWhiteSpace(training.Snippet) ? training.Notes : training.Snippet,
                    Url: _urlBuilder.ProjectOfficeTotDetails(training.Id),
                    Date: training.LastModifiedAtUtc ?? training.CreatedAtUtc,
                    Score: 0.48m,
                    FileType: null,
                    Extra: null));
            }
        }

        // SECTION: Proliferation survey search
        private async Task AppendProliferationAsync(NpgsqlTsQuery searchQuery, string headlineOptions, int limit, ICollection<GlobalSearchHit> hits, CancellationToken cancellationToken)
        {
            var records = await _dbContext.ProliferationSurveys
                .AsNoTracking()
                .Where(record =>
                    EF.Functions.ToTsVector("english",
                        (record.Country ?? string.Empty) + " " +
                        (record.Topic ?? string.Empty) + " " +
                        (record.Remarks ?? string.Empty))
                        .Matches(searchQuery))
                .OrderByDescending(record => record.LastModifiedAtUtc ?? record.CreatedAtUtc)
                .Take(limit)
                .Select(record => new
                {
                    record.Id,
                    record.Topic,
                    record.Country,
                    record.Remarks,
                    record.SurveyDate,
                    record.CreatedAtUtc,
                    record.LastModifiedAtUtc,
                    Snippet = EF.Functions.TsHeadline(
                        "english",
                        (record.Country ?? string.Empty) + " " +
                        (record.Topic ?? string.Empty) + " " +
                        (record.Remarks ?? string.Empty),
                        searchQuery,
                        headlineOptions)
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var record in records)
            {
                var date = record.LastModifiedAtUtc ?? record.CreatedAtUtc;
                var title = record.Topic ?? record.Country;

                if (record.SurveyDate.HasValue)
                {
                    title = string.IsNullOrWhiteSpace(title)
                        ? record.SurveyDate.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
                        : $"{title} ({record.SurveyDate.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)})";
                }

                hits.Add(new GlobalSearchHit(
                    Source: "Proliferation survey",
                    Title: title,
                    Snippet: string.IsNullOrWhiteSpace(record.Snippet) ? record.Remarks : record.Snippet,
                    Url: _urlBuilder.ProjectOfficeProliferationDetails(record.Id),
                    Date: date,
                    Score: 0.47m,
                    FileType: null,
                    Extra: record.Country));
            }
        }
    }
}
