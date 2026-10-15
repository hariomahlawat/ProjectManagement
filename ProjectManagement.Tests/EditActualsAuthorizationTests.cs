using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Stages;
using ProjectManagement.Pages.Projects.Timeline;
using ProjectManagement.Services;
using ProjectManagement.Services.Stages;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class EditActualsAuthorizationTests
{
    [Theory]
    [InlineData("Project Officer", true, true)]
    [InlineData("Project Officer", false, false)]
    [InlineData("HoD", false, true)]
    [InlineData("Admin", false, true)]
    [InlineData("Viewer", true, false)]
    public async Task OnPostAsync_PreservesCurrentAuthority(string role, bool assigned, bool allowed)
    {
        await using var db = CreateContext();
        await SeedAsync(db, assigned);
        var page = CreatePage(db, role);
        page.Input = Input(new DateOnly(2024, 1, 11), null);

        var result = await page.OnPostAsync(1, CancellationToken.None);

        if (allowed)
        {
            Assert.IsType<RedirectToPageResult>(result);
            Assert.False(page.TempData.ContainsKey("Error"));
            Assert.Equal(new DateOnly(2024, 1, 11), (await db.ProjectStages.SingleAsync()).ActualStart);
        }
        else
        {
            Assert.IsType<ForbidResult>(result);
            Assert.Equal(new DateOnly(2024, 1, 10), (await db.ProjectStages.SingleAsync()).ActualStart);
        }
    }

    [Theory]
    [InlineData("Project Officer")]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task OnPostAsync_AllAuthorisedRolesRejectGenericCompletion(string role)
    {
        await using var db = CreateContext();
        await SeedAsync(db, true);
        var page = CreatePage(db, role);
        page.Input = Input(new DateOnly(2024, 1, 10), new DateOnly(2024, 1, 25));

        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(1, CancellationToken.None));
        Assert.Contains("stage-transition", Assert.IsType<string>(page.TempData["Error"]), StringComparison.OrdinalIgnoreCase);
        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(StageStatus.InProgress, stage.Status);
        Assert.Null(stage.CompletedOn);
        Assert.Empty(await db.StageChangeLogs.ToArrayAsync());
    }

    public static IEnumerable<object[]> IpaRoleCases()
    {
        foreach (var source in new[] { "none", "draft", "published" })
        {
            yield return [source, "Project Officer", true, true];
            yield return [source, "Project Officer", false, false];
            yield return [source, "HoD", false, true];
            yield return [source, "Admin", false, true];
            yield return [source, "Viewer", true, false];
        }
    }

    [Theory]
    [MemberData(nameof(IpaRoleCases))]
    public async Task OnPostAsync_IpaCompletionCorrectionUsesConditionalPublishedAuthority(
        string source, string role, bool assigned, bool authorised)
    {
        await using var db = CreateContext();
        await SeedIpaAsync(db, assigned, source);
        var page = CreatePage(db, role);
        var requestedCompletion = new DateOnly(2024, 1, 22);
        page.Input = new ActualsEditInput
        {
            ProjectId = 1,
            Rows = [new ActualsEditRowInput { StageCode = StageCodes.IPA, CompletedOn = requestedCompletion }]
        };

        var result = await page.OnPostAsync(1, CancellationToken.None);

        var ipa = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA);
        if (!authorised)
        {
            Assert.IsType<ForbidResult>(result);
            Assert.Equal(new DateOnly(2024, 1, 20), ipa.CompletedOn);
            Assert.Empty(await db.StageChangeLogs.ToArrayAsync());
            return;
        }

        Assert.IsType<RedirectToPageResult>(result);
        if (source == "published")
        {
            Assert.Contains("ARPP", Assert.IsType<string>(page.TempData["Error"]), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(new DateOnly(2024, 1, 20), ipa.CompletedOn);
            Assert.Empty(await db.StageChangeLogs.ToArrayAsync());
        }
        else
        {
            Assert.False(page.TempData.ContainsKey("Error"));
            Assert.Equal(requestedCompletion, ipa.CompletedOn);
            var history = await db.StageChangeLogs.SingleAsync();
            Assert.Equal(new DateOnly(2024, 1, 20), history.FromCompletedOn);
            Assert.Equal(requestedCompletion, history.ToCompletedOn);
        }
        Assert.Equal(new DateOnly(2024, 1, 10), ipa.ActualStart);
    }

    [Theory]
    [MemberData(nameof(IpaRoleCases))]
    public async Task OnPostAsync_IpaStartRemainsSeparatelyCorrectableUnderNormalRoleAuthority(
        string source, string role, bool assigned, bool authorised)
    {
        await using var db = CreateContext();
        await SeedIpaAsync(db, assigned, source);
        var page = CreatePage(db, role);
        var correctedStart = new DateOnly(2024, 1, 11);
        page.Input = new ActualsEditInput
        {
            ProjectId = 1,
            Rows = [new ActualsEditRowInput { StageCode = StageCodes.IPA, ActualStart = correctedStart }]
        };

        var result = await page.OnPostAsync(1, CancellationToken.None);

        var ipa = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA);
        Assert.Equal(new DateOnly(2024, 1, 20), ipa.CompletedOn);
        if (authorised)
        {
            Assert.IsType<RedirectToPageResult>(result);
            Assert.False(page.TempData.ContainsKey("Error"));
            Assert.Equal(correctedStart, ipa.ActualStart);
            Assert.Equal(correctedStart, (await db.StageChangeLogs.SingleAsync()).ToActualStart);
        }
        else
        {
            Assert.IsType<ForbidResult>(result);
            Assert.Equal(new DateOnly(2024, 1, 10), ipa.ActualStart);
            Assert.Empty(await db.StageChangeLogs.ToArrayAsync());
        }
    }

    private static async Task SeedIpaAsync(ApplicationDbContext db, bool assigned, string source)
    {
        db.Projects.Add(new Project
        {
            Id = 1, Name = "Conditional IPA", CreatedByUserId = "seed",
            LeadPoUserId = assigned ? "actor" : "other", WorkflowVersion = ProcurementWorkflow.VersionV1
        });
        db.ProjectStages.AddRange(
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.FS, Status = StageStatus.Completed, CompletedOn = new DateOnly(2024, 1, 1) },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.IPA, Status = StageStatus.Completed,
                ActualStart = new DateOnly(2024, 1, 10), CompletedOn = new DateOnly(2024, 1, 20) });
        if (source != "none")
        {
            var issue = new ArppIssue
            {
                Id = 1, Name = "IPA source", FinancialYearStart = 2023,
                Kind = ArppIssueKind.Original, IssueSequence = 0, IssueDate = new DateOnly(2024, 1, 20),
                CreatedByUserId = "seed", UpdatedByUserId = "seed",
                Entries = { new ArppEntry { ProjectId = 1, ProjectReference = "Conditional IPA", CreatedByUserId = "seed", UpdatedByUserId = "seed" } }
            };
            if (source == "published")
            {
                issue.PublishedSnapshot = new ArppPublishedIssue
                {
                    ArppIssueId = 1, Name = issue.Name, FinancialYearStart = issue.FinancialYearStart,
                    Kind = issue.Kind, IssueSequence = issue.IssueSequence, IssueDate = issue.IssueDate,
                    RevisionNumber = 1, PublishedByUserId = "seed",
                    Entries = { new ArppPublishedEntry { ArppIssueId = 1, SourceEntryId = 1, ProjectId = 1, ProjectReference = "Conditional IPA" } }
                };
            }
            db.ArppIssues.Add(issue);
        }
        await db.SaveChangesAsync();
    }

    private static ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task SeedAsync(ApplicationDbContext db, bool assigned)
    {
        db.Projects.Add(new Project { Id = 1, Name = "Actuals", CreatedByUserId = "seed", LeadPoUserId = assigned ? "actor" : "other" });
        db.ProjectStages.Add(new ProjectStage { ProjectId = 1, StageCode = StageCodes.FS, Status = StageStatus.InProgress, ActualStart = new DateOnly(2024, 1, 10) });
        await db.SaveChangesAsync();
    }

    private static ActualsEditInput Input(DateOnly start, DateOnly? completed) => new()
    {
        ProjectId = 1, Rows = new List<ActualsEditRowInput> { new() { StageCode = StageCodes.FS, ActualStart = start, CompletedOn = completed } }
    };

    private static EditActualsModel CreatePage(ApplicationDbContext db, string role)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "actor"), new Claim(ClaimTypes.Role, role) }, "Test"));
        var http = new DefaultHttpContext { User = user };
        var service = new StageActualsUpdateService(db, new Clock(), new Audit(), NullLogger<StageActualsUpdateService>.Instance);
        return new EditActualsModel(service, db, new UserContext(user), NullLogger<EditActualsModel>.Instance)
        {
            PageContext = new PageContext(new ActionContext(http, new RouteData(), new ActionDescriptor())),
            TempData = new TempDataDictionary(http, new TempDataProvider())
        };
    }

    private sealed class UserContext(ClaimsPrincipal user) : IUserContext
    {
        public ClaimsPrincipal User => user;
        public string? UserId => user.FindFirstValue(ClaimTypes.NameIdentifier);
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => new(2024, 2, 1, 0, 0, 0, TimeSpan.Zero); }
    private sealed class TempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }
    private sealed class Audit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null,
            IDictionary<string, string?>? data = null, HttpContext? http = null) => Task.CompletedTask;
    }
}
