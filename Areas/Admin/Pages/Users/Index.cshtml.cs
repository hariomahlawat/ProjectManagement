using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Roles = "Admin")]
    public class IndexModel : PageModel
    {
        private readonly IUserManagementService _userService;
        public IList<UserViewModel> Users { get; private set; } = new List<UserViewModel>();
        public IndexModel(IUserManagementService userService) => _userService = userService;

        public async Task OnGetAsync()
        {
            var users = await _userService.GetUsersAsync();
            var list = new List<UserViewModel>();
            foreach (var u in users)
            {
                var roles = await _userService.GetUserRolesAsync(u.Id);
                list.Add(new UserViewModel
                {
                    Id = u.Id,
                    UserName = u.UserName!,
                    IsActive = !u.LockoutEnd.HasValue || u.LockoutEnd <= DateTimeOffset.UtcNow,
                    Roles = string.Join(", ", roles)
                });
            }
            Users = list;
        }

        public class UserViewModel
        {
            public string Id { get; set; } = string.Empty;
            public string UserName { get; set; } = string.Empty;
            public bool IsActive { get; set; }
            public string Roles { get; set; } = string.Empty;
        }
    }
}
