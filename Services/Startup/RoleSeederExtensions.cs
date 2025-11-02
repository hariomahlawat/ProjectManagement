using System;
using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Configuration;

namespace ProjectManagement.Services.Startup;

public static class RoleSeederExtensions
{
    private static readonly string[] Roles =
    {
        RoleNames.Admin,
        RoleNames.HoD,
        RoleNames.Comdt,
        RoleNames.ProjectOfficer,
        RoleNames.ProjectOffice,
        RoleNames.ProjectOfficeSpc,
        RoleNames.MainOffice,
        RoleNames.MainOfficeClerk,
        RoleNames.McCellClerk,
        RoleNames.ItCellClerk,
        RoleNames.MCO,
        RoleNames.TA,
        RoleNames.ITO
    };

    public static async Task EnsureRolesAsync(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in Roles)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole(role));
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Failed creating role '{role}': {errors}");
            }
        }
    }
}
