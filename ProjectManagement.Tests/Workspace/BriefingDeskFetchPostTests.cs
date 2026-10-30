using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Workspace;
using Xunit;
using WorkspaceIndexModel = ProjectManagement.Pages.Workspace.IndexModel;

namespace ProjectManagement.Tests.Workspace;

/// <summary>
/// A desk post sent by the desk's script gets a JSON reason when refused, so the page keeps the
/// typed text without reloading; a plain form post keeps the redirect back to the card.
/// </summary>
public sealed class BriefingDeskFetchPostTests
{
    [Fact]
    public async Task RefusedFetchPost_ReturnsTheReasonAs422()
    {
        await using var db = CreateContext();
        var page = CreatePage(db, RoleNames.ProjectOfficer, fetch: true);

        var result = await page.OnPostDeskProjectUpdateAsync(1, "   ", CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, json.StatusCode);
        Assert.Contains("Write something before posting.", System.Text.Json.JsonSerializer.Serialize(json.Value));
        Assert.False(page.TempData.ContainsKey(WorkspaceIndexModel.DeskDraftTextKey));
    }

    [Fact]
    public async Task ForbiddenFetchPost_Returns403WithTheReason()
    {
        await using var db = CreateContext();
        var page = CreatePage(db, RoleNames.HoD, fetch: true);

        var result = await page.OnPostDeskProjectUpdateAsync(1, "Progress", CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, json.StatusCode);
    }

    [Fact]
    public async Task RefusedPlainPost_StillRedirectsToTheCardWithTheDraftKept()
    {
        await using var db = CreateContext();
        var page = CreatePage(db, RoleNames.ProjectOfficer, fetch: false);

        var result = await page.OnPostDeskTaskSubmitAsync(7, rowVersion: null, note: "   ", CancellationToken.None);

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Contains("#desk-task-7", redirect.Url);
        Assert.Equal("Add a closure note.", page.TempData["ToastError"]);
    }

    private static WorkspaceIndexModel CreatePage(ApplicationDbContext db, string role, bool fetch)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var userManager = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(db),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        // These refusals happen before the command service touches the database or other services.
        var commands = new BriefingDeskCommandService(db, null!, null!, null!, null!, null!, null!);
        var page = new WorkspaceIndexModel(null!, null!, null!, userManager, null!, commands, NullLogger<WorkspaceIndexModel>.Instance);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "po-user"), new Claim(ClaimTypes.Role, role) },
                "test"))
        };
        if (fetch)
        {
            httpContext.Request.Headers[WorkspaceIndexModel.DeskFetchHeader] = WorkspaceIndexModel.DeskFetchValue;
        }

        page.PageContext = new PageContext { HttpContext = httpContext, RouteData = new RouteData() };
        page.TempData = new TempDataDictionary(httpContext, new InMemoryTempDataProvider());
        return page;
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        private IDictionary<string, object> _values = new Dictionary<string, object>();

        public IDictionary<string, object> LoadTempData(HttpContext context) => _values;

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => _values = values;
    }
}
