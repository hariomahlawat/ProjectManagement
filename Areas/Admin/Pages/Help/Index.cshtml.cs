using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;

namespace ProjectManagement.Areas.Admin.Pages.Help;

[Authorize(Policy = AdminPolicies.Access)]
[ResponseCache(NoStore = true)]
public sealed class IndexModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Section { get; set; }

    public AdminPageHeaderModel Header { get; private set; } = new();

    public void OnGet()
    {
        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Administration guidance",
            Title = "Administration guide",
            Description = "Operational guidance for access control, monitoring, recovery, maintenance and configuration governance.",
            Icon = "bi-question-circle"
        };
    }
}
