using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using ProjectManagement.Models;

namespace ProjectManagement.Data
{
    public static class IdentitySeeder
    {
        public static async Task SeedAsync(IServiceProvider services)
        {
            var roles = new[]
            {
                "Project Officer",
                "HoD",
                "Comdt",
                "Admin",
                "TA",
                "MCO",
                "Project Office",
                "Main Office"
            };
            var roleMgr = services.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var r in roles)
                if (!await roleMgr.RoleExistsAsync(r))
                    await roleMgr.CreateAsync(new IdentityRole(r));

            var userMgr = services.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await userMgr.FindByNameAsync("admin");
            if (admin == null)
            {
                admin = new ApplicationUser
                {
                    UserName = "admin",
                    EmailConfirmed = true,
                    MustChangePassword = false,
                    FullName = "Administrator",
                    Rank = "Admin"
                };
                await userMgr.CreateAsync(admin, "ChangeMe!123");
                await userMgr.AddToRoleAsync(admin, "Admin");
            }

            var env = services.GetRequiredService<IWebHostEnvironment>();
            if (env.IsDevelopment())
            {
                await EnsureTestUserAsync(
                    userMgr,
                    userName: "test_hod",
                    fullName: "Test HoD",
                    rank: "Test",
                    role: "HoD",
                    password: "ChangeMe!123",
                    mustChangePassword: false);

                await EnsureTestUserAsync(
                    userMgr,
                    userName: "test_project_offr",
                    fullName: "Test Project Officer",
                    rank: "Test",
                    role: "Project Officer",
                    password: "ChangeMe!123",
                    mustChangePassword: false);

                await EnsureTestUserAsync(
                    userMgr,
                    userName: "hariomahlawat",
                    fullName: "Hari Om Ahlawat",
                    rank: "Colonel",
                    role: "HoD",
                    password: "Sdd@123456",
                    mustChangePassword: false);

                await EnsureTestUserAsync(
                    userMgr,
                    userName: "anupam",
                    fullName: "Anupam Porwal",
                    rank: "Lt Col",
                    role: "Project Officer",
                    password: "Sdd@123456",
                    mustChangePassword: false);
            }
        }

        private static async Task EnsureTestUserAsync(
            UserManager<ApplicationUser> userMgr,
            string userName,
            string fullName,
            string rank,
            string role,
            string password,
            bool mustChangePassword)
        {
            var user = await userMgr.FindByNameAsync(userName);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = userName,
                    Email = $"{userName}@example.local",
                    MustChangePassword = mustChangePassword,
                    FullName = fullName,
                    Rank = rank,
                    LockoutEnabled = false
                };
                var res = await userMgr.CreateAsync(user, password);
                if (!res.Succeeded)
                {
                    throw new InvalidOperationException($"Failed creating {userName}: {string.Join(", ", res.Errors.Select(e => e.Description))}");
                }
            }
            else
            {
                user.MustChangePassword = mustChangePassword;
                user.FullName = fullName;
                user.Rank = rank;
                user.LockoutEnabled = false;
                await userMgr.UpdateAsync(user);
            }

            if (!await userMgr.IsInRoleAsync(user, role))
            {
                await userMgr.AddToRoleAsync(user, role);
            }

            await userMgr.UpdateSecurityStampAsync(user);
        }
    }
}
