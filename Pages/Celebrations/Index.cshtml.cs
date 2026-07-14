using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Pages.Celebrations;

[Authorize(Policy = Policies.Calendar.ManageCelebrations)]
public sealed class IndexModel : PageModel
{
    private readonly ICelebrationAdministrationService _celebrations;
    private readonly IAuthorizationService _authorization;
    private readonly UserManager<ApplicationUser> _users;

    public IndexModel(
        ICelebrationAdministrationService celebrations,
        IAuthorizationService authorization,
        UserManager<ApplicationUser> users)
    {
        _celebrations = celebrations ?? throw new ArgumentNullException(nameof(celebrations));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _users = users ?? throw new ArgumentNullException(nameof(users));
    }

    [BindProperty(SupportsGet = true)] public string Type { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public string Window { get; set; } = "all";
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "pageNumber")] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;

    public CelebrationDirectoryResult Result { get; private set; } = new(
        Array.Empty<CelebrationAdminRow>(), 0, 0, 0, 0, 0, 1, 25, 1, string.Empty, "all", "all");
    public AdminPageHeaderModel Header { get; private set; } = new();
    public bool CanManageBirthdays { get; private set; }
    public bool CanManageAnniversaries { get; private set; }
    public bool CanEdit => CanManageBirthdays || CanManageAnniversaries;
    public string AddButtonLabel => (CanManageBirthdays, CanManageAnniversaries) switch
    {
        (true, true) => "Add celebration",
        (true, false) => "Add birthday",
        (false, true) => "Add anniversary",
        _ => "Add celebration"
    };

    public bool CanManage(CelebrationType eventType) => eventType switch
    {
        CelebrationType.Birthday => CanManageBirthdays,
        CelebrationType.Anniversary => CanManageAnniversaries,
        _ => false
    };

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadPermissionsAsync();
        Result = await _celebrations.ListAsync(
            new CelebrationDirectoryRequest(Search, Type, Window, PageNumber, PageSize),
            cancellationToken);
        Search = Result.Search;
        Type = Result.Type;
        Window = Result.Window;
        PageNumber = Result.Page;
        PageSize = Result.PageSize;
        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Master data · Calendar configuration",
            Title = "Celebrations",
            Description = "Maintain birthdays and anniversaries shown in the shared calendar without changing user-account profiles.",
            Icon = "bi-stars",
            Actions = CanEdit
                ? new[]
                {
                    new AdminPageActionModel { Text = AddButtonLabel, Href = Url.Page("./Edit"), Icon = "bi-plus-lg", IsPrimary = true }
                }
                : Array.Empty<AdminPageActionModel>()
        };
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await _celebrations.GetAsync(id, cancellationToken);
        if (item is null) return RedirectToPage(new { Type, Window, q = Search, pageNumber = PageNumber, pageSize = PageSize });

        var authorization = await _authorization.AuthorizeAsync(User, Policies.Calendar.PolicyFor(item.EventType));
        if (!authorization.Succeeded) return Forbid();

        var actorUserId = _users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(actorUserId)) return Forbid();

        var result = await _celebrations.DeleteAsync(id, actorUserId, cancellationToken);
        TempData[result.Succeeded ? FlashMessageKeys.CelebrationsSuccess : FlashMessageKeys.CelebrationsError] = result.UserMessage;
        return RedirectToPage(new { Type, Window, q = Search, pageNumber = PageNumber, pageSize = PageSize });
    }

    private async Task LoadPermissionsAsync()
    {
        CanManageBirthdays = (await _authorization.AuthorizeAsync(User, Policies.Calendar.ManageBirthdays)).Succeeded;
        CanManageAnniversaries = (await _authorization.AuthorizeAsync(User, Policies.Calendar.ManageAnniversaries)).Succeeded;
    }
}
