using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services;

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

        [BindProperty(SupportsGet = true)] public string? Q { get; set; }
        [BindProperty(SupportsGet = true)] public string? Role { get; set; }
        [BindProperty(SupportsGet = true)] public string? Status { get; set; }
        public IList<string> AllRoles { get; private set; } = new List<string>();

        public class UserRow
        {
            public string Id { get; set; } = "";
            public string UserName { get; set; } = "";
            public string FullName { get; set; } = "";
            public string Rank { get; set; } = "";
            public IList<string> Roles { get; set; } = new List<string>();
            public bool IsDisabled { get; set; }
            public DateTime? LastLogin { get; set; }
            public int LoginCount { get; set; }
            public DateTime CreatedUtc { get; set; }
        }

        private async Task LoadAsync()
        {
            AllRoles = await _users.GetRolesAsync();
            var all = await _users.GetUsersAsync();
            var rows = new List<UserRow>();
            foreach (var u in all)
            {
                var roles = await _users.GetUserRolesAsync(u.Id);
                rows.Add(new UserRow
                {
                    Id = u.Id,
                    UserName = u.UserName ?? string.Empty,
                    FullName = u.FullName,
                    Rank = u.Rank,
                    Roles = roles.OrderBy(r => r).ToList(),
                    IsDisabled = u.IsDisabled,
                    CreatedUtc = u.CreatedUtc,
                    LastLogin = u.LastLoginUtc.HasValue ? IstClock.ToIst(u.LastLoginUtc.Value) : null,
                    LoginCount = u.LoginCount
                });
            }

            IEnumerable<UserRow> query = rows;
            if (!string.IsNullOrWhiteSpace(Q))
            {
                var q = Q.ToLowerInvariant();
                query = query.Where(r => r.UserName.ToLowerInvariant().Contains(q)
                    || (r.FullName?.ToLowerInvariant().Contains(q) ?? false)
                    || r.Roles.Any(role => role.ToLowerInvariant().Contains(q)));
            }
            if (!string.IsNullOrEmpty(Role))
                query = query.Where(r => r.Roles.Contains(Role));
            if (Status == "active")
                query = query.Where(r => !r.IsDisabled);
            else if (Status == "disabled")
                query = query.Where(r => r.IsDisabled);

            Users = query.ToList();
        }

        public async Task OnGet() => await LoadAsync();

        public async Task<IActionResult> OnGetExport()
        {
            await LoadAsync();
            var sb = new StringBuilder();
            sb.AppendLine("UserName,FullName,Roles,LastLogin,LoginCount,Status");
            foreach (var u in Users)
            {
                var roles = string.Join(';', u.Roles);
                var last = TimeFmt.ToIst(u.LastLogin);
                var status = u.IsDisabled ? "Disabled" : "Active";
                sb.AppendLine($"{u.UserName},{u.FullName},{roles},{last},{u.LoginCount},{status}");
            }
            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "users.csv");
        }
    }
}
