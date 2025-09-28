using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Pages.Projects.Timeline;
using ProjectManagement.Services;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests;

public class PlanDraftDeletionIntegrationTests
{
    [Fact]
    public async Task OwnerCanDeleteDraftAndRedirectsWithSuccessMessage()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var project = new Project
        {
            Id = 41,
            Name = "Integration",
            LeadPoUserId = "po-user"
        };

        var plan = new PlanVersion
        {
            ProjectId = 41,
            VersionNo = 1,
            Title = "Draft",
            Status = PlanVersionStatus.Draft,
            CreatedByUserId = "po-user",
            OwnerUserId = "po-user",
            CreatedOn = DateTimeOffset.UtcNow
        };

        plan.StagePlans.Add(new StagePlan
        {
            StageCode = StageCodes.FS,
            PlannedStart = new DateOnly(2024, 1, 5),
            PlannedDue = new DateOnly(2024, 1, 12)
        });

        db.Projects.Add(project);
        db.PlanVersions.Add(plan);
        await db.SaveChangesAsync();

        var clock = new TestClock(new DateTimeOffset(2024, 3, 5, 8, 30, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var userContext = new PrincipalUserContext(CreatePrincipal("po-user", "Project Officer"));
        var planDraft = new PlanDraftService(db, clock, NullLogger<PlanDraftService>.Instance, audit, userContext);
        var page = new EditPlanModel(
            db,
            audit,
            new PlanGenerationService(db),
            planDraft,
            new PlanApprovalService(db, clock, NullLogger<PlanApprovalService>.Instance, new PlanSnapshotService(db)),
            NullLogger<EditPlanModel>.Instance,
            userContext);

        ConfigurePageContext(page, userContext);

        var result = await page.OnPostDeleteDraftAsync(41, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Projects/Overview", redirect.PageName);
        Assert.Equal("Draft discarded.", page.TempData["Flash"]);
        Assert.Empty(await db.PlanVersions.ToListAsync());
        Assert.Empty(await db.StagePlans.ToListAsync());
        Assert.Single(audit.Entries);
    }

    private static void ConfigurePageContext(PageModel page, PrincipalUserContext userContext)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = userContext.User;

        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());
        page.PageContext = new PageContext(actionContext);
        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());
    }

    private static ClaimsPrincipal CreatePrincipal(string userId, string role)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, userId),
            new Claim(ClaimTypes.Role, role)
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private sealed class PrincipalUserContext : IUserContext
    {
        public PrincipalUserContext(ClaimsPrincipal user)
        {
            User = user;
        }

        public ClaimsPrincipal User { get; }

        public string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }

    private sealed class RecordingAudit : IAuditService
    {
        public List<(string Action, IDictionary<string, string?> Data, string? UserId)> Entries { get; } = new();

        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null)
        {
            Entries.Add((action, data ?? new Dictionary<string, string?>(), userId));
            return Task.CompletedTask;
        }
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset now)
        {
            UtcNow = now;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
