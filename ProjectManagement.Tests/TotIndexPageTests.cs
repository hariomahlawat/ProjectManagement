using System;
using System.Collections.Generic;
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
    public async Task OnPostDecideAsync_AllowsApprovalWhenRowVersionEmpty()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var project = new Project
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
                RowVersion = Array.Empty<byte>()
            }
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var trackerService = new ProjectTotTrackerReadService(db);
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 6, 1, 6, 0, 0, TimeSpan.Zero));
        var totService = new ProjectTotService(db, clock);
        var exportService = new StubProjectTotExportService();
        var authorizationService = new AllowAllAuthorizationService();
        using var userManager = CreateUserManager(db);
        var remarkService = new StubRemarkService();
        var page = new IndexModel(
            db,
            trackerService,
            totService,
            exportService,
            authorizationService,
            userManager,
            remarkService,
            NullLogger<IndexModel>.Instance);

        ConfigurePageContext(page, CreatePrincipal("approver", "HoD"));

        page.DecideInput = new IndexModel.DecideRequestInput
        {
            ProjectId = 501,
            Approve = true,
            RowVersion = string.Empty
        };

        page.DecideContextBody = null;

        var formValues = new Dictionary<string, StringValues>
        {
            ["DecideInput.ProjectId"] = "501",
            ["DecideInput.Approve"] = "true",
            ["DecideInput.RowVersion"] = string.Empty
        };

        page.Request.ContentType = "application/x-www-form-urlencoded";
        page.Request.Form = new FormCollection(formValues);

        var result = await page.OnPostDecideAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);

        var request = await db.ProjectTotRequests.AsNoTracking().SingleAsync(r => r.ProjectId == 501);
        Assert.Equal(ProjectTotRequestDecisionState.Approved, request.DecisionState);
        Assert.NotNull(request.RowVersion);
        Assert.NotEmpty(request.RowVersion);

        var tot = await db.ProjectTots.AsNoTracking().SingleAsync(t => t.ProjectId == 501);
        Assert.Equal(ProjectTotStatus.Completed, tot.Status);
        Assert.Equal(new DateOnly(2024, 4, 1), tot.StartedOn);
        Assert.Equal(new DateOnly(2024, 5, 15), tot.CompletedOn);
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
