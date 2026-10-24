// -----------------------------------------------------------------------------
// Areas/ProjectOfficeReports/Pages/Training/View.cshtml.cs
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Application.Training.Dtos;
using ProjectManagement.Configuration;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Training;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewTrainingTracker)]
public sealed class ViewModel : PageModel
{
    // -------------------------------------------------------------------------
    // dependencies
    // -------------------------------------------------------------------------
    private readonly IOptionsSnapshot<TrainingTrackerOptions> _options;
    private readonly TrainingTrackerReadService _readService;
    private readonly IAuthorizationService _authorizationService;

    public ViewModel(
        IOptionsSnapshot<TrainingTrackerOptions> options,
        TrainingTrackerReadService readService,
        IAuthorizationService authorizationService)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
    }

    // -------------------------------------------------------------------------
    // view-facing properties
    // -------------------------------------------------------------------------
    public TrainingDetailsVm Training { get; private set; } = null!;

    public bool IsFeatureEnabled { get; private set; }

    public bool CanManageTrainingTracker { get; private set; }

    // -------------------------------------------------------------------------
    // header and record panel text (formatting only; the facts come from the read service)
    // -------------------------------------------------------------------------

    // The record's title is what it trained on: its project, or the type when none is linked.
    public string Title => Training.Projects.Count switch
    {
        0 => $"{Training.TrainingTypeName} training",
        1 => Training.Projects[0].Name,
        _ => $"{Training.Projects[0].Name} and {Training.Projects.Count - 1} more"
    };

    public string PeriodText
    {
        get
        {
            var inv = CultureInfo.InvariantCulture;
            if ((Training.PeriodStart ?? Training.PeriodEnd) is { } start)
            {
                var end = Training.PeriodEnd ?? start;
                if (end <= start)
                {
                    return start.ToString("dddd, d MMMM yyyy", inv);
                }

                if (start.Year == end.Year && start.Month == end.Month)
                {
                    return $"{start.Day}–{end.ToString("d MMMM yyyy", inv)}";
                }

                return start.Year == end.Year
                    ? $"{start.ToString("d MMM", inv)} – {end.ToString("d MMM yyyy", inv)}"
                    : $"{start.ToString("d MMM yyyy", inv)} – {end.ToString("d MMM yyyy", inv)}";
            }

            if (Training.TrainingYear is { } year && Training.TrainingMonth is { } month && month is >= 1 and <= 12)
            {
                return $"{new DateOnly(year, month, 1).ToString("MMMM yyyy", inv)} (month only)";
            }

            return "Date not set";
        }
    }

    public string? DurationText => Training.DurationDays switch
    {
        null => null,
        1 => "1 day",
        var days => $"{days} days"
    };

    public IReadOnlyList<string> Units => Training.Roster
        .Select(row => row.UnitName)
        .Where(unit => !string.IsNullOrWhiteSpace(unit))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    // Times in India Standard Time, as on every other PRISM record page.
    public static string Stamp(DateTimeOffset utc)
        => IstClock.ToIst(utc).ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " IST";

    // -------------------------------------------------------------------------
    // GET
    // -------------------------------------------------------------------------
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        IsFeatureEnabled = _options.Value.Enabled;

        var training = await _readService.GetDetailsAsync(id, cancellationToken);
        if (training is null)
        {
            return NotFound();
        }

        Training = training;
        CanManageTrainingTracker = (await _authorizationService.AuthorizeAsync(User, ProjectOfficeReportsPolicies.ManageTrainingTracker)).Succeeded;

        return Page();
    }
}
