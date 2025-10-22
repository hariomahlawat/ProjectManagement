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
                ProposedCompletedOn = new DateOnly(2024, 5, 15),
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

        ConfigurePageContext(page, CreatePrincipal("approver", "Approver"));

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
        page.HttpContext.Features.Set<IFormFeature>(new FormFeature(new FormCollection(formValues)));

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
