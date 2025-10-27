using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text;
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
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(IActivityService activityService,
                      IActivityTypeService activityTypeService,
                      UserManager<ApplicationUser> userManager)
    {
        _activityService = activityService;
        _activityTypeService = activityTypeService;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)]
    public int Page { get; set; } = 1;

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
    public string? CreatedBy { get; set; }

    [BindProperty(SupportsGet = true)]
    public ActivityAttachmentTypeFilter AttachmentType { get; set; } = ActivityAttachmentTypeFilter.Any;

    public ActivityListViewModel? ViewModel { get; private set; }

    public IReadOnlyList<SelectListItem> ActivityTypeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> AttachmentTypeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> SortOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> PageSizeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public bool CanCreateActivities { get; private set; }

    public bool CanExportActivities { get; private set; }

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
            CreatedBySearch: CreatedBy,
            AttachmentType);

        var result = await _activityService.ListAsync(request, cancellationToken);

        var currentUserId = _userManager.GetUserId(User) ?? string.Empty;
        var isAdminOrHod = IsAdminOrHod(User);

        var rows = result.Items.Select(item =>
        {
            var displayName = string.IsNullOrWhiteSpace(item.CreatedByDisplayName)
                ? (string.IsNullOrWhiteSpace(item.CreatedByEmail) ? item.CreatedByUserId : item.CreatedByEmail)
                : item.CreatedByDisplayName;

            var canManage = isAdminOrHod || string.Equals(item.CreatedByUserId, currentUserId, StringComparison.OrdinalIgnoreCase);

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
                canManage);
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
            ActivityTypeId,
            CreatedBy);

        await BuildFilterOptionsAsync(cancellationToken);

        CanCreateActivities = isAdminOrHod;
        CanExportActivities = result.TotalCount > 0;

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            await _activityService.DeleteAsync(id, cancellationToken);
            TempData["ToastMessage"] = "Activity deleted.";
        }
        catch (ActivityAuthorizationException)
        {
            TempData["Error"] = "You are not authorised to delete this activity.";
        }
        catch (KeyNotFoundException)
        {
            TempData["Error"] = "The selected activity could not be found.";
        }

        return RedirectToPage(null, BuildRouteValues());
    }

    public async Task<IActionResult> OnPostExportAsync(CancellationToken cancellationToken)
    {
        var request = new ActivityListRequest(1,
            0,
            SortBy,
            IsSortDescending,
            FromDate,
            ToDate,
            ActivityTypeId,
            CreatedByUserId: null,
            CreatedBySearch: CreatedBy,
            AttachmentType);

        var result = await _activityService.ListAsync(request, cancellationToken);

        if (result.TotalCount == 0)
        {
            TempData["ToastMessage"] = "No activities match the selected filters.";
            return RedirectToPage(null, BuildRouteValues());
        }

        var builder = new StringBuilder();
        builder.AppendLine("Title,Type,Scheduled Start,Scheduled End,Created At,Created By,Pdf Attachments,Photo Attachments,Video Attachments,Total Attachments");

        foreach (var item in result.Items)
        {
            builder
                .Append(EscapeCsv(item.Title)).Append(',')
                .Append(EscapeCsv(item.ActivityTypeName)).Append(',')
                .Append(EscapeCsv(FormatInstant(item.ScheduledStartUtc))).Append(',')
                .Append(EscapeCsv(FormatInstant(item.ScheduledEndUtc))).Append(',')
                .Append(EscapeCsv(item.CreatedAtUtc.ToString("u", CultureInfo.InvariantCulture))).Append(',')
                .Append(EscapeCsv(item.CreatedByDisplayName ?? item.CreatedByEmail ?? item.CreatedByUserId)).Append(',')
                .Append(item.PdfAttachmentCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(item.PhotoAttachmentCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(item.VideoAttachmentCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(item.AttachmentCount.ToString(CultureInfo.InvariantCulture))
                .AppendLine();
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        var fileName = $"activities-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
        return File(bytes, "text/csv", fileName);
    }

    public RouteValueDictionary BuildRoute(int? page = null,
                                           ActivityListSort? sort = null,
                                           string? sortDir = null,
                                           int? pageSize = null)
    {
        var values = new RouteValueDictionary
        {
            ["Page"] = page ?? Page,
            ["PageSize"] = pageSize ?? PageSize,
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
            values["ActivityTypeId"] = ActivityTypeId.Value;
        }

        if (!string.IsNullOrWhiteSpace(CreatedBy))
        {
            values["CreatedBy"] = CreatedBy;
        }

        if (AttachmentType != ActivityAttachmentTypeFilter.Any)
        {
            values["AttachmentType"] = AttachmentType.ToString();
        }

        return values;
    }

    public RouteValueDictionary BuildRouteForDelete(int id)
    {
        var values = BuildRoute(Page, SortBy, SortDirection, PageSize);
        values["id"] = id;
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

        SortOptions = new List<SelectListItem>
        {
            new("Scheduled", ActivityListSort.ScheduledStart.ToString(), SortBy == ActivityListSort.ScheduledStart),
            new("Created", ActivityListSort.CreatedAt.ToString(), SortBy == ActivityListSort.CreatedAt),
            new("Title", ActivityListSort.Title.ToString(), SortBy == ActivityListSort.Title),
            new("Activity type", ActivityListSort.ActivityType.ToString(), SortBy == ActivityListSort.ActivityType)
        };

        PageSizeOptions = AllowedPageSizes
            .Select(size => new SelectListItem(size.ToString(CultureInfo.InvariantCulture), size.ToString(CultureInfo.InvariantCulture), PageSize == size))
            .ToList();
    }

    private static bool IsAdminOrHod(ClaimsPrincipal user)
    {
        return user.IsInRole("Admin") || user.IsInRole("HoD");
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
        var routeValues = BuildRoute(Page, SortBy, SortDirection, PageSize);
        return routeValues;
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Contains('"'))
        {
            value = value.Replace("\"", "\"\"");
        }

        if (value.Contains(',') || value.Contains('\n'))
        {
            return $"\"{value}\"";
        }

        return value;
    }

    private static string FormatInstant(DateTimeOffset? instant)
    {
        return instant?.ToString("u", CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
