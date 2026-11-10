using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Configuration;
using ProjectManagement.Infrastructure.Ui;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Services;
using ProjectManagement.Services.ItProjects;
using ProjectManagement.Utilities;

namespace ProjectManagement.Pages.ItProjects;

[Authorize(Policy = Policies.ItProjects.Manage)]
public class CreateModel : PageModel
{
    /// <summary>How many past financial years the picker offers, besides the current and the next one.</summary>
    public const int PastFinancialYearsOffered = 10;

    private readonly IItProjectCommandService _commands;
    private readonly IClock _clock;

    public CreateModel(IItProjectCommandService commands, IClock clock)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<SelectListItem> FinancialYearOptions { get; private set; } = Array.Empty<SelectListItem>();

    /// <summary>The years a carried-over case may have been first listed in: the ten before the current one.</summary>
    public IReadOnlyList<SelectListItem> FirstYearOptions { get; private set; } = Array.Empty<SelectListItem>();

    /// <summary>The financial year running now (IST), the year a carried-over case is CL or CF in.</summary>
    public int CurrentFinancialYear { get; private set; }

    public const string KindNew = "New";
    public const string KindCommittedLiability = "CL";
    public const string KindCarriedForward = "CF";

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Give the project a title.")]
        [StringLength(ItProjectCommandService.TitleMaxLength, ErrorMessage = "The title must be {1} characters or fewer.")]
        [Display(Name = "Title")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Give the IT ARPPP reference number.")]
        [StringLength(ItProjectCommandService.ReferenceMaxLength, ErrorMessage = "The IT ARPPP reference number must be {1} characters or fewer.")]
        [Display(Name = "IT ARPPP reference number")]
        public string PppReferenceNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Choose the financial year.")]
        [Display(Name = "Financial year")]
        public int? FinancialYearStart { get; set; }

        [Required(ErrorMessage = "Give the project a description.")]
        [StringLength(ItProjectCommandService.DescriptionMaxLength, ErrorMessage = "The description must be {1} characters or fewer.")]
        [Display(Name = "Description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// New (a case in its own financial year), or CL / CF (already in this year's IT ARPPP,
        /// carried over from an earlier year). For CL / CF, <see cref="PppReferenceNumber"/> is
        /// this year's number.
        /// </summary>
        public string Kind { get; set; } = KindNew;

        /// <summary>CL / CF only: the year the case was first listed in.</summary>
        [Display(Name = "First listed in")]
        public int? FirstFinancialYearStart { get; set; }

        /// <summary>CL / CF only: the first year's IT ARPPP number, if it is known.</summary>
        [StringLength(ItProjectCommandService.ReferenceMaxLength, ErrorMessage = "The IT ARPPP reference number must be {1} characters or fewer.")]
        [Display(Name = "First IT ARPPP reference number")]
        public string? FirstPppReferenceNumber { get; set; }
    }

    /// <summary>The year type a CL / CF choice stands for, or null for New (or anything unknown).</summary>
    public static ItProjectYearType? CarriedOverType(string? kind) => (kind ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "CL" => ItProjectYearType.CommittedLiability,
        "CF" => ItProjectYearType.CarriedForward,
        _ => null
    };

    public bool IsCarriedOver => CarriedOverType(Input.Kind) is not null;

    public void OnGet()
    {
        Input.FinancialYearStart = CurrentFinancialYearStart();
        FinancialYearOptions = BuildFinancialYearOptions(Input.FinancialYearStart.Value, Input.FinancialYearStart.Value);
        BuildFirstYearOptions(null);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var current = CurrentFinancialYearStart();
        var carriedOver = CarriedOverType(Input.Kind);
        var yearKey = $"{nameof(Input)}.{nameof(Input.FinancialYearStart)}";
        var yearIsValid = Input.FinancialYearStart is { } posted && IsSupportedFinancialYear(posted);
        if (carriedOver is null && Input.FinancialYearStart.HasValue && !yearIsValid)
        {
            ModelState.AddModelError(yearKey, "Choose the financial year.");
        }

        if (carriedOver is not null)
        {
            // The New case's year picker is not used for a carried-over case.
            ModelState.Remove(yearKey);
            if (Input.FirstFinancialYearStart is not { } first || !IsSupportedFinancialYear(first) || first >= current)
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.FirstFinancialYearStart)}", "Choose the year the case was first listed in.");
            }
        }
        else
        {
            Input.Kind = KindNew;
        }

