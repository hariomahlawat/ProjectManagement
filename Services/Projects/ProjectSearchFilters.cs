using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;

namespace ProjectManagement.Services.Projects
{
    public enum ProjectLifecycleFilter
    {
        All = 0,
        Active = 1,
        Completed = 2,
        Cancelled = 3,
        Legacy = 4
    }

    /// <summary>
    /// Repository-wide ordering choices. Operational is the default and keeps
    /// the card view, table view and pagination on one authoritative sequence.
    /// </summary>
    public enum ProjectRepositorySort
    {
        Operational = 0,
        Project = 1,
        Status = 2,
        Officer = 3,
        Category = 4,
        CaseFile = 5
    }

    public enum ProjectSortDirection
    {
        Asc = 0,
        Desc = 1
    }

    public record ProjectSearchFilters(
        string? Query,
        int? CategoryId,
        int? TechnicalCategoryId = null,
        string? LeadPoUserId = null,
        string? HodUserId = null,
        ProjectLifecycleFilter Lifecycle = ProjectLifecycleFilter.All,
        int? CompletedYear = null,
        ProjectTotStatus? TotStatus = null,
        bool IncludeArchived = false,
        string? StageCode = null,
        DateOnly? StageCompletedMonth = null,
        string? SlipBucket = null,
        bool IncludeCategoryDescendants = false,
        IReadOnlyCollection<int>? CategoryIds = null);

    public static class ProjectSearchQueryExtensions
    {
        private static readonly Expression<Func<Project, int>> OperationalLifecycleRank = project =>
            project.LifecycleStatus == ProjectLifecycleStatus.Active
                ? 0
                : project.LifecycleStatus == ProjectLifecycleStatus.Completed && !project.IsLegacy
                    ? 1
                    : project.LifecycleStatus == ProjectLifecycleStatus.Completed
                        ? 2
                        : 3;

