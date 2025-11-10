using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
            var pattern = $"%{trimmed}%";
            var limit = Math.Max(1, maxResults);
            var perSourceLimit = Math.Max(1, (int)Math.Ceiling(limit / 5d));
            var hits = new List<GlobalSearchHit>();

            await AppendVisitsAsync(pattern, perSourceLimit, hits, cancellationToken).ConfigureAwait(false);
            await AppendSocialMediaAsync(pattern, perSourceLimit, hits, cancellationToken).ConfigureAwait(false);
            await AppendTrainingAsync(trimmed, pattern, perSourceLimit, hits, cancellationToken).ConfigureAwait(false);
            await AppendTotAsync(pattern, perSourceLimit, hits, cancellationToken).ConfigureAwait(false);
            await AppendProliferationAsync(pattern, perSourceLimit, hits, cancellationToken).ConfigureAwait(false);

            return hits;
        }

        // SECTION: Visits tracker search
        private async Task AppendVisitsAsync(string pattern, int limit, ICollection<GlobalSearchHit> hits, CancellationToken cancellationToken)
        {
            var visits = await _dbContext.Visits
                .AsNoTracking()
                .Where(visit =>
                    EF.Functions.ILike(visit.VisitorName, pattern) ||
                    EF.Functions.ILike(visit.Remarks ?? string.Empty, pattern) ||
                    (visit.VisitType != null && EF.Functions.ILike(visit.VisitType.Name, pattern)))
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
                    TypeName = visit.VisitType != null ? visit.VisitType.Name : null
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var visit in visits)
            {
                var date = visit.LastModifiedAtUtc ?? visit.CreatedAtUtc;
                var title = string.IsNullOrWhiteSpace(visit.TypeName)
                    ? visit.VisitorName
                    : $"{visit.TypeName} · {visit.VisitorName}";
                var snippet = string.IsNullOrWhiteSpace(visit.Remarks)
                    ? $"Visit on {visit.DateOfVisit.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}"
                    : visit.Remarks;

                hits.Add(new GlobalSearchHit(
                    Source: "Visits tracker",
                    Title: title,
                    Snippet: snippet,
                    // <- actual razor page route uses a segment, not ?id=
                    Url: $"/ProjectOfficeReports/Visits/Details/{visit.Id}",
                    Date: date,
                    Score: 0.55m,
                    FileType: null,
                    Extra: null));
            }
        }

        // SECTION: Social media tracker search
        private async Task AppendSocialMediaAsync(string pattern, int limit, ICollection<GlobalSearchHit> hits, CancellationToken cancellationToken)
        {
            var events = await _dbContext.SocialMediaEvents
                .AsNoTracking()
                .Where(@event =>
                    EF.Functions.ILike(@event.Title, pattern) ||
                    EF.Functions.ILike(@event.Description ?? string.Empty, pattern) ||
                    (@event.SocialMediaPlatform != null && EF.Functions.ILike(@event.SocialMediaPlatform.Name, pattern)))
                .OrderByDescending(@event => @event.LastModifiedAtUtc ?? @event.CreatedAtUtc)
                .Take(limit)
                .Select(@event => new
                {
                    @event.Id,
                    @event.Title,
                    @event.Description,
                    @event.CreatedAtUtc,
                    @event.LastModifiedAtUtc,
                    PlatformName = @event.SocialMediaPlatform != null ? @event.SocialMediaPlatform.Name : null
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var socialMediaEvent in events)
            {
                var date = socialMediaEvent.LastModifiedAtUtc ?? socialMediaEvent.CreatedAtUtc;
                var title = socialMediaEvent.PlatformName is null
                    ? socialMediaEvent.Title
                    : $"{socialMediaEvent.Title} ({socialMediaEvent.PlatformName})";

                hits.Add(new GlobalSearchHit(
                    Source: "Social media tracker",
                    Title: title,
                    Snippet: socialMediaEvent.Description,
                    Url: _urlBuilder.ProjectOfficeSocialMediaDetails(socialMediaEvent.Id),
                    Date: date,
                    Score: 0.52m,
                    FileType: null,
                    Extra: null));
            }
        }

        // SECTION: Training tracker search
        private async Task AppendTrainingAsync(string trimmedQuery, string pattern, int limit, ICollection<GlobalSearchHit> hits, CancellationToken cancellationToken)
        {
            var numericMatch = int.TryParse(trimmedQuery, NumberStyles.Integer, CultureInfo.InvariantCulture, out var yearQuery)
                ? yearQuery
                : (int?)null;

            var trainings = await _dbContext.Trainings
                .AsNoTracking()
                .Where(training =>
                    EF.Functions.ILike(training.Notes ?? string.Empty, pattern) ||
                    (training.TrainingType != null && EF.Functions.ILike(training.TrainingType.Name, pattern)) ||
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
                    TrainingTypeName = training.TrainingType != null ? training.TrainingType.Name : null
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
                    }.Where(value => !string.IsNullOrWhiteSpace(value)));

                    if (!string.IsNullOrWhiteSpace(range))
                    {
                        titleParts.Add(range);
                    }
                }

                var title = titleParts.Count == 0 ? "Training session" : string.Join(" · ", titleParts);

                hits.Add(new GlobalSearchHit(
                    Source: "Training tracker",
                    Title: title,
                    Snippet: training.Notes,
                    Url: _urlBuilder.ProjectOfficeTrainingManage(training.Id),
                    Date: date,
                    Score: 0.48m,
                    FileType: null,
                    Extra: null));
            }
        }

        // SECTION: Transfer of Technology tracker search
        private async Task AppendTotAsync(string pattern, int limit, ICollection<GlobalSearchHit> hits, CancellationToken cancellationToken)
        {
            // 1) EF-friendly query (no custom methods in OrderBy)
            var rawTots = await _dbContext.ProjectTots
                .AsNoTracking()
                .Include(t => t.Project)
                .Where(t =>
                    t.Project != null &&
                    !t.Project.IsDeleted &&
                    !t.Project.IsArchived &&
                    (
                        EF.Functions.ILike(t.Project.Name, pattern) ||
                        EF.Functions.ILike(t.MetDetails ?? string.Empty, pattern)
                    ))
                .Take(limit * 3) // grab a bit more, we'll sort in memory
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // 2) sort in memory using the date preference we wanted
            var ordered = rawTots
                .Select(t =>
                {
                    var pickedDate =
                        t.LastApprovedOnUtc.HasValue
                            ? new DateTimeOffset(DateTime.SpecifyKind(t.LastApprovedOnUtc.Value, DateTimeKind.Utc))
                            : ToDateTimeOffset(t.CompletedOn)
                            ?? ToDateTimeOffset(t.StartedOn)
                            ?? DateTimeOffset.MinValue;

                    return new
                    {
                        Tot = t,
                        Date = pickedDate
                    };
                })
                .OrderByDescending(x => x.Date)
                .Take(limit)
                .ToList();

            foreach (var item in ordered)
            {
                var tot = item.Tot;
                var title = $"{tot.Project!.Name} · {tot.Status}";

                hits.Add(new GlobalSearchHit(
                    Source: "ToT tracker",
                    Title: title,
                    Snippet: tot.MetDetails,
                    Url: _urlBuilder.ProjectOfficeTotTracker(tot.ProjectId),
                    Date: item.Date,
                    Score: 0.46m,
                    FileType: null,
                    Extra: tot.Status.ToString()));
            }
        }

        // SECTION: Proliferation tracker search
        private async Task AppendProliferationAsync(string pattern, int limit, ICollection<GlobalSearchHit> hits, CancellationToken cancellationToken)
        {
            await AppendProliferationYearlyAsync(pattern, limit, hits, cancellationToken).ConfigureAwait(false);
            await AppendProliferationGranularAsync(pattern, limit, hits, cancellationToken).ConfigureAwait(false);
        }

        private async Task AppendProliferationYearlyAsync(string pattern, int limit, ICollection<GlobalSearchHit> hits, CancellationToken cancellationToken)
        {
            var yearly = await (from record in _dbContext.ProliferationYearlies.AsNoTracking()
                                join project in _dbContext.Projects.AsNoTracking() on record.ProjectId equals project.Id
                                where !project.IsDeleted && !project.IsArchived && (
                                    EF.Functions.ILike(project.Name, pattern) ||
                                    EF.Functions.ILike(project.CaseFileNumber ?? string.Empty, pattern) ||
                                    EF.Functions.ILike(record.Remarks ?? string.Empty, pattern))
                                orderby record.LastUpdatedOnUtc descending
                                select new
                                {
                                    record.Id,
                                    record.ProjectId,
                                    ProjectName = project.Name,
                                    ProjectCaseFile = project.CaseFileNumber,
                                    record.Source,
                                    record.Year,
                                    record.TotalQuantity,
                                    record.Remarks,
                                    record.LastUpdatedOnUtc
                                })
                .Take(limit)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var record in yearly)
            {
                var title = $"{record.ProjectName} · {record.Year} {record.Source.ToDisplayName()}";
                var snippet = string.IsNullOrWhiteSpace(record.Remarks)
                    ? $"Quantity: {record.TotalQuantity.ToString(CultureInfo.InvariantCulture)}"
                    : record.Remarks;

                hits.Add(new GlobalSearchHit(
                    Source: "Proliferation tracker",
                    Title: title,
                    Snippet: snippet,
                    Url: _urlBuilder.ProjectOfficeProliferationManage(record.ProjectId, ProliferationRecordKind.Yearly, record.Source, record.Year),
                    Date: new DateTimeOffset(DateTime.SpecifyKind(record.LastUpdatedOnUtc, DateTimeKind.Utc)),
                    Score: 0.44m,
                    FileType: null,
                    Extra: record.ProjectCaseFile));
            }
        }

        private async Task AppendProliferationGranularAsync(string pattern, int limit, ICollection<GlobalSearchHit> hits, CancellationToken cancellationToken)
        {
            var granular = await (from record in _dbContext.ProliferationGranularEntries.AsNoTracking()
                                  join project in _dbContext.Projects.AsNoTracking() on record.ProjectId equals project.Id
                                  where !project.IsDeleted && !project.IsArchived && (
                                      EF.Functions.ILike(project.Name, pattern) ||
                                      EF.Functions.ILike(project.CaseFileNumber ?? string.Empty, pattern) ||
                                      EF.Functions.ILike(record.UnitName, pattern) ||
                                      EF.Functions.ILike(record.Remarks ?? string.Empty, pattern))
                                  orderby record.LastUpdatedOnUtc descending
                                  select new
                                  {
                                      record.Id,
                                      record.ProjectId,
                                      ProjectName = project.Name,
                                      ProjectCaseFile = project.CaseFileNumber,
                                      record.Source,
                                      record.UnitName,
                                      record.ProliferationDate,
                                      record.Quantity,
                                      record.Remarks,
                                      record.LastUpdatedOnUtc
                                  })
                .Take(limit)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var record in granular)
            {
                var title = $"{record.ProjectName} · {record.UnitName}";
                var date = new DateTimeOffset(DateTime.SpecifyKind(record.LastUpdatedOnUtc, DateTimeKind.Utc));
                var snippet = string.IsNullOrWhiteSpace(record.Remarks)
                    ? $"Quantity: {record.Quantity.ToString(CultureInfo.InvariantCulture)}"
                    : record.Remarks;

                hits.Add(new GlobalSearchHit(
                    Source: "Proliferation tracker",
                    Title: title,
                    Snippet: snippet,
                    Url: _urlBuilder.ProjectOfficeProliferationManage(record.ProjectId, ProliferationRecordKind.Granular, record.Source, record.ProliferationDate.Year),
                    Date: date,
                    Score: 0.43m,
                    FileType: null,
                    Extra: record.ProjectCaseFile));
            }
        }

        // SECTION: Helper utilities
        private static DateTime? ToDateTime(DateOnly? value)
            => value?.ToDateTime(TimeOnly.MinValue);

        private static DateTimeOffset? ToDateTimeOffset(DateOnly? value)
            => value.HasValue
                ? new DateTimeOffset(value.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
                : null;
    }
}
