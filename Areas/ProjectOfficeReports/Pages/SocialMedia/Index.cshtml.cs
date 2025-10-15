using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.SocialMedia.ViewModels;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.SocialMedia;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly SocialMediaEventService _eventService;
    private readonly ISocialMediaExportService _exportService;
    private readonly SocialMediaPlatformService _platformService;
    private readonly IAuthorizationService _authorizationService;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(
        SocialMediaEventService eventService,
        ISocialMediaExportService exportService,
        SocialMediaPlatformService platformService,
        IAuthorizationService authorizationService,
        UserManager<ApplicationUser> userManager)
    {
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
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
    public Guid? PlatformId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool OnlyActiveEventTypes { get; set; }

    public IReadOnlyList<SocialMediaEventListItem> Events { get; private set; } = Array.Empty<SocialMediaEventListItem>();

    public SocialMediaEventListFilter Filter { get; private set; } = new();

    public bool CanManage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await PopulateAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostExportAsync(CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();
        if (!CanManage)
        {
            return Forbid();
        }

        await PopulateFilterAsync(cancellationToken);
        Events = await _eventService.SearchAsync(BuildQuery(), cancellationToken);

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var request = BuildExportRequest(userId);
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
        await PopulatePermissionsAsync();
        if (!CanManage)
        {
            return Forbid();
        }

        await PopulateFilterAsync(cancellationToken);
        Events = await _eventService.SearchAsync(BuildQuery(), cancellationToken);

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var request = BuildExportRequest(userId);
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

    private async Task PopulateAsync(CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();
        await PopulateFilterAsync(cancellationToken);
        Events = await _eventService.SearchAsync(BuildQuery(), cancellationToken);
    }

    private async Task PopulatePermissionsAsync()
    {
        var authorizationResult = await _authorizationService.AuthorizeAsync(
            User,
            null,
            ProjectOfficeReportsPolicies.ManageSocialMediaEvents);

        CanManage = authorizationResult.Succeeded;
    }

    private async Task PopulateFilterAsync(CancellationToken cancellationToken)
    {
        var eventTypes = await _eventService.GetEventTypesAsync(includeInactive: true, cancellationToken);
        var options = new List<SelectListItem>
        {
            new("All event types", string.Empty)
        };

        foreach (var type in eventTypes)
        {
            options.Add(new SelectListItem(type.Name, type.Id.ToString())
            {
                Selected = EventTypeId.HasValue && EventTypeId.Value == type.Id
            });
        }

        var platforms = await _platformService.GetAllAsync(includeInactive: true, cancellationToken);
        var platformOptions = new List<SelectListItem>
        {
            new("All platforms", string.Empty)
        };

        foreach (var platform in platforms)
        {
            platformOptions.Add(new SelectListItem(platform.Name, platform.Id.ToString())
            {
                Selected = PlatformId.HasValue && PlatformId.Value == platform.Id
            });
        }

        var selectedPlatform = platformOptions.FirstOrDefault(option => option.Selected);

        Filter = new SocialMediaEventListFilter
        {
            EventTypeId = EventTypeId,
            StartDate = ParseDate(From),
            EndDate = ParseDate(To),
            SearchQuery = Q,
            PlatformId = PlatformId,
            PlatformName = selectedPlatform?.Text,
            OnlyActiveEventTypes = OnlyActiveEventTypes,
            EventTypeOptions = options,
            PlatformOptions = platformOptions
        };
    }

    private SocialMediaEventQueryOptions BuildQuery()
    {
        return new SocialMediaEventQueryOptions(
            EventTypeId,
            ParseDate(From),
            ParseDate(To),
            Q,
            PlatformId,
            OnlyActiveEventTypes);
    }

    private SocialMediaExportRequest BuildExportRequest(string userId)
    {
        var options = BuildQuery();
        return new SocialMediaExportRequest(
            options.EventTypeId,
            options.StartDate,
            options.EndDate,
            options.SearchQuery,
            options.PlatformId,
            Filter.PlatformName,
            options.OnlyActiveEventTypes,
            userId);
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
