using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services;
using ProjectManagement.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Roles = "Admin")]
    [ResponseCache(NoStore = true)]
    public class IndexModel : PageModel
    {
        private readonly IUserManagementService _users;
        private readonly UserLifecycleOptions _opts;
        public IndexModel(IUserManagementService users, Microsoft.Extensions.Options.IOptions<UserLifecycleOptions> opts)
        {
            _users = users;
            _opts = opts.Value;
        }

        public IList<UserRow> Users { get; private set; } = new List<UserRow>();
        public UserLifecycleOptions Options => _opts;

        public class UserRow
        {
            public string Id { get; set; } = "";
            public string UserName { get; set; } = "";
            public string FullName { get; set; } = "";
            public string Rank { get; set; } = "";
            public IList<string> Roles { get; set; } = new List<string>();
            public bool IsActive { get; set; }
            public DateTime? LastLogin { get; set; }
            public int LoginCount { get; set; }
            public bool PendingDeletion { get; set; }
            public DateTime? DeletionRequestedUtc { get; set; }
            public DateTime CreatedUtc { get; set; }
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
                    FullName = u.FullName,
                    Rank = u.Rank,
                    Roles = roles.OrderBy(r => r).ToList(),
                    IsActive = !u.IsDisabled && !u.PendingDeletion,
                    PendingDeletion = u.PendingDeletion,
                    DeletionRequestedUtc = u.DeletionRequestedUtc,
                    CreatedUtc = u.CreatedUtc,
                    LastLogin = IstClock.ToIst(u.LastLoginUtc),
                    LoginCount = u.LoginCount
                });
            }
            Users = rows;
        }
    }
}
