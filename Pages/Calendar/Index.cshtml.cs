using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Models;

namespace ProjectManagement.Pages.Calendar;

[Authorize]
public class IndexModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthorizationService _authorization;

    public IndexModel(
        UserManager<ApplicationUser> userManager,
        IAuthorizationService authorization)
    {
        _userManager = userManager;
        _authorization = authorization;
    }

    public bool CanEdit { get; private set; }
    public bool CanManageBirthdays { get; private set; }
    public bool CanManageAnniversaries { get; private set; }
    public bool CanManageCelebrations => CanManageBirthdays || CanManageAnniversaries;
    public string CelebrationManagementLabel => (CanManageBirthdays, CanManageAnniversaries) switch
    {
        (true, true) => "Manage celebrations",
        (true, false) => "Manage birthdays",
        (false, true) => "Manage anniversaries",
        _ => "Manage celebrations"
    };
    public bool ShowCelebrations { get; private set; }

    public async Task OnGetAsync()
    {
        CanEdit = (await _authorization.AuthorizeAsync(User, Policies.Calendar.ManageEvents)).Succeeded;
        CanManageBirthdays = (await _authorization.AuthorizeAsync(User, Policies.Calendar.ManageBirthdays)).Succeeded;
        CanManageAnniversaries = (await _authorization.AuthorizeAsync(User, Policies.Calendar.ManageAnniversaries)).Succeeded;

        var user = await _userManager.GetUserAsync(User);
        ShowCelebrations = user?.ShowCelebrationsInCalendar ?? true;
    }
}
