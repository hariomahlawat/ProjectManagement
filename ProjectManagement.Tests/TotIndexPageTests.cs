using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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
using Microsoft.Extensions.Primitives;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.Tot;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Remarks;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.Utilities.PartialDates;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class TotIndexPageTests
{
    [Fact]
    public async Task OnPostDecideAsync_WithReviewedVersion_ApprovesAndRecordsHistory()
    {
        await using var db = CreateContext();
        var version = await SeedPendingRequestAsync(db);

        var (_, result) = await DecideAsync(db, approve: true, Convert.ToBase64String(version), reason: null);

        Assert.IsType<RedirectToPageResult>(result);
        var request = await db.ProjectTotRequests.AsNoTracking().SingleAsync(r => r.ProjectId == 501);
        Assert.Equal(ProjectTotRequestDecisionState.Approved, request.DecisionState);
        Assert.NotEqual(version, request.RowVersion);

        var tot = await db.ProjectTots.AsNoTracking().SingleAsync(t => t.ProjectId == 501);
        Assert.Equal(ProjectTotStatus.Completed, tot.Status);
        Assert.Equal(new DateOnly(2024, 5, 15), tot.CompletedOn);

        var audit = await db.ProjectAudits.AsNoTracking().SingleAsync(a => a.ProjectId == 501);
        Assert.Equal(ProjectTotAuditActions.Approved, audit.Action);
        Assert.Equal("approver", audit.PerformedByUserId);
    }

    [Fact]
    public async Task OnPostDecideAsync_WithoutVersionToken_IsRefused()
    {
        await using var db = CreateContext();
        await SeedPendingRequestAsync(db);

        var (page, result) = await DecideAsync(db, approve: true, rowVersion: string.Empty, reason: null);

        Assert.IsType<PageResult>(result);
        Assert.Equal(ProjectTotService.StaleRequestMessage, page.DecisionAlertMessage);
        var request = await db.ProjectTotRequests.AsNoTracking().SingleAsync(r => r.ProjectId == 501);
        Assert.Equal(ProjectTotRequestDecisionState.Pending, request.DecisionState);
    }

    [Fact]
    public async Task OnPostDecideAsync_RejectWithoutReason_IsRefused()
    {
        await using var db = CreateContext();
        var version = await SeedPendingRequestAsync(db);

        var (page, result) = await DecideAsync(db, approve: false, Convert.ToBase64String(version), reason: null);

        Assert.IsType<PageResult>(result);
        Assert.Equal(ProjectTotService.RejectionReasonRequiredMessage, page.DecisionAlertMessage);
        var request = await db.ProjectTotRequests.AsNoTracking().SingleAsync(r => r.ProjectId == 501);
        Assert.Equal(ProjectTotRequestDecisionState.Pending, request.DecisionState);
        Assert.False(await db.ProjectAudits.AnyAsync());
    }

    [Fact]
    public async Task OnPostDecideAsync_RejectWithReason_KeepsReasonInHistory()
    {
        await using var db = CreateContext();
        var version = await SeedPendingRequestAsync(db);

        var (_, result) = await DecideAsync(db, approve: false, Convert.ToBase64String(version), reason: "MET dates do not match the trial report.");

        Assert.IsType<RedirectToPageResult>(result);
        var history = await new ProjectTotTrackerReadService(db).GetHistoryAsync(501);
        var entry = Assert.Single(history);
        Assert.Equal(ProjectTotAuditActions.Rejected, entry.Action);
        Assert.Equal("MET dates do not match the trial report.", entry.Reason);
        Assert.Equal("Completed", entry.Values!.StatusLabel);
        Assert.Equal("15 May 2024", entry.Values.Completed);
    }

    [Fact]
    public async Task OnPostSubmitAsync_ProjectOfficerForAnotherProject_IsRefused()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 630,
            Name = "Another officer project",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            LeadPoUserId = "other-po"
        });
        await db.SaveChangesAsync();

        var page = CreatePage(db, FakeClock.AtUtc(new DateTimeOffset(2025, 10, 1, 6, 0, 0, TimeSpan.Zero)), out var userManager);
        using var disposeUsers = userManager;
        ConfigurePageContext(page, CreatePrincipal("po-1", "Project Officer"));
        page.SubmitInput = new IndexModel.SubmitRequestInput { ProjectId = 630, Status = ProjectTotStatus.NotRequired };

        var result = await page.OnPostSubmitAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.False(await db.ProjectTotRequests.AnyAsync());
    }

    [Fact]
    public async Task OnPostSubmitAsync_ProjectOfficerForOwnProject_Submits()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 631,
            Name = "Own project",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            LeadPoUserId = "po-1"
        });
        await db.SaveChangesAsync();

        var page = CreatePage(db, FakeClock.AtUtc(new DateTimeOffset(2025, 10, 1, 6, 0, 0, TimeSpan.Zero)), out var userManager);
        using var disposeUsers = userManager;
        ConfigurePageContext(page, CreatePrincipal("po-1", "Project Officer"));
        page.SubmitInput = new IndexModel.SubmitRequestInput { ProjectId = 631, Status = ProjectTotStatus.NotRequired };

        var result = await page.OnPostSubmitAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        var request = await db.ProjectTotRequests.AsNoTracking().SingleAsync(r => r.ProjectId == 631);
        Assert.Equal(ProjectTotRequestDecisionState.Pending, request.DecisionState);
        var audit = await db.ProjectAudits.AsNoTracking().SingleAsync(a => a.ProjectId == 631);
        Assert.Equal(ProjectTotAuditActions.Submitted, audit.Action);
    }

    private static async Task<byte[]> SeedPendingRequestAsync(ApplicationDbContext db)
    {
        db.Projects.Add(new Project
        {
            Id = 501,
            Name = "Project Apex",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            Tot = new ProjectTot
            {
                ProjectId = 501,
                Status = ProjectTotStatus.InProgress,
                StartedOn = new DateOnly(2024, 4, 1)
            },
            TotRequest = new ProjectTotRequest
            {
                ProjectId = 501,
                DecisionState = ProjectTotRequestDecisionState.Pending,
                ProposedStatus = ProjectTotStatus.Completed,
                ProposedStartedOn = new DateOnly(2024, 4, 1),
                ProposedStartDatePrecision = PartialDatePrecision.Day,
                ProposedCompletedOn = new DateOnly(2024, 5, 15),
                ProposedCompletionDatePrecision = PartialDatePrecision.Day,
                ProposedMetDetails = "MET delivered",
                ProposedMetCompletedOn = new DateOnly(2024, 5, 10),
                ProposedFirstProductionModelManufactured = true,
                ProposedFirstProductionModelManufacturedOn = new DateOnly(2024, 5, 12),
                SubmittedByUserId = "office"
            }
        });
        await db.SaveChangesAsync();

        // The context assigns the version on save; return what an approver's page would carry.
        return (await db.ProjectTotRequests.AsNoTracking().SingleAsync(r => r.ProjectId == 501)).RowVersion;
    }

    private static async Task<(IndexModel Page, IActionResult Result)> DecideAsync(
        ApplicationDbContext db,
        bool approve,
        string rowVersion,
        string? reason)
    {
        var page = CreatePage(db, FakeClock.AtUtc(new DateTimeOffset(2024, 6, 1, 6, 0, 0, TimeSpan.Zero)), out var userManager);
        using var disposeUsers = userManager;
        ConfigurePageContext(page, CreatePrincipal("approver", "HoD"));

        page.DecideInput = new IndexModel.DecideRequestInput
        {
            ProjectId = 501,
            Approve = approve,
            RowVersion = rowVersion
        };
        page.DecideContextBody = reason;

        page.Request.ContentType = "application/x-www-form-urlencoded";
        page.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["DecideInput.ProjectId"] = "501",
            ["DecideInput.Approve"] = approve ? "true" : "false",
            ["DecideInput.RowVersion"] = rowVersion
        });

        var result = await page.OnPostDecideAsync(CancellationToken.None);
        return (page, result);
    }

    private static IndexModel CreatePage(ApplicationDbContext db, FakeClock clock, out UserManager<ApplicationUser> userManager)
    {
        userManager = CreateUserManager(db);
        return new IndexModel(
            db,
            new ProjectTotTrackerReadService(db),
            new ProjectTotService(db, clock),
            new StubProjectTotExportService(),
            new AllowAllAuthorizationService(),
            userManager,
            new StubRemarkService(),
            NullLogger<IndexModel>.Instance);
    }


    [Fact]
    public async Task BuildSubmitInput_KeepsStoredPrecision_AndIgnoresRejectedProposal()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 610,
            Name = "Project Year Only",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            Tot = new ProjectTot
            {
                ProjectId = 610,
                Status = ProjectTotStatus.Completed,
                StartedOn = new DateOnly(2023, 6, 1),
                StartDatePrecision = PartialDatePrecision.Month,
                CompletedOn = new DateOnly(2024, 12, 31),
                CompletionDatePrecision = PartialDatePrecision.Year,
                MetDetails = "Current MET"
            },
            TotRequest = new ProjectTotRequest
            {
                ProjectId = 610,
                DecisionState = ProjectTotRequestDecisionState.Rejected,
                ProposedStatus = ProjectTotStatus.InProgress,
                ProposedStartedOn = new DateOnly(2022, 1, 15),
                ProposedStartDatePrecision = PartialDatePrecision.Day,
                ProposedMetDetails = "Rejected MET",
                SubmittedByUserId = "office",
                RowVersion = Guid.NewGuid().ToByteArray()
            }
        });
        await db.SaveChangesAsync();

        var row = Assert.Single(await new ProjectTotTrackerReadService(db).GetAsync(new ProjectTotTrackerFilter()));
        var input = IndexModel.BuildSubmitInput(row);

        Assert.Equal(ProjectTotStatus.Completed, input.Status);
        Assert.Equal("Current MET", input.MetDetails);
        Assert.Equal(2023, input.StartYear);
        Assert.Equal(6, input.StartMonth);
        Assert.Null(input.StartDay);
        Assert.Equal(2024, input.CompletionYear);
        Assert.Null(input.CompletionMonth);
        Assert.Null(input.CompletionDay);
    }

    [Fact]
    public async Task BuildSubmitInput_UsesPendingProposal()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 611,
            Name = "Project Pending",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            Tot = new ProjectTot { ProjectId = 611, Status = ProjectTotStatus.NotStarted },
            TotRequest = new ProjectTotRequest
            {
                ProjectId = 611,
                DecisionState = ProjectTotRequestDecisionState.Pending,
                ProposedStatus = ProjectTotStatus.InProgress,
                ProposedStartedOn = new DateOnly(2024, 1, 1),
                ProposedStartDatePrecision = PartialDatePrecision.Year,
                SubmittedByUserId = "office",
                RowVersion = Guid.NewGuid().ToByteArray()
            }
        });
        await db.SaveChangesAsync();

        var row = Assert.Single(await new ProjectTotTrackerReadService(db).GetAsync(new ProjectTotTrackerFilter()));
        var input = IndexModel.BuildSubmitInput(row);

        Assert.Equal(ProjectTotStatus.InProgress, input.Status);
        Assert.Equal(2024, input.StartYear);
        Assert.Null(input.StartMonth);
        Assert.Null(input.StartDay);
    }

    [Fact]
    public async Task OnPostSubmitAsync_WhenServiceRejects_KeepsTheUsersDates()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 620,
            Name = "Project Keep Input",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            Tot = new ProjectTot
            {
                ProjectId = 620,
                Status = ProjectTotStatus.InProgress,
                StartedOn = new DateOnly(2021, 1, 1),
                StartDatePrecision = PartialDatePrecision.Year
            }
        });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2025, 10, 1, 6, 0, 0, TimeSpan.Zero));
        using var userManager = CreateUserManager(db);
        var page = new IndexModel(
            db,
            new ProjectTotTrackerReadService(db),
            new ProjectTotService(db, clock),
            new StubProjectTotExportService(),
            new AllowAllAuthorizationService(),
            userManager,
            new StubRemarkService(),
            NullLogger<IndexModel>.Instance);
        ConfigurePageContext(page, CreatePrincipal("office", "Project Office"));

        page.SelectedProjectId = 620;
        page.SubmitInput = new IndexModel.SubmitRequestInput
        {
            ProjectId = 620,
            Status = ProjectTotStatus.Completed,
            StartYear = 2025,
            StartMonth = 6,
            CompletionYear = 2025,
            CompletionMonth = 3
        };

        var result = await page.OnPostSubmitAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(page.ShowSubmitModal);
        Assert.Equal(ProjectTotStatus.Completed, page.SubmitInput.Status);
        Assert.Equal(2025, page.SubmitInput.StartYear);
        Assert.Equal(6, page.SubmitInput.StartMonth);
        Assert.Equal(2025, page.SubmitInput.CompletionYear);
        Assert.Equal(3, page.SubmitInput.CompletionMonth);
        Assert.False(await db.ProjectTotRequests.AnyAsync());
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static void ConfigurePageContext(PageModel page, ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());

        page.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };

        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());
    }

    private static ClaimsPrincipal CreatePrincipal(string userId, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userId),
            new(ClaimTypes.Role, role)
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
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

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }

    private sealed class StubProjectTotExportService : IProjectTotExportService
    {
        public Task<ProjectTotExportResult> ExportAsync(ProjectTotExportRequest request, CancellationToken cancellationToken)
            => Task.FromResult(ProjectTotExportResult.Failure("Not supported in tests."));
    }

    private sealed class AllowAllAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Success());
    }

    private sealed class StubRemarkService : IRemarkService
    {
        public Task<Remark> CreateRemarkAsync(CreateRemarkRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new Remark());

        public Task<RemarkListResult> ListRemarksAsync(ListRemarksRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new RemarkListResult(0, Array.Empty<Remark>(), 1, 0));

        public Task<Remark?> EditRemarkAsync(int remarkId, EditRemarkRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult<Remark?>(null);

        public Task<bool> SoftDeleteRemarkAsync(int remarkId, SoftDeleteRemarkRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<RemarkAudit>> GetRemarkAuditAsync(int remarkId, RemarkActorContext actor, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RemarkAudit>>(Array.Empty<RemarkAudit>());
    }
}
