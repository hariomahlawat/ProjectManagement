using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Models;

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

    public record ProjectSearchFilters(
        string? Query,
        int? CategoryId,
        string? LeadPoUserId,
        string? HodUserId,
        ProjectLifecycleFilter Lifecycle = ProjectLifecycleFilter.All,
        int? CompletedYear = null,
        ProjectTotStatus? TotStatus = null,
        bool IncludeArchived = false);

    public static class ProjectSearchQueryExtensions
    {
        public static IQueryable<Project> ApplyProjectSearch(this IQueryable<Project> source, ProjectSearchFilters filters)
        {
            if (filters is null)
            {
                throw new ArgumentNullException(nameof(filters));
            }

            if (!string.IsNullOrWhiteSpace(filters.Query))
            {
                var term = filters.Query.Trim();
                var like = $"%{term}%";
                var normalized = term.ToLowerInvariant();

                source = source.Where(p =>
                    EF.Functions.ILike(p.Name, like) ||
                    p.Name.ToLower().Contains(normalized) ||
                    (p.Description != null &&
                        (EF.Functions.ILike(p.Description!, like) || p.Description!.ToLower().Contains(normalized))) ||
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

            if (filters.CategoryId.HasValue)
            {
                source = source.Where(p => p.CategoryId == filters.CategoryId);
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
                source = source.Where(p => p.Tot != null && p.Tot.Status == status);
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

            return source;
        }

        public static IQueryable<Project> ApplyProjectOrdering(this IQueryable<Project> source, ProjectSearchFilters filters)
        {
            if (filters is null)
            {
                throw new ArgumentNullException(nameof(filters));
            }

            if (!string.IsNullOrWhiteSpace(filters.Query))
            {
                var term = filters.Query.Trim();
                var like = $"%{term}%";
                var normalized = term.ToLowerInvariant();

                return source
                    .OrderByDescending(p => p.CaseFileNumber != null && EF.Functions.ILike(p.CaseFileNumber!, term))
                    .ThenByDescending(p =>
                        (p.Name != null &&
                            (EF.Functions.ILike(p.Name, like) || p.Name.ToLower().Contains(normalized))) ||
                        (p.Description != null &&
                            (EF.Functions.ILike(p.Description!, like) || p.Description!.ToLower().Contains(normalized))))
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
                                (EF.Functions.ILike(p.LeadPoUser.UserName!, like) || p.LeadPoUser.UserName!.ToLower().Contains(normalized))))))
                    .ThenByDescending(p => p.CreatedAt);
            }

            return source.OrderByDescending(p => p.CreatedAt);
        }
    }
}
