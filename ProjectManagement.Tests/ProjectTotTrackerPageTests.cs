using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.Tot;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectTotTrackerPageTests
{
    [Fact]
    public async Task SubmitUpdate_ForbidsUnassignedProjectOfficer()
    {
        await using var context = CreateContext();
        using var userManager = CreateUserManager(context);

        context.Roles.Add(new IdentityRole { Id = "role-po", Name = "Project Officer", NormalizedName = "PROJECT OFFICER" });
        context.Projects.Add(new Project
        {
            Id = 1,
            Name = "Delta",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator",
            LeadPoUserId = "po-owner",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        });

        await context.SaveChangesAsync();

        var intruder = new ApplicationUser { Id = "po-intruder", UserName = "intruder" };
        await userManager.CreateAsync(intruder);
        await userManager.AddToRoleAsync(intruder, "Project Officer");

        var trackerService = new ProjectTotTrackerReadService(context);
        var totService = new ProjectTotService(context, new FixedClock(DateTimeOffset.UtcNow));
        var totUpdateService = new ProjectTotUpdateService(context, userManager, new FixedClock(DateTimeOffset.UtcNow));
        var authService = new StubAuthorizationService(canSubmit: true, canApprove: true);

        var page = new IndexModel(trackerService, totService, totUpdateService, authService, userManager)
        {
            PageContext = BuildPageContext(CreatePrincipal(intruder.Id, "Project Officer"))
        };

        page.SubmitUpdate = new IndexModel.SubmitUpdateInput
        {
            ProjectId = 1,
            Body = "Attempted update"
        };

        var result = await page.OnPostSubmitUpdateAsync(CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(await context.ProjectTotProgressUpdates.ToListAsync());
    }

    [Fact]
    public async Task SubmitUpdate_AllowsAssignedProjectOfficer()
    {
        await using var context = CreateContext();
        using var userManager = CreateUserManager(context);

        context.Roles.Add(new IdentityRole { Id = "role-po", Name = "Project Officer", NormalizedName = "PROJECT OFFICER" });
        context.Projects.Add(new Project
        {
            Id = 2,
            Name = "Gamma",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator",
            LeadPoUserId = "po-2",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        });

        await context.SaveChangesAsync();

        var officer = new ApplicationUser { Id = "po-2", UserName = "officer" };
        await userManager.CreateAsync(officer);
        await userManager.AddToRoleAsync(officer, "Project Officer");

        var trackerService = new ProjectTotTrackerReadService(context);
        var totService = new ProjectTotService(context, new FixedClock(DateTimeOffset.UtcNow));
        var totUpdateService = new ProjectTotUpdateService(context, userManager, new FixedClock(DateTimeOffset.UtcNow));
        var authService = new StubAuthorizationService(canSubmit: true, canApprove: true);

        var page = new IndexModel(trackerService, totService, totUpdateService, authService, userManager)
        {
            PageContext = BuildPageContext(CreatePrincipal(officer.Id, "Project Officer"))
        };

        page.SubmitUpdate = new IndexModel.SubmitUpdateInput
        {
            ProjectId = 2,
            Body = "Shared documentation",
            EventDate = new DateOnly(2024, 10, 5)
        };

        var result = await page.OnPostSubmitUpdateAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(2, Convert.ToInt32(redirect.RouteValues!["SelectedProjectId"]));

        var update = await context.ProjectTotProgressUpdates.SingleAsync();
        Assert.Equal(ProjectTotProgressUpdateState.Pending, update.State);
        Assert.Equal("Shared documentation", update.Body);
    }

    [Fact]
    public async Task DecideUpdate_ForbidsWhenUserNotApprover()
    {
        await using var context = CreateContext();
        using var userManager = CreateUserManager(context);

        context.Roles.AddRange(
            new IdentityRole { Id = "role-po", Name = "Project Officer", NormalizedName = "PROJECT OFFICER" },
            new IdentityRole { Id = "role-admin", Name = "Admin", NormalizedName = "ADMIN" });

        context.Projects.Add(new Project
        {
            Id = 3,
            Name = "Sigma",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator",
            LeadPoUserId = "po-3",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        });

        await context.SaveChangesAsync();

        var officer = new ApplicationUser { Id = "po-3", UserName = "officer" };
        var admin = new ApplicationUser { Id = "admin", UserName = "admin" };
        await userManager.CreateAsync(officer);
        await userManager.CreateAsync(admin);
        await userManager.AddToRoleAsync(officer, "Project Officer");
        await userManager.AddToRoleAsync(admin, "Admin");

        var trackerService = new ProjectTotTrackerReadService(context);
        var totService = new ProjectTotService(context, new FixedClock(DateTimeOffset.UtcNow));
        var totUpdateService = new ProjectTotUpdateService(context, userManager, new FixedClock(DateTimeOffset.UtcNow));
        var authService = new StubAuthorizationService(canSubmit: true, canApprove: false);

        var page = new IndexModel(trackerService, totService, totUpdateService, authService, userManager)
        {
            PageContext = BuildPageContext(CreatePrincipal(admin.Id, "Admin"))
        };

        await totUpdateService.SubmitAsync(3, "Pending update", null, CreatePrincipal(officer.Id, "Project Officer"));
        var pending = await context.ProjectTotProgressUpdates.SingleAsync();

        page.DecideUpdate = new IndexModel.DecideUpdateInput
        {
            ProjectId = 3,
            UpdateId = pending.Id,
            Approve = true,
            RowVersion = Convert.ToBase64String(pending.RowVersion)
        };

        var result = await page.OnPostDecideUpdateAsync(CancellationToken.None);
        Assert.IsType<ForbidResult>(result);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext context)
    {
        return new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(context),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private static ClaimsPrincipal CreatePrincipal(string userId, string role)
        => new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role)
        }, "TestAuth"));

    private static PageContext BuildPageContext(ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());
        return new PageContext(actionContext)
        {
            HttpContext = httpContext,
            RouteData = actionContext.RouteData
        };
    }

    private sealed class StubAuthorizationService : IAuthorizationService
    {
        private readonly bool _canSubmit;
        private readonly bool _canApprove;

        public StubAuthorizationService(bool canSubmit, bool canApprove)
        {
            _canSubmit = canSubmit;
            _canApprove = canApprove;
        }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
        {
            if (_canSubmit || _canApprove)
            {
                return Task.FromResult(AuthorizationResult.Success());
            }

            return Task.FromResult(AuthorizationResult.Failed());
        }

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            var result = policyName switch
            {
                ProjectOfficeReportsPolicies.ManageTotTracker => _canSubmit,
                ProjectOfficeReportsPolicies.ApproveTotTracker => _canApprove,
                _ => false
            };

            return Task.FromResult(result ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; }
    }
}
