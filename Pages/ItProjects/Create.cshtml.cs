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

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Give the project a title.")]
        [StringLength(ItProjectCommandService.TitleMaxLength, ErrorMessage = "The title must be {1} characters or fewer.")]
        [Display(Name = "Title")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Give the IT PPP reference number.")]
        [StringLength(ItProjectCommandService.ReferenceMaxLength, ErrorMessage = "The IT PPP reference number must be {1} characters or fewer.")]
        [Display(Name = "IT PPP reference number")]
        public string PppReferenceNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Choose the financial year.")]
        [Display(Name = "Financial year")]
        public int? FinancialYearStart { get; set; }

        [Required(ErrorMessage = "Give the project a description.")]
        [Display(Name = "Description")]
        public string Description { get; set; } = string.Empty;
    }

    public void OnGet()
    {
        Input.FinancialYearStart = CurrentFinancialYearStart();
        FinancialYearOptions = BuildFinancialYearOptions(Input.FinancialYearStart.Value);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        FinancialYearOptions = BuildFinancialYearOptions(Input.FinancialYearStart ?? CurrentFinancialYearStart());
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Forbid();
        }

        var (result, id) = await _commands.CreateAsync(
            new ItProjectDetailsInput(Input.Title, Input.PppReferenceNumber, Input.FinancialYearStart!.Value, Input.Description),
            new ItProjectActor(userId, User.Identity?.Name),
            cancellationToken);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return Page();
        }

        TempData.ToastSuccess($"IT project \"{Input.Title.Trim()}\" created. It starts at IT PPP / IPA.");
        return RedirectToPage("./Index", new { scope = "active" });
    }

    private int CurrentFinancialYearStart()
        => FinancialYearHelper.GetStartYear(DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime));

    /// <summary>Next FY, the current one, then the past ten; the chosen year is always offered.</summary>
    internal static IReadOnlyList<SelectListItem> BuildFinancialYearOptions(int selected)
    {
        var current = selected;
        var years = Enumerable.Range(current - PastFinancialYearsOffered, PastFinancialYearsOffered + 2)
            .Reverse()
            .Where(year => year >= FinancialYearHelper.MinimumSupportedStartYear)
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
