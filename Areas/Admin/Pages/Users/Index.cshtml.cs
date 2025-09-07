using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Roles = "Admin")]
    public class IndexModel : PageModel
    {
        private readonly IUserManagementService _users;
        public IndexModel(IUserManagementService users) => _users = users;

        public IList<UserRow> Users { get; private set; } = new List<UserRow>();

        public class UserRow
        {
            public string Id { get; set; } = "";
            public string UserName { get; set; } = "";
            public string Roles { get; set; } = "";
            public bool IsActive { get; set; }
        }

        public async Task OnGet()
        {
            var all = await _users.GetUsersAsync();
            var rows = new List<UserRow>();
            foreach (var u in all)
            {
                var roles = await _users.GetUserRolesAsync(u.Id);
                rows.Add(new UserRow
                {
                    Id = u.Id,
                    UserName = u.UserName ?? "",
                    Roles = string.Join(", ", roles.OrderBy(r => r)),
                    IsActive = !u.LockoutEnd.HasValue || u.LockoutEnd <= DateTimeOffset.UtcNow
                });
            }
            Users = rows;
        }
    }
}
