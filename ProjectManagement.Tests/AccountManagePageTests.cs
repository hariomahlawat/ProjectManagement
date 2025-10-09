using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.Identity.Pages.Account.Manage;
using ProjectManagement.Data;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AccountManagePageTests
{
    [Fact]
    public async Task OnGetAsync_LoadsAssignedRoles()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var userManager = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(context),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services,
            new Logger<UserManager<ApplicationUser>>(new LoggerFactory()));

        var user = new ApplicationUser
        {
            Id = "user-1",
            UserName = "user.one@example.com",
            FullName = "User One"
        };

        await userManager.CreateAsync(user);

        context.Roles.AddRange(
            new IdentityRole
            {
                Id = "role-1",
                Name = "ProjectOfficer",
                NormalizedName = "PROJECTOFFICER"
            },
            new IdentityRole
            {
                Id = "role-2",
                Name = "Reviewer",
                NormalizedName = "REVIEWER"
            });

        await context.SaveChangesAsync();

        await userManager.AddToRoleAsync(user, "ProjectOfficer");
        await userManager.AddToRoleAsync(user, "Reviewer");

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName ?? string.Empty)
            }, "TestAuth"))
        };

        var page = new IndexModel(userManager)
        {
            PageContext = new PageContext(new ActionContext(
                httpContext,
                new RouteData(),
                new ActionDescriptor()))
        };

        var result = await page.OnGetAsync();

        Assert.IsType<PageResult>(result);
        Assert.Equal(2, page.Roles.Count);
        Assert.True(page.Roles.SequenceEqual(new[] { "ProjectOfficer", "Reviewer" }));
    }
}
