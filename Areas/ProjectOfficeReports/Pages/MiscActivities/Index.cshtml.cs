using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.MiscActivities.ViewModels;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.MiscActivities;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewMiscActivities)]
public sealed class IndexModel : PageModel
{
    private readonly IMiscActivityViewService _viewService;
    private readonly IAuthorizationService _authorizationService;

    public IndexModel(
        IMiscActivityViewService viewService,
        IAuthorizationService authorizationService)
    {
        _viewService = viewService ?? throw new ArgumentNullException(nameof(viewService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
    }

    [BindProperty(SupportsGet = true)]
    public Guid? ActivityTypeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? StartDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? EndDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool IncludeDeleted { get; set; }

    [BindProperty(SupportsGet = true)]
    public MiscActivitySortField Sort { get; set; } = MiscActivitySortField.OccurrenceDate;

    [BindProperty(SupportsGet = true)]
    public bool SortDescending { get; set; } = true;

    [BindProperty(SupportsGet = true)]
    public string? CreatorUserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public MiscActivityAttachmentTypeFilter AttachmentType { get; set; } = MiscActivityAttachmentTypeFilter.Any;

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    public MiscActivityIndexViewModel ViewModel { get; private set; } = new();

    public bool CanManage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var options = new MiscActivityQueryOptions(
            ActivityTypeId,
            StartDate,
            EndDate,
            Search,
            IncludeDeleted,
            Sort,
            SortDescending,
            CreatorUserId,
            AttachmentType,
            PageNumber,
            PageSize);

        ViewModel = await _viewService.GetIndexAsync(options, cancellationToken);
        var authorization = await _authorizationService.AuthorizeAsync(
            User,
            null,
            ProjectOfficeReportsPolicies.ManageMiscActivities);
        CanManage = authorization.Succeeded;

        var resolvedFilter = ViewModel.Filter;
        ActivityTypeId = resolvedFilter.ActivityTypeId;
        StartDate = resolvedFilter.StartDate;
        EndDate = resolvedFilter.EndDate;
        Search = resolvedFilter.Search;
        IncludeDeleted = resolvedFilter.IncludeDeleted;
        Sort = resolvedFilter.Sort;
        SortDescending = resolvedFilter.SortDescending;
        CreatorUserId = resolvedFilter.CreatorUserId;
        AttachmentType = resolvedFilter.AttachmentType;
        PageNumber = resolvedFilter.PageNumber;
        PageSize = resolvedFilter.PageSize;
    }
}