        public static IQueryable<Project> ApplyProjectSearch(this IQueryable<Project> source, ProjectSearchFilters filters)
        {
            if (filters is null)
            {
                throw new ArgumentNullException(nameof(filters));
            }

            source = source.Where(p => !p.IsDeleted);

            if (!string.IsNullOrWhiteSpace(filters.Query))
            {
                var term = filters.Query.Trim();
                var like = $"%{term}%";
                var normalized = term.ToLowerInvariant();

                source = source.Where(p =>
                    EF.Functions.ILike(p.Name, like) ||
                    p.Name.ToLower().Contains(normalized) ||
                    (p.ProjectBrief != null &&
                        (EF.Functions.ILike(p.ProjectBrief!, like) || p.ProjectBrief!.ToLower().Contains(normalized))) ||
                    (p.CaseFileNumber != null &&
                        (EF.Functions.ILike(p.CaseFileNumber!, like) || p.CaseFileNumber!.ToLower().Contains(normalized))) ||
                    (p.Category != null &&
                        (EF.Functions.ILike(p.Category.Name, like) || p.Category.Name.ToLower().Contains(normalized))) ||
                    (p.HodUser != null &&
                        ((p.HodUser.FullName != null &&
                            (EF.Functions.ILike(p.HodUser.FullName!, like) || p.HodUser.FullName!.ToLower().Contains(normalized))) ||
                         (p.HodUser.UserName != null &&
                            (EF.Functions.ILike(p.HodUser.UserName!, like) || p.HodUser.UserName!.ToLower().Contains(normalized))))) ||
                    (p.LeadPoUser != null &&
                        ((p.LeadPoUser.FullName != null &&
                            (EF.Functions.ILike(p.LeadPoUser.FullName!, like) || p.LeadPoUser.FullName!.ToLower().Contains(normalized))) ||
                         (p.LeadPoUser.UserName != null &&
                            (EF.Functions.ILike(p.LeadPoUser.UserName!, like) || p.LeadPoUser.UserName!.ToLower().Contains(normalized))))));
            }

            var resolvedCategoryIds = filters.CategoryIds;
            if (resolvedCategoryIds is not null && resolvedCategoryIds.Count > 0)
            {
                var ids = resolvedCategoryIds as int[] ?? resolvedCategoryIds.ToArray();
                source = source.Where(p => p.CategoryId.HasValue && ids.Contains(p.CategoryId.Value));
            }
            else if (filters.CategoryId.HasValue)
            {
                source = source.Where(p => p.CategoryId == filters.CategoryId);
            }

            if (filters.TechnicalCategoryId.HasValue)
            {
                source = source.Where(p => p.TechnicalCategoryId == filters.TechnicalCategoryId);
            }

            source = filters.Lifecycle switch
            {
                ProjectLifecycleFilter.Active => source.Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Active),
                ProjectLifecycleFilter.Completed => source.Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Completed),
                ProjectLifecycleFilter.Cancelled => source.Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Cancelled),
                ProjectLifecycleFilter.Legacy => source.Where(p => p.IsLegacy),
                _ => source
            };

            if (filters.CompletedYear.HasValue)
            {
                var year = filters.CompletedYear.Value;
                source = source.Where(p => p.CompletedYear.HasValue && p.CompletedYear == year);
            }

            if (!filters.IncludeArchived)
            {
                source = source.Where(p => !p.IsArchived);
            }

            if (filters.TotStatus.HasValue)
            {
                var status = filters.TotStatus.Value;
                source = source
                    .Where(ProjectTotApplicabilityPolicy.EligibleProjectPredicate)
                    .Where(p => p.Tot != null && p.Tot.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(filters.LeadPoUserId))
            {
                var leadId = filters.LeadPoUserId.Trim();
                source = source.Where(p => p.LeadPoUserId != null && p.LeadPoUserId == leadId);
            }

            if (!string.IsNullOrWhiteSpace(filters.HodUserId))
            {
                var hodId = filters.HodUserId.Trim();
                source = source.Where(p => p.HodUserId != null && p.HodUserId == hodId);
            }

            if (filters.StageCompletedMonth.HasValue && !string.IsNullOrWhiteSpace(filters.StageCode))
            {
                var stageCode = filters.StageCode.Trim();
                var monthStart = new DateOnly(filters.StageCompletedMonth.Value.Year, filters.StageCompletedMonth.Value.Month, 1);
                var nextMonth = monthStart.AddMonths(1);
                source = source.Where(p => p.ProjectStages.Any(s =>
                    s.StageCode == stageCode &&
                    s.CompletedOn.HasValue &&
                    s.CompletedOn.Value >= monthStart &&
                    s.CompletedOn.Value < nextMonth));
            }
            else if (!string.IsNullOrWhiteSpace(filters.StageCode))
            {
                var stageCode = filters.StageCode.Trim();
                source = source.Where(p => p.ProjectStages.Any(s =>
                    s.StageCode == stageCode &&
                    (s.Status == StageStatus.InProgress || s.Status == StageStatus.Completed)));
            }

            return source;
        }

        /// <summary>
        /// Applies one server-side order to the complete filtered dataset before
        /// pagination. This keeps cards, table rows and every page in the same
        /// deterministic sequence.
        /// </summary>
        public static IQueryable<Project> ApplyProjectOrdering(
            this IQueryable<Project> source,
            ProjectSearchFilters filters,
            ProjectRepositorySort sort = ProjectRepositorySort.Operational,
            ProjectSortDirection direction = ProjectSortDirection.Asc)
        {
            if (filters is null)
            {
                throw new ArgumentNullException(nameof(filters));
            }

            if (sort != ProjectRepositorySort.Operational)
            {
                return ApplyExplicitOrdering(source, sort, direction);
            }

            if (!string.IsNullOrWhiteSpace(filters.Query))
            {
                return ApplyOperationalTieBreakers(ApplySearchRelevanceOrdering(source, filters));
            }

            return ApplyOperationalOrdering(source);
        }

        private static IOrderedQueryable<Project> ApplySearchRelevanceOrdering(
            IQueryable<Project> source,
            ProjectSearchFilters filters)
        {
            var term = filters.Query!.Trim();
            var like = $"%{term}%";
            var normalized = term.ToLowerInvariant();

            return source
                .OrderByDescending(p => p.CaseFileNumber != null && EF.Functions.ILike(p.CaseFileNumber!, term))
                .ThenByDescending(p =>
                    (p.Name != null &&
                        (EF.Functions.ILike(p.Name, like) || p.Name.ToLower().Contains(normalized))) ||
                    (p.ProjectBrief != null &&
                        (EF.Functions.ILike(p.ProjectBrief!, like) || p.ProjectBrief!.ToLower().Contains(normalized))))
                .ThenByDescending(p =>
                    p.CaseFileNumber != null &&
                    (EF.Functions.ILike(p.CaseFileNumber!, like) || p.CaseFileNumber!.ToLower().Contains(normalized)))
                .ThenByDescending(p =>
                    (p.Category != null &&
                        (EF.Functions.ILike(p.Category.Name, like) || p.Category.Name.ToLower().Contains(normalized))) ||
                    (p.HodUser != null &&
                        ((p.HodUser.FullName != null &&
                            (EF.Functions.ILike(p.HodUser.FullName!, like) || p.HodUser.FullName!.ToLower().Contains(normalized))) ||
                         (p.HodUser.UserName != null &&
                            (EF.Functions.ILike(p.HodUser.UserName!, like) || p.HodUser.UserName!.ToLower().Contains(normalized))))) ||
                    (p.LeadPoUser != null &&
                        ((p.LeadPoUser.FullName != null &&
                            (EF.Functions.ILike(p.LeadPoUser.FullName!, like) || p.LeadPoUser.FullName!.ToLower().Contains(normalized))) ||
                         (p.LeadPoUser.UserName != null &&
                            (EF.Functions.ILike(p.LeadPoUser.UserName!, like) || p.LeadPoUser.UserName!.ToLower().Contains(normalized))))));
        }

        private static IOrderedQueryable<Project> ApplyOperationalOrdering(IQueryable<Project> source)
        {
            return ApplyOperationalDates(source.OrderBy(OperationalLifecycleRank));
        }

        private static IOrderedQueryable<Project> ApplyOperationalTieBreakers(IOrderedQueryable<Project> source)
        {
            return ApplyOperationalDates(source.ThenBy(OperationalLifecycleRank));
        }

        private static IOrderedQueryable<Project> ApplyOperationalDates(IOrderedQueryable<Project> source)
        {
            return source
                // Active projects: latest recorded remark/edit first; project
                // creation is the deterministic fallback for records without remarks.
                .ThenByDescending(project => project.LifecycleStatus == ProjectLifecycleStatus.Active
                    ? project.Remarks
                        .Where(remark => !remark.IsDeleted)
                        .Select(remark => (DateTime?)(remark.LastEditedAtUtc ?? remark.CreatedAtUtc))
                        .Max() ?? project.CreatedAt
                    : (DateTime?)null)
                .ThenByDescending(project => project.LifecycleStatus == ProjectLifecycleStatus.Active
                    ? project.ContentUpdatedAtUtc
                    : null)
                .ThenByDescending(project => project.LifecycleStatus == ProjectLifecycleStatus.Active
                    ? project.ProjectStages.Select(stage => stage.CompletedOn).Max()
                    : null)
                .ThenByDescending(project => project.LifecycleStatus == ProjectLifecycleStatus.Active
                    ? project.ProjectStages.Select(stage => stage.ActualStart).Max()
                    : null)

                // Completed projects: authoritative completion precision first.
                .ThenByDescending(project => project.LifecycleStatus == ProjectLifecycleStatus.Completed
                    ? project.CompletedYear
                    : null)
                .ThenByDescending(project => project.LifecycleStatus == ProjectLifecycleStatus.Completed
                    ? project.CompletedMonth
                    : null)
                .ThenByDescending(project => project.LifecycleStatus == ProjectLifecycleStatus.Completed
                    ? project.CompletedOn
                    : null)
                .ThenByDescending(project => project.LifecycleStatus == ProjectLifecycleStatus.Completed
                    ? (DateTime?)project.CreatedAt
                    : null)

                // Cancelled projects: latest cancellation first.
                .ThenByDescending(project => project.LifecycleStatus == ProjectLifecycleStatus.Cancelled
                    ? project.CancelledOn
                    : null)
                .ThenByDescending(project => project.LifecycleStatus == ProjectLifecycleStatus.Cancelled
                    ? (DateTime?)project.CreatedAt
                    : null)

                // Stable cross-provider tie-breakers are essential before paging.
                .ThenBy(project => project.Name)
                .ThenBy(project => project.Id);
        }

        private static IOrderedQueryable<Project> ApplyExplicitOrdering(
            IQueryable<Project> source,
            ProjectRepositorySort sort,
            ProjectSortDirection direction)
        {
            return sort switch
            {
                ProjectRepositorySort.Project => direction == ProjectSortDirection.Desc
                    ? source.OrderByDescending(project => project.Name).ThenByDescending(project => project.Id)
                    : source.OrderBy(project => project.Name).ThenBy(project => project.Id),

                ProjectRepositorySort.Status => direction == ProjectSortDirection.Desc
                    ? source.OrderByDescending(OperationalLifecycleRank).ThenByDescending(project => project.Name).ThenByDescending(project => project.Id)
                    : source.OrderBy(OperationalLifecycleRank).ThenBy(project => project.Name).ThenBy(project => project.Id),

                ProjectRepositorySort.Officer => ApplyOfficerOrdering(source, direction),
                ProjectRepositorySort.Category => ApplyCategoryOrdering(source, direction),
                ProjectRepositorySort.CaseFile => ApplyCaseFileOrdering(source, direction),
                _ => ApplyOperationalOrdering(source)
            };
        }

        private static IOrderedQueryable<Project> ApplyOfficerOrdering(
            IQueryable<Project> source,
            ProjectSortDirection direction)
        {
            var assignedFirst = source.OrderBy(project => project.LeadPoUserId == null);

            return direction == ProjectSortDirection.Desc
                ? assignedFirst
                    .ThenByDescending(project => project.LeadPoUser != null
                        ? project.LeadPoUser.FullName ?? project.LeadPoUser.UserName ?? string.Empty
                        : string.Empty)
                    .ThenByDescending(project => project.Name)
                    .ThenByDescending(project => project.Id)
                : assignedFirst
                    .ThenBy(project => project.LeadPoUser != null
                        ? project.LeadPoUser.FullName ?? project.LeadPoUser.UserName ?? string.Empty
                        : string.Empty)
                    .ThenBy(project => project.Name)
                    .ThenBy(project => project.Id);
        }

        private static IOrderedQueryable<Project> ApplyCategoryOrdering(
            IQueryable<Project> source,
            ProjectSortDirection direction)
        {
            var categorisedFirst = source.OrderBy(project => project.CategoryId == null);

            return direction == ProjectSortDirection.Desc
                ? categorisedFirst
                    .ThenByDescending(project => project.Category != null ? project.Category.Name : string.Empty)
                    .ThenByDescending(project => project.Name)
                    .ThenByDescending(project => project.Id)
                : categorisedFirst
                    .ThenBy(project => project.Category != null ? project.Category.Name : string.Empty)
                    .ThenBy(project => project.Name)
                    .ThenBy(project => project.Id);
        }

        private static IOrderedQueryable<Project> ApplyCaseFileOrdering(
            IQueryable<Project> source,
            ProjectSortDirection direction)
        {
            var recordedFirst = source.OrderBy(project => project.CaseFileNumber == null || project.CaseFileNumber == string.Empty);

            return direction == ProjectSortDirection.Desc
                ? recordedFirst
                    .ThenByDescending(project => project.CaseFileNumber)
                    .ThenByDescending(project => project.Name)
                    .ThenByDescending(project => project.Id)
                : recordedFirst
                    .ThenBy(project => project.CaseFileNumber)
                    .ThenBy(project => project.Name)
                    .ThenBy(project => project.Id);
        }
    }
}
