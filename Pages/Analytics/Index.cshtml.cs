using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Pages.Analytics
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public IndexModel(ApplicationDbContext db)
        {
            _db = db;
        }

        private static readonly ProjectLifecycleFilter[] LifecycleFilters =
        {
            ProjectLifecycleFilter.Active,
            ProjectLifecycleFilter.Completed,
            ProjectLifecycleFilter.Cancelled,
            ProjectLifecycleFilter.All
        };

        public AnalyticsTab ActiveTab { get; private set; } = AnalyticsTab.Completed;

        public IReadOnlyList<CategoryOption> Categories { get; private set; } = Array.Empty<CategoryOption>();
        public IReadOnlyList<TechnicalCategoryOption> TechnicalCategories { get; private set; } = Array.Empty<TechnicalCategoryOption>();

        public int CompletedCount { get; private set; }
        public int OngoingCount { get; private set; } = 14;
        public int CoeCount { get; private set; } = 12;

        public CompletedAnalyticsVm? Completed { get; private set; }
        public OngoingAnalyticsVm? Ongoing { get; private set; }
        public CoeAnalyticsVm? Coe { get; private set; }

        public ProjectLifecycleFilter DefaultLifecycle => ProjectLifecycleFilter.Active;

        public int LifecycleViewCount => LifecycleFilters.Length;

        public int CategoryCount => Categories.Count;

        public int TechnicalGroupCount => TechnicalCategories.Count;

        public async Task OnGetAsync(string? tab)
        {
            ActiveTab = tab?.ToLowerInvariant() switch
            {
                "ongoing" => AnalyticsTab.Ongoing,
                "coe" => AnalyticsTab.Coe,
                _ => AnalyticsTab.Completed
            };

            await LoadAnalyticsAsync();

            // SECTION: Active tab hydration
            switch (ActiveTab)
            {
                case AnalyticsTab.Completed:
                    Completed = await BuildCompletedAnalyticsAsync();
                    CompletedCount = Completed.TotalCompletedProjects;
                    break;

                case AnalyticsTab.Ongoing:
                    Ongoing = new OngoingAnalyticsVm
                    {
                        TotalOngoingProjects = OngoingCount
                    };
                    break;

                case AnalyticsTab.Coe:
                    Coe = new CoeAnalyticsVm
                    {
                        TotalCoeProjects = CoeCount
                    };
                    break;
            }
            // END SECTION
        }

        private async Task LoadAnalyticsAsync()
        {
            Categories = await _db.ProjectCategories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CategoryOption(c.Id, c.Name))
                .ToListAsync();

            TechnicalCategories = await _db.TechnicalCategories
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .Select(c => new TechnicalCategoryOption(c.Id, c.Name))
                .ToListAsync();

            if (ActiveTab != AnalyticsTab.Completed)
            {
                CompletedCount = await _db.Projects
                    .AsNoTracking()
                    .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Completed)
                    .CountAsync();
            }
        }

        private async Task<CompletedAnalyticsVm> BuildCompletedAnalyticsAsync()
        {
            // SECTION: Completed analytics aggregation
            var completedQuery = _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Completed);

            var byCategory = await completedQuery
                .GroupBy(p => p.Category != null ? p.Category.Name : "Uncategorized")
                .Select(g => new CompletedByCategoryPoint(g.Key, g.Count()))
                .OrderByDescending(x => x.Count)
                .ToListAsync();

            var byTechnical = await completedQuery
                .GroupBy(p => p.TechnicalCategory != null ? p.TechnicalCategory.Name : "Uncategorized")
                .Select(g => new CompletedByTechnicalPoint(g.Key, g.Count()))
                .OrderByDescending(x => x.Count)
                .ToListAsync();

            var perYear = await completedQuery
                .Select(p => p.CompletedYear ?? (p.CompletedOn.HasValue ? p.CompletedOn.Value.Year : (int?)null))
                .Where(year => year.HasValue)
                .GroupBy(year => year!.Value)
                .Select(g => new CompletedPerYearPoint(g.Key, g.Count()))
                .OrderBy(x => x.Year)
                .ToListAsync();

            return new CompletedAnalyticsVm
            {
                ByCategory = byCategory,
                ByTechnical = byTechnical,
                PerYear = perYear,
                TotalCompletedProjects = await completedQuery.CountAsync()
            };
            // END SECTION
        }

        public sealed record CategoryOption(int Id, string Name);
        public sealed record TechnicalCategoryOption(int Id, string Name);
    }
}
