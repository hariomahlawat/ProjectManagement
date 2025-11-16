using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
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

        public AnalyticsTab ActiveTab { get; private set; } = AnalyticsTab.Completed;

        public IReadOnlyList<CategoryOption> Categories { get; private set; } = Array.Empty<CategoryOption>();
        public IReadOnlyList<TechnicalCategoryOption> TechnicalCategories { get; private set; } = Array.Empty<TechnicalCategoryOption>();

        public int CompletedCount { get; private set; } = 47;
        public int OngoingCount { get; private set; } = 14;
        public int CoeCount { get; private set; } = 12;

        public CompletedAnalyticsVm? Completed { get; private set; }
        public OngoingAnalyticsVm? Ongoing { get; private set; }
        public CoeAnalyticsVm? Coe { get; private set; }

        public ProjectLifecycleFilter DefaultLifecycle => ProjectLifecycleFilter.Active;

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
                    Completed = new CompletedAnalyticsVm
                    {
                        TotalCompletedProjects = CompletedCount
                    };
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
        }

        public sealed record CategoryOption(int Id, string Name);
        public sealed record TechnicalCategoryOption(int Id, string Name);
    }
}
