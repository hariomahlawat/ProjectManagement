// -----------------------------------------------------------------------------
// Areas/ProjectOfficeReports/Pages/Training/Records.cshtml.cs
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Training;

// The full records list is now the training register itself (Index). Old links and bookmarks
// land on the register with their filters and page kept.
[Authorize(Policy = ProjectOfficeReportsPolicies.ViewTrainingTracker)]
public class RecordsModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public FilterInput Filter { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public IActionResult OnGet()
    {
        var values = new Dictionary<string, string?>
        {
            ["TypeId"] = Filter.TypeId is { } typeId && typeId != Guid.Empty ? typeId.ToString() : null,
            ["ProjectTechnicalCategoryId"] = Filter.ProjectTechnicalCategoryId?.ToString(CultureInfo.InvariantCulture),
            ["ProjectId"] = Filter.ProjectId?.ToString(CultureInfo.InvariantCulture),
            ["Category"] = Filter.Category?.ToString(),
            ["From"] = Filter.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["To"] = Filter.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Search"] = string.IsNullOrWhiteSpace(Filter.Search) ? null : Filter.Search.Trim(),
            ["PageNumber"] = PageNumber > 1 ? PageNumber.ToString(CultureInfo.InvariantCulture) : null
        };

        return RedirectToPage("./Index", values
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => (object?)pair.Value));
    }

    public sealed class FilterInput
    {
        public Guid? TypeId { get; set; }

        public int? ProjectId { get; set; }

        public int? ProjectTechnicalCategoryId { get; set; }

        public TrainingCategory? Category { get; set; }

        public DateOnly? From { get; set; }

        public DateOnly? To { get; set; }

        public string? Search { get; set; }
    }
}
