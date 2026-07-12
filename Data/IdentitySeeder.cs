using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ProjectManagement.Configuration;
using ProjectManagement.Models;

namespace ProjectManagement.Data
{
    public static class IdentitySeeder
    {
        private const string BootstrapPasswordEnvironmentVariable = "PRISM_BOOTSTRAP_ADMIN_PASSWORD";

        public static async Task SeedAsync(IServiceProvider services)
        {
            var roles = new[]
            {
                RoleNames.ProjectOfficer,
                RoleNames.HoD,
                RoleNames.Comdt,
                RoleNames.Admin,
                RoleNames.Ta,
                RoleNames.Mco,
                RoleNames.ProjectOffice,
                RoleNames.MainOfficeClerk,
                RoleNames.McCellClerk,
                RoleNames.ItCellClerk
            };

            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in roles)
            {
                if (await roleManager.RoleExistsAsync(role))
                {
                    continue;
                }

                EnsureSucceeded(
                    await roleManager.CreateAsync(new IdentityRole(role)),
                    $"Failed to create the '{role}' role.");
            }

            var configuration = services.GetRequiredService<IConfiguration>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var bootstrapUserName = configuration["Security:BootstrapAdminUserName"]?.Trim();
            if (string.IsNullOrWhiteSpace(bootstrapUserName))
            {
                bootstrapUserName = "admin";
            }

            var admin = await userManager.FindByNameAsync(bootstrapUserName);
            if (admin is null)
            {
                var bootstrapPassword = configuration["Security:BootstrapAdminPassword"]
                    ?? Environment.GetEnvironmentVariable(BootstrapPasswordEnvironmentVariable);

                if (string.IsNullOrWhiteSpace(bootstrapPassword))
                {
                    throw new InvalidOperationException(
                        $"No administrator account exists. Set '{BootstrapPasswordEnvironmentVariable}' " +
                        "or configuration key 'Security:BootstrapAdminPassword' for the one-time bootstrap, " +
                        "then remove the secret after the account is created.");
                }

                admin = new ApplicationUser
                {
                    UserName = bootstrapUserName,
                    EmailConfirmed = true,
                    MustChangePassword = true,
                    FullName = "Administrator",
                    Rank = "Admin",
                    LockoutEnabled = true
                };

                await CreateUserWithRoleAsync(
                    userManager,
                    admin,
                    bootstrapPassword,
                    RoleNames.Admin,
                    "bootstrap administrator");
            }
            else if (!await userManager.IsInRoleAsync(admin, RoleNames.Admin))
            {
                EnsureSucceeded(
                    await userManager.AddToRoleAsync(admin, RoleNames.Admin),
                    "Failed to restore the Admin role to the configured bootstrap administrator account.");
                EnsureSucceeded(
                    await userManager.UpdateSecurityStampAsync(admin),
                    "Failed to refresh the bootstrap administrator security state.");
            }

            var environment = services.GetRequiredService<IWebHostEnvironment>();
            if (!environment.IsDevelopment())
            {
                return;
            }

            await EnsureConfiguredDevelopmentUserAsync(
                userManager,
                configuration,
                keyPrefix: "DevelopmentSeedUsers:TestHoD",
                userName: "test_hod",
                fullName: "Test HoD",
                rank: "Test",
                role: RoleNames.HoD);

            await EnsureConfiguredDevelopmentUserAsync(
                userManager,
                configuration,
                keyPrefix: "DevelopmentSeedUsers:TestProjectOfficer",
                userName: "test_project_offr",
                fullName: "Test Project Officer",
                rank: "Test",
                role: RoleNames.ProjectOfficer);
        }

        private static async Task EnsureConfiguredDevelopmentUserAsync(
            UserManager<ApplicationUser> userManager,
            IConfiguration configuration,
            string keyPrefix,
            string userName,
            string fullName,
            string rank,
            string role)
        {
            var password = configuration[$"{keyPrefix}:Password"];
            if (string.IsNullOrWhiteSpace(password))
            {
                return;
            }

            var user = await userManager.FindByNameAsync(userName);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = userName,
                    Email = $"{userName}@example.local",
                    MustChangePassword = true,
                    FullName = fullName,
                    Rank = rank,
                    LockoutEnabled = true
                };

                await CreateUserWithRoleAsync(
                    userManager,
                    user,
                    password,
                    role,
                    $"development user '{userName}'");
                return;
            }

            if (!await userManager.IsInRoleAsync(user, role))
            {
                EnsureSucceeded(
                    await userManager.AddToRoleAsync(user, role),
                    $"Failed to assign role '{role}' to development user '{userName}'.");
                EnsureSucceeded(
                    await userManager.UpdateSecurityStampAsync(user),
                    $"Failed to refresh the security state for development user '{userName}'.");
            }
        }

        private static async Task CreateUserWithRoleAsync(
            UserManager<ApplicationUser> userManager,
            ApplicationUser user,
            string password,
            string role,
            string accountDescription)
        {
            EnsureSucceeded(
                await userManager.CreateAsync(user, password),
                $"Failed to create {accountDescription}.");

            try
            {
                EnsureSucceeded(
                    await userManager.AddToRoleAsync(user, role),
                    $"Failed to assign role '{role}' to {accountDescription}.");
                EnsureSucceeded(
                    await userManager.UpdateSecurityStampAsync(user),
                    $"Failed to initialise the security state for {accountDescription}.");
            }
            catch (Exception provisioningError)
            {
                var cleanupResult = await userManager.DeleteAsync(user);
                if (!cleanupResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Provisioning {accountDescription} failed and the partial account could not be removed. " +
                        string.Join("; ", cleanupResult.Errors.Select(error => error.Description)),
                        provisioningError);
                }

                throw;
            }
        }

        private static void EnsureSucceeded(IdentityResult result, string message)
        {
            if (result.Succeeded)
            {
                return;
            }

            throw new InvalidOperationException(
                $"{message} {string.Join("; ", result.Errors.Select(error => error.Description))}");
        }
    }
}
