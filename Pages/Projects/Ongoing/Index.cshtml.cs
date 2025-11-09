using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Pages.Projects.Ongoing
{
    [Authorize]
    public sealed class IndexModel : PageModel
    {
        private readonly OngoingProjectsReadService _ongoingService;
        private readonly IOngoingProjectsExcelBuilder _excelBuilder;
        private readonly IClock _clock;
        private readonly ApplicationDbContext _db;

        public IndexModel(
            OngoingProjectsReadService ongoingService,
            IOngoingProjectsExcelBuilder excelBuilder,
            IClock clock,
            ApplicationDbContext db)
        {
            _ongoingService = ongoingService ?? throw new ArgumentNullException(nameof(ongoingService));
            _excelBuilder = excelBuilder ?? throw new ArgumentNullException(nameof(excelBuilder));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        [BindProperty(SupportsGet = true)]
        public int? ProjectCategoryId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        [BindProperty(SupportsGet = true)]
        public string ViewMode { get; set; } = "list";

        public IReadOnlyList<SelectListItem> ProjectCategoryOptions { get; private set; }
            = Array.Empty<SelectListItem>();

        public IReadOnlyList<OngoingProjectRowDto> Items { get; private set; }
            = Array.Empty<OngoingProjectRowDto>();

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            await LoadCategoriesAsync(cancellationToken);

            Items = await _ongoingService.GetAsync(
                ProjectCategoryId,
                Normalize(Search),
                cancellationToken);
        }

        public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
        {
            var items = await _ongoingService.GetAsync(
                ProjectCategoryId,
                Normalize(Search),
                cancellationToken);

            var now = _clock.UtcNow; // DateTimeOffset

            var csv = _excelBuilder.Build(
                new OngoingProjectsExportContext(
                    items,
                    now,
                    ProjectCategoryId,
                    Normalize(Search)));

            var fileName = $"ongoing-projects-{now:yyyyMMddHHmmss}.csv";
            return File(csv, "text/csv", fileName);
        }

        private async Task LoadCategoriesAsync(CancellationToken ct)
        {
            var cats = await _db.ProjectCategories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync(ct);

            var list = new List<SelectListItem>
            {
                new SelectListItem("All categories", string.Empty)
            };

            foreach (var c in cats)
            {
                list.Add(new SelectListItem(c.Name, c.Id.ToString(), c.Id == ProjectCategoryId));
            }

            ProjectCategoryOptions = list;
        }

        private static string? Normalize(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
