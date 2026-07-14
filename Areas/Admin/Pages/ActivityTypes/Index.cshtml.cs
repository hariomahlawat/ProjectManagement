using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Activities;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Areas.Admin.Pages.ActivityTypes;

[Authorize(Policy = AdminPolicies.ActivityTypesManage)]
public sealed class IndexModel : PageModel
{
    private readonly IMasterDataAdministrationQueryService _query;
    private readonly IActivityTypeService _activityTypes;
    private readonly IAdminTimeService _time;
    private readonly IAuthorizationService _authorization;

    public IndexModel(
        IMasterDataAdministrationQueryService query,
        IActivityTypeService activityTypes,
        IAdminTimeService time,
        IAuthorizationService authorization)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _activityTypes = activityTypes ?? throw new ArgumentNullException(nameof(activityTypes));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "active";

    [BindProperty(SupportsGet = true, Name = "pageNumber")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    public ActivityTypeDirectoryResult Result { get; private set; } = new(
        Array.Empty<ActivityTypeAdminRow>(),
        0, 0, 0, 0, 0,
        1, 25, 1,
        string.Empty,
        "active");

    public AdminPageHeaderModel Header { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Result = await _query.GetActivityTypesAsync(
            new ActivityTypeDirectoryRequest(Search, Status, PageNumber, PageSize),
            cancellationToken);
        Search = Result.Search;
        Status = Result.Status;
        PageNumber = Result.Page;
        PageSize = Result.PageSize;
        var canOpenCentre = (await _authorization.AuthorizeAsync(User, AdminPolicies.MasterDataManage)).Succeeded;
        BuildHeader(canOpenCentre);
    }

    public async Task<IActionResult> OnPostToggleAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            var existing = (await _activityTypes.ListAsync(cancellationToken)).FirstOrDefault(item => item.Id == id);
            if (existing is null) return NotFound();

            await _activityTypes.UpdateAsync(
                id,
                new ActivityTypeInput(existing.Name, existing.Description, !existing.IsActive, existing.RowVersion),
                cancellationToken);

            TempData[FlashMessageKeys.AdminMasterDataSuccess] = existing.IsActive
                ? $"Deactivated '{existing.Name}'."
                : $"Activated '{existing.Name}'.";
        }
        catch (ActivityAuthorizationException)
        {
            TempData[FlashMessageKeys.AdminMasterDataError] = "You are not authorised to manage activity types.";
        }
        catch (ActivityValidationException exception)
        {
            TempData[FlashMessageKeys.AdminMasterDataError] = string.Join(" ", exception.Errors.SelectMany(item => item.Value));
        }

        return RedirectToPage(new { q = Search, status = Status, pageNumber = Math.Max(1, PageNumber), pageSize = PageSize });
    }

    public string FormatIst(DateTimeOffset? value) =>
        value.HasValue ? _time.FormatIst(value.Value) : "Not recorded";

    private void BuildHeader(bool canOpenCentre)
    {
        var actions = new List<AdminPageActionModel>();
        if (canOpenCentre)
        {
            actions.Add(new AdminPageActionModel
            {
                Text = "Master data centre",
                Href = Url.Page("/MasterData/Index", new { area = "Admin" }),
                Icon = "bi-arrow-left"
            });
        }
        actions.Add(new AdminPageActionModel
        {
            Text = "Add activity type",
            Href = Url.Page("./Create", new { area = "Admin" }),
            Icon = "bi-plus-lg",
            IsPrimary = true
        });

        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Master data · Operational classification",
            Title = "Activity types",
            Description = "Maintain the controlled activity classifications available in planning and reporting workflows.",
            Icon = "bi-list-task",
            Actions = actions
        };
    }
}