        // The picker must never be built from an unsupported year: the helper that formats it throws.
        FinancialYearOptions = BuildFinancialYearOptions(yearIsValid ? Input.FinancialYearStart!.Value : current, current);
        BuildFirstYearOptions(Input.FirstFinancialYearStart);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Forbid();
        }

        var actor = new ItProjectActor(userId, User.Identity?.Name);
        var (result, id) = carriedOver is { } type
            ? await _commands.CreateCarriedOverAsync(
                new ItProjectCarriedOverInput(Input.Title, Input.Description, type, Input.PppReferenceNumber, Input.FirstFinancialYearStart!.Value, Input.FirstPppReferenceNumber),
                actor,
                cancellationToken)
            : await _commands.CreateAsync(
                new ItProjectDetailsInput(Input.Title, Input.PppReferenceNumber, Input.FinancialYearStart!.Value, Input.Description),
                actor,
                cancellationToken);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return Page();
        }

        TempData.ToastSuccess(carriedOver is { } created
            ? $"IT project \"{Input.Title.Trim()}\" created as {ItProjectYearTypes.Code(created)} in FY {FinancialYearHelper.Format(current)}. Tick the stages it has already reached."
            : $"IT project \"{Input.Title.Trim()}\" created. Tick the stages it has already reached.");
        return RedirectToPage("./Details", new { id, open = DetailsModel.OpenStages });
    }

    private int CurrentFinancialYearStart()
    {
        CurrentFinancialYear = ItProjectYearTypes.CurrentFinancialYear(_clock.UtcNow);
        return CurrentFinancialYear;
    }

    /// <summary>The ten years before the current one, newest first; last year is chosen by default.</summary>
    private void BuildFirstYearOptions(int? selected)
    {
        var current = CurrentFinancialYearStart();
        var chosen = selected is { } year && year < current && IsSupportedFinancialYear(year) ? year : current - 1;
        var years = Enumerable.Range(current - PastFinancialYearsOffered, PastFinancialYearsOffered)
            .Where(IsSupportedFinancialYear)
            .ToList();
        if (IsSupportedFinancialYear(chosen) && !years.Contains(chosen))
        {
            years.Add(chosen);
        }

        FirstYearOptions = years
            .OrderByDescending(year => year)
            .Select(year => new SelectListItem(FinancialYearHelper.Format(year), year.ToString(), year == chosen))
            .ToList();
    }

    internal static bool IsSupportedFinancialYear(int startYear)
        => startYear is >= FinancialYearHelper.MinimumSupportedStartYear and <= FinancialYearHelper.MaximumSupportedStartYear;

    /// <summary>Options around <paramref name="selected"/> itself; see the overload taking the current year.</summary>
    internal static IReadOnlyList<SelectListItem> BuildFinancialYearOptions(int selected)
        => BuildFinancialYearOptions(selected, selected);

    /// <summary>
    /// Next FY, the current one, then the past ten, counted from the current year (not from the
    /// project's year, so editing an old project can still move it to this year in one step),
    /// all within the helper's supported range; the chosen year is always offered. The caller
    /// passes a supported selected year.
    /// </summary>
    internal static IReadOnlyList<SelectListItem> BuildFinancialYearOptions(int selected, int currentFinancialYearStart)
    {
        if (!IsSupportedFinancialYear(selected))
        {
            throw new ArgumentOutOfRangeException(nameof(selected), selected, "Unsupported financial year.");
        }

        var anchor = IsSupportedFinancialYear(currentFinancialYearStart) ? currentFinancialYearStart : selected;
        var years = Enumerable.Range(anchor - PastFinancialYearsOffered, PastFinancialYearsOffered + 2)
            .Reverse()
            .Where(IsSupportedFinancialYear)
            .ToList();
        if (!years.Contains(selected))
        {
            years.Add(selected);
            years.Sort((a, b) => b.CompareTo(a));
        }

        return years
            .Select(year => new SelectListItem(FinancialYearHelper.Format(year), year.ToString(), year == selected))
            .ToList();
    }
}
