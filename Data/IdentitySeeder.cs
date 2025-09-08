using Microsoft.AspNetCore.Identity;
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
                    MustChangePassword = true,
                    FullName = "Administrator",
                    Rank = "Admin"
                };
                await userMgr.CreateAsync(admin, "ChangeMe!123");
                await userMgr.AddToRoleAsync(admin, "Admin");
            }
        }
    }
}
