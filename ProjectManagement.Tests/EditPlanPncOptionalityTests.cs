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
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests;

public class EditPlanPncOptionalityTests
{
    [Fact]
    public async Task DurationsFlow_BlankPncMarksPlanAsNotApplicable()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        SeedStageTemplates(db);

        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Durations",
            LeadPoUserId = "po-user"
        });

        await db.SaveChangesAsync();

        var clock = new TestClock(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var userContext = new PrincipalUserContext(CreatePrincipal("po-user", "Project Officer"));
        var planDraft = new PlanDraftService(db, clock, NullLogger<PlanDraftService>.Instance, audit, userContext);
        var planApproval = new PlanApprovalService(db, clock, NullLogger<PlanApprovalService>.Instance, new PlanSnapshotService(db));
        var planGeneration = new PlanGenerationService(db);

        var page = new EditPlanModel(db, audit, planGeneration, planDraft, planApproval, NullLogger<EditPlanModel>.Instance, userContext)
        {
            Input = new PlanEditInput
            {
                ProjectId = 1,
                Mode = PlanEditorModes.Durations,
                Action = PlanEditActions.SaveDraft,
                AnchorStart = new DateOnly(2024, 1, 1),
                IncludeWeekends = false,
                SkipHolidays = false,
                NextStageStartPolicy = NextStageStartPolicies.NextWorkingDay,
                Rows = new List<PlanEditInputRow>
                {
                    new() { Code = StageCodes.IPA, Name = "IPA", DurationDays = 10 },
                    new() { Code = StageCodes.PNC, Name = "PNC", DurationDays = 0 }
                }
            }
        };

        ConfigurePageContext(page, userContext);

        var result = await page.OnPostAsync(1, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);

        var draft = await db.PlanVersions.SingleAsync();
        Assert.False(draft.PncApplicable);
    }

    [Fact]
    public async Task ExactFlow_WithDatesMarksPlanAsApplicable()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        SeedStageTemplates(db);

        db.Projects.Add(new Project
        {
            Id = 2,
            Name = "Exact",
            LeadPoUserId = "po-user"
        });

        await db.SaveChangesAsync();

        var clock = new TestClock(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var userContext = new PrincipalUserContext(CreatePrincipal("po-user", "Project Officer"));
        var planDraft = new PlanDraftService(db, clock, NullLogger<PlanDraftService>.Instance, audit, userContext);
        var planApproval = new PlanApprovalService(db, clock, NullLogger<PlanApprovalService>.Instance, new PlanSnapshotService(db));
        var planGeneration = new PlanGenerationService(db);

        var page = new EditPlanModel(db, audit, planGeneration, planDraft, planApproval, NullLogger<EditPlanModel>.Instance, userContext)
        {
            Input = new PlanEditInput
            {
                ProjectId = 2,
                Mode = PlanEditorModes.Exact,
                Action = PlanEditActions.SaveDraft,
                Rows = new List<PlanEditInputRow>
                {
                    new() { Code = StageCodes.PNC, Name = "PNC", PlannedStart = new DateOnly(2024, 3, 1), PlannedDue = new DateOnly(2024, 3, 15) }
                }
            }
        };

        ConfigurePageContext(page, userContext);

        var result = await page.OnPostAsync(2, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);

        var draft = await db.PlanVersions.SingleAsync();
        Assert.True(draft.PncApplicable);
    }

    private static void SeedStageTemplates(ApplicationDbContext db)
    {
        db.StageTemplates.AddRange(
            new StageTemplate
            {
                Version = PlanConstants.StageTemplateVersion,
                Code = StageCodes.IPA,
                Name = "IPA",
                Sequence = 10
            },
            new StageTemplate
            {
                Version = PlanConstants.StageTemplateVersion,
                Code = StageCodes.PNC,
                Name = "PNC",
                Sequence = 20
            });
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

    private static void ConfigurePageContext(PageModel page, PrincipalUserContext userContext)
    {
        var httpContext = new DefaultHttpContext
        {
            User = userContext.User
        };

        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());
        page.PageContext = new PageContext(actionContext);
        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());
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
