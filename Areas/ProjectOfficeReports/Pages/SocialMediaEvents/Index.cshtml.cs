using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.SocialMediaEvents;

[Authorize]
public class IndexModel : PageModel
{
    private readonly SocialMediaEventService _eventService;
    private readonly ISocialMediaExportService _exportService;
    private readonly IAuthorizationService _authorizationService;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(
        SocialMediaEventService eventService,
        ISocialMediaExportService exportService,
        IAuthorizationService authorizationService,
        UserManager<ApplicationUser> userManager)
    {
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    }

    [BindProperty(SupportsGet = true)]
    public Guid? EventTypeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Platform { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool OnlyActiveEventTypes { get; set; }

    public IReadOnlyList<SocialMediaEventListItem> Items { get; private set; } = Array.Empty<SocialMediaEventListItem>();

    public bool CanManage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanManage = await EvaluateManagePolicyAsync();
        Items = await _eventService.SearchAsync(BuildQuery(), cancellationToken);
    }

    public async Task<IActionResult> OnPostExportAsync(CancellationToken cancellationToken)
    {
        CanManage = await EvaluateManagePolicyAsync();
        if (!CanManage)
        {
            return Forbid();
        }

        Items = await _eventService.SearchAsync(BuildQuery(), cancellationToken);

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var options = BuildQuery();
        var request = new SocialMediaExportRequest(
            options.EventTypeId,
            options.StartDate,
            options.EndDate,
            options.SearchQuery,
            options.Platform,
            options.OnlyActiveEventTypes,
            userId);

        var result = await _exportService.ExportAsync(request, cancellationToken);
        if (!result.Success || result.File is null)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            if (result.Errors.Count > 0)
            {
                TempData["ToastError"] = result.Errors[0];
            }

            return Page();
        }

        return File(result.File.Content, result.File.ContentType, result.File.FileName);
    }

    public async Task<IActionResult> OnPostExportPdfAsync(CancellationToken cancellationToken)
    {
        CanManage = await EvaluateManagePolicyAsync();
        if (!CanManage)
        {
            return Forbid();
        }

        Items = await _eventService.SearchAsync(BuildQuery(), cancellationToken);

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var options = BuildQuery();
        var request = new SocialMediaExportRequest(
            options.EventTypeId,
            options.StartDate,
            options.EndDate,
            options.SearchQuery,
            options.Platform,
            options.OnlyActiveEventTypes,
            userId);

        var result = await _exportService.ExportPdfAsync(request, cancellationToken);
        if (!result.Success || result.File is null)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            if (result.Errors.Count > 0)
            {
                TempData["ToastError"] = result.Errors[0];
            }

            return Page();
        }

        return File(result.File.Content, result.File.ContentType, result.File.FileName);
    }

    private SocialMediaEventQueryOptions BuildQuery()
    {
        return new SocialMediaEventQueryOptions(
            EventTypeId,
            ParseDate(From),
            ParseDate(To),
            Q,
            Platform,
            OnlyActiveEventTypes);
    }

    private async Task<bool> EvaluateManagePolicyAsync()
    {
        var result = await _authorizationService.AuthorizeAsync(
            User,
            null,
            ProjectOfficeReportsPolicies.ManageSocialMediaEvents);

        return result.Succeeded;
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
