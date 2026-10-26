using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models.Analytics;

namespace ProjectManagement.Services.Analytics
{
    /// <summary>
    /// Analytics → CoE (AR/VR): what the AR/VR projects have delivered beyond the workflow, from the
    /// proliferation tracker (approved rows, the tracker's own year-preference reconciliation) and
    /// the training tracker (its headcount and training-year rules, each course counted once).
    /// </summary>
    public interface IArVrImpactAnalyticsService
    {
        /// <param name="referenceDateIst">The IST "as of" day; its calendar year and training year are "this year".</param>
        Task<ArVrImpactVm> BuildAsync(DateOnly referenceDateIst, CancellationToken cancellationToken = default);
    }

    public sealed class ArVrImpactAnalyticsService : IArVrImpactAnalyticsService
    {
        private readonly ApplicationDbContext _db;
        private readonly ProliferationAggregateReadService _proliferationAggregates;

        public ArVrImpactAnalyticsService(ApplicationDbContext db, ProliferationAggregateReadService proliferationAggregates)
        {
            _db = db;
            _proliferationAggregates = proliferationAggregates;
        }

        public async Task<ArVrImpactVm> BuildAsync(DateOnly referenceDateIst, CancellationToken cancellationToken = default)
        {
            var projects = await LoadArVrProjectsAsync(cancellationToken);
            if (projects.Count == 0)
            {
                return new ArVrImpactVm
                {
                    ProliferationCurrentYear = referenceDateIst.Year,
                    TrainingCurrentYearLabel = TrainingReach.TrainingYearLabel(TrainingReach.TrainingYearStartOf(referenceDateIst))
                };
            }

            var proliferation = await BuildProliferationAsync(projects, referenceDateIst, cancellationToken);
            var training = await BuildTrainingAsync(projects, referenceDateIst, cancellationToken);

            return new ArVrImpactVm
            {
                ProliferationUnitsDelivered = proliferation.ByYear.Sum(point => point.Total),
                ProliferationReceivingUnits = proliferation.ReceivingUnits,
                ProliferationProjects = proliferation.ProjectCount,
                ProliferationThisYear = proliferation.ByYear.FirstOrDefault(point => point.Year == referenceDateIst.Year)?.Total ?? 0,
                ProliferationCurrentYear = referenceDateIst.Year,
                ProliferationByYear = proliferation.ByYear,
                ProliferationBoard = proliferation.Board,
                TrainingCourses = training.Courses,
                TrainingPeople = training.ByYear.Sum(point => point.People),
                TrainingProjects = training.ProjectCount,
                TrainingPeopleThisYear = training.ByYear.FirstOrDefault(point => point.StartYear == TrainingReach.TrainingYearStartOf(referenceDateIst))?.People ?? 0,
                TrainingCurrentYearLabel = TrainingReach.TrainingYearLabel(TrainingReach.TrainingYearStartOf(referenceDateIst)),
                TrainingByYear = training.ByYear,
                TrainingBoard = training.Board
            };
        }

        // SECTION: AR/VR projects (technical category by name; live projects only)
        private async Task<Dictionary<int, string>> LoadArVrProjectsAsync(CancellationToken cancellationToken)
        {
            var technical = await _db.TechnicalCategories.AsNoTracking().Select(t => new { t.Id, t.Name }).ToListAsync(cancellationToken);
            var technicalIds = technical.Where(t => ArVrTechnicalCategories.Matches(t.Name)).Select(t => t.Id).ToList();
            if (technicalIds.Count == 0)
            {
                return new Dictionary<int, string>();
            }

            return await _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && !p.IsArchived && p.TechnicalCategoryId.HasValue && technicalIds.Contains(p.TechnicalCategoryId.Value))
                .Select(p => new { p.Id, p.Name })
                .ToDictionaryAsync(p => p.Id, p => string.IsNullOrWhiteSpace(p.Name) ? "Untitled project" : p.Name.Trim(), cancellationToken);
        }
        // END SECTION

