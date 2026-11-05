using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.Tot;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Utilities.PartialDates;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// The ToT register hosts the shared drawer with its own rules: Project Office, HoD and Admin act
/// on any project, a Project Officer on projects they lead; HoD and Admin save directly and decide,
/// everyone else submits for approval; viewers (the Comdt) only read (redesign PR 6b).
/// </summary>
public sealed class TotIndexPageTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 6, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("office", "Project Office", true)]
    [InlineData("office-alt", "ProjectOffice", true)]
    [InlineData("hod", "HoD", true)]
    [InlineData("po-1", "Project Officer", false)]
    [InlineData("comdt", "Comdt", false)]
    public async Task Details_ReportWhoMayUpdateAnotherOfficersProject(string userId, string role, bool canManage)
    {
        await using var db = await SeedAsync();

        var details = await DetailsAsync(db, 630, userId, role);

        Assert.Equal(canManage, details.GetProperty("canManage").GetBoolean());
        Assert.Equal("Another officer project", details.GetProperty("projectName").GetString());
    }

    [Fact]
    public async Task ProjectOfficer_SubmitsForTheirOwnProjectOnly()
    {
        await using var db = await SeedAsync();

        var other = await SaveAsync(db, 630, "po-1", "Project Officer", ProjectTotStatus.NotRequired);
        var own = await SaveAsync(db, 631, "po-1", "Project Officer", ProjectTotStatus.NotRequired);

        Assert.Equal(StatusCodes.Status403Forbidden, other.Status);
        Assert.Equal(StatusCodes.Status200OK, own.Status);
        Assert.True(own.Body.GetProperty("submittedForApproval").GetBoolean());
        Assert.False(await db.ProjectTotRequests.AnyAsync(r => r.ProjectId == 630));
        Assert.Equal(ProjectTotStatus.NotRequired, (await db.ProjectTotRequests.SingleAsync(r => r.ProjectId == 631)).ProposedStatus);
    }

    [Fact]
    public async Task ProjectOffice_SubmitsForAnyProject_ForApproval()
    {
        await using var db = await SeedAsync();

        var result = await SaveAsync(db, 630, "office", "Project Office", ProjectTotStatus.NotRequired);

        Assert.Equal(StatusCodes.Status200OK, result.Status);
        Assert.True(result.Body.GetProperty("submittedForApproval").GetBoolean());
        db.ChangeTracker.Clear();
        Assert.Equal(ProjectTotStatus.NotStarted, (await db.ProjectTots.SingleAsync(t => t.ProjectId == 630)).Status);
    }

    [Fact]
    public async Task Hod_SavesDirectly()
    {
        await using var db = await SeedAsync();

        var result = await SaveAsync(db, 630, "hod", "HoD", ProjectTotStatus.NotRequired);

        Assert.Equal(StatusCodes.Status200OK, result.Status);
        Assert.False(result.Body.GetProperty("submittedForApproval").GetBoolean());
        db.ChangeTracker.Clear();
        Assert.Equal(ProjectTotStatus.NotRequired, (await db.ProjectTots.SingleAsync(t => t.ProjectId == 630)).Status);
    }

    [Fact]
    public async Task Viewer_CannotSaveOrDecide()
    {
        await using var db = await SeedAsync();

        var save = await SaveAsync(db, 630, "comdt", "Comdt", ProjectTotStatus.NotRequired);
        var decide = await DecideAsync(db, 630, "comdt", "Comdt", approve: true, version: "AAAA", reason: null);

        Assert.Equal(StatusCodes.Status403Forbidden, save.Status);
        Assert.Equal(StatusCodes.Status403Forbidden, decide.Status);
    }

    [Fact]
    public async Task Hod_DecidesASubmissionFromTheRegister()
    {
        await using var db = await SeedAsync();
        await SaveAsync(db, 630, "office", "Project Office", ProjectTotStatus.NotRequired);
        var details = await DetailsAsync(db, 630, "hod", "HoD");
        var pending = details.GetProperty("pending");
        Assert.True(pending.GetProperty("canDecide").GetBoolean());

        var stale = await DecideAsync(db, 630, "hod", "HoD", approve: true, version: "AAAAAAAAAAAAAAAAAAAAAA==", reason: null);
        var rejectWithoutReason = await DecideAsync(db, 630, "hod", "HoD", approve: false, pending.GetProperty("rowVersion").GetString(), reason: " ");
        var approve = await DecideAsync(db, 630, "hod", "HoD", approve: true, pending.GetProperty("rowVersion").GetString(), reason: null);

        Assert.Equal(StatusCodes.Status409Conflict, stale.Status);
        Assert.Equal(StatusCodes.Status400BadRequest, rejectWithoutReason.Status);
        Assert.Equal(StatusCodes.Status200OK, approve.Status);
        db.ChangeTracker.Clear();
        Assert.Equal(ProjectTotStatus.NotRequired, (await db.ProjectTots.SingleAsync(t => t.ProjectId == 630)).Status);
    }

    [Fact]
    public async Task ProjectOffice_CannotDecide()
    {
        await using var db = await SeedAsync();
        await SaveAsync(db, 630, "po-other", "Project Officer", ProjectTotStatus.NotRequired);
        var details = await DetailsAsync(db, 630, "office", "Project Office");

        var decide = await DecideAsync(db, 630, "office", "Project Office", approve: true, details.GetProperty("pending").GetProperty("rowVersion").GetString(), reason: null);

        Assert.False(details.GetProperty("pending").GetProperty("canDecide").GetBoolean());
        Assert.Equal(StatusCodes.Status403Forbidden, decide.Status);
    }

    [Fact]
    public async Task ArchivedProject_IsReadOnly()
    {
        await using var db = await SeedAsync();
        var project = await db.Projects.SingleAsync(p => p.Id == 631);
        project.IsArchived = true;
        await db.SaveChangesAsync();

        var result = await SaveAsync(db, 631, "office", "Project Office", ProjectTotStatus.NotRequired);

        Assert.NotEqual(StatusCodes.Status200OK, result.Status);
    }

    private static async Task<JsonElement> DetailsAsync(ApplicationDbContext db, int projectId, string userId, string role)
    {
        var page = CreatePage(db, userId, role);
        var result = Assert.IsType<JsonResult>(await page.OnGetTotDetailsAsync(projectId, CancellationToken.None));
        return JsonSerializer.SerializeToElement(result.Value);
    }

    private static async Task<(int Status, JsonElement Body)> SaveAsync(
        ApplicationDbContext db, int projectId, string userId, string role, ProjectTotStatus status)
    {
        var page = CreatePage(db, userId, role);
        var input = new ProjectTotDrawerInput { ProjectId = projectId, Status = status };
        var result = Assert.IsType<JsonResult>(await page.OnPostTotAsync(projectId, input, CancellationToken.None));
        db.ChangeTracker.Clear();
        return (result.StatusCode ?? StatusCodes.Status200OK, JsonSerializer.SerializeToElement(result.Value));
    }

    private static async Task<(int Status, JsonElement Body)> DecideAsync(
        ApplicationDbContext db, int projectId, string userId, string role, bool approve, string? version, string? reason)
    {
        var page = CreatePage(db, userId, role);
        var result = Assert.IsType<JsonResult>(await page.OnPostTotDecideAsync(projectId, approve, version, reason, CancellationToken.None));
        db.ChangeTracker.Clear();
        return (result.StatusCode ?? StatusCodes.Status200OK, JsonSerializer.SerializeToElement(result.Value));
    }

    private static async Task<ApplicationDbContext> SeedAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        foreach (var id in new[] { "office", "office-alt", "hod", "po-1", "po-other", "comdt" })
        {
            db.Users.Add(new ApplicationUser { Id = id, UserName = id, FullName = id });
        }

        db.Projects.AddRange(
            new Project
            {
                Id = 630,
                Name = "Another officer project",
                CreatedByUserId = "office",
                CreatedAt = new DateTime(2015, 1, 1),
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                CompletedYear = 2015,
                LeadPoUserId = "po-other",
                RowVersion = new byte[] { 1 },
                Tot = new ProjectTot { ProjectId = 630, Status = ProjectTotStatus.NotStarted }
            },
            new Project
            {
                Id = 631,
                Name = "Own project",
                CreatedByUserId = "office",
                CreatedAt = new DateTime(2016, 1, 1),
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                CompletedYear = 2016,
                LeadPoUserId = "po-1",
                RowVersion = new byte[] { 1 },
                Tot = new ProjectTot { ProjectId = 631, Status = ProjectTotStatus.NotStarted, StartDatePrecision = PartialDatePrecision.None }
            });
        await db.SaveChangesAsync();
        return db;
    }

    private static IndexModel CreatePage(ApplicationDbContext db, string userId, string role)
    {
        var page = new IndexModel(
            db,
            new ProjectTotTrackerReadService(db),
            new StubProjectTotExportService(),
            new RoleAuthorizationService(),
            CreateUserManager(db),
            new FixedClock(Now),
            NullLogger<IndexModel>.Instance);

        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Name, userId), new Claim(ClaimTypes.Role, role) },
            "Test"));
        var httpContext = new DefaultHttpContext { User = user };
        page.PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()))
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
        return page;
    }

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext db)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ILookupNormalizer, UpperInvariantLookupNormalizer>()
            .BuildServiceProvider();

        return new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(db),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            services.GetRequiredService<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            services,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    // Grants the ToT policies by the same role lists the app registers.
    private sealed class RoleAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            var roles = policyName switch
            {
                ProjectOfficeReportsPolicies.ManageTotTracker => ProjectOfficeReportsPolicies.TotTrackerSubmitterRoles,
                ProjectOfficeReportsPolicies.ApproveTotTracker => ProjectOfficeReportsPolicies.TotTrackerApproverRoles,
                _ => Array.Empty<string>()
            };
            return Task.FromResult(roles.Any(user.IsInRole) ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        }
    }

    private sealed class StubProjectTotExportService : IProjectTotExportService
    {
        public Task<ProjectTotExportResult> ExportAsync(ProjectTotExportRequest request, CancellationToken cancellationToken)
            => Task.FromResult(ProjectTotExportResult.Failure("Not supported in tests."));
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; }
    }
}
