using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using System.Collections.Generic;

namespace ProjectManagement.Areas.Admin.Pages
{
    [Authorize(Roles = "Admin")]
    public class IndexModel : PageModel
    {
        public IList<AdminCard> Cards { get; } = new List<AdminCard>
        {
            new("Manage Users", "/Users/Index", "Create, edit and disable users", "users"),
            new("Analytics", "/Analytics/Index", "Review platform analytics", "analytics"),
            new("Logs", "/Logs/Index", "Inspect application logs", "logs")
        };

        public void OnGet() { }
    }
}
