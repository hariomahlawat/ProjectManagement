using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Pages.Projects.Stages;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class StageBackfillAuthorizationTests
{
    [Fact]
    public async Task ProjectOfficer_NotAssignedToProject_IsForbidden()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);
        db.Projects.Add(new Project
        {
            Id = 17,
            Name = "Restricted project",
            CreatedByUserId = "seed",
            LeadPoUserId = "assigned-po"
        });
        await db.SaveChangesAsync();

        var service = new StageBackfillService(db, FakeClock.AtUtc(DateTimeOffset.UtcNow));
        var page = new BackfillApplyModel(service, db, NullLogger<BackfillApplyModel>.Instance);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "other-po"),
                new Claim(ClaimTypes.Role, "Project Officer")
            }, "Test"))
        };
        page.PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));

        var result = await page.OnPostAsync(new BackfillApplyModel.BackfillApplyInput
        {
            ProjectId = 17,
            Stages = new List<BackfillApplyModel.BackfillStageInput>
            {
                new() { StageCode = "FS", CompletedOn = new DateOnly(2026, 6, 20) }
            }
        }, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }
}
