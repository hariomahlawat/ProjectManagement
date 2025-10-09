using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.ViewFeatures.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Pages.Projects;
using ProjectManagement.Services;
using ProjectManagement.Services.Notifications;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AssignRolesPageTests
{
    [Fact]
    public async Task OnPostAsync_ProjectOfficerChange_PublishesNotifications()
    {
        var scope = CreateContextWithIdentity();
        await using var db = scope.Context;
        var userManager = scope.UserManager;

        await userManager.CreateAsync(new ApplicationUser { Id = "po-old", UserName = "old.po", FullName = "Olivia Officer" });
        await userManager.CreateAsync(new ApplicationUser { Id = "po-new", UserName = "new.po", FullName = "Noah Officer" });

        db.Projects.Add(new Project
        {
            Id = 10,
            Name = "Transit Hub",
            LeadPoUserId = "po-old"
        });
        await db.SaveChangesAsync();

        var project = await db.Projects.SingleAsync(p => p.Id == 10);

        var publisher = new RecordingNotificationPublisher();
        var audit = new NoOpAuditService();

        var page = new AssignRolesModel(db, userManager, audit, publisher)
        {
            Input = new AssignRolesModel.InputModel
            {
                ProjectId = project.Id,
                HodUserId = null,
                PoUserId = "po-new",
                RowVersion = Convert.ToBase64String(project.RowVersion)
            }
        };

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "actor-1"),
                new Claim(ClaimTypes.Name, "Alex Admin")
            }, "TestAuth"))
        };

        page.PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));
        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());

        var result = await page.OnPostAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Projects/Overview", redirect.PageName);

        var notification = Assert.Single(publisher.Events);
        Assert.Equal(NotificationKind.ProjectAssignmentChanged, notification.Kind);
        Assert.Equal("Projects", notification.Module);
        Assert.Equal("ProjectOfficerAssignmentChanged", notification.EventType);
        Assert.Equal("Project", notification.ScopeType);
        Assert.Equal("10", notification.ScopeId);
        Assert.Equal(10, notification.ProjectId);
        Assert.Equal("actor-1", notification.ActorUserId);
        Assert.Equal("/projects/overview/10", notification.Route);
        Assert.Equal("Transit Hub project officer updated", notification.Title);

        var expectedSummary = string.Format(
            CultureInfo.InvariantCulture,
            "Project officer assignment changed from {0} to {1}. Review the project overview for details.",
            "Olivia Officer",
            "Noah Officer");
        Assert.Equal(expectedSummary, notification.Summary);

        Assert.Contains("po-old", notification.Recipients);
        Assert.Contains("po-new", notification.Recipients);

        var payload = Assert.IsType<ProjectAssignmentChangedNotificationPayload>(notification.Payload);
        Assert.Equal(10, payload.ProjectId);
        Assert.Equal("Transit Hub", payload.ProjectName);
        Assert.Equal("po-old", payload.PreviousProjectOfficerUserId);
        Assert.Equal("Olivia Officer", payload.PreviousProjectOfficerName);
        Assert.Equal("po-new", payload.CurrentProjectOfficerUserId);
        Assert.Equal("Noah Officer", payload.CurrentProjectOfficerName);
    }

    private static (ApplicationDbContext Context, UserManager<ApplicationUser> UserManager) CreateContextWithIdentity()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);

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

        return (Context: context, UserManager: userManager);
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
            => Task.CompletedTask;
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context)
            => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }
}
