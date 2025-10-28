using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Models;
using ProjectManagement.Services.Activities;
using ProjectManagement.ViewModels.Activities;

namespace ProjectManagement.Pages.Activities;

[Authorize]
public sealed class IndexModel : PageModel
{
    private static readonly IReadOnlyList<int> AllowedPageSizes = new[] { 10, 25, 50, 100 };

    private readonly IActivityService _activityService;
    private readonly IActivityTypeService _activityTypeService;
    private readonly IActivityExportService _activityExportService;
    private readonly IActivityDeleteRequestService _deleteRequestService;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(IActivityService activityService,
                      IActivityTypeService activityTypeService,
                      IActivityExportService activityExportService,
                      IActivityDeleteRequestService deleteRequestService,
                      UserManager<ApplicationUser> userManager)
    {
        _activityService = activityService;
        _activityTypeService = activityTypeService;
        _activityExportService = activityExportService;
        _deleteRequestService = deleteRequestService;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)]
    public new int Page { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    [BindProperty(SupportsGet = true)]
    public ActivityListSort SortBy { get; set; } = ActivityListSort.ScheduledStart;

    [BindProperty(SupportsGet = true)]
    public string? SortDir { get; set; } = "desc";

    [BindProperty(SupportsGet = true)]
    public DateOnly? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? ActivityTypeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public ActivityAttachmentTypeFilter AttachmentType { get; set; } = ActivityAttachmentTypeFilter.Any;

    public ActivityListViewModel? ViewModel { get; private set; }

    public IReadOnlyList<SelectListItem> ActivityTypeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> AttachmentTypeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> PageSizeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public bool CanCreateActivities { get; private set; }

    public bool CanExportActivities { get; private set; }

    public bool IsDeleteApprover { get; private set; }

    public bool IsSortDescending => string.IsNullOrWhiteSpace(SortDir) || !string.Equals(SortDir, "asc", StringComparison.OrdinalIgnoreCase);

    public string SortDirection => IsSortDescending ? "desc" : "asc";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Page = Page <= 0 ? 1 : Page;
        PageSize = NormalizePageSize(PageSize);

        var request = new ActivityListRequest(Page,
            PageSize,
            SortBy,
            IsSortDescending,
            FromDate,
            ToDate,
            ActivityTypeId,
            CreatedByUserId: null,
            AttachmentType);

        var result = await _activityService.ListAsync(request, cancellationToken);

        var currentUserId = _userManager.GetUserId(User) ?? string.Empty;
        var isManager = IsManager(User);
        var isApprover = IsApprover(User);

        IsDeleteApprover = isApprover;

        var rows = result.Items.Select(item =>
        {
            var displayName = string.IsNullOrWhiteSpace(item.CreatedByDisplayName)
                ? (string.IsNullOrWhiteSpace(item.CreatedByEmail) ? item.CreatedByUserId : item.CreatedByEmail)
                : item.CreatedByDisplayName;

            var canManage = isManager || string.Equals(item.CreatedByUserId, currentUserId, StringComparison.OrdinalIgnoreCase);
            var canRequestDelete = isManager;

            return new ActivityListRowViewModel(
                item.Id,
                item.Title,
                item.ActivityTypeName,
                item.Location,
                item.ScheduledStartUtc,
                item.ScheduledEndUtc,
                item.CreatedAtUtc,
                displayName,
                item.CreatedByEmail,
                item.AttachmentCount,
                item.PdfAttachmentCount,
                item.PhotoAttachmentCount,
                item.VideoAttachmentCount,
                canManage,
                canRequestDelete);
        }).ToList();

        var totalPages = result.PageSize <= 0
            ? (result.TotalCount > 0 ? 1 : 0)
            : (int)Math.Ceiling(result.TotalCount / (double)Math.Max(result.PageSize, 1));

        ViewModel = new ActivityListViewModel(rows,
            result.Page,
            result.PageSize,
            result.TotalCount,
            totalPages,
            result.Sort,
            result.SortDescending,
            AttachmentType,
            FromDate,
            ToDate,
            ActivityTypeId);

        await BuildFilterOptionsAsync(cancellationToken);

        CanCreateActivities = isManager;
        CanExportActivities = result.TotalCount > 0;

        return Page();
    }

    public async Task<IActionResult> OnPostRequestDeleteAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            TempData["Error"] = "The selected activity could not be found.";
            return RedirectToPage(null, BuildRouteValues());
        }

        try
        {
            await _deleteRequestService.RequestAsync(id, reason: null, cancellationToken);
            TempData["ToastMessage"] = "Delete request submitted for approval.";
        }
        catch (ActivityAuthorizationException)
        {
            TempData["Error"] = "You are not authorised to request deletion for this activity.";
        }
        catch (InvalidOperationException)
        {
            TempData["Error"] = "A delete request is already pending for this activity.";
        }
        catch (KeyNotFoundException)
        {
            TempData["Error"] = "The selected activity could not be found.";
        }

        return RedirectToPage(null, BuildRouteValues());
    }

    public async Task<IActionResult> OnPostExportAsync(CancellationToken cancellationToken)
    {
        var exportRequest = new ActivityExportRequest(
            SortBy,
            IsSortDescending,
            FromDate,
            ToDate,
            ActivityTypeId,
            CreatedByUserId: null,
            AttachmentType);

        var export = await _activityExportService.ExportAsync(exportRequest, cancellationToken);

        if (export is null)
        {
            TempData["ToastMessage"] = "No activities match the selected filters.";
            return RedirectToPage(null, BuildRouteValues());
        }

        return File(export.Content, export.ContentType, export.FileName);
    }

    public Dictionary<string, string?> BuildRoute(int? page = null,
                                                 ActivityListSort? sort = null,
                                                 string? sortDir = null,
                                                 int? pageSize = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Page"] = (page ?? Page).ToString(CultureInfo.InvariantCulture),
            ["PageSize"] = (pageSize ?? PageSize).ToString(CultureInfo.InvariantCulture),
            ["SortBy"] = (sort ?? SortBy).ToString(),
            ["SortDir"] = sortDir ?? SortDirection
        };

        if (FromDate.HasValue)
        {
            values["FromDate"] = FromDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (ToDate.HasValue)
        {
            values["ToDate"] = ToDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (ActivityTypeId.HasValue)
        {
            values["ActivityTypeId"] = ActivityTypeId.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (AttachmentType != ActivityAttachmentTypeFilter.Any)
        {
            values["AttachmentType"] = AttachmentType.ToString();
        }

        return values;
    }

    public Dictionary<string, string?> BuildRouteForDeleteRequest(int id)
    {
        var values = new Dictionary<string, string?>(BuildRoute(Page, SortBy, SortDirection, PageSize), StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = id.ToString(CultureInfo.InvariantCulture)
        };

        return values;
    }

    public string GetSortDirectionFor(ActivityListSort column)
    {
        if (SortBy == column)
        {
            return IsSortDescending ? "asc" : "desc";
        }

        return column == ActivityListSort.ScheduledStart ? "desc" : "asc";
    }

    public string GetSortIconClass(ActivityListSort column)
    {
        if (SortBy != column)
        {
            return "bi bi-arrow-down-up text-muted";
        }

        return IsSortDescending ? "bi bi-arrow-down" : "bi bi-arrow-up";
    }

    private async Task BuildFilterOptionsAsync(CancellationToken cancellationToken)
    {
        var types = await _activityTypeService.ListAsync(cancellationToken);

        var typeOptions = new List<SelectListItem>
        {
            new("All types", string.Empty, ActivityTypeId is null or <= 0)
        };

        foreach (var type in types.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            typeOptions.Add(new SelectListItem(type.Name, type.Id.ToString(CultureInfo.InvariantCulture), type.Id == ActivityTypeId));
        }

        ActivityTypeOptions = typeOptions;

        AttachmentTypeOptions = new List<SelectListItem>
        {
            new("Any attachment", ActivityAttachmentTypeFilter.Any.ToString(), AttachmentType == ActivityAttachmentTypeFilter.Any),
            new("PDF", ActivityAttachmentTypeFilter.Pdf.ToString(), AttachmentType == ActivityAttachmentTypeFilter.Pdf),
            new("Photo", ActivityAttachmentTypeFilter.Photo.ToString(), AttachmentType == ActivityAttachmentTypeFilter.Photo),
            new("Video", ActivityAttachmentTypeFilter.Video.ToString(), AttachmentType == ActivityAttachmentTypeFilter.Video)
        };

        PageSizeOptions = AllowedPageSizes
            .Select(size => new SelectListItem(size.ToString(CultureInfo.InvariantCulture), size.ToString(CultureInfo.InvariantCulture), PageSize == size))
            .ToList();
    }

    private static bool IsManager(ClaimsPrincipal user)
    {
        return user.IsInRole("Admin") ||
               user.IsInRole("HoD") ||
               user.IsInRole("Project Office") ||
               user.IsInRole("TA");
    }

    private static int NormalizePageSize(int requested)
    {
        if (requested <= 0)
        {
            return 25;
        }

        if (AllowedPageSizes.Contains(requested))
        {
            return requested;
        }

        var closest = AllowedPageSizes
            .OrderBy(size => Math.Abs(size - requested))
            .ThenBy(size => size)
            .First();

        return closest;
    }

    private RouteValueDictionary BuildRouteValues()
    {
        return new RouteValueDictionary(BuildRoute(Page, SortBy, SortDirection, PageSize));
    }

}