        // SECTION: Proliferation (the tracker's approved aggregates; receiving units from the approved granular ledger)
        private async Task<(IReadOnlyList<ArVrProliferationYearVm> ByYear, IReadOnlyList<AnalyticsBoardGroupVm> Board, int ReceivingUnits, int ProjectCount)>
            BuildProliferationAsync(Dictionary<int, string> projects, DateOnly referenceDateIst, CancellationToken cancellationToken)
        {
            // A year the tracker's own policy rejects (a mistyped ledger date) is left out of the chart and the counts.
            var now = new DateTimeOffset(referenceDateIst.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var aggregates = (await _proliferationAggregates.GetApprovedAggregatesAsync(null, cancellationToken))
                .Where(row => projects.ContainsKey(row.ProjectId) && row.ReportedTotal > 0 && ProliferationYearPolicy.IsValid(row.Year, now))
                .ToList();

            var projectIds = projects.Keys.ToList();
            var ledger = await _db.ProliferationGranularEntries
                .AsNoTracking()
                .Where(entry => entry.ApprovalStatus == ApprovalStatus.Approved && projectIds.Contains(entry.ProjectId))
                .Select(entry => new { entry.ProjectId, Year = entry.ProliferationDate.Year, entry.UnitName })
                .ToListAsync(cancellationToken);
            ledger = ledger.Where(entry => ProliferationYearPolicy.IsValid(entry.Year, now)).ToList();

            var unitsByYearProject = ledger
                .GroupBy(entry => (entry.ProjectId, entry.Year))
                .ToDictionary(group => group.Key, group => group.Select(entry => entry.UnitName.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            var unitsByYear = ledger
                .GroupBy(entry => entry.Year)
                .ToDictionary(group => group.Key, group => group.Select(entry => entry.UnitName.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count());

            var byYear = aggregates
                .GroupBy(row => row.Year)
                .Select(group => new ArVrProliferationYearVm(
                    group.Key,
                    group.Where(row => row.Source == ProliferationSource.Sdd).Sum(row => row.ReportedTotal),
                    group.Where(row => row.Source == ProliferationSource.Abw515).Sum(row => row.ReportedTotal),
                    group.Select(row => row.ProjectId).Distinct().Count(),
                    unitsByYear.TryGetValue(group.Key, out var units) ? units : 0))
                .OrderBy(point => point.Year)
                .ToList();

            var board = byYear
                .OrderByDescending(point => point.Year)
                .Select(point => new AnalyticsBoardGroupVm(
                    point.Year.ToString(),
                    point.Year.ToString(),
                    point.Total,
                    aggregates
                        .Where(row => row.Year == point.Year)
                        .GroupBy(row => row.ProjectId)
                        .Select(group => (ProjectId: group.Key, Units: group.Sum(row => row.ReportedTotal), Receiving: unitsByYearProject.TryGetValue((group.Key, point.Year), out var n) ? n : 0))
                        .OrderByDescending(item => item.Units)
                        .ThenBy(item => projects[item.ProjectId], StringComparer.OrdinalIgnoreCase)
                        .Select(item => new AnalyticsBoardProjectVm(
                            item.ProjectId,
                            projects[item.ProjectId],
                            item.Receiving > 0 ? $"{item.Units} {Plural(item.Units, "unit")} delivered · {item.Receiving} receiving {Plural(item.Receiving, "unit")}" : $"{item.Units} {Plural(item.Units, "unit")} delivered"))
                        .ToList(),
                    $"{point.Projects} {Plural(point.Projects, "project")}" + (point.ReceivingUnits > 0 ? $" · {point.ReceivingUnits} receiving {Plural(point.ReceivingUnits, "unit")}" : "")))
                .ToList();

            var receivingUnits = ledger.Select(entry => entry.UnitName.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            return (byYear, board, receivingUnits, aggregates.Select(row => row.ProjectId).Distinct().Count());
        }
        // END SECTION

        // SECTION: Training (courses linked to AR/VR projects; headcount and training year as the tracker counts them)
        private async Task<(IReadOnlyList<ArVrTrainingYearVm> ByYear, IReadOnlyList<AnalyticsBoardGroupVm> Board, int Courses, int ProjectCount)>
            BuildTrainingAsync(Dictionary<int, string> projects, DateOnly referenceDateIst, CancellationToken cancellationToken)
        {
            var projectIds = projects.Keys.ToList();
            var links = await _db.TrainingProjects
                .AsNoTracking()
                .Where(link => projectIds.Contains(link.ProjectId))
                .Select(link => new { link.TrainingId, link.ProjectId })
                .ToListAsync(cancellationToken);
            if (links.Count == 0)
            {
                return (Array.Empty<ArVrTrainingYearVm>(), Array.Empty<AnalyticsBoardGroupVm>(), 0, 0);
            }

            var trainingIds = links.Select(link => link.TrainingId).Distinct().ToList();
            var courses = await _db.Trainings
                .AsNoTracking()
                .Where(training => trainingIds.Contains(training.Id))
                .Select(training => new TrainingRow(
                    training.Id,
                    training.StartDate,
                    training.EndDate,
                    training.TrainingYear,
                    training.TrainingMonth,
                    training.Counters != null ? training.Counters.Officers : training.LegacyOfficerCount,
                    training.Counters != null ? training.Counters.JuniorCommissionedOfficers : training.LegacyJcoCount,
                    training.Counters != null ? training.Counters.OtherRanks : training.LegacyOrCount))
                .ToListAsync(cancellationToken);

            // Each course is placed in one training year by the tracker's rule; a course with no date at all is left out.
            var placed = courses
                .Select(course => (Course: course, StartYear: TrainingReach.TrainingYearStart(new TrainingReachRow(course.StartDate, course.EndDate, course.TrainingYear, course.TrainingMonth, course.People))))
                .Where(item => item.StartYear.HasValue)
                .Select(item => (item.Course, StartYear: item.StartYear!.Value))
                .ToList();

            var projectsOfCourse = links
                .GroupBy(link => link.TrainingId)
                .ToDictionary(group => group.Key, group => group.Select(link => link.ProjectId).Distinct().ToList());

            var byYear = placed
                .GroupBy(item => item.StartYear)
                .Select(group => new ArVrTrainingYearVm(
                    group.Key,
                    TrainingReach.TrainingYearLabel(group.Key),
                    group.Count(),
                    group.Sum(item => item.Course.Officers),
                    group.Sum(item => item.Course.Jcos),
                    group.Sum(item => item.Course.OtherRanks),
                    group.SelectMany(item => projectsOfCourse[item.Course.Id]).Distinct().Count()))
                .OrderBy(point => point.StartYear)
                .ToList();

            var board = byYear
                .OrderByDescending(point => point.StartYear)
                .Select(point => new AnalyticsBoardGroupVm(
                    point.StartYear.ToString(),
                    point.Label,
                    point.People,
                    placed
                        .Where(item => item.StartYear == point.StartYear)
                        .SelectMany(item => projectsOfCourse[item.Course.Id].Select(projectId => (ProjectId: projectId, item.Course)))
                        .GroupBy(item => item.ProjectId)
                        .Select(group => (ProjectId: group.Key, Courses: group.Count(), People: group.Sum(item => item.Course.People)))
                        .OrderByDescending(item => item.People)
                        .ThenBy(item => projects[item.ProjectId], StringComparer.OrdinalIgnoreCase)
                        .Select(item => new AnalyticsBoardProjectVm(
                            item.ProjectId,
                            projects[item.ProjectId],
                            $"{item.Courses} {Plural(item.Courses, "course")} · {item.People} {Plural(item.People, "person", "people")}"))
                        .ToList(),
                    $"{point.Courses} {Plural(point.Courses, "course")} · {point.Projects} {Plural(point.Projects, "project")}"))
                .ToList();

            var projectCount = placed.SelectMany(item => projectsOfCourse[item.Course.Id]).Distinct().Count();
            return (byYear, board, placed.Count, projectCount);
        }

        private static string Plural(int count, string one, string? many = null) => count == 1 ? one : many ?? one + "s";

        private sealed record TrainingRow(Guid Id, DateOnly? StartDate, DateOnly? EndDate, int? TrainingYear, int? TrainingMonth, int Officers, int Jcos, int OtherRanks)
        {
            public int People => Officers + Jcos + OtherRanks;
        }
        // END SECTION
    }
}
